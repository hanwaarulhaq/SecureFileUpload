using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1.Ocsp;
using WebApplication.Controllers;
using WebApplication.Helpers;
using WebApplication.Models;

namespace WebApplication.Services
{
    /// <summary>
    /// Service for handling file uploads with validation, encryption, and compression
    /// </summary>
    public class FileUploadService
    {
        #region Fields and Constructor

        private readonly string _tempUploadPath;
        private readonly string _finalUploadPath;
        private readonly IConfiguration _configuration;
        private readonly IAntivirusChecker _antivirusChecker;
        private readonly ILogger<FileUploadService> _logger;
        private static readonly List<FileUploadSession> _sessions = new List<FileUploadSession>();

        public FileUploadService(IConfiguration configuration, IAntivirusChecker antivirusChecker, ILogger<FileUploadService> logger)
        {
            _configuration = configuration;
            _antivirusChecker = antivirusChecker;
            _logger = logger;

            // Initialize upload directories
            _tempUploadPath = PathHelper.SafeCombine(Directory.GetCurrentDirectory(), "TempUploads");
            _finalUploadPath = PathHelper.SafeCombine(Directory.GetCurrentDirectory(), "Uploads");

            EnsureDirectoryExists(_tempUploadPath);
            EnsureDirectoryExists(_finalUploadPath);
        }

        #endregion

        #region Session Management

        /// <summary>
        /// Gets or creates a file upload session
        /// </summary>
        public FileUploadSession GetSession(string sessionId)
        {
            var session = _sessions.FirstOrDefault(s => s.SessionId == sessionId);

            if (session == null)
            {
                session = new FileUploadSession(_tempUploadPath, _finalUploadPath)
                {
                    SessionId = sessionId
                };
            }

            return session;
        }

        #endregion

        #region File Validation

        /// <summary>
        /// Validates a file against the specified configuration
        /// </summary>
        public async Task ValidateFileAsync(IFormFile file, UploadConfiguration config, string declaredFileType = null)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty or null");

            var fileName = SanitizeFileName(file.FileName);
            ValidateFileExtension(fileName, config);

            if (!string.IsNullOrEmpty(declaredFileType))
            {
                ValidateDeclaredFileType(fileName, declaredFileType, config);
            }

            ValidateFileSize(file.Length, config.MaxSize);

            if (!string.IsNullOrEmpty(config.AllowedMimeTypes))
            {
                await ValidateFileContentAsync(file, config.AllowedMimeTypes);
            }
        }

        private void ValidateFileExtension(string fileName, UploadConfiguration config)
        {
            var fileExt = Path.GetExtension(fileName).ToLowerInvariant();
            var allowedExtensions = config.AllowedTypes.Split(',')
                .Select(e => e.Trim().ToLowerInvariant())
                .ToArray();

            // Check for Unicode tricks in extension
            if (fileExt.Any(c => c > 0x007F))
            {
                _logger.LogWarning($"Non-ASCII characters in file extension: {fileName}");
                throw new Exception("Non-ASCII characters in file extension not allowed");
            }

            if (!allowedExtensions.Contains(fileExt))
                throw new Exception($"File type {fileExt} is not allowed");
        }

        private void ValidateDeclaredFileType(string fileName, string declaredFileType, UploadConfiguration config)
        {
            var fileExt = Path.GetExtension(fileName).ToLowerInvariant();
            var allowedExtensions = config.AllowedTypes.Split(',')
                .Select(e => e.Trim().ToLowerInvariant())
                .ToArray();

            var expectedExtensions = _fileTypeMap
                .Where(f => f.Mime.Equals(declaredFileType, StringComparison.OrdinalIgnoreCase))
                .SelectMany(f => allowedExtensions)
                .Distinct();

            if (!expectedExtensions.Contains(fileExt))
            {
                _logger.LogWarning($"File extension {fileExt} doesn't match declared type {declaredFileType}");
                throw new Exception($"File extension doesn't match declared file type");
            }
        }

        private void ValidateFileSize(long fileSize, long maxFileSize)
        {
            if (fileSize > maxFileSize)
                throw new Exception($"File size exceeds maximum allowed size of {maxFileSize} bytes");
        }

