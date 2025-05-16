using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShowStasher.Services
{
    public class FileOrganizerService
    {
        private readonly Action<string> _log;

        public FileOrganizerService(Action<string> log)
        {
            _log = log;
        }

        public void Organize(string sourcePath, string destinationPath)
        {
            if (!Directory.Exists(sourcePath))
            {
                _log($"Source folder doesn't exist: {sourcePath}");
                return;
            }

            var files = Directory.GetFiles(sourcePath);
            _log($"Found {files.Length} files to process.");

            foreach (var filePath in files)
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);

                    // 1. Identify what type it is
                    var type = ClassifyFile(fileName);
                    _log($"Classified '{fileName}' as {type}");

                    // 2. Query metadata (stub for now)
                    var metadata = FetchMetadata(fileName, type);

                    // 3. Determine destination folder
                    string targetFolder = GetTargetFolder(destinationPath, metadata, type);
                    Directory.CreateDirectory(targetFolder);

                    // 4. Move the file
                    string newFilePath = Path.Combine(targetFolder, Path.GetFileName(filePath));
                    if (File.Exists(newFilePath))
                    {
                        _log($"File already exists: {newFilePath}, skipping move.");
                    }
                    else
                    {
                        File.Move(filePath, newFilePath);
                        _log($"Moved: {fileName} → {targetFolder}");
                    }

                    // 5. Save metadata file and poster (placeholders)
                    SaveSynopsisIfMissing(targetFolder, metadata);
                    SavePosterIfMissing(targetFolder, metadata);
                }
                catch (Exception ex)
                {
                    _log($"Error processing file: {filePath} - {ex.Message}");
                }
            }
        }

        private string ClassifyFile(string fileName)
        {
            // TODO: Improve logic to detect season/episode patterns
            if (Regex.IsMatch(fileName, @"S\d{1,2}E\d{1,2}", RegexOptions.IgnoreCase))
                return "Series";

            if (Regex.IsMatch(fileName, @"Season\s*\d+", RegexOptions.IgnoreCase))
                return "Series";

            // Default to Movie
            return "Movie";
        }

        private MediaMetadata FetchMetadata(string title, string type)
        {
            // This is a stub. Replace with TMDb / Jikan logic.
            return new MediaMetadata
            {
                Title = title,
                Type = type,
                Synopsis = "Sample synopsis.",
                Rating = "PG-13",
                PG = "PG-13",
                PosterUrl = "",
                Season = 1,
                Episode = 1
            };
        }

        private string GetTargetFolder(string basePath, MediaMetadata metadata, string type)
        {
            char firstLetter = char.ToUpper(metadata.Title[0]);
            string letterFolder = Path.Combine(basePath, type, firstLetter.ToString());

            string target = type == "Series"
                ? Path.Combine(letterFolder, metadata.Title, $"Season {metadata.Season}")
                : Path.Combine(letterFolder, metadata.Title);

            return target;
        }

        private void SaveSynopsisIfMissing(string folder, MediaMetadata metadata)
        {
            string synopsisPath = Path.Combine(folder, "synopsis.txt");
            if (!File.Exists(synopsisPath))
            {
                File.WriteAllText(synopsisPath,
                    $"{metadata.Title}\nRating: {metadata.Rating}\n\n{metadata.Synopsis}");
                _log($"Saved synopsis.txt in {folder}");
            }
        }

        private void SavePosterIfMissing(string folder, MediaMetadata metadata)
        {
            // TODO: Implement download and save of poster image
            _log($"Poster saving is not yet implemented for {metadata.Title}");
        }
    }
}
