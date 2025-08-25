using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using Windows.Media.Protection.PlayReady;
using static ShowStasher.MVVM.ViewModels.MainViewModel;

namespace ShowStasher.Services
{
    public class TMDbService
    {
        private readonly string _apiKey;
        private readonly Action<string, AppLogLevel> _log;
        private readonly SqliteDbService _dbService;
        private readonly IMetadataSelectionService _selectionService;

        public TMDbService(string apiKey, SqliteDbService cache, Action<string, AppLogLevel> log, IMetadataSelectionService selectionService)
        {
            _apiKey = apiKey;
            _dbService = cache;
            _log = log;
            _selectionService = selectionService;
        }

        public async Task<MediaMetadata?> GetSeriesMetadataAsync(string title, int? season = null, int? episode = null)
        {
            // Check cache using named args to avoid parameter-order mistakes
            var cached = await _dbService.GetCachedMetadataAsync(title, "Series", year: null, season: season, episode: episode);
            if (cached != null)
            {
                _log($"Series '{title}' S{season}E{episode} found in cache.", AppLogLevel.Success);
                return cached;
            }

            _log($"Searching for series title '{title}' on TMDb.", AppLogLevel.Info);
            int? tvId;
            try
            {
                tvId = await GetTvShowIdAsync(title);
            }
            catch (HttpRequestException e)
            {
                _log($"Network error while fetching series ID: {e.Message}", AppLogLevel.Error);
                return null;
            }

            if (tvId == null)
            {
                _log($"No TMDb match found for series title '{title}'.", AppLogLevel.Warning);
                return null;
            }

            _log($"Found series ID {tvId} for '{title}'.", AppLogLevel.Info);

            bool seasonFetched = false;
            string seriesDisplayTitle = title; // fallback

            if (season.HasValue && episode.HasValue)
            {
                try
                {
                    string seriesUrl = $"https://api.themoviedb.org/3/tv/{tvId}?api_key={_apiKey}";
                    _log($"Fetching series metadata from {seriesUrl}", AppLogLevel.Info);

                    using var client = new HttpClient();
                    var seriesResponse = await client.GetAsync(seriesUrl);
                    seriesResponse.EnsureSuccessStatusCode();
                    var seriesJson = await seriesResponse.Content.ReadAsStringAsync();
                    var seriesData = JObject.Parse(seriesJson);

                    seriesDisplayTitle = seriesData["name"]?.ToString() ?? title;
                    string seriesOverview = seriesData["overview"]?.ToString() ?? "";
                    string posterPath = seriesData["poster_path"]?.ToString();
                    string fullPosterUrl = string.IsNullOrWhiteSpace(posterPath) ? "" : "https://image.tmdb.org/t/p/w500" + posterPath;

                    int? seriesRating = null;
                    if (seriesData["vote_average"]?.Type is JTokenType.Float or JTokenType.Integer)
                    {
                        var voteAvg = seriesData["vote_average"]?.ToObject<double>() ?? 0;
                        if (voteAvg > 0)
                            seriesRating = (int)Math.Round(voteAvg * 10);
                    }

                    var cast = await GetCastAsync("tv", tvId.Value);
                    var pgRating = await GetPgRatingAsync(tvId.Value, isMovie: false);

                    string seasonUrl = $"https://api.themoviedb.org/3/tv/{tvId}/season/{season}?api_key={_apiKey}";
                    _log($"Fetching season metadata from {seasonUrl}", AppLogLevel.Info);

                    var response = await client.GetAsync(seasonUrl);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();
                    var seasonData = JObject.Parse(json);
                    var episodes = seasonData["episodes"] as JArray;

                    if (episodes != null)
                    {
                        foreach (var ep in episodes)
                        {
                            int epNum = ep["episode_number"]?.ToObject<int>() ?? -1;
                            if (epNum < 0) continue;

                            var epTitle = ep["name"]?.ToString()?.Trim() ?? "";
                            var airDateStr = ep["air_date"]?.ToString();

                            if (string.IsNullOrWhiteSpace(epTitle) || string.IsNullOrWhiteSpace(airDateStr))
                            {
                                _log($"Skipping episode S{season}E{epNum} due to missing title or air date.", AppLogLevel.Warning);
                                continue;
                            }

                            if (!DateTime.TryParse(airDateStr, out var airDate) || airDate > DateTime.Today)
                            {
                                _log($"Skipping episode S{season}E{epNum} with future air date ({airDateStr}).", AppLogLevel.Warning);
                                continue;
                            }

                            // Ensure we set Title (display title) and OriginalFilename reliably
                            var episodeMeta = new MediaMetadata
                            {
                                LookupKey = NormalizeTitleKey(title),
                                Title = seriesDisplayTitle,
                                Type = "Series",
                                Synopsis = seriesOverview,
                                PosterUrl = fullPosterUrl,
                                Season = season,
                                Episode = epNum,
                                EpisodeTitle = epTitle,
                                PG = pgRating,
                                Cast = cast,
                                Rating = seriesRating
                            };

                            // Save under the filename-based normalized key (caller-provided title)
                            string normalizedKey = NormalizeTitleKey(title);
                            await _dbService.SaveMetadataAsync(normalizedKey, episodeMeta);
                            _log($"Saved metadata (key='{normalizedKey}') for S{season}E{epNum} - '{epTitle}'.", AppLogLevel.Success);
                        }

                        seasonFetched = true;
                    }
                }
                catch (Exception ex)
                {
                    _log($"Failed to prefetch season metadata: {ex.Message}", AppLogLevel.Warning);
                }
            }

            // Try read from cache again (use named args)
            var final = await _dbService.GetCachedMetadataAsync(title, "Series", year: null, season: season, episode: episode);
            if (final != null)
            {
                _log($"Metadata for '{title}' S{season}E{episode} retrieved from cache.", AppLogLevel.Success);
                return final;
            }

            // Fallback: fetch single episode if season-prefetch didn't happen
            if (season.HasValue && episode.HasValue && !seasonFetched)
            {
                try
                {
                    using var client = new HttpClient();

                    string epUrl = $"https://api.themoviedb.org/3/tv/{tvId}/season/{season}/episode/{episode}?api_key={_apiKey}";
                    _log($"Fetching individual episode metadata from {epUrl}", AppLogLevel.Info);

                    var epResponse = await client.GetAsync(epUrl);
                    epResponse.EnsureSuccessStatusCode();
                    var epJson = await epResponse.Content.ReadAsStringAsync();
                    var ep = JObject.Parse(epJson);

                    var epTitle = ep["name"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(epTitle))
                    {
                        _log("Episode title not found during fallback fetch.", AppLogLevel.Warning);
                        return null;
                    }

                    // Fetch series details for synopsis/poster
                    var seriesUrl = $"https://api.themoviedb.org/3/tv/{tvId}?api_key={_apiKey}";
                    var seriesResponse = await client.GetAsync(seriesUrl);
                    seriesResponse.EnsureSuccessStatusCode();
                    var seriesJson = await seriesResponse.Content.ReadAsStringAsync();
                    var seriesData = JObject.Parse(seriesJson);

                    string seriesOverview = seriesData["overview"]?.ToString() ?? "";
                    string posterPath = seriesData["poster_path"]?.ToString();
                    string fullPosterUrl = string.IsNullOrWhiteSpace(posterPath) ? "" : "https://image.tmdb.org/t/p/w500" + posterPath;

                    int? seriesRating = null;
                    if (seriesData["vote_average"]?.Type is JTokenType.Float or JTokenType.Integer)
                    {
                        var voteAvg = seriesData["vote_average"]?.ToObject<double>() ?? 0;
                        if (voteAvg > 0)
                            seriesRating = (int)Math.Round(voteAvg * 10);
                    }

                    var cast = await GetCastAsync("tv", tvId.Value);
                    var pgRating = await GetPgRatingAsync(tvId.Value, isMovie: false);

                    var episodeMeta = new MediaMetadata
                    {
                        LookupKey = NormalizeTitleKey(title),
                        Title = seriesData["name"]?.ToString() ?? title,
                        Type = "Series",
                        Synopsis = seriesOverview,
                        PosterUrl = fullPosterUrl,
                        Season = season,
                        Episode = episode,
                        EpisodeTitle = epTitle,
                        PG = pgRating,
                        Cast = cast,
                        Rating = seriesRating
                    };

                    string normalizedKey = NormalizeTitleKey(title);
                    await _dbService.SaveMetadataAsync(normalizedKey, episodeMeta);
                    _log($"Fallback episode metadata saved for '{title}' S{season}E{episode}. (key='{normalizedKey}')", AppLogLevel.Success);

                    return episodeMeta;
                }
                catch (Exception ex)
                {
                    _log($"Fallback fetch failed: {ex.Message}", AppLogLevel.Error);
                }
            }

            _log($"No metadata found for '{title}' S{season}E{episode}.", AppLogLevel.Error);
            return null;
        }


