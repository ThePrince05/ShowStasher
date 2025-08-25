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
using Microsoft.Extensions.Logging;
using static ShowStasher.MVVM.ViewModels.MainViewModel;

namespace ShowStasher.Services
{
    public class FileOrganizerService
    {
        private readonly Action<string, AppLogLevel> _log;
        private readonly TMDbService _tmdbService;
        private readonly JikanService _jikanService;
        private readonly SqliteDbService _dbService;
        private readonly DisplayTitleResolverService _displayTitleResolver;


        public FileOrganizerService(Action<string, AppLogLevel> log, TMDbService tmdbService, JikanService jikanService, SqliteDbService cacheService, DisplayTitleResolverService displayTitleResolver)
        {
            _log = log;
            _tmdbService = tmdbService;
            _jikanService = jikanService;
            _dbService = cacheService;
            _displayTitleResolver = displayTitleResolver;
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
                _log("No files selected for organization.", AppLogLevel.Warning);
                progress?.Report(100);
                return;
            }

            var processedEpisodes = new HashSet<string>();
            _log($"Organizing {totalFiles} files...", AppLogLevel.Action);

            await Task.Run(async () =>
            {
                foreach (var file in selectedFiles)
                {
                    try
                    {
                        var parsed = FilenameParser.Parse(file);
                        if (string.IsNullOrWhiteSpace(parsed.Title))
                        {
                            _log($"Skipped: Could not parse title from '{Path.GetFileName(file)}'.", AppLogLevel.Warning);
                            continue;
                        }

                        string episodeKey = parsed.Type == "Series"
                        ? $"{parsed.Title}|S{parsed.Season ?? -1:00}|E{parsed.Episode ?? -1:00}"
                        : $"{parsed.Title}|Movie";

                        if (!processedEpisodes.Add(episodeKey))
                        {
                            _log($"Skipped duplicate episode: {episodeKey}", AppLogLevel.Debug);
                            continue;
                        }

                        MediaMetadata? metadata;

                        if (isOfflineMode)
                        {
                            _log($"Offline mode: Loading metadata for '{parsed.Title}' from cache...", AppLogLevel.Debug);
                            metadata = await TryLoadFromCacheOnly(parsed);

                            if (metadata == null)
                            {
                                _log($"No cached metadata found for '{parsed.Title}', falling back to filename data.", AppLogLevel.Warning);
                                metadata = new MediaMetadata
                                {
                                    LookupKey = parsed.Title,
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
                                _log($"Skipped: Metadata not found for '{parsed.Title}' S{parsed.Season}E{parsed.Episode}.", AppLogLevel.Warning);
                                continue;
                            }
                        }

                        if (metadata.Type == "Series")
                        {
                            _log($"Organizing: {metadata.Title} S{metadata.Season:D2}E{metadata.Episode:D2} – {metadata.EpisodeTitle}", AppLogLevel.Action);
                        }
                        else
                        {
                            _log($"Organizing movie: {metadata.Title}", AppLogLevel.Action);
                        }

                        await MoveAndOrganizeAsync(file, metadata, destinationFolder, isOfflineMode);
                    }
                    catch (Exception ex)
                    {
                        _log($"Error processing '{Path.GetFileName(file)}': {ex.Message}", AppLogLevel.Error);
                    }
                    finally
                    {
                        processedCount++;
                        double progressOffset = 1.0 + (processedCount / (double)totalFiles) * 99.0;
                        progress?.Report((int)progressOffset);
                    }
                }
            });

            _log("Organization complete.", AppLogLevel.Success);
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
            if (string.IsNullOrWhiteSpace(parsed.Title))
            {
                _log("Parsed title is null/empty during cache lookup — skipping", AppLogLevel.Warning);
                return null;
            }

            string normalizedKey = NormalizeTitleKey(parsed.Title);
            int sentinel = -1;

            if (!parsed.Type.Equals("Movie", StringComparison.OrdinalIgnoreCase))
            {
                _log($"Offline cache lookup for series/anime: {parsed.Title}", AppLogLevel.Debug);

                var cached = await _dbService.GetCachedMetadataAsync(
                    normalizedKey, parsed.Type,
                    year: parsed.Year,
                    season: parsed.Season,
                    episode: parsed.Episode);

                if (cached != null)
                {
                    cached.Type = parsed.Type;
                    return cached;
                }

                _log($"No cached metadata found for series/anime '{parsed.Title}'", AppLogLevel.Warning);
            }
            else
            {
                _log($"Offline cache lookup for movie: {parsed.Title}", AppLogLevel.Debug);

                var cached = await _dbService.GetCachedMetadataAsync(
                    normalizedKey, "movie",
                    year: parsed.Year,
                    season: sentinel,
                    episode: sentinel);

                if (cached != null)
                {
                    cached.Type = "Movie";
                    return cached;
                }

                _log($"No cached metadata found for movie '{parsed.Title}'", AppLogLevel.Warning);
            }

            return null;
        }