        public async Task ValidateFileContentAsync(IFormFile file, string allowedMimeTypes)
        {
            if (string.IsNullOrEmpty(allowedMimeTypes))
                return;

            var allowedMimeTypesArray = allowedMimeTypes.Split(',')
                .Select(s => s.Trim().ToLowerInvariant())
                .ToArray();

            if (!await ValidateFileHeaderAsync(file, allowedMimeTypesArray))
            {
                throw new Exception($"File type (header) is not allowed. Allowed MIME types: {allowedMimeTypes}");
            }
        }

        private async Task<bool> ValidateFileHeaderAsync(IFormFile file, string[] allowedMimeTypes)
        {
            using (var stream = file.OpenReadStream())
            {
                byte[] buffer = new byte[20];
                await stream.ReadAsync(buffer, 0, buffer.Length);
                stream.Position = 0;

                foreach (var mimeType in allowedMimeTypes)
                {
                    var config = _fileTypeMap.FirstOrDefault(m =>
                        m.Mime.Equals(mimeType, StringComparison.OrdinalIgnoreCase));

                    if (config != null && (config.MagicNumbers.Length == 0 ||
                        config.MagicNumbers.Any(magic => StartsWith(buffer, magic))))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion

        #region File Name Sanitization

        /// <summary>
        /// Sanitizes a file name by removing invalid characters and checking for potential threats
        /// </summary>
        public string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name cannot be empty");

            // Normalize the Unicode string (Form KC is most comprehensive)
            fileName = fileName.Normalize(NormalizationForm.FormKC);

            // Check for mixed scripts (Latin + other scripts)
            if (ContainsMixedScripts(fileName))
            {
                throw new ArgumentException("Mixed script characters not allowed in filename");
            }

            // Check for right-to-left override characters
            if (ContainsBidirectionalControlCharacters(fileName))
            {
                throw new ArgumentException("Bidirectional control characters not allowed");
            }

            // Check for homoglyphs (characters that look similar to others)
            fileName = ReplaceHomoglyphs(fileName);

            fileName = fileName.Replace("../", "").Replace("..\\", "");

            if (ContainsZeroWidthCharacters(fileName))
            {
                throw new ArgumentException("Zero-width characters not allowed in filename");
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName
                .Where(ch => !invalidChars.Contains(ch))
                .ToArray());

            if (string.IsNullOrWhiteSpace(sanitized))
                throw new ArgumentException("Invalid file name");

            return sanitized;
        }

        #endregion

        #region File Upload Processing

        /// <summary>
        /// Initiates a new file upload session
        /// </summary>
        public async Task<FileUploadSession> InitiateUploadSession(InitiateUploadRequest request, IFormFile uploadedFile = null)
        {
            // Get the configuration for this page type
            var config = GetUploadConfig(request.PageType);
           
            var sanitizedFileName = SanitizeFileName(request.FileName);

            if (uploadedFile != null)
            {
                //Validates a file against the specified configuration
                await ValidateFileAsync(uploadedFile, config, request.FileType);
            }
            else
            {
                ValidateFileExtension(sanitizedFileName, config);
                ValidateFileSize(request.FileSize, config.MaxSize);

                if (!string.IsNullOrEmpty(request.FileType))
                {
                    ValidateDeclaredFileType(sanitizedFileName, request.FileType, config);
                }
            }

            var session = new FileUploadSession(_tempUploadPath, _finalUploadPath)
            {
                SessionId = Guid.NewGuid().ToString(),
                FileName = sanitizedFileName,
                FileSize = request.FileSize,
                FileType = request.FileType,
                TotalChunks = (int)Math.Ceiling((double)request.FileSize / config.ChunkSize),
                IsEncrypted = config.Encrypt,
                IsCompressed = config.Compress,
                PageType = request.PageType,
                TempFilePath = PathHelper.SafeCombine(_tempUploadPath, $"{Guid.NewGuid()}.tmp")
            };

            if (session.IsEncrypted)
            {
                byte[] key = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(key);
                }

                session.EncryptionKey = Convert.ToBase64String(key);
            }

            _sessions.Add(session);
            return session;
        }

        /// <summary>
        /// Processes an uploaded chunk of a file
        /// </summary>
        public async Task<FileUploadSession> ProcessChunk(string sessionId, IFormFile file, int chunkNumber)
        {
            var session = GetSession(sessionId) ?? throw new Exception($"Session {sessionId} not found");

            var tempChunkPath = PathHelper.SafeCombine(_tempUploadPath, $"{session.SessionId}_{chunkNumber}.part");

            using (var stream = new FileStream(tempChunkPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            session.ChunksUploaded++;
            //session.TotalChunks = totalChunks;

            return session;
        }

        /// <summary>
        /// Completes the file upload by combining chunks and processing the final file
        /// </summary>
        public async Task<string> CompleteUpload(FileUploadSession session)
        {
            var currentSession = GetSession(session.SessionId) ??
                throw new Exception($"Session with ID '{session.SessionId}' not found");

            ValidateUploadCompletion(currentSession);
            await CombineChunksAsync(currentSession);

            byte[] fileBytes = await ProcessUploadedFile(currentSession);
            fileBytes = await ScanVirus(currentSession, fileBytes);

            var finalPath = SaveFinalFile(currentSession, fileBytes);
            CleanupTempFiles(currentSession.SessionId);

            currentSession.FinalFilePath = finalPath;
            currentSession.Status = "Completed";

            return finalPath;
        }
        public void CleanTempFile(string sessionId)
        {
            CleanupTempFiles(sessionId);
        }
        #endregion

        #region Helper Methods

        private void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        private bool StartsWith(byte[] array, byte[] subarray)
        {
            if (subarray.Length > array.Length)
                return false;

            for (int i = 0; i < subarray.Length; i++)
            {
                if (array[i] != subarray[i])
                    return false;
            }
            return true;
        }

        private void ValidateUploadCompletion(FileUploadSession session)
        {
            if (session.ChunksUploaded != session.TotalChunks)
                throw new Exception("Not all chunks have been uploaded");

            var fileExt = Path.GetExtension(session.FileName);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(session.FileName);

            if (Path.HasExtension(nameWithoutExt))
            {
                _logger.LogWarning($"Double extension detected in final validation: {session.FileName}");
                throw new Exception("Invalid file name: double extension detected");
            }
        }

        private async Task CombineChunksAsync(FileUploadSession session)
        {
            session.TempFilePath = PathHelper.SafeCombine(_tempUploadPath, $"{session.SessionId}.tmp");

            using (var finalStream = new FileStream(session.TempFilePath, FileMode.Create))
            {
                for (int i = 0; i < session.TotalChunks; i++)
                {
                    var chunkPath = PathHelper.SafeCombine(_tempUploadPath, $"{session.SessionId}_{i}.part");
                    using (var chunkStream = new FileStream(chunkPath, FileMode.Open))
                    {
                        await chunkStream.CopyToAsync(finalStream);
                    }
                    File.Delete(chunkPath);
                }
            }
        }

        private async Task<byte[]> ProcessUploadedFile(FileUploadSession session)
        {
            byte[] fileBytes;
            using (var fileStream = new FileStream(session.TempFilePath, FileMode.Open))
            {
                fileBytes = new byte[fileStream.Length];
                await fileStream.ReadAsync(fileBytes, 0, (int)fileStream.Length);
            }

            if (session.IsEncrypted)
            {
                byte[] encryptionKey = Convert.FromBase64String(session.EncryptionKey);
                fileBytes = DecryptAES(fileBytes, encryptionKey);
            }

            if (session.IsCompressed)
            {
                fileBytes = DecompressGzip(fileBytes);
            }

            return fileBytes;
        }
        // **Updated AES Decryption Method (Extracts IV)**
        private byte[] DecryptAES(byte[] fileBytes, byte[] key)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = key;
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                // Convert fileBytes to string, then split IV and encrypted data
                string encryptedText = Encoding.UTF8.GetString(fileBytes);
                string[] parts = encryptedText.Split(':');  // Ensure IV is stored correctly

                if (parts.Length < 2)
                {
                    throw new ArgumentException("Invalid encrypted data format.");
                }

                byte[] iv = Convert.FromBase64String(parts[0]);
                byte[] encryptedData = Convert.FromBase64String(parts[1]);

                using (var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, iv))
                {
                    return decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
                }
            }
        }


