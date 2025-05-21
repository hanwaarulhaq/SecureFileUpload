using System.IO;
using System;

namespace WebApplication.Helpers
{
    public static class PathHelper
    {

        
        public static string SafeCombine(string basePath, string relativePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(basePath))
                    throw new ArgumentException("Base path cannot be empty", nameof(basePath));

                if (string.IsNullOrWhiteSpace(relativePath))
                    return basePath;

                // Normalize paths
                basePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar);
                relativePath = relativePath.TrimStart(Path.DirectorySeparatorChar);

                // Combine and get full path
                string combinedPath = Path.GetFullPath(Path.Combine(basePath, relativePath));

                // Verify the combined path is within the base directory
                if (!combinedPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Invalid path: Attempted path traversal detected");
                }

                return combinedPath;
            }
            catch (ArgumentException ex)
            {
                
                throw;
            }

        }

        public static bool IsPathSafe(string basePath, string relativePath)
        {
            try
            {
                SafeCombine(basePath, relativePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