        private async Task<MediaMetadata?> EnsureMetadataInCacheAsync(ParsedMediaInfo parsed, bool isOfflineMode)
        {
            if (string.IsNullOrWhiteSpace(parsed.Title))
            {
                _log("Parsed title is null/empty during metadata ensure — skipping", AppLogLevel.Warning);
                return null;
            }

            string normalizedKey = NormalizeTitleKey(parsed.Title);
            _log($"Ensuring metadata: ParsedTitle='{parsed.Title}', Type='{parsed.Type}', Year={parsed.Year}, Season={parsed.Season}, Episode={parsed.Episode}", AppLogLevel.Debug);

            try
            {
                // --- check cache first ---
                var cached = await _dbService.GetCachedMetadataAsync(normalizedKey, parsed.Type, parsed.Year, parsed.Season, parsed.Episode);
                if (cached != null)
                {
                    cached.Type = parsed.Type;
                    cached.LookupKey = normalizedKey;
                    _log($"Cache hit for key='{normalizedKey}' -> using cached display title '{cached.Title}'", AppLogLevel.Success);
                    return cached;
                }

                if (isOfflineMode)
                {
                    _log($"Offline mode: No cache found for '{parsed.Title}' (key='{normalizedKey}')", AppLogLevel.Info);
                    return null;
                }

                // Resolve display title (keep this; it may just return the filename if nothing found)
                // OPTIONAL: consider passing parsed.Type to ResolveDisplayTitleAsync so it doesn't try the wrong type.
                string displayTitle = await _displayTitleResolver.ResolveDisplayTitleAsync(parsed, isOfflineMode);

                MediaMetadata? fetched = null;

                // Branch by parsed.Type — this prevents Movie filenames from triggering Series lookups
                if (parsed.Type.Equals("Anime", StringComparison.OrdinalIgnoreCase))
                {
                    try { fetched = await _jikanService.GetAnimeMetadataAsync(parsed.Title, parsed.Season, parsed.Episode); }
                    catch (Exception ex) { _log($"Jikan fetch failed: {parsed.Title} – {ex.Message}", AppLogLevel.Error); }

                    if (fetched == null)
                    {
                        try { fetched = await _tmdbService.GetSeriesMetadataAsync(parsed.Title, parsed.Season, parsed.Episode); }
                        catch (Exception ex) { _log($"TMDb fallback failed: {parsed.Title} – {ex.Message}", AppLogLevel.Error); }
                    }
                }
                else if (parsed.Type.Equals("Series", StringComparison.OrdinalIgnoreCase))
                {
                    try { fetched = await _tmdbService.GetSeriesMetadataAsync(parsed.Title, parsed.Season, parsed.Episode); }
                    catch (Exception ex) { _log($"TMDb series fetch failed: {parsed.Title} – {ex.Message}", AppLogLevel.Error); }

                    // Optionally: fallback to anime (Jikan) only if you think some series are anime
                    if (fetched == null)
                    {
                        try { fetched = await _jikanService.GetAnimeMetadataAsync(parsed.Title, parsed.Season, parsed.Episode); }
                        catch (Exception ex) { _log($"Jikan fallback failed: {parsed.Title} – {ex.Message}", AppLogLevel.Error); }
                    }
                }
                else // treat as Movie (default) — do NOT look up series
                {
                    try { fetched = await _tmdbService.GetMovieMetadataAsync(parsed.Title, parsed.Year); }
                    catch (Exception ex) { _log($"TMDb movie fetch failed: {parsed.Title} – {ex.Message}", AppLogLevel.Error); }

                    // Optional fallback: if movie fetch fails, you can still try series only if you explicitly want that.
                    // But per your request, we avoid series lookups here.
                }

                if (fetched != null)
                {
                    // attach parsed context
                    fetched.Type = parsed.Type;
                    fetched.Season = parsed.Season;
                    fetched.Episode = parsed.Episode;
                    fetched.LookupKey = normalizedKey;
                    if (!string.IsNullOrWhiteSpace(displayTitle) && string.IsNullOrWhiteSpace(fetched.Title))
                        fetched.Title = displayTitle;

                    // Save/cache under normalized key if not present
                    var exist = await _dbService.GetCachedMetadataAsync(normalizedKey, parsed.Type, parsed.Year, parsed.Season, parsed.Episode);
                    if (exist == null)
                    {
                        _log($"Caching new metadata under key='{normalizedKey}' (DisplayTitle='{fetched.Title ?? displayTitle}')", AppLogLevel.Action);
                        await _dbService.SaveMetadataAsync(normalizedKey, fetched);
                    }
                    else
                    {
                        _log($"Metadata already cached for key='{normalizedKey}'", AppLogLevel.Info);
                    }

                    return fetched;
                }

                // If fetch failed, create minimal metadata (so organizer still has a nice title)
                if (!string.IsNullOrWhiteSpace(displayTitle))
                {
                    var minimal = new MediaMetadata
                    {
                        LookupKey = normalizedKey,
                        Title = displayTitle,
                        Type = parsed.Type,
                        Year = parsed.Year,
                        Season = parsed.Season,
                        Episode = parsed.Episode
                    };

                    try
                    {
                        _log($"Caching minimal metadata under key='{normalizedKey}' (DisplayTitle='{displayTitle}')", AppLogLevel.Action);
                        await _dbService.SaveMetadataAsync(normalizedKey, minimal);
                    }
                    catch (Exception ex)
                    {
                        _log($"Failed to cache minimal metadata for '{parsed.Title}' – {ex.Message}", AppLogLevel.Error);
                    }

                    return minimal;
                }

                _log($"Failed to fetch metadata for '{parsed.Title}' and no display title resolved.", AppLogLevel.Error);
                return null;
            }
            catch (Exception ex)
            {
                _log($"Unexpected error in EnsureMetadataInCacheAsync for '{parsed.Title}': {ex.Message}", AppLogLevel.Error);
                return null;
            }
        }



