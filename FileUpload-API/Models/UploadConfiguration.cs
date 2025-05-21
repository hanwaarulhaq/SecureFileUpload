namespace WebApplication.Models
{
    // Models/UploadConfiguration.cs
    public class UploadConfiguration
    {
        public long MaxSize { get; set; }
        public string AllowedTypes { get; set; }
        public string AllowedMimeTypes { get; set; }
        public int ChunkSize { get; set; }
        public bool Encrypt { get; set; }
        public bool Compress { get; set; }
    }

    public class InitiateUploadRequest
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string FileType { get; set; }
        public string PageType { get; set; } // New property
    }

    public class FileTypeConfig
    {
        public string Mime { get; set; }
        public byte[][] MagicNumbers { get; set; }
    }
    public class CompleteRequest
    {
        public string SessionId { get; set; }
        public int TotalChunks { get; set; }
    }
}