        private async Task<byte[]> ScanVirus(FileUploadSession session, byte[] fileBytes)
        {
            var IsWindowsDefenderServiceRunning = WindowsServiceHelper.IsServiceRunning("Windows Defender Advanced Threat Protection Service");

            if (IsWindowsDefenderServiceRunning)
            {
                var isVirus = await _antivirusChecker.IsVirus(fileBytes);
                if (isVirus)
                {
                    throw new Exception("File contains virus, system has aborted file upload.");
                }
            }
            return fileBytes;
        }

        private string SaveFinalFile(FileUploadSession session, byte[] fileBytes)
        {
            var finalFileName = $"{Guid.NewGuid()}{Path.GetExtension(session.FileName)}";
            var finalPath = PathHelper.SafeCombine(_finalUploadPath, finalFileName);
            File.WriteAllBytes(finalPath, fileBytes);
            return finalPath;
        }

        private void CleanupTempFiles(string sessionId)
        {
            var session = GetSession(sessionId);
            if (session != null && File.Exists(session.TempFilePath))
            {
                File.Delete(session.TempFilePath);
            }
        }

        private byte[] DecompressGzip(byte[] compressedData)
        {
            try
            {
                using (var compressedStream = new MemoryStream(compressedData))
                {
                    if (compressedData.Length < 10 || compressedData[0] != 0x1F || compressedData[1] != 0x8B)
                    {
                        throw new InvalidDataException("Invalid GZIP header");
                    }

                    using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
                    using (var resultStream = new MemoryStream())
                    {
                        gzipStream.CopyTo(resultStream);
                        return resultStream.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to decompress file", ex);
            }
        }

        #endregion

        #region Unicode Handling

        private bool ContainsMixedScripts(string input)
        {
            // This is a simplified version - consider using a proper script detection library
            bool hasLatin = false;
            bool hasNonLatin = false;

            foreach (var c in input)
            {
                if (c <= 0x007F) // Basic Latin
                {
                    hasLatin = true;
                }
                else
                {
                    hasNonLatin = true;
                }

                if (hasLatin && hasNonLatin)
                    return true;
            }

            return false;
        }

        private bool ContainsBidirectionalControlCharacters(string input)
        {
            // Check for RLO/LRO/PDF control characters
            foreach (var c in input)
            {
                if (c == '\u202E' || // RLO - Right-to-Left Override
                    c == '\u202D' || // LRO - Left-to-Right Override
                    c == '\u202C')   // PDF - Pop Directional Formatting
                {
                    return true;
                }
            }
            return false;
        }

        private string ReplaceHomoglyphs(string input)
        {
            // Basic homoglyph replacement - consider a more comprehensive mapping
            var homoglyphs = new Dictionary<char, char>
            {
                ['а'] = 'a',  // Cyrillic 'a'
                ['е'] = 'e',  // Cyrillic 'e'
                ['о'] = 'o',  // Cyrillic 'o'
                ['р'] = 'p',  // Cyrillic 'p'
                ['с'] = 'c',  // Cyrillic 'c'
                ['і'] = 'i',  // Cyrillic 'i'
                // Add more homoglyph mappings as needed
            };

            return new string(input.Select(c => homoglyphs.TryGetValue(c, out var replacement) ? replacement : c).ToArray());
        }

        private bool ContainsZeroWidthCharacters(string input)
        {
            foreach (var c in input)
            {
                if (c == '\u200B' || // Zero-width space
                    c == '\u200C' || // Zero-width non-joiner
                    c == '\u200D' || // Zero-width joiner
                    c == '\uFEFF')   // Zero-width no-break space
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Configuration Methods
        public UploadConfiguration GetUploadConfig(string pageType)
        {
            // Initialize with default values
            var config = new UploadConfiguration
            {
                ChunkSize = 1 * 1024 * 1024, // 1MB chunks
                Compress = true
            };

            // Set values based on pageType
            switch (pageType)
            {
                case "picture-upload":
                    config.MaxSize = 5 * 1024 * 1024; // 5MB
                    config.AllowedTypes = ".jpg,.png,.gif";
                    config.AllowedMimeTypes = "image/jpeg,image/png,image/gif";
                    config.Encrypt = true;
                    break;

                case "document-upload":
                    config.MaxSize = 50 * 1024 * 1024; // 50MB
                    config.AllowedTypes = ".pdf,.doc,.docx,.xls,.xlsx";
                    config.AllowedMimeTypes = "application/pdf,application/msword," +
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document," +
                        "application/vnd.ms-excel,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    config.Encrypt = true;
                    break;

                default:
                    config.MaxSize = 10 * 1024 * 1024; // Default 10MB
                    config.AllowedTypes = ".jpg,.png,.gif,.pdf,.doc,.docx,.xls,.xlsx";
                    config.AllowedMimeTypes = "image/jpeg,image/png,image/gif,application/pdf," +
                        "application/msword,application/vnd.openxmlformats-officedocument.wordprocessingml.document" +
                        "application/vnd.ms-excel,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    config.Encrypt = true;
                    break;
            }

            return config;
        }

        #endregion

        #region File Type Configuration

        private static readonly FileTypeConfig[] _fileTypeMap = new FileTypeConfig[]
        {
            // 📷 Image Files
            new FileTypeConfig {
                Mime = "image/jpeg",
                MagicNumbers = new byte[][] { new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 } }
            },
            new FileTypeConfig {
                Mime = "image/png",
                MagicNumbers = new byte[][] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } }
            },
        
            // 📄 Document Files (Word, Excel, PDF)
            new FileTypeConfig {
                Mime = "application/pdf",
                MagicNumbers = new byte[][] { new byte[] { 0x25, 0x50, 0x44, 0x46 } } // "%PDF"
            },
            new FileTypeConfig {
                Mime = "application/msword",
                MagicNumbers = new byte[][] { new byte[] { 0xD0, 0xCF, 0x11, 0xE0 } } // DOC (OLE Format)
            },
            new FileTypeConfig {
                Mime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                MagicNumbers = new byte[][] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } // DOCX (ZIP Format)
            },
            new FileTypeConfig {
                Mime = "application/vnd.ms-excel",
                MagicNumbers = new byte[][] { new byte[] { 0xD0, 0xCF, 0x11, 0xE0 } } // XLS (OLE Format)
            },
            new FileTypeConfig {
                Mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                MagicNumbers = new byte[][] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } // XLSX (ZIP Format)
            },
        