        private async Task MoveAndOrganizeAsync(string filePath, MediaMetadata metadata, string destinationRoot, bool isOfflineMode)
        {
            _log($"Organizing file using metadata: Title='{metadata.Title}', Year={metadata.Year}", AppLogLevel.Debug);

            string targetFolder = GetTargetFolder(destinationRoot, metadata, metadata.Type, isOfflineMode);
            Directory.CreateDirectory(targetFolder);

            string metadataRootFolder = metadata.Type == "Series"
                ? Directory.GetParent(targetFolder)!.FullName
                : targetFolder;

            string extension = Path.GetExtension(filePath);
            string newFileName = metadata.Type == "Series" && metadata.Season.HasValue && metadata.Episode.HasValue
                ? GenerateEpisodeFilename(metadata, extension)
                : $"{SanitizeFilename(ToTitleCase(isOfflineMode && !string.IsNullOrWhiteSpace(metadata.LookupKey) ? metadata.LookupKey : metadata.Title))}{extension}";

            string newFilePath = Path.Combine(targetFolder, newFileName);

            _log($"Generated filename: '{newFileName}' in folder: '{targetFolder}'", AppLogLevel.Info);

            // Move file if the destination doesn't already exist
            if (!File.Exists(newFilePath))
            {
                await Task.Run(() => File.Move(filePath, newFilePath));
                _log($"Moved file:\nFROM: {filePath}\nTO:   {newFilePath}", AppLogLevel.Success);

                // Log the file movement to the history table
                await LogFileMoveToHistoryAsync(filePath, newFilePath);
            }
            else
            {
                _log($"Skipped moving: Target already exists → '{newFilePath}'", AppLogLevel.Warning);
            }

            // Save supporting content (if available)
            SaveSynopsis(metadataRootFolder, metadata, isOfflineMode);
            await SavePosterAsync(metadataRootFolder, metadata, isOfflineMode);
        }



