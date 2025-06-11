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
using System.Collections.ObjectModel;

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

            public async Task OrganizeFilesAsync(
            IEnumerable<PreviewItem> selectedItems,
            string destinationFolder,
            bool isOfflineMode,
            IProgress<int>? progress = null)
            {
                var selectedFiles = selectedItems
                    .SelectMany(item => GetAllFiles(item))
                    .Distinct()
                    .ToList();

            int totalFiles = selectedFiles.Count;
            int processedCount = 0;

            progress?.Report(1);

            if (totalFiles == 0)
            {
                _log("[Info] No files selected to process.");
                progress?.Report(100);
                return;
            }

            var processedEpisodes = new HashSet<string>();

            foreach (var file in selectedFiles)
            {
                try
                {
                    var parsed = FilenameParser.Parse(file);
                    if (string.IsNullOrWhiteSpace(parsed.Title))
                    {
                        _log($"[Skipped] Title could not be parsed from filename: {Path.GetFileName(file)}");
                        continue;
                    }

                    string episodeKey = $"{parsed.Title}|S{parsed.Season}|E{parsed.Episode}";
                    if (!processedEpisodes.Add(episodeKey))
                    {
                        _log($"[Skipped duplicate] {episodeKey}");
                        continue;
                    }

                    MediaMetadata? metadata;

                    if (isOfflineMode)
                    {
                        _log($"[OFFLINE] Attempting to load from cache only: {parsed.Title}");
                        metadata = await TryLoadFromCacheOnly(parsed);

                        if (metadata == null)
                        {
                            _log($"[OFFLINE] No cached metadata for '{parsed.Title}', using filename data.");
                            metadata = new MediaMetadata
                            {
                                Title = parsed.Title,
                                Season = parsed.Season,
                                Episode = parsed.Episode,
                                Type = parsed.Type
                            };
                        }
                    }
                    else
                    {
                        metadata = await EnsureMetadataInCacheAsync(parsed, isOfflineMode);

                        if (metadata == null)
                        {
                            _log($"[Failure] No metadata found for: {parsed.Title}. Skipping.");
                            continue;
                        }
                    }

                    if (metadata.Type == "Series")
                        _log($"[Organizing] {metadata.Title} S{metadata.Season:D2}E{metadata.Episode:D2} – {metadata.EpisodeTitle}");
                    else
                        _log($"[Organizing] Movie: {metadata.Title}");

                    await MoveAndOrganizeAsync(file, metadata, destinationFolder, isOfflineMode);
                }
                catch (Exception ex)
                {
                    _log($"[ERROR] Failed to process '{Path.GetFileName(file)}': {ex.Message}");
                }
                finally
                {
                    processedCount++;
                    double progressOffset = 1.0 + (processedCount / (double)totalFiles) * 99.0;
                    progress?.Report((int)progressOffset);
                }
            }
        }

        private IEnumerable<string> GetAllFiles(PreviewItem item)
        {
            if (item.IsFile && !string.IsNullOrEmpty(item.SourcePath))
            {
                yield return item.SourcePath;
            }
            else if (item.Children != null)
            {
                foreach (var child in item.Children)
                {
                    foreach (var f in GetAllFiles(child))
                        yield return f;
                }
            }
        }



        private async Task<MediaMetadata?> TryLoadFromCacheOnly(ParsedMediaInfo parsed)
        {
            string normalizedKey = NormalizeTitleKey(parsed.Title);
            int sentinel = -1;

            if (!parsed.Type.Equals("Movie", StringComparison.OrdinalIgnoreCase))
            {
                _log($"[OFFLINE-CACHE] Looking for series/anime: {parsed.Title}");

                var cached = await _cacheService.GetCachedMetadataAsync(
                    normalizedKey, parsed.Type,
                    year: parsed.Year,
                    season: parsed.Season,
                    episode: parsed.Episode);

                if (cached != null)
                {
                    cached.Type = parsed.Type;
                    return cached;
                }

                _log($"[OFFLINE-MISS] No cache hit for series/anime '{parsed.Title}'");
            }
            else
            {
                _log($"[OFFLINE-CACHE] Looking for movie: {parsed.Title}");

                var cached = await _cacheService.GetCachedMetadataAsync(
                    normalizedKey, "movie",
                    year: parsed.Year,
                    season: sentinel,
                    episode: sentinel);

                if (cached != null)
                {
                    cached.Type = "Movie";
                    return cached;
                }

                _log($"[OFFLINE-MISS] No cache hit for movie '{parsed.Title}'");
            }

            return null;
        }


        private async Task<MediaMetadata?> EnsureMetadataInCacheAsync(ParsedMediaInfo parsed, bool isOfflineMode)
        {
            string normalizedKey = NormalizeTitleKey(parsed.Title);
            int sentinel = -1;

            _log($"[ENTER] EnsureMetadataInCacheAsync: " +
                 $"Title='{parsed.Title}', Type='{parsed.Type}', Year={parsed.Year}, " +
                 $"Season={parsed.Season}, Episode={parsed.Episode}");

            if (!parsed.Type.Equals("Movie", StringComparison.OrdinalIgnoreCase))
            {
                var cached1 = await _cacheService.GetCachedMetadataAsync(
                    normalizedKey, parsed.Type, null, parsed.Season, parsed.Episode);
                if (cached1 != null)
                {
                    _log($"[CACHE-HIT1] Series/Anime '{normalizedKey}' (no-year)");
                    cached1.Type = parsed.Type;
                    return cached1;
                }

                var cached2 = await _cacheService.GetCachedMetadataAsync(
                    normalizedKey, parsed.Type, parsed.Year, parsed.Season, parsed.Episode);
                if (cached2 != null)
                {
                    _log($"[CACHE-HIT2] Series/Anime '{normalizedKey}' (year={parsed.Year})");
                    cached2.Type = parsed.Type;
                    return cached2;
                }

                _log($"[CACHE-MISS] '{normalizedKey}' not found in cache.");

                if (isOfflineMode)
                {
                    _log($"[OFFLINE] Skipping online fetch for '{parsed.Title}' due to offline mode.");
                    return null;
                }

                MediaMetadata? fetched = null;

                if (parsed.Type.Equals("Anime", StringComparison.OrdinalIgnoreCase))
                {
                    fetched = await _jikanService.GetAnimeMetadataAsync(parsed.Title, parsed.Season, parsed.Episode)
                              ?? await _tmdbService.GetSeriesMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                }
                else
                {
                    fetched = await _tmdbService.GetSeriesMetadataAsync(parsed.Title, parsed.Season, parsed.Episode)
                              ?? await _jikanService.GetAnimeMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                }

                if (fetched != null)
                {
                    fetched.Type = parsed.Type;
                    fetched.Title = parsed.Title;
                    fetched.Season = parsed.Season;
                    fetched.Episode = parsed.Episode;

                    var existAgain = await _cacheService.GetCachedMetadataAsync(
                        normalizedKey, parsed.Type, parsed.Year, parsed.Season, parsed.Episode);
                    if (existAgain == null)
                    {
                        _log($"[SAVE] Caching fetched metadata for '{normalizedKey}'");
                        await _cacheService.SaveMetadataAsync(normalizedKey, fetched);
                    }
                    else
                    {
                        _log($"[SKIP-SAVE] '{normalizedKey}' already cached.");
                    }

                    return fetched;
                }

                _log($"[FAIL] Unable to fetch Series/Anime metadata for '{normalizedKey}'");
                return null;
            }
            else
            {
                var cachedMovie = await _cacheService.GetCachedMetadataAsync(
                    normalizedKey, "movie", parsed.Year, sentinel, sentinel);
                if (cachedMovie != null)
                {
                    _log($"[CACHE-HIT] Movie '{normalizedKey}' (year={parsed.Year})");
                    cachedMovie.Type = "Movie";
                    return cachedMovie;
                }

                if (isOfflineMode)
                {
                    _log($"[OFFLINE] Skipping TMDb fetch for '{parsed.Title}' due to offline mode.");
                    return null;
                }

                _log($"[FETCH-MOVIE] Fetching movie metadata for '{parsed.Title}'");

                var movieMeta = await _tmdbService.GetMovieMetadataAsync(parsed.Title.Trim());
                if (movieMeta == null)
                {
                    _log($"[FAIL] Could not fetch TMDb metadata for '{parsed.Title}'");
                    return null;
                }

                movieMeta.Type = "Movie";
                movieMeta.Title = parsed.Title;
                if (!movieMeta.Year.HasValue && parsed.Year.HasValue)
                    movieMeta.Year = parsed.Year;
                movieMeta.Season = sentinel;
                movieMeta.Episode = sentinel;

                var existAgain = await _cacheService.GetCachedMetadataAsync(
                    normalizedKey, "movie", movieMeta.Year, sentinel, sentinel);
                if (existAgain == null)
                {
                    _log($"[SAVE] Caching fetched movie '{normalizedKey}'");
                    await _cacheService.SaveMetadataAsync(normalizedKey, movieMeta);
                }
                else
                {
                    _log($"[SKIP-SAVE] Movie '{normalizedKey}' already in cache");
                }

                return movieMeta;
            }
        }


        private async Task MoveAndOrganizeAsync(string filePath, MediaMetadata metadata, string destinationRoot, bool isOfflineMode)
        {
            _log($"Metadata.Title: {metadata.Title}, Year: {metadata.Year}");

            string targetFolder = GetTargetFolder(destinationRoot, metadata, metadata.Type);
            Directory.CreateDirectory(targetFolder);

            string metadataRootFolder = metadata.Type == "Series"
                ? Directory.GetParent(targetFolder)!.FullName
                : targetFolder;

            string extension = Path.GetExtension(filePath);
            string newFileName;

            if (metadata.Type == "Series" && metadata.Season.HasValue && metadata.Episode.HasValue)
            {
                string epNum = metadata.Episode.Value.ToString("D2");
                string epTitle = string.IsNullOrWhiteSpace(metadata.EpisodeTitle) ? "" : SanitizeFilename(ToTitleCase(metadata.EpisodeTitle));

                if (Regex.IsMatch(epTitle, @"^Episode\s*\d+$", RegexOptions.IgnoreCase))
                {
                    _log($"[Info] Skipping generic episode title '{epTitle}' in filename.");
                    epTitle = "";
                }

                newFileName = string.IsNullOrEmpty(epTitle)
                    ? $"{epNum}{extension}"
                    : $"{epNum} - {epTitle}{extension}";
            }
            else
            {
                newFileName = $"{SanitizeFilename(ToTitleCase(metadata.Title))}{extension}";
            }

            _log($"Generated filename: {newFileName} in folder {targetFolder}");

            string newFilePath = Path.Combine(targetFolder, newFileName);

            if (!File.Exists(newFilePath))
            {
                // Run blocking File.Move off the UI thread:
                await Task.Run(() => File.Move(filePath, newFilePath));
                _log($"Moved: {filePath} → {newFilePath}");
            }
            else
            {
                _log($"Skipped (already exists): {newFilePath}");
            }

            SaveSynopsis(metadataRootFolder, metadata);
            await SavePosterAsync(metadataRootFolder, metadata, isOfflineMode);
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
            // 2. Title-case the show/movie title and append year if present
            string titleBase = ToTitleCase(SanitizeFilename(metadata.Title));
            string safeTitle = metadata.Year.HasValue
                ? $"{titleBase} ({metadata.Year.Value})"
                : titleBase;

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

        public async Task<ObservableCollection<PreviewItem>> GetDryRunTreeAsync(string sourceFolder, bool isOfflineMode)
        {
            var rootItems = new ObservableCollection<PreviewItem>();
            var allFiles = Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories);

            // Avoid duplicate synopsis/poster additions
            var addedExtras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in allFiles)
            {
                var parsed = FilenameParser.Parse(file);
                if (string.IsNullOrWhiteSpace(parsed.Title))
                {
                    _log($"[DryRun] Skipping: Couldn't parse title from {file}");
                    continue;
                }

                var metadata = await EnsureMetadataInCacheAsync(parsed, isOfflineMode);
                if (metadata == null)
                {
                    metadata = new MediaMetadata
                    {
                        Title = parsed.Title,
                        Type = parsed.Type,
                        Season = parsed.Season,
                        Episode = parsed.Episode
                    };
                }

                // Build destination path
                string relativeDestination = GetRelativeDestinationPath(Path.GetFileName(file), metadata);
                string[] pathParts = Path.Combine("PREVIEW_ROOT", relativeDestination)
                                     .Split(Path.DirectorySeparatorChar)
                                     .Select(ToTitleCase)
                                     .ToArray();

                // Add media file to preview
                AddFileToPreviewTree(rootItems, pathParts, file);

                // Determine metadata root folder for extras (TV Series/<Title> or Movies/<Title>)
                // Determine metadata root folder for extras (TV Series/<Title> or Movies/<Title>)
                string metadataRoot;
                if (metadata.Type == "Series" && pathParts.Length >= 4)
                {
                    metadataRoot = Path.Combine(pathParts[0], pathParts[1], pathParts[2], pathParts[3]); // e.g. Preview_Root/TV Series/Y/Yaiba Samurai Legend
                }
                else if (metadata.Type == "Movie" && pathParts.Length >= 4)
                {
                    metadataRoot = Path.Combine(pathParts[0], pathParts[1], pathParts[2], pathParts[3]); // ✅ e.g. Preview_Root/Movies/S/Sinners
                }
                else
                {
                    _log($"[DryRun] Skipping metadata extras for {file}, invalid folder structure.");
                    continue;
                }


                // Only add extras if online and not yet added
                if (!isOfflineMode && addedExtras.Add(metadataRoot))
                {
                    if (!string.IsNullOrWhiteSpace(metadata.Synopsis))
                    {
                        AddFileToPreviewTree(rootItems,
                            Path.Combine(metadataRoot, "synopsis.txt").Split(Path.DirectorySeparatorChar),
                            null);
                    }

                    if (!string.IsNullOrWhiteSpace(metadata.PosterUrl))
                    {
                        AddFileToPreviewTree(rootItems,
                            Path.Combine(metadataRoot, "poster.jpg").Split(Path.DirectorySeparatorChar),
                            null);
                    }
                }
            }

            return rootItems;
        }




        private void AddFileToPreviewTree(ObservableCollection<PreviewItem> rootItems, string[] pathParts, string sourceFile)
        {
            var current = rootItems;
            PreviewItem? parent = null;

            foreach (var part in pathParts[..^1]) // All but the last part, which is the file name
            {
                var folderName = ToTitleCase(part);

                var existing = current.FirstOrDefault(p => p.Name == folderName && !p.IsFile);
                if (existing == null)
                {
                    existing = new PreviewItem
                    {
                        Name = folderName,
                        IsFile = false,
                        Children = new ObservableCollection<PreviewItem>()
                    };
                    current.Add(existing);
                }

                parent = existing;
                current = existing.Children;
            }

            // Last part is the file name, don't title-case it
            string fileName = Path.GetFileName(sourceFile);

            if (current != null)
            {
                current.Add(new PreviewItem
                {
                    Name = fileName,
                    SourcePath = sourceFile,
                    DestinationPath = Path.Combine(pathParts), // Use original path parts for destination
                    IsFile = true
                });
            }
        }


        public string GetRelativeDestinationPath(string sourceFilePath, MediaMetadata metadata)
        {
            string fileName;

            if (metadata.Type == "Movie")
            {
                fileName = Path.GetFileName(sourceFilePath);
                var firstLetter = GetFirstLetter(metadata.Title);
                return Path.Combine("Movies", firstLetter, metadata.Title, fileName);
            }
            else if (metadata.Type == "Series")
            {
                var safeEpisodeTitle = string.IsNullOrEmpty(metadata.EpisodeTitle)
                    ? $"Episode {metadata.Episode:D2}"
                    : $"Episode {metadata.Episode:D2} - {Sanitize(metadata.EpisodeTitle)}";

                var extension = Path.GetExtension(sourceFilePath);
                var episodeFile = $"{safeEpisodeTitle}{extension}";
                var firstLetter = GetFirstLetter(metadata.Title);

                return Path.Combine("TV Series", firstLetter, metadata.Title, $"Season {metadata.Season}", episodeFile);
            }
            else
            {
                // Fallback
                return Path.GetFileName(sourceFilePath);
            }
        }

        private string GetFirstLetter(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "#";

            char first = char.ToUpper(title[0]);
            return char.IsLetter(first) ? first.ToString() : "#";
        }

        private string Sanitize(string input)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                input = input.Replace(c, '_');
            return input;
        }


        private void SaveSynopsis(string folder, MediaMetadata metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.Synopsis))
            {
                _log($"[INFO] Synopsis is empty or whitespace for '{metadata.Title}', skipping saving synopsis.txt.");
                return;
            }

            string synopsisPath = Path.Combine(folder, "synopsis.txt");

            // Debug logs for everything before writing
            _log($"[DEBUG] Preparing to write synopsis:");
            _log($"[DEBUG] Title: {metadata.Title}");
            _log($"[DEBUG] PG Rating: {metadata.PG}");
            _log($"[DEBUG] User Score: {metadata.Rating}");
            _log($"[DEBUG] Cast: {metadata.Cast}");
            _log($"[DEBUG] Synopsis: {(string.IsNullOrWhiteSpace(metadata.Synopsis) ? "[Empty]" : metadata.Synopsis.Substring(0, Math.Min(100, metadata.Synopsis.Length)) + "...")}");

            if (!File.Exists(synopsisPath))
            {
                File.WriteAllText(synopsisPath,
                    $"{ToTitleCase(metadata.Title)} \n" +
                    $"PG Rating: {metadata.PG} \n" +
                    $"User Score: {metadata.Rating}/100 \n" +
                    $"Cast: {metadata.Cast} \n\n" +
                    $"{metadata.Synopsis}");

                _log($"Saved synopsis.txt in {folder}");
            }
            else
            {
                _log($"[DEBUG] synopsis.txt already exists in {folder}, skipping write.");
            }
        }



        private async Task SavePosterAsync(string folder, MediaMetadata metadata, bool isOfflineMode)
        {
            if (isOfflineMode)
            {
                _log("[OFFLINE] Skipping poster download.");
                return;
            }

            if (string.IsNullOrWhiteSpace(metadata.PosterUrl))
                return;

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

        private string NormalizeTitleKey(string title)
        {
            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "") // remove punctuation
                        .Trim(); // remove surrounding whitespace
        }

    }

}