        public async Task<MediaMetadata?> GetMovieMetadataAsync(string title, int? year = null)
        {
            var cached = await _dbService.GetCachedMetadataAsync(title, "Movie", year: year, season: null, episode: null);
            if (cached != null)
            {
                _log($"Cache hit for movie '{title}'", AppLogLevel.Success);
                return cached;
            }

            _log($"Searching movie '{title}'", AppLogLevel.Action);

            try
            {
                using var client = new HttpClient();

                var searchUrl = $"https://api.themoviedb.org/3/search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(title)}";
                var searchResponse = await client.GetAsync(searchUrl);
                searchResponse.EnsureSuccessStatusCode();

                var searchJson = await searchResponse.Content.ReadAsStringAsync();
                var searchObj = JObject.Parse(searchJson);
                var results = searchObj["results"] as JArray;

                if (results == null || !results.Any())
                {
                    _log($"No results found for movie '{title}'", AppLogLevel.Warning);
                    return null;
                }

                var candidates = results
                    .Select(r => new SearchCandidate
                    {
                        Id = r["id"]?.ToObject<int>() ?? 0,
                        Title = r["title"]?.ToString() ?? "",
                        Year = DateTime.TryParse(r["release_date"]?.ToString(), out var dt) ? dt.Year : null,
                        PosterPath = r["poster_path"]?.ToString()
                    })
                    .ToList();

                int? selectedId = candidates.Count == 1
                    ? candidates[0].Id
                    : await _selectionService.PromptUserToSelectMovieAsync(title, candidates);

                if (selectedId == null)
                {
                    _log("Movie selection cancelled by user", AppLogLevel.Warning);
                    return null;
                }

                var detailsUrl = $"https://api.themoviedb.org/3/movie/{selectedId}?api_key={_apiKey}";
                _log($"Fetching details from {detailsUrl}", AppLogLevel.Action);

                var detailsResponse = await client.GetAsync(detailsUrl);
                if (detailsResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    _log($"Movie details not found (404) for ID {selectedId}", AppLogLevel.Warning);
                    return null;
                }
                detailsResponse.EnsureSuccessStatusCode();

                var detailsJson = await detailsResponse.Content.ReadAsStringAsync();
                var obj = JObject.Parse(detailsJson);

                string? releaseDateStr = obj["release_date"]?.ToString();
                int? parsedYear = DateTime.TryParse(releaseDateStr, out var dt) ? dt.Year : null;
                double? rating = obj["vote_average"]?.ToObject<double>();
                string posterPath = obj["poster_path"]?.ToString();

                var metadata = new MediaMetadata
                {
                    LookupKey = NormalizeTitleKey(title),
                    Title = obj["title"]?.ToString() ?? title,
                    Type = "Movie",
                    Synopsis = obj["overview"]?.ToString() ?? "",
                    Cast = await GetCastAsync("movie", selectedId.Value),
                    PosterUrl = !string.IsNullOrWhiteSpace(posterPath) ? $"https://image.tmdb.org/t/p/w500{posterPath}" : "",
                    Year = parsedYear,
                    PG = await GetPgRatingAsync(selectedId.Value, isMovie: true),
                    Rating = rating.HasValue ? (int)Math.Round(rating.Value * 10) : null
                };

                string normalizedKey = NormalizeTitleKey(title);
                await _dbService.SaveMetadataAsync(normalizedKey, metadata);

                _log($"Movie metadata cached for '{metadata.Title}' (key='{normalizedKey}')", AppLogLevel.Success);

                return metadata;
            }
            catch (HttpRequestException e)
            {
                _log($"HTTP error fetching movie metadata: {e.Message}", AppLogLevel.Error);
                return null;
            }
        }

        public async Task<string> GetDisplayTitleAsync(string title, string type, int? year)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            try
            {
                using var client = new HttpClient();

                if (type?.Equals("Movie", StringComparison.OrdinalIgnoreCase) == true)
                {
                    string url = $"https://api.themoviedb.org/3/search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(title)}";
                    if (year.HasValue)
                        url += $"&year={year.Value}";

                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();
                    var obj = JObject.Parse(json);
                    var results = obj["results"] as JArray;

                    return results?.FirstOrDefault()?["title"]?.ToString() ?? title;
                }
                else if (type?.Equals("Series", StringComparison.OrdinalIgnoreCase) == true)
                {
                    string url = $"https://api.themoviedb.org/3/search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(title)}";
                    if (year.HasValue)
                        url += $"&first_air_date_year={year.Value}";

                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();
                    var obj = JObject.Parse(json);
                    var results = obj["results"] as JArray;

                    return results?.FirstOrDefault()?["name"]?.ToString() ?? title;
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"GetDisplayTitleAsync failed for '{title}': {ex.Message}", AppLogLevel.Warning);
            }

            return title; // fallback
        }





        public async Task<bool> DownloadPosterAsync(string url, string savePath)
        {
            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            try
            {
                _log($"Starting poster download from {url}", AppLogLevel.Action);

                var data = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(savePath, data);

                _log($"Poster saved to {Path.GetDirectoryName(savePath)}", AppLogLevel.Success);
                return true;
            }
            catch (TaskCanceledException)
            {
                _log($"Timeout downloading poster: {Path.GetFileName(savePath)}", AppLogLevel.Warning);
            }
            catch (HttpRequestException e)
            {
                _log($"HTTP error downloading poster: {e.Message}", AppLogLevel.Error);
            }
            catch (Exception e)
            {
                _log($"Unexpected error downloading poster: {e.Message}", AppLogLevel.Error);
            }

            return false;
        }