        private async Task LogFileMoveToHistoryAsync(string originalFilePath, string newFilePath)
        {
            var historyEntry = new MoveHistory
            {
                OriginalFileName = Path.GetFileName(originalFilePath),
                NewFileName = Path.GetFileName(newFilePath),
                SourcePath = Path.GetDirectoryName(originalFilePath)!,
                DestinationPath = Path.GetDirectoryName(newFilePath)!,
                MovedAt = DateTime.UtcNow
            };

            await _dbService.SaveMoveHistoryAsync(historyEntry);
        }



        private static string SanitizeFilename(string name)
        {
            // Replace invalid filename chars with space, but allow apostrophes
            foreach (char c in Path.GetInvalidFileNameChars())
                if (c != '\'')
                    name = name.Replace(c, ' ');
            // Collapse multiple spaces/underscores
            name = Regex.Replace(name, @"[_\s]{2,}", " ").Trim();
            return name;
        }

        private string GetTargetFolder(string root, MediaMetadata metadata, string type, bool isOfflineMode)
        {
            string kind = type;

            // Use filename in offline mode, server name in online mode
            string titleBase = isOfflineMode && !string.IsNullOrWhiteSpace(metadata.LookupKey)
                ? ToTitleCase(SanitizeFilename(metadata.LookupKey))
                : ToTitleCase(SanitizeFilename(metadata.Title));

            string safeTitle = metadata.Year.HasValue
                ? $"{titleBase} ({metadata.Year.Value})"
                : titleBase;

            string initial = safeTitle.Length > 0
                ? safeTitle[0].ToString(CultureInfo.InvariantCulture)
                : "_";

            // If first char is a digit, override initial to "1 - 1000"
            if (char.IsDigit(initial[0]))
            {
                initial = "1 - 1000";
            }

            if (kind.Equals("Movie", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(root, "Movies", initial, safeTitle);
            }
            else if (kind.Equals("Series", StringComparison.OrdinalIgnoreCase))
            {
                string seasonFolder = metadata.Season.HasValue
                    ? $"Season {metadata.Season.Value}"
                    : "Season 1";
                return Path.Combine(root, "TV Series", initial, safeTitle, seasonFolder);
            }
            else
            {
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

                // Skip existing extra files
                if (fileName.Equals("poster.jpg", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("synopsis.txt", StringComparison.OrdinalIgnoreCase))
                {
                    _log($"Skipped existing extra file '{fileName}'", AppLogLevel.Debug);
                    continue;
                }

                var parsed = FilenameParser.Parse(file);
                if (string.IsNullOrWhiteSpace(parsed.Title))
                {
                    _log($"Could not parse title from '{file}'", AppLogLevel.Warning);
                    continue;
                }

                MediaMetadata? metadata;

                if (isOfflineMode)
                {
                    metadata = await TryLoadFromCacheOnly(parsed) ?? new MediaMetadata
                    {
                        Title = parsed.Title,
                        LookupKey = parsed.Title,
                        Type = parsed.Type,
                        Season = parsed.Season,
                        Episode = parsed.Episode,
                        Year = parsed.Year
                    };
                }
                else
                {
                    metadata = await EnsureMetadataInCacheAsync(parsed, isOfflineMode);
                    if (metadata == null)
                    {
                        _log($"Failed to fetch metadata for '{parsed.Title}' — skipped from preview", AppLogLevel.Warning);
                        continue;
                    }
                }

                string extension = Path.GetExtension(file);

                // --- Build folder path and file name for the preview tree ---
                string relativeFolderPath;
                string fileNameForPreview;
                string extrasFolderPath = null; // folder for poster/synopsis

                if (metadata.Type == "Series" && metadata.Season.HasValue)
                {
                    // TV Series/S/Secrets/Season 1
                    string firstLetterFolder = (metadata.Title?[0].ToString().ToUpper() ?? "Unknown");
                    string seriesFolder = metadata.Title ?? parsed.Title;
                    string seasonFolder = $"Season {metadata.Season.Value}"; // no zero padding

                    relativeFolderPath = Path.Combine("PREVIEW DESTINATION", "TV Series", firstLetterFolder, seriesFolder, seasonFolder);

                    // Episode filename
                    fileNameForPreview = GenerateEpisodeFilename(metadata, extension); // e.g., 01 - Pilot.mkv

                    // Extras folder is the series root, not the season folder
                    extrasFolderPath = Path.Combine("PREVIEW DESTINATION", "TV Series", firstLetterFolder, seriesFolder);
                }
                else
                {
                    // Movies/A/Anaconda (2025)/Anaconda.mkv
                    string firstLetterFolder = (metadata.Title?[0].ToString().ToUpper() ?? "Unknown");

                    // Use proper title-casing for folder and filename
                    string movieTitle = metadata.Title ?? parsed.Title;
                    string titleCased = ToTitleCase(movieTitle); // helper method below

                    string movieFolder = titleCased;
                    if (metadata.Year.HasValue)
                        movieFolder += $" ({metadata.Year.Value})";

                    relativeFolderPath = Path.Combine("PREVIEW DESTINATION", "Movies", firstLetterFolder, movieFolder);

                    // Filename is now title-cased + extension
                    fileNameForPreview = titleCased + extension;

                    // Extras folder is the movie folder itself
                    extrasFolderPath = relativeFolderPath;
                }


                // Split into path parts for the tree
                var pathParts = relativeFolderPath.Split(Path.DirectorySeparatorChar);

                // Remove apostrophes from folder names
                for (int i = 0; i < pathParts.Length; i++)
                    pathParts[i] = pathParts[i].Replace("'", "");

                // Add the actual file to the preview tree
                AddFileToPreviewTree(rootItems, pathParts.Append(fileNameForPreview).ToArray(), file, fileNameForPreview);

                // --- Add poster.jpg and synopsis.txt in extrasFolderPath ---
                if (!addedExtras.Contains(extrasFolderPath))
                {
                    addedExtras.Add(extrasFolderPath);

                    var extrasParts = extrasFolderPath.Split(Path.DirectorySeparatorChar)
                        .Select(p => p.Replace("'", "")) // just in case
                        .ToArray();

                    AddFileToPreviewTree(rootItems, extrasParts.Append("synopsis.txt").ToArray(), null);
                    AddFileToPreviewTree(rootItems, extrasParts.Append("poster.jpg").ToArray(), null);
                }
            }

            return rootItems;
        }




        private string GenerateEpisodeFilename(MediaMetadata metadata, string extension)
        {
            if (!metadata.Season.HasValue || !metadata.Episode.HasValue)
                throw new InvalidOperationException("GenerateEpisodeFilename called without Season/Episode");

            string epNum = metadata.Episode.Value.ToString("D2");
            string rawTitle = metadata.EpisodeTitle ?? "";
            string epTitle = string.IsNullOrWhiteSpace(rawTitle)
                ? ""
                : SanitizeFilename(ToTitleCase(rawTitle));

            if (!string.IsNullOrEmpty(epTitle) &&
                Regex.IsMatch(epTitle, @"^(Episode|Ep|E)\s*\d+$", RegexOptions.IgnoreCase))
            {
                _log($"Ignored generic episode title '{epTitle}'", AppLogLevel.Debug);
                epTitle = "";
            }

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



        public string GetRelativeDestinationPath(string sourceFilePath, MediaMetadata metadata, bool isOfflineMode)
        {
            string extension = Path.GetExtension(sourceFilePath) ?? "";

            // Decide which title to use: offline → original filename, online → TMDb title
            string titleToUse = isOfflineMode && !string.IsNullOrWhiteSpace(metadata.LookupKey)
                ? metadata.LookupKey
                : metadata.Title;

            if (metadata.Type == "Movie")
            {
                string safeTitle = ToTitleCase(Sanitize(titleToUse));

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

                string folderName = string.IsNullOrEmpty(yearPart) ? safeTitle : $"{safeTitle} ({yearPart})";
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
                var firstLetter = GetFirstLetter(titleToUse);

                return Path.Combine("TV Series", firstLetter, titleToUse, $"Season {metadata.Season}", episodeFile);
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

            char first = title.Trim()[0];

            if (char.IsDigit(first))
                return "1 - 1000";

            if (char.IsLetter(first))
                return char.ToUpperInvariant(first).ToString();

            return "#";
        }


        private string Sanitize(string input)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                if (c == '\'') // Allow apostrophe
                    continue;
                input = input.Replace(c, '_');
            }
            return input;
        }


        private void SaveSynopsis(string folder, MediaMetadata metadata, bool isOfflineMode)
        {
            if (isOfflineMode)
            {
                _log($"Skipping synopsis save (offline mode) for '{metadata.Title}'", AppLogLevel.Debug);
                return;
            }

            if (string.IsNullOrWhiteSpace(metadata.Synopsis))
            {
                _log($"Synopsis is empty for '{metadata.Title}', skipping save.", AppLogLevel.Warning);
                return;
            }

            string synopsisPath = Path.Combine(folder, "synopsis.txt");

            _log($"Writing synopsis for '{metadata.Title}' to: {synopsisPath}", AppLogLevel.Debug);
            _log($"→ PG Rating: {metadata.PG}, User Score: {metadata.Rating}/100", AppLogLevel.Debug);
            _log($"→ Cast: {metadata.Cast}", AppLogLevel.Debug);
            _log($"→ Synopsis preview: {(metadata.Synopsis.Length > 100 ? metadata.Synopsis.Substring(0, 100) + "..." : metadata.Synopsis)}", AppLogLevel.Debug);

            if (!File.Exists(synopsisPath))
            {
                File.WriteAllText(synopsisPath,
                    $"{ToTitleCase(metadata.Title)} \n" +
                    $"PG Rating: {metadata.PG} \n" +
                    $"User Score: {metadata.Rating}/100 \n" +
                    $"Cast: {metadata.Cast} \n\n" +
                    $"{metadata.Synopsis}");

                _log($"Saved synopsis.txt for '{metadata.Title}' in '{folder}'", AppLogLevel.Success);
            }
            else
            {
                _log($"synopsis.txt already exists in '{folder}', skipping write.", AppLogLevel.Debug);
            }
        }


        private async Task SavePosterAsync(string folder, MediaMetadata metadata, bool isOfflineMode)
        {
            if (isOfflineMode)
            {
                _log("Skipping poster download (offline mode).", AppLogLevel.Debug);
                return;
            }

            if (string.IsNullOrWhiteSpace(metadata.PosterUrl))
            {
                _log($"No poster URL found for '{metadata.Title}', skipping download.", AppLogLevel.Warning);
                return;
            }

            string posterPath = Path.Combine(folder, "poster.jpg");

            if (!File.Exists(posterPath))
            {
                _log($"Downloading poster for '{metadata.Title}' to '{posterPath}'", AppLogLevel.Action);
                await _tmdbService.DownloadPosterAsync(metadata.PosterUrl, posterPath);
                _log($"Poster saved for '{metadata.Title}'", AppLogLevel.Success);
            }
            else
            {
                _log($"Poster already exists for '{metadata.Title}' at '{posterPath}', skipping download.", AppLogLevel.Debug);
            }
        }


        private static string ToTitleCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var words = input.Split(' ');
            var result = new List<string>();

            foreach (var word in words)
            {
                if (string.IsNullOrWhiteSpace(word))
                {
                    result.Add(word);
                    continue;
                }

                if (word.Length == 1)
                {
                    result.Add(char.ToUpper(word[0]).ToString());
                }
                else
                {
                    // Capitalize the first letter, leave the rest as-is
                    result.Add(char.ToUpper(word[0]) + word.Substring(1));
                }
            }

            return string.Join(" ", result);
        }


        private string NormalizeTitleKey(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return ""; // or "" if you prefer empty key

            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s']", "")
                        .Trim();
        }


    }

}
