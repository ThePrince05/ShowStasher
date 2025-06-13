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
                            _log($"[Skipped] Metadata not found for: '{parsed.Title}' S{parsed.Season}E{parsed.Episode}. Moving to next.");
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

            try
            {
                // SERIES / ANIME block
                if (!parsed.Type.Equals("Movie", StringComparison.OrdinalIgnoreCase))
                {
                    // 1. Try cache lookups
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

                    // If offline, skip any fetch
                    if (isOfflineMode)
                    {
                        _log($"[OFFLINE] Skipping online fetch for '{parsed.Title}' due to offline mode.");
                        return null;
                    }

                    // 2. Try fetching from services with robust try-catch
                    MediaMetadata? fetched = null;

                    if (parsed.Type.Equals("Anime", StringComparison.OrdinalIgnoreCase))
                    {
                        // Try Jikan first
                        try
                        {
                            fetched = await _jikanService.GetAnimeMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                        }
                        catch (Exception ex)
                        {
                            _log($"[Error] Jikan fetch failed for '{parsed.Title}' S{parsed.Season}E{parsed.Episode}: {ex.Message}");
                            fetched = null;
                        }

                        // If Jikan returned null or failed, try TMDb series endpoint
                        if (fetched == null)
                        {
                            try
                            {
                                fetched = await _tmdbService.GetSeriesMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                            }
                            catch (Exception ex)
                            {
                                _log($"[Error] TMDb series fetch failed for '{parsed.Title}' S{parsed.Season}E{parsed.Episode}: {ex.Message}");
                                fetched = null;
                            }
                        }
                    }
                    else
                    {
                        // Try TMDb first
                        try
                        {
                            fetched = await _tmdbService.GetSeriesMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                        }
                        catch (Exception ex)
                        {
                            _log($"[Error] TMDb series fetch failed for '{parsed.Title}' S{parsed.Season}E{parsed.Episode}: {ex.Message}");
                            fetched = null;
                        }

                        // If TMDb returned null or failed, try Jikan
                        if (fetched == null)
                        {
                            try
                            {
                                fetched = await _jikanService.GetAnimeMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                            }
                            catch (Exception ex)
                            {
                                _log($"[Error] Jikan fetch failed for '{parsed.Title}' S{parsed.Season}E{parsed.Episode}: {ex.Message}");
                                fetched = null;
                            }
                        }
                    }

                    // 3. If fetched succeeded, cache and return
                    if (fetched != null)
                    {
                        try
                        {
                            fetched.Type = parsed.Type;
                            fetched.Title = parsed.Title;
                            fetched.Season = parsed.Season;
                            fetched.Episode = parsed.Episode;

                            // Double-check not already cached before saving
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
                        }
                        catch (Exception ex)
                        {
                            _log($"[Error] Caching metadata failed for '{normalizedKey}': {ex.Message}");
                            // Even if caching fails, we can still return fetched
                        }

                        return fetched;
                    }

                    _log($"[FAIL] Unable to fetch Series/Anime metadata for '{normalizedKey}'");
                    return null;
                }
                // MOVIE block
                else
                {
                    // 1. Try cache
                    var cachedMovie = await _cacheService.GetCachedMetadataAsync(
                        normalizedKey, "movie", parsed.Year, sentinel, sentinel);
                    if (cachedMovie != null)
                    {
                        _log($"[CACHE-HIT] Movie '{normalizedKey}' (year={parsed.Year})");
                        cachedMovie.Type = "Movie";
                        return cachedMovie;
                    }

                    // 2. Offline skip
                    if (isOfflineMode)
                    {
                        _log($"[OFFLINE] Skipping TMDb fetch for '{parsed.Title}' due to offline mode.");
                        return null;
                    }

                    _log($"[FETCH-MOVIE] Fetching movie metadata for '{parsed.Title}'");

                    // 3. Fetch with try-catch
                    MediaMetadata? movieMeta = null;
                    try
                    {
                        movieMeta = await _tmdbService.GetMovieMetadataAsync(parsed.Title.Trim());
                    }
                    catch (Exception ex)
                    {
                        _log($"[Error] TMDb movie fetch failed for '{parsed.Title}': {ex.Message}");
                        movieMeta = null;
                    }

                    if (movieMeta == null)
                    {
                        _log($"[FAIL] Could not fetch TMDb metadata for '{parsed.Title}'");
                        return null;
                    }

                    // Populate fields
                    movieMeta.Type = "Movie";
                    movieMeta.Title = parsed.Title;
                    if (!movieMeta.Year.HasValue && parsed.Year.HasValue)
                        movieMeta.Year = parsed.Year;
                    movieMeta.Season = sentinel;
                    movieMeta.Episode = sentinel;

                    // 4. Cache it
                    try
                    {
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
                    }
                    catch (Exception ex)
                    {
                        _log($"[Error] Caching movie metadata failed for '{normalizedKey}': {ex.Message}");
                    }

                    return movieMeta;
                }
            }
            catch (Exception ex)
            {
                // Catch-any unexpected in the overall logic
                _log($"[Error] Unexpected error in EnsureMetadataInCacheAsync for '{parsed.Title}': {ex.Message}");
                return null;
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
                newFileName = GenerateEpisodeFilename(metadata, extension);
            }
            else
            {
                newFileName = $"{SanitizeFilename(ToTitleCase(metadata.Title))}{extension}";
            }

            _log($"Generated filename: {newFileName} in folder {targetFolder}");

            string newFilePath = Path.Combine(targetFolder, newFileName);

            if (!File.Exists(newFilePath))
            {
                await Task.Run(() => File.Move(filePath, newFilePath));
                _log($"Moved: {filePath} → {newFilePath}");
            }
            else
            {
                _log($"Skipped (already exists): {newFilePath}");
            }

            SaveSynopsis(metadataRootFolder, metadata, isOfflineMode);

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

            var addedExtras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in allFiles)
            {
                string fileName = Path.GetFileName(file);
                
                // Skip extras from original source
                if (fileName.Equals("poster.jpg", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("synopsis.txt", StringComparison.OrdinalIgnoreCase))
                {
                    _log($"[DryRun] Skipping existing extra file: {fileName}");
                    continue;
                }

                var parsed = FilenameParser.Parse(file);
                if (string.IsNullOrWhiteSpace(parsed.Title))
                {
                    _log($"[DryRun] Skipping: Couldn't parse title from {file}");
                    continue;
                }

                MediaMetadata? metadata = null;

                if (isOfflineMode)
                {
                    metadata = await TryLoadFromCacheOnly(parsed);

                    if (metadata == null)
                    {
                        _log($"[DryRun-Offline] No cached metadata for '{parsed.Title}', using fallback.");
                        metadata = new MediaMetadata
                        {
                            Title = parsed.Title,
                            Type = parsed.Type,
                            Season = parsed.Season,
                            Episode = parsed.Episode
                        };
                    }
                }
                else
                {
                    metadata = await EnsureMetadataInCacheAsync(parsed, isOfflineMode);
                    if (metadata == null)
                    {
                        _log($"[DryRun] Skipping '{parsed.Title}' – failed to fetch metadata.");
                        continue; // Don't include in TreeView
                    }
                }

                // Compute destination relative path
                string extension = Path.GetExtension(file);
                string renamedFile;

                if (metadata.Type == "Series" && metadata.Season.HasValue && metadata.Episode.HasValue)
                {
                    renamedFile = GenerateEpisodeFilename(metadata, extension);
                }
                else
                {
                    renamedFile = $"{SanitizeFilename(ToTitleCase(metadata.Title))}{extension}";
                }

                string relativeDestination = GetRelativeDestinationPath(renamedFile, metadata);

                string previewRootPrefix = "PREVIEW DESTINATION";

                string fullRelativePath = Path.Combine(previewRootPrefix, relativeDestination);
                var pathParts = fullRelativePath.Split(Path.DirectorySeparatorChar);

                // Extract clean base names
                string renamedBase = Path.GetFileNameWithoutExtension(Path.GetFileName(relativeDestination));

                // Add media file to preview
                AddFileToPreviewTree(rootItems, pathParts, file, renamedBase);

                // Prepare metadata root folder path
                string metadataRoot;
                if (metadata.Type == "Series" && pathParts.Length >= 4)
                {
                    metadataRoot = Path.Combine(pathParts[0], pathParts[1], pathParts[2], pathParts[3]);
                }
                else if (metadata.Type == "Movie" && pathParts.Length >= 4)
                {
                    metadataRoot = Path.Combine(pathParts[0], pathParts[1], pathParts[2], pathParts[3]);
                }
                else
                {
                    _log($"[DryRun] Skipping metadata extras for {file}, invalid folder structure.");
                    continue;
                }

                // Only add metadata files once per movie/series root
                if (!isOfflineMode && !addedExtras.Contains(metadataRoot))
                {
                    addedExtras.Add(metadataRoot);

                    var synopsisParts = metadataRoot
                        .Split(Path.DirectorySeparatorChar)
                        .Select(ToTitleCase)
                        .Append("synopsis.txt")
                        .ToArray();

                    AddFileToPreviewTree(rootItems, synopsisParts, null);

                    var posterParts = metadataRoot
                        .Split(Path.DirectorySeparatorChar)
                        .Select(ToTitleCase)
                        .Append("poster.jpg")
                        .ToArray();

                    AddFileToPreviewTree(rootItems, posterParts, null);
                }
            }

            return rootItems;
        }

        private string GenerateEpisodeFilename(MediaMetadata metadata, string extension)
        {
            // Ensure Episode number exists
            if (!metadata.Season.HasValue || !metadata.Episode.HasValue)
                throw new InvalidOperationException("GenerateEpisodeFilename called without Season/Episode");

            // Format episode number
            string epNum = metadata.Episode.Value.ToString("D2");

            // Normalize title: Title-case and sanitize
            string rawTitle = metadata.EpisodeTitle ?? "";
            string epTitle = string.IsNullOrWhiteSpace(rawTitle)
                ? ""
                : SanitizeFilename(ToTitleCase(rawTitle));

            // Detect generic patterns: "Episode 1", "Ep 1", "E01", case-insensitive
            if (!string.IsNullOrEmpty(epTitle))
            {
                // Regex: ^(Episode|Ep|E)\s*\d+$
                if (Regex.IsMatch(epTitle, @"^(Episode|Ep|E)\s*\d+$", RegexOptions.IgnoreCase))
                {
                    _log($"[Info] Skipping generic episode title '{epTitle}' in filename.");
                    epTitle = "";
                }
            }

            // Build filename
            return string.IsNullOrEmpty(epTitle)
                ? $"{epNum}{extension}"
                : $"{epNum} - {epTitle}{extension}";
        }



        private void AddFileToPreviewTree(
        ObservableCollection<PreviewItem> rootItems,
        string[] pathParts,
        string? sourceFile,
        string? renamedFilename = null)
            {
                var current = rootItems;
                var accumulatedPathSegments = new List<string>();

                for (int i = 0; i < pathParts.Length; i++)
                {
                    bool isLast = (i == pathParts.Length - 1);
                    string rawPart = pathParts[i];

                    string nodeName;
                    string folderOrFileForPath;

                    if (isLast)
                    {
                        if (sourceFile != null)
                        {
                            string originalName = Path.GetFileNameWithoutExtension(sourceFile);
                            string extension = Path.GetExtension(sourceFile);
                            string displayRenamedBase = renamedFilename ?? originalName;
                            nodeName = $"{originalName} → {displayRenamedBase}";
                            folderOrFileForPath = displayRenamedBase + extension;
                        }
                        else
                        {
                            nodeName = rawPart;
                            folderOrFileForPath = rawPart;
                        }
                    }
                    else
                    {
                        string folderName = ToTitleCase(rawPart);
                        nodeName = folderName;
                        folderOrFileForPath = folderName;
                    }

                    accumulatedPathSegments.Add(folderOrFileForPath);
                    string destinationPath = Path.Combine(accumulatedPathSegments.ToArray());

                    var existing = current.FirstOrDefault(p =>
                        string.Equals(p.Name, nodeName, StringComparison.OrdinalIgnoreCase)
                        && p.IsFile == isLast);

                    bool shouldShowCheckbox = (!isLast && i == 3);
                    // i==3 and not last: the folder node at depth 3: e.g., "Sinners" or "Yaiba Samurai Legend"

                    if (existing == null)
                    {
                        existing = new PreviewItem
                        {
                            Name = nodeName,
                            OriginalName = isLast && sourceFile != null
                                           ? Path.GetFileNameWithoutExtension(sourceFile)
                                           : null,
                            RenamedName = isLast && sourceFile != null
                                          ? (renamedFilename ?? Path.GetFileNameWithoutExtension(sourceFile))
                                          : null,
                            IsFile = isLast,
                            IsFolder = !isLast,
                            SourcePath = isLast ? sourceFile : null,
                            DestinationPath = destinationPath,
                            Children = new ObservableCollection<PreviewItem>(),
                            ShowCheckbox = shouldShowCheckbox,
                            IsChecked = true  // default; user can uncheck later
                        };
                        current.Add(existing);
                    }
                    else
                    {
                        // Update flags/properties in case reused. Also update ShowCheckbox each time.
                        existing.IsFile = isLast;
                        existing.IsFolder = !isLast;
                        existing.DestinationPath = destinationPath;

                        existing.ShowCheckbox = shouldShowCheckbox;

                        if (isLast)
                        {
                            existing.SourcePath = sourceFile;
                            existing.OriginalName = Path.GetFileNameWithoutExtension(sourceFile!);
                            existing.RenamedName = renamedFilename ?? Path.GetFileNameWithoutExtension(sourceFile!);
                            // Often for files we do not show checkbox, but ShowCheckbox is false for isLast anyway
                        }
                        else
                        {
                            existing.OriginalName = null;
                            existing.RenamedName = null;
                            existing.SourcePath = null;
                        }
                    }

                    current = existing.Children;
                }
        }



        public string GetRelativeDestinationPath(string sourceFilePath, MediaMetadata metadata)
        {
            string extension = Path.GetExtension(sourceFilePath) ?? "";

            if (metadata.Type == "Movie")
            {
                // Sanitize movie title
                string safeTitle = ToTitleCase(Sanitize(metadata.Title));

                // Try to extract the year
                string yearPart = "";
                if (TryGetYearFromMetadata(metadata, out int metaYear) && metaYear > 0)
                {
                    yearPart = metaYear.ToString();
                }
                else
                {
                    // Fallback: Extract from original filename
                    string originalBase = Path.GetFileNameWithoutExtension(sourceFilePath);
                    var yearMatch = Regex.Match(originalBase, @"\b(19|20)\d{2}\b");
                    if (yearMatch.Success)
                    {
                        yearPart = yearMatch.Value;
                    }
                }

                // Folder: Sinners (2025)
                string folderName = string.IsNullOrEmpty(yearPart)
                    ? safeTitle
                    : $"{safeTitle} ({yearPart})";

                // Filename: Sinners.mp4
                string newFileName = $"{safeTitle}{extension}";

                var firstLetter = GetFirstLetter(safeTitle);
                return Path.Combine("Movies", firstLetter, folderName, newFileName);
            }
            else if (metadata.Type == "Series")
            {
                string epNum = metadata.Episode?.ToString("D2") ?? "00";
                string episodeTitle = metadata.EpisodeTitle ?? "";
                bool isGeneric = Regex.IsMatch(episodeTitle, @"^Episode\s*\d+$", RegexOptions.IgnoreCase);

                string safeEpisodeTitle = isGeneric || string.IsNullOrWhiteSpace(episodeTitle)
                    ? epNum
                    : $"{epNum} - {Sanitize(episodeTitle)}";

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

        private bool TryGetYearFromMetadata(MediaMetadata metadata, out int year)
        {
            year = 0;
            if (!string.IsNullOrEmpty(metadata.Year.ToString()))
            {
                var match = Regex.Match(metadata.Year.ToString(), @"\b(19|20)\d{2}\b");
                if (match.Success && int.TryParse(match.Value, out year))
                {
                    return true;
                }
            }
            return false;
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


        private void SaveSynopsis(string folder, MediaMetadata metadata, bool isOfflineMode)
        {
            if (isOfflineMode)
            {
                _log($"[OFFLINE] Skipping synopsis save for '{metadata.Title}'");
                return;
            }

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
