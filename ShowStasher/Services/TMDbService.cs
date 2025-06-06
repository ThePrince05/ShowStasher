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

            string? episodeTitle = null;
            if (season.HasValue && episode.HasValue)
            {
                _log($"Fetching title for S{season.Value:D2}E{episode.Value:D2}");
                try
                {
                    episodeTitle = await GetEpisodeTitleAsync(tvId.Value, season.Value, episode.Value);
                    _log($"Episode title: {episodeTitle ?? "Not found"}");
                }
                catch (HttpRequestException e)
                {
                    _log($"Failed to get episode title: {e.Message}");
                }
            }

            try
            {
                var detailsUrl = $"https://api.themoviedb.org/3/tv/{tvId}?api_key={_apiKey}";
                _log($"Fetching series details: {detailsUrl}");

                using var client = new HttpClient();
                var response = await client.GetAsync(detailsUrl);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _log("Series details returned 404 Not Found.");
                    return null;
                }
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                
                double? rating = obj["vote_average"]?.ToObject<double>();

                var metadata = new MediaMetadata
                {
                    Title = obj["name"]?.ToString() ?? title,
                    Type = "Series",
                    Synopsis = obj["overview"]?.ToString() ?? "",
                    Cast = await GetCastCsvAsync(tvId.Value, isMovie: false),
                    PosterUrl = obj["poster_path"]?.ToString() is string path && !string.IsNullOrWhiteSpace(path)
                ? "https://image.tmdb.org/t/p/w500" + path : "",
                    Season = season,
                    Episode = episode,
                    EpisodeTitle = episodeTitle,
                    PG = await GetPgRatingAsync(tvId.Value, isMovie: false),
                    Rating = rating.HasValue ? (int)Math.Round(rating.Value * 10) : null
                };

                string normalizedKey = NormalizeTitleKey(metadata.Title);
                await _cache.SaveMetadataAsync(normalizedKey, metadata);

                return metadata;
            }
            catch (HttpRequestException e)
            {
                _log($"Failed to get series details: {e.Message}");
                return null;
            }
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
                    Cast = await GetCastCsvAsync(movieId, isMovie: true),
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

        private async Task<string?> GetEpisodeTitleAsync(int tvId, int season, int episode)
        {
            using var client = new HttpClient();
            var url = $"https://api.themoviedb.org/3/tv/{tvId}/season/{season}/episode/{episode}?api_key={_apiKey}";
            _log($"Requesting episode info: {url}");

            var json = await client.GetStringAsync(url);
            var data = JObject.Parse(json);

            return data["name"]?.ToString();
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
        private async Task<string> GetCastCsvAsync(int id, bool isMovie)
        {
            string url = $"https://api.themoviedb.org/3/{(isMovie ? "movie" : "tv")}/{id}/credits?api_key={_apiKey}";

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
                        : null;
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry)) ?? [];

            // Director
            var director = obj["crew"]?
                .FirstOrDefault(c => c["job"]?.ToString() == "Director")?["name"]?.ToString();

            var combined = castList.ToList();
            if (!string.IsNullOrWhiteSpace(director))
                combined.Add($"Director: {director}");

            return string.Join(", ", combined);
        }


    }
}
