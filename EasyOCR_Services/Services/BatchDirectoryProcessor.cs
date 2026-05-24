using EasyOCR_Services.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace EasyOCR_Services.Services
{
    public class BatchDirectoryProcessor
    {
        // This is a static property, so a HashSet is created once and shared across all instances of BatchDirectoryProcessor.
        public static HashSet<string>  ImageExtentions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"
        };

        public IEnumerable<List<ImageGroup>> GetBatches(string rootPath, string outputRoot, bool useConsistentPath = false, int batchSize = 10)
        {
            var imageExtensions = ImageExtentions;

            var currentBatch = new List<ImageGroup>(batchSize);

            foreach (var directory in GetDirectoriesWithImages(rootPath))
            {
                DirectoryInfo dirInfo = new DirectoryInfo(directory);
                var images = Directory.GetFiles(directory, "*.*")
                    .Where(f => imageExtensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f)
                    .ToList();

                var dirRelativePath = Path.GetRelativePath(rootPath, directory);
                var pathForPdf = useConsistentPath ? Path.Combine(outputRoot, dirRelativePath, $"{dirInfo.Name}.pdf") : Path.Combine(outputRoot, $"{dirInfo.Name}.pdf");
                var imageGroup = new ImageGroup(dirInfo.Name, images, pathForPdf);
                currentBatch.Add(imageGroup);

                if (currentBatch.Count == batchSize)
                {
                    yield return currentBatch;
                    currentBatch = new List<ImageGroup>(batchSize);
                }
            }

            // Return last batch if not empty
            if (currentBatch.Any())
            {
                yield return currentBatch;
            }
        }

        private IEnumerable<string> GetDirectoriesWithImages(string rootPath)
        {
            if (!Directory.Exists(rootPath)) yield break;

            var imageExtensions = ImageExtentions;

            foreach (string directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
            {
                bool hasImage = false;
                try
                {
                    hasImage = Directory.EnumerateFiles(directory)
                                .Any(f => imageExtensions.Contains(Path.GetExtension(f)));
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }

                if (hasImage)
                {
                    yield return directory;
                }
            }
        }
    }
}