            // 🎵 Audio Files
            new FileTypeConfig {
                Mime = "audio/mpeg",
                MagicNumbers = new byte[][] { new byte[] { 0xFF, 0xFB }, new byte[] { 0xFF, 0xF3 }, new byte[] { 0xFF, 0xF2 } } // MP3
            },
            new FileTypeConfig {
                Mime = "audio/wav",
                MagicNumbers = new byte[][] { new byte[] { 0x52, 0x49, 0x46, 0x46 } } // WAV
            },
        
            // 🎬 Video Files
            new FileTypeConfig {
                Mime = "video/mp4",
                MagicNumbers = new byte[][] { new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 } } // MP4
            },
            new FileTypeConfig {
                Mime = "video/x-msvideo",
                MagicNumbers = new byte[][] { new byte[] { 0x52, 0x49, 0x46, 0x46 } } // AVI
            },
        
            // 📦 Archive Files
            new FileTypeConfig {
                Mime = "application/zip",
                MagicNumbers = new byte[][] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } // ZIP
            },
            new FileTypeConfig {
                Mime = "application/x-rar-compressed",
                MagicNumbers = new byte[][] { new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07 } } // RAR
            },
            new FileTypeConfig {
                Mime = "application/x-tar",
                MagicNumbers = new byte[][] { new byte[] { 0x75, 0x73, 0x74, 0x61, 0x72 } } // TAR
            },
        
            // 💻 Executable Files
            new FileTypeConfig {
                Mime = "application/x-msdownload",
                MagicNumbers = new byte[][] { new byte[] { 0x4D, 0x5A } } // EXE (Windows Executable)
            },
        
            // 📜 Generic Binary
            new FileTypeConfig {
                Mime = "application/octet-stream",
                MagicNumbers = new byte[][] { }
            }
        };

        #endregion
    }
}