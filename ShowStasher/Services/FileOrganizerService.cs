using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ShowStasher.Helpers;
using System.Net.Http;
using System.Globalization;

namespace ShowStasher.Services
{
    public class FileOrganizerService
    {
        private readonly Action<string> _log;
        private readonly TMDbService _tmdbService;
        private readonly JikanService _jikanService;
        private readonly MetadataCacheService _cacheService;

        public FileOrganizerService(Action<string> log, TMDbService tmdbService, JikanService jikanService, MetadataCacheService cacheService)
        {
            _log = log;
            _tmdbService = tmdbService;
            _jikanService = jikanService;
            _cacheService = cacheService;
        }

        public async Task OrganizeFilesAsync(string sourceFolder, string destinationFolder)
        {
            var allowedExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov" };
            var files = Directory.GetFiles(sourceFolder)
                                 .Where(f => allowedExtensions.Contains(Path.GetExtension(f).ToLower()))
                                 .ToList();

            var processed = new HashSet<string>();

            foreach (var file in files)
            {
                var parsed = FilenameParser.Parse(file);
                if (string.IsNullOrWhiteSpace(parsed.Title))
                    continue;

                string episodeKey = $"{parsed.Title}|S{parsed.Season}|E{parsed.Episode}";
                if (!processed.Add(episodeKey))
                {
                    _log($"Skipped duplicate: {episodeKey}");
                    continue;
                }

                // Fetch & cache if needed, but only use cached metadata
                var metadata = await EnsureMetadataInCacheAsync(parsed);
                if (metadata == null)
                {
                    _log($"No metadata found even after attempting to fetch: {parsed.Title}");
                    continue;
                }

                if (metadata.Type == "Series")
                    _log($"Preparing to save episode: {metadata.Title} S{metadata.Season:D2}E{metadata.Episode:D2} – {metadata.EpisodeTitle}");
                else
                    _log($"Preparing to save movie: {metadata.Title}");

                await MoveAndOrganizeAsync(file, metadata, destinationFolder);
            }
        }

        private async Task<MediaMetadata?> EnsureMetadataInCacheAsync(ParsedFileInfo parsed)
        {
            // 1. Non-movies: try cache first
            if (!parsed.Type.Equals("Movie", StringComparison.OrdinalIgnoreCase))
            {
                var cached = await _cacheService.GetCachedMetadataAsync(
                    parsed.Title, parsed.Type, parsed.Season, parsed.Episode);
                if (cached != null)
                {
                    cached.Type = parsed.Type;
                    return cached;
                }
            }

            // 2. Movies: same as before
            if (parsed.Type.Equals("Movie", StringComparison.OrdinalIgnoreCase))
            {
                var movieMeta = await _tmdbService.GetMovieMetadataAsync(parsed.Title);
                if (movieMeta != null)
                {
                    movieMeta.Type = "Movie";
                    // use parser’s title as the cache key
                    movieMeta.Title = parsed.Title;
                    await _cacheService.SaveMetadataAsync(movieMeta);
                }
                return movieMeta;
            }

            // 3. Series/Anime fetch
            MediaMetadata? fetched = null;
            if (parsed.Type.Equals("Anime", StringComparison.OrdinalIgnoreCase))
            {
                fetched = await _jikanService.GetAnimeMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                if (fetched == null || string.IsNullOrWhiteSpace(fetched.EpisodeTitle))
                {
                    _log($"Jikan failed → fallback to TMDb for '{parsed.Title}'");
                    fetched = await _tmdbService.GetSeriesMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                }
            }
            else // Series
            {
                fetched = await _tmdbService.GetSeriesMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                if (fetched == null || string.IsNullOrWhiteSpace(fetched.EpisodeTitle))
                {
                    _log($"TMDb failed → fallback to Jikan for '{parsed.Title}'");
                    fetched = await _jikanService.GetAnimeMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                }
            }

            // 4. Force the key fields *and* the parser’s title, then save
            if (fetched != null)
            {
                fetched.Type = parsed.Type;
                fetched.Season = parsed.Season;
                fetched.Episode = parsed.Episode;
                // **IMPORTANT**: Use the parser’s sanitized title as the cache key
                fetched.Title = parsed.Title;

                await _cacheService.SaveMetadataAsync(fetched);
                _log($"Saving metadata to cache: {fetched.Title} S{fetched.Season}E{fetched.Episode}");
            }

            // 5. Now this lookup must succeed
            var final = await _cacheService.GetCachedMetadataAsync(
                parsed.Title, parsed.Type, parsed.Season, parsed.Episode);
            if (final != null)
                final.Type = parsed.Type;
            else
                _log($"Failed to fetch or cache metadata for: {parsed.Title} S{parsed.Season}E{parsed.Episode}");

            return final;
        }


        private async Task MoveAndOrganizeAsync(string filePath, MediaMetadata metadata, string destinationRoot)
        {
            // 1. Build destination path for file (includes Season folder or Movie folder)
            string targetFolder = GetTargetFolder(destinationRoot, metadata, metadata.Type);
            Directory.CreateDirectory(targetFolder);

            // 2. Determine metadataRootFolder:
            //    - For Series: one level up from the season folder
            //    - For Movies: the movie folder itself
            string metadataRootFolder;
            if (metadata.Type == "Series")
            {
                // targetFolder: .../Series/A/Show Title/Season 1
                metadataRootFolder = Directory.GetParent(targetFolder)!.FullName;
            }
            else
            {
                // targetFolder: .../Movies/N/Movie Title
                metadataRootFolder = targetFolder;
            }

            // 3. Determine new filename like: 06 - Episode Title.mkv or Title.mkv
            string extension = Path.GetExtension(filePath);
            string newFileName;

            if (metadata.Type == "Series" && metadata.Season.HasValue && metadata.Episode.HasValue)
            {
                string epNum = metadata.Episode.Value.ToString("D2");

                // Sanitize episode title
                string epTitle = string.IsNullOrWhiteSpace(metadata.EpisodeTitle)
                 ? ""
                 : SanitizeFilename(ToTitleCase(metadata.EpisodeTitle));

                newFileName = string.IsNullOrEmpty(epTitle)
                    ? $"{epNum}{extension}"
                    : $"{epNum} - {epTitle}{extension}";

            }
            else
            {
                // For movies or fallback
                newFileName = $"{SanitizeFilename(ToTitleCase(metadata.Title))}{extension}";
            }

            _log($"Generated filename: {newFileName} in folder {targetFolder}");

            // 4. Move file
            string newFilePath = Path.Combine(targetFolder, newFileName);

            if (!File.Exists(newFilePath))
            {
                File.Move(filePath, newFilePath);
                _log($"Moved: {filePath} → {newFilePath}");
            }
            else
            {
                _log($"Skipped (already exists): {newFilePath}");
            }

            // 5. Save synopsis and poster in actual content folder
            SaveSynopsisIfMissing(metadataRootFolder, metadata);
            await SavePosterIfMissingAsync(metadataRootFolder, metadata);

            // 6. Log folder usage (e.g., "Series: C\Common Side Effects" or "Movie: N\Nope")
            LogFolderUsage(metadata.Type, metadataRootFolder);
        }



        private void LogFolderUsage(string type, string folderPath)
        {
            if (type == "Anime") type = "Series"; // Normalize for logging
            string logPath = Path.Combine(AppContext.BaseDirectory, $"{type}_folders.log");
            File.AppendAllText(logPath, folderPath + Environment.NewLine);
        }


        private static string SanitizeFilename(string name)
        {
            // Replace invalid filename chars with space
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, ' ');

            // Collapse multiple spaces/underscores
            name = Regex.Replace(name, @"[_\s]{2,}", " ").Trim();

            return name;
        }

        private string GetTargetFolder(string root, MediaMetadata metadata, string type)
        {
            // 1. Decide final “kind” (case-insensitive checks):
            //    - Episodic anime → Series
            //    - Anime movies (no episode) → Movie
            //    - Everything else → respect incoming type
            string kind = type;
            if (type.Equals("Anime", StringComparison.OrdinalIgnoreCase))
            {
                kind = metadata.Episode.HasValue
                    ? "Series"
                    : "Movie";
            }

            // 2. Title-case the show/movie title for folders & filenames
            string safeTitle = ToTitleCase(SanitizeFilename(metadata.Title));
            string initial = safeTitle.Length > 0
                ? safeTitle[0].ToString(CultureInfo.InvariantCulture)
                : "_";

            // 3. Use case-insensitive comparisons for folder logic
            if (kind.Equals("Movie", StringComparison.OrdinalIgnoreCase))
            {
                // Movies/FirstLetter/Title
                return Path.Combine(root, "Movies", initial, safeTitle);
            }
            else if (kind.Equals("Series", StringComparison.OrdinalIgnoreCase))
            {
                // TV Series/FirstLetter/Title/Season X
                string seasonFolder = metadata.Season.HasValue
                    ? $"Season {metadata.Season.Value}"
                    : "Season 1";
                return Path.Combine(root, "TV Series", initial, safeTitle, seasonFolder);
            }
            else
            {
                // Fallback
                return Path.Combine(root, "Unknown", safeTitle);
            }
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

        private async Task SavePosterIfMissingAsync(string folder, MediaMetadata metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.PosterUrl)) return;

            string posterPath = Path.Combine(folder, "poster.jpg");
            if (!File.Exists(posterPath))
                await _tmdbService.DownloadPosterAsync(metadata.PosterUrl, posterPath);
        }
        private static string ToTitleCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var culture = CultureInfo.CurrentCulture;
            return culture.TextInfo.ToTitleCase(input.ToLower());
        }


    }



}