        private async Task<int?> GetTvShowIdAsync(string title)
        {
            _log($"Searching TV series: '{title}'", AppLogLevel.Action);

            var searchUrl = $"https://api.themoviedb.org/3/search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(title)}";
            using var client = new HttpClient();

            try
            {
                var searchResponse = await client.GetAsync(searchUrl);
                searchResponse.EnsureSuccessStatusCode();

                var json = await searchResponse.Content.ReadAsStringAsync();
                var results = JObject.Parse(json)["results"] as JArray;

                if (results == null || !results.Any())
                {
                    _log($"No TV series results found for '{title}'.", AppLogLevel.Warning);
                    return null;
                }

                List<SearchCandidate> candidates = results
                    .Select(r => new SearchCandidate
                    {
                        Id = r["id"]?.ToObject<int>() ?? 0,
                        Title = r["name"]?.ToString() ?? "",
                        Year = DateTime.TryParse(r["first_air_date"]?.ToString(), out var dt) ? dt.Year : null,
                        PosterPath = r["poster_path"]?.ToString()
                    })
                    .ToList();

                int? selectedId = candidates.Count == 1
                    ? candidates[0].Id
                    : await _selectionService.PromptUserToSelectSeriesAsync(title, candidates);

                if (selectedId == null)
                {
                    _log("User cancelled TV series selection.", AppLogLevel.Info);
                }
                else
                {
                    _log($"Selected TV series ID: {selectedId}", AppLogLevel.Success);
                }

                return selectedId;
            }
            catch (HttpRequestException e)
            {
                _log($"HTTP error while searching TV series: {e.Message}", AppLogLevel.Error);
                return null;
            }
            catch (Exception e)
            {
                _log($"Unexpected error while searching TV series: {e.Message}", AppLogLevel.Error);
                return null;
            }
        }




