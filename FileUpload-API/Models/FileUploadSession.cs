using System;
using System.IO;
using WebApplication.Helpers;

namespace WebApplication.Models
{
    // Models/FileUploadSession.cs
    public class FileUploadSession
    {
        private string _tempFilePath;
        private string _finalFilePath;
        private readonly string _rootTempPath;
        private readonly string _rootFinalPath;

        public FileUploadSession(string rootTempPath, string rootFinalPath)
        {
            _rootTempPath = rootTempPath ?? throw new ArgumentNullException(nameof(rootTempPath));
            _rootFinalPath = rootFinalPath ?? throw new ArgumentNullException(nameof(rootFinalPath));
        }

        public string SessionId { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string FileType { get; set; }
        public int TotalChunks { get; set; }
        public int ChunksUploaded { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "Initialized";
        public bool IsEncrypted { get; set; }
        public bool IsCompressed { get; set; }
        public string EncryptionKey { get; set; }
        public string PageType { get; set; }
        //public string IV { get; set; }

        public string TempFilePath
        {
            get => _tempFilePath;
            set => _tempFilePath = PathHelper.SafeCombine(_rootTempPath, value);
        }

        public string FinalFilePath
        {
            get => _finalFilePath;
            set => _finalFilePath = PathHelper.SafeCombine(_rootFinalPath, value);
        }
        //public string PublicKey { get; internal set; }
    }
}
