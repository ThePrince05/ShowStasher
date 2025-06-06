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

            // hashset used to make there there no duplicate file being organised
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
        private async Task<MediaMetadata?> EnsureMetadataInCacheAsync(ParsedMediaInfo parsed)
        {
            string normalizedKey = NormalizeTitleKey(parsed.Title);
            int sentinel = -1; // for movie Season/Episode

            _log($"[ENTER] EnsureMetadataInCacheAsync: " +
                 $"Title='{parsed.Title}', Type='{parsed.Type}', Year={parsed.Year}, " +
                 $"Season={parsed.Season}, Episode={parsed.Episode}");

            // 1. Non‐movie (Series/Anime) branch
            if (!parsed.Type.Equals("Movie", StringComparison.OrdinalIgnoreCase))
            {
                // 1a. Cache‐lookup (no year)
                _log($"[SERIES-LOOKUP1] Title='{normalizedKey}', Type='{parsed.Type}', " +
                     $"Year=null, Season={parsed.Season}, Episode={parsed.Episode}");
                var cached1 = await _cacheService.GetCachedMetadataAsync(
                    normalizedKey, parsed.Type,
                    year: null,
                    season: parsed.Season,
                    episode: parsed.Episode);
                if (cached1 != null)
                {
                    _log($"[CACHE-HIT1] Series/Anime '{normalizedKey}' (no‐year lookup)");
                    cached1.Type = parsed.Type;
                    return cached1;
                }
                _log($"[CACHE-MISS1] Series/Anime '{normalizedKey}' (no‐year lookup)");

                // 1b. Cache‐lookup (with year)
                _log($"[SERIES-LOOKUP2] Title='{normalizedKey}', Type='{parsed.Type}', " +
                     $"Year={parsed.Year}, Season={parsed.Season}, Episode={parsed.Episode}");
                var cached2 = await _cacheService.GetCachedMetadataAsync(
                    normalizedKey, parsed.Type,
                    year: parsed.Year,
                    season: parsed.Season,
                    episode: parsed.Episode);
                if (cached2 != null)
                {
                    _log($"[CACHE-HIT2] Series/Anime '{normalizedKey}' (year={parsed.Year} lookup)");
                    cached2.Type = parsed.Type;
                    return cached2;
                }
                _log($"[CACHE-MISS2] Series/Anime '{normalizedKey}' (year={parsed.Year} lookup)");

                // 1c. Fetch from API for Series/Anime
                _log($"[FETCH-SERIES] No cache; fetching '{parsed.Title}' (Series/Anime)");
                MediaMetadata? fetched = null;
                if (parsed.Type.Equals("Anime", StringComparison.OrdinalIgnoreCase))
                {
                    fetched = await _jikanService.GetAnimeMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                    if (fetched == null || string.IsNullOrWhiteSpace(fetched.EpisodeTitle))
                    {
                        _log($"[JIKAN-FAIL] Fallback to TMDb for '{parsed.Title}'");
                        fetched = await _tmdbService.GetSeriesMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                    }
                }
                else // Series
                {
                    fetched = await _tmdbService.GetSeriesMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                    if (fetched == null || string.IsNullOrWhiteSpace(fetched.EpisodeTitle))
                    {
                        _log($"[TMDB-FAIL] Fallback to Jikan for '{parsed.Title}'");
                        fetched = await _jikanService.GetAnimeMetadataAsync(parsed.Title, parsed.Season, parsed.Episode);
                    }
                }

                if (fetched != null)
                {
                    fetched.Type = parsed.Type;
                    fetched.Title = parsed.Title;
                    fetched.Season = parsed.Season;
                    fetched.Episode = parsed.Episode;

                    // 1d. Idempotent‐save: re‐check cache before actually inserting
                    _log($"[SERIES-RECHECK] Checking again before saving '{normalizedKey}' S{fetched.Season}E{fetched.Episode}");
                    var existAgain = await _cacheService.GetCachedMetadataAsync(
                        normalizedKey, parsed.Type,
                        year: parsed.Year,
                        season: parsed.Season,
                        episode: parsed.Episode);
                    if (existAgain == null)
                    {
                        _log($"[SAVE-SERIES] Caching fetched '{normalizedKey}' S{fetched.Season}E{fetched.Episode}");
                        await _cacheService.SaveMetadataAsync(normalizedKey, fetched);
                    }
                    else
                    {
                        _log($"[SKIP-SAVE] Series/Anime '{normalizedKey}' already in cache after re‐check");
                    }

                    return fetched;
                }

                _log($"[FAIL-SERIES] Could not fetch/cache Series/Anime '{normalizedKey}'");
                return null;
            }
            else // 2. Movie branch (single lookup → fetch → single save)
            {
                // 2a. Single cache lookup (with sentinel values)
                _log($"[MOVIE-LOOKUP] Title='{normalizedKey}', Type='movie', Year={parsed.Year}, " +
                     $"Season={sentinel}, Episode={sentinel}");
                var cachedMovie = await _cacheService.GetCachedMetadataAsync(
                    normalizedKey, "movie",
                    year: parsed.Year,
                    season: sentinel,
                    episode: sentinel);
                if (cachedMovie != null)
                {
                    _log($"[CACHE-HIT] Movie '{normalizedKey}' (year={parsed.Year} lookup)");
                    cachedMovie.Type = "Movie";
                    return cachedMovie;
                }
                _log($"[CACHE-MISS] Movie '{normalizedKey}' (year={parsed.Year} lookup)");

                // 2b. Fetch from TMDb if not cached
                _log($"[FETCH-MOVIE] No cache; fetching '{parsed.Title}' from TMDb");
                var movieMeta = await _tmdbService.GetMovieMetadataAsync(parsed.Title.Trim());
                if (movieMeta == null)
                {
                    _log($"[FAIL-MOVIE] Could not fetch TMDb metadata for '{parsed.Title}'");
                    return null;
                }

                movieMeta.Type = "Movie";
                movieMeta.Title = parsed.Title;
                if (!movieMeta.Year.HasValue && parsed.Year.HasValue)
                    movieMeta.Year = parsed.Year;

                // 2c. Use sentinel for season and episode
                movieMeta.Season = sentinel;
                movieMeta.Episode = sentinel;

                // 2d. Idempotent‐save: re‐check cache before insert
                _log($"[MOVIE-RECHECK] Checking again before saving '{normalizedKey}' (Movie)");
                var existAgain = await _cacheService.GetCachedMetadataAsync(
                    normalizedKey, "movie",
                    year: movieMeta.Year,
                    season: sentinel,
                    episode: sentinel);
                if (existAgain == null)
                {
                    _log($"[SAVE-MOVIE] Caching fetched '{normalizedKey}' (Movie)");
                    await _cacheService.SaveMetadataAsync(normalizedKey, movieMeta);
                }
                else
                {
                    _log($"[SKIP-SAVE] Movie '{normalizedKey}' already in cache after re‐check");
                }

                return movieMeta;
            }
        }



        private async Task MoveAndOrganizeAsync(string filePath, MediaMetadata metadata, string destinationRoot)
        {
            _log($"Metadata.Title: {metadata.Title}, Year: {metadata.Year}");

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

        private void SaveSynopsisIfMissing(string folder, MediaMetadata metadata)
        {
            string synopsisPath = Path.Combine(folder, "synopsis.txt");
            if (!File.Exists(synopsisPath))
            {
                File.WriteAllText(synopsisPath,
                    $"{metadata.Title} \nPG Rating: {metadata.PG} \nUser Score: {metadata.Rating}/100 \nCast: {metadata.Cast} \n\n{metadata.Synopsis}");
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

        private string NormalizeTitleKey(string title)
        {
            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "") // remove punctuation
                        .Trim(); // remove surrounding whitespace
        }

    }

}