        private string NormalizeTitleKey(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty; // or some default like "unknown"

            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "")
                        .Trim();
        }


        private async Task<string> GetPgRatingAsync(int tmdbId, bool isMovie)
        {
            var endpoint = isMovie
                ? $"https://api.themoviedb.org/3/movie/{tmdbId}/release_dates?api_key={_apiKey}"
                : $"https://api.themoviedb.org/3/tv/{tmdbId}/content_ratings?api_key={_apiKey}";

            _log($"Fetching PG rating from {(isMovie ? "movie" : "TV")} endpoint: {endpoint}", AppLogLevel.Action);

            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);

                string? usRating = null;

                var results = obj["results"] as JArray;
                var usEntry = results?.FirstOrDefault(x => x["iso_3166_1"]?.ToString() == "US");

                if (isMovie)
                {
                    usRating = usEntry?["release_dates"]?[0]?["certification"]?.ToString();
                }
                else
                {
                    usRating = usEntry?["rating"]?.ToString();
                }

                var convertedRating = ConvertUsToSouthAfricaRating(usRating ?? string.Empty);
                _log($"Converted US rating '{usRating}' to South African rating '{convertedRating}'.", AppLogLevel.Success);

                return convertedRating;
            }
            catch (Exception ex)
            {
                _log($"Failed to fetch PG rating: {ex.Message}", AppLogLevel.Error);
                return "PG"; // Default fallback rating
            }
        }

        private string ConvertUsToSouthAfricaRating(string usRating)
        {
            if (string.IsNullOrWhiteSpace(usRating))
                return "PG"; // default

            usRating = usRating.ToUpperInvariant();

            return usRating switch
            {
                // Movie Ratings
                "G" => "A",
                "PG" => "PG",
                "PG-13" => "13",
                "R" => "16",
                "NC-17" => "18",
                "X" => "X18",
                "XX" => "XX",
                "MATURE" => "16",

                // TV Ratings
                "TV-Y" => "A",       // All children
                "TV-Y7" => "7",      // Directed to older children
                "TV-G" => "PG",      // General audience
                "TV-PG" => "13",     // Parental guidance suggested
                "TV-14" => "16",     // Parents strongly cautioned
                "TV-MA" => "18",     // Mature audience only

                _ => "PG" // Default fallback
            };
        }

        private async Task<string> GetCastAsync(string mediaType, int id)
        {
            var url = $"https://api.themoviedb.org/3/{mediaType}/{id}/credits?api_key={_apiKey}";
            _log($"Fetching cast info from: {url}", AppLogLevel.Action);

            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);

                var castList = obj["cast"]?
                    .Take(3)
                    .Select(c =>
                    {
                        var actor = c["name"]?.ToString();
                        var character = c["character"]?.ToString();
                        return !string.IsNullOrWhiteSpace(actor) && !string.IsNullOrWhiteSpace(character)
                            ? $"{actor} ({character})"
                            : actor;
                    })
                    .Where(entry => !string.IsNullOrWhiteSpace(entry))
                    .ToList() ?? new List<string>();

                var director = obj["crew"]?
                    .FirstOrDefault(c => c["job"]?.ToString()?.Contains("Director") == true)?["name"]?.ToString();

                if (!string.IsNullOrWhiteSpace(director))
                    castList.Add($"Director: {director}");

                var combinedCast = string.Join(", ", castList);
                _log($"Fetched cast: {combinedCast}", AppLogLevel.Success);

                return combinedCast;
            }
            catch (Exception ex)
            {
                _log($"Failed to fetch cast for {mediaType}/{id}: {ex.Message}", AppLogLevel.Warning);
                return string.Empty;
            }
        }
    }
}
