using System;
using System.Collections.Generic;
using System.Configuration;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ShowStasher.MVVM.Models;
using System.Security.Authentication;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace ShowStasher.Services
{
    public class TMDbService
    {
        private readonly string _apiKey;
        private readonly Action<string> _log;
        private readonly MetadataCacheService _cache;

        public TMDbService(string apiKey, MetadataCacheService cache, Action<string> log)
        {
            _apiKey = apiKey;
            _cache = cache;
            _log = log;
        }

        public async Task<MediaMetadata?> GetSeriesMetadataAsync(string title, int? season = null, int? episode = null)
        {
            var cached = await _cache.GetCachedMetadataAsync(title, "Series", season, episode);
            if (cached != null)
            {
                _log($"[Cache] Found cached series metadata for '{title}' S{season}E{episode}");
                return cached;
            }

            _log($"Searching for series: '{title}'");
            int? tvId;
            try
            {
                tvId = await GetTvShowIdAsync(title);
            }
            catch (HttpRequestException e)
            {
                _log($"Error getting series ID: {e.Message}");
                return null;
            }

            if (tvId == null)
            {
                _log($"No series found for '{title}'.");
                return null;
            }

            _log($"Found series ID {tvId} for title '{title}'");

            bool seasonFetched = false;

            if (season.HasValue && episode.HasValue)
            {
                try
                {
                    // Fetch series-level metadata
                    string seriesUrl = $"https://api.themoviedb.org/3/tv/{tvId}?api_key={_apiKey}";
                    _log($"Fetching series details from {seriesUrl}");

                    using var client = new HttpClient();
                    var seriesResponse = await client.GetAsync(seriesUrl);
                    seriesResponse.EnsureSuccessStatusCode();

                    var seriesJson = await seriesResponse.Content.ReadAsStringAsync();
                    var seriesData = JObject.Parse(seriesJson);

                    string seriesOverview = seriesData["overview"]?.ToString() ?? "";
                    string posterPath = seriesData["poster_path"]?.ToString();
                    string fullPosterUrl = string.IsNullOrWhiteSpace(posterPath) ? "" : "https://image.tmdb.org/t/p/w500" + posterPath;

                    int? seriesRating = null;
                    if (seriesData["vote_average"]?.Type == JTokenType.Float || seriesData["vote_average"]?.Type == JTokenType.Integer)
                    {
                        var voteAvg = seriesData["vote_average"]?.ToObject<double>() ?? 0;
                        if (voteAvg > 0)
                            seriesRating = (int)Math.Round(voteAvg * 10);
                    }

                    // Fetch shared PG and cast
                    var cast = await GetCastAsync("tv", tvId.Value);
                    var pgRating = await GetPgRatingAsync(tvId.Value, isMovie: false);

                    // Fetch all episodes in the season
                    string seasonUrl = $"https://api.themoviedb.org/3/tv/{tvId}/season/{season}?api_key={_apiKey}";
                    _log($"Fetching all episodes for season {season} from {seasonUrl}");

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
                                _log($"[Skip] Episode S{season}E{epNum} missing title or air date. Skipping.");
                                continue;
                            }

                            if (!DateTime.TryParse(airDateStr, out var airDate) || airDate > DateTime.Today)
                            {
                                _log($"[Skip] Episode S{season}E{epNum} has not aired yet (Air date: {airDateStr}). Skipping.");
                                continue;
                            }

                            var episodeCached = await _cache.GetCachedMetadataAsync(title, "Series", year: null, season: season, episode: epNum);
                            if (episodeCached != null)
                            {
                                _log($"[Cache] Skipping already-cached episode S{season}E{epNum} - '{epTitle}'");
                                continue;
                            }

                            var episodeMeta = new MediaMetadata
                            {
                                Title = title,
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

                            string normalizedKey = NormalizeTitleKey(episodeMeta.Title);
                            await _cache.SaveMetadataAsync(normalizedKey, episodeMeta);
                            _log($"[Saved] Cached S{season}E{epNum} - '{epTitle}'");
                        }

                        seasonFetched = true;
                    }
                }
                catch (Exception ex)
                {
                    _log($"[Warning] Failed to prefetch season data: {ex.Message}");
                }
            }

            var final = await _cache.GetCachedMetadataAsync(title, "Series", year: null, season: season, episode: episode);
            if (final != null)
            {
                _log($"Returning metadata for '{title}' S{season}E{episode} from cache.");
                return final;
            }

            // Fallback fetch
            if (season.HasValue && episode.HasValue && !seasonFetched)
            {
                try
                {
                    using var client = new HttpClient();

                    // Get episode
                    string epUrl = $"https://api.themoviedb.org/3/tv/{tvId}/season/{season}/episode/{episode}?api_key={_apiKey}";
                    _log($"[Fallback] Fetching individual episode from {epUrl}");

                    var epResponse = await client.GetAsync(epUrl);
                    epResponse.EnsureSuccessStatusCode();

                    var epJson = await epResponse.Content.ReadAsStringAsync();
                    var ep = JObject.Parse(epJson);

                    var epTitle = ep["name"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(epTitle))
                    {
                        _log($"[Fallback] Episode title missing. Skipping.");
                        return null;
                    }

                    // Get series metadata for fallback too
                    string seriesUrl = $"https://api.themoviedb.org/3/tv/{tvId}?api_key={_apiKey}";
                    var seriesResponse = await client.GetAsync(seriesUrl);
                    seriesResponse.EnsureSuccessStatusCode();
                    var seriesJson = await seriesResponse.Content.ReadAsStringAsync();
                    var seriesData = JObject.Parse(seriesJson);

                    string seriesOverview = seriesData["overview"]?.ToString() ?? "";
                    string posterPath = seriesData["poster_path"]?.ToString();
                    string fullPosterUrl = string.IsNullOrWhiteSpace(posterPath) ? "" : "https://image.tmdb.org/t/p/w500" + posterPath;

                    int? seriesRating = null;
                    if (seriesData["vote_average"]?.Type == JTokenType.Float || seriesData["vote_average"]?.Type == JTokenType.Integer)
                    {
                        var voteAvg = seriesData["vote_average"]?.ToObject<double>() ?? 0;
                        if (voteAvg > 0)
                            seriesRating = (int)Math.Round(voteAvg);
                    }

                    var cast = await GetCastAsync("tv",tvId.Value);
                    var pgRating = await GetPgRatingAsync(tvId.Value, isMovie: false);

                    var episodeMeta = new MediaMetadata
                    {
                        Title = title,
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

                    string normalizedKey = NormalizeTitleKey(episodeMeta.Title);
                    await _cache.SaveMetadataAsync(normalizedKey, episodeMeta);

                    _log($"[Fallback] Saved fallback metadata for '{title}' S{season}E{episode}");
                    return episodeMeta;
                }
                catch (Exception ex)
                {
                    _log($"[Error] Fallback fetch for episode failed: {ex.Message}");
                }
            }

            _log($"[Error] Metadata not found for '{title}' S{season}E{episode} after fallback.");
            return null;
        }




        public async Task<MediaMetadata?> GetMovieMetadataAsync(string title, int? year = null)
        {
            var cached = await _cache.GetCachedMetadataAsync(title, "Movie");
            if (cached != null)
            {
                _log($"[Cache] Found cached movie metadata for '{title}'");
                return cached;
            }

            _log($"Searching for movie: '{title}'");
            try
            {
                var searchUrl = $"https://api.themoviedb.org/3/search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(title)}";
                using var client = new HttpClient();
                var searchResponse = await client.GetAsync(searchUrl);
                searchResponse.EnsureSuccessStatusCode();

                var searchJson = await searchResponse.Content.ReadAsStringAsync();
                var searchObj = JObject.Parse(searchJson);
                var results = searchObj["results"] as JArray;

                if (results == null || !results.Any())
                {
                    _log($"No movie results found for '{title}'.");
                    return null;
                }

                // Optional: prioritize match by year if given
                JObject? bestMatch = null;
                if (year.HasValue)
                {
                    bestMatch = results.FirstOrDefault(r =>
                    {
                        var releaseDateStr = r["release_date"]?.ToString();
                        return DateTime.TryParse(releaseDateStr, out var releaseDate) && releaseDate.Year == year.Value;
                    }) as JObject;
                }

                // Fallback: use first result
                bestMatch ??= results.FirstOrDefault() as JObject;

                if (bestMatch == null)
                {
                    _log("No suitable match found.");
                    return null;
                }

                int movieId = bestMatch["id"]?.ToObject<int>() ?? 0;
                if (movieId == 0)
                {
                    _log("Movie ID not found.");
                    return null;
                }

                var detailsUrl = $"https://api.themoviedb.org/3/movie/{movieId}?api_key={_apiKey}";
                _log($"Fetching movie details: {detailsUrl}");

                var detailsResponse = await client.GetAsync(detailsUrl);
                if (detailsResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    _log("Movie details returned 404 Not Found.");
                    return null;
                }
                detailsResponse.EnsureSuccessStatusCode();

                var detailsJson = await detailsResponse.Content.ReadAsStringAsync();
                var obj = JObject.Parse(detailsJson);

                string? releaseDateStr = obj["release_date"]?.ToString();
                int? parsedYear = DateTime.TryParse(releaseDateStr, out var dt) ? dt.Year : null;

                double? rating = obj["vote_average"]?.ToObject<double>();

                var metadata = new MediaMetadata
                {
                    Title = obj["title"]?.ToString() ?? title,
                    Type = "Movie",
                    Synopsis = obj["overview"]?.ToString() ?? "",
                    Cast = await GetCastAsync("movie",movieId),
                    PosterUrl = obj["poster_path"]?.ToString() is string path && !string.IsNullOrWhiteSpace(path)
                 ? "https://image.tmdb.org/t/p/w500" + path : "",
                    Year = parsedYear,
                    PG = await GetPgRatingAsync(movieId, isMovie: true),
                    Rating = rating.HasValue ? (int)Math.Round(rating.Value * 10) : null  // Convert 0–10 to 0–100
                };

                string normalizedKey = NormalizeTitleKey(metadata.Title);
                await _cache.SaveMetadataAsync(normalizedKey, metadata);

                return metadata;
            }
            catch (HttpRequestException e)
            {
                _log($"Failed to fetch movie metadata: {e.Message}");
                return null;
            }
        }


        public async Task<bool> DownloadPosterAsync(string url, string savePath)
        {
            // 10‐second timeout + enforce TLS 1.2+
            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            try
            {
                _log($"Downloading poster: {url}");
                var data = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(savePath, data);
                _log($"Saved poster.jpg in {Path.GetDirectoryName(savePath)}");
                return true;
            }

            catch (TaskCanceledException)
            {
                _log($"Timeout while downloading poster for {Path.GetFileName(savePath)}");
            }
            catch (HttpRequestException e)
            {
                _log($"HTTP error downloading poster: {e.Message}");
            }
            catch (Exception e)
            {
                _log($"Error downloading poster: {e.Message}");
            }
            return false;
        }


        private async Task<int?> GetTvShowIdAsync(string title)
        {
            using var client = new HttpClient();
            var url = $"https://api.themoviedb.org/3/search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(title)}";
            _log($"Requesting: {url}");

            var json = await client.GetStringAsync(url);
            var data = JObject.Parse(json);
            var first = data["results"]?.FirstOrDefault();

            return first?["id"]?.Value<int>();
        }


        private string NormalizeTitleKey(string title)
        {
            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "") // remove punctuation
                        .Trim(); // remove surrounding whitespace
        }

        private async Task<string> GetPgRatingAsync(int tmdbId, bool isMovie)
        {
            using var client = new HttpClient();
            var endpoint = isMovie
                ? $"https://api.themoviedb.org/3/movie/{tmdbId}/release_dates?api_key={_apiKey}"
                : $"https://api.themoviedb.org/3/tv/{tmdbId}/content_ratings?api_key={_apiKey}";

            try
            {
                var response = await client.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);

                string? usRating = null;

                if (isMovie)
                {
                    var results = obj["results"] as JArray;
                    var usEntry = results?.FirstOrDefault(x => x["iso_3166_1"]?.ToString() == "US");
                    usRating = usEntry?["release_dates"]?[0]?["certification"]?.ToString();
                }
                else
                {
                    var results = obj["results"] as JArray;
                    var usEntry = results?.FirstOrDefault(x => x["iso_3166_1"]?.ToString() == "US");
                    usRating = usEntry?["rating"]?.ToString();
                }

                return ConvertUsToSouthAfricaRating(usRating ?? string.Empty);
            }
            catch (Exception ex)
            {
                _log($"[Rating] Failed to fetch PG rating: {ex.Message}");
                return "PG"; // Default rating in case of error
            }
        }

        private string ConvertUsToSouthAfricaRating(string usRating)
        {
            return usRating.ToUpperInvariant() switch
            {
                "G" => "A",
                "PG" => "PG",
                "PG-13" => "13",
                "R" => "16",
                "NC-17" => "18",
                "X" => "X18",
                "XX" => "XX",
                "MATURE" => "16", // Assuming 'Mature' maps to 16
                _ => "PG" // Default rating
            };
        }
        private async Task<string> GetCastAsync(string mediaType, int id)
        {
            try
            {
                string url = $"https://api.themoviedb.org/3/{mediaType}/{id}/credits?api_key={_apiKey}";

                using var client = new HttpClient();
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);

                // Top 3 cast with "Name (Character)"
                var castList = obj["cast"]?
                    .Take(3)
                    .Select(c =>
                    {
                        var actor = c["name"]?.ToString();
                        var character = c["character"]?.ToString();
                        return !string.IsNullOrWhiteSpace(actor) && !string.IsNullOrWhiteSpace(character)
                            ? $"{actor} ({character})"
                            : actor; // fallback: actor only
                    })
                    .Where(entry => !string.IsNullOrWhiteSpace(entry)) ?? [];

                // Director or similar lead crew role
                var director = obj["crew"]?
                    .FirstOrDefault(c => c["job"]?.ToString()?.Contains("Director") == true)?["name"]?.ToString();

                var combined = castList.ToList();
                if (!string.IsNullOrWhiteSpace(director))
                    combined.Add($"Director: {director}");

                return string.Join(", ", combined);
            }
            catch (Exception ex)
            {
                _log($"[Warning] Failed to fetch cast for {mediaType}/{id}: {ex.Message}");
                return "";
            }
        }
    }
}
