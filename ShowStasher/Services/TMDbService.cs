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
            // Check cache first
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

                var metadata = new MediaMetadata
                {
                    Title = obj["name"]?.ToString() ?? title,
                    Type = "Series",
                    Synopsis = obj["overview"]?.ToString() ?? "",
                    Rating = "N/A",
                    PG = obj["adult"]?.ToObject<bool>() == true ? "18+" : "PG-13",
                    PosterUrl = obj["poster_path"]?.ToString() is string path && !string.IsNullOrWhiteSpace(path)
                                ? "https://image.tmdb.org/t/p/w500" + path : "",
                    Season = season,
                    Episode = episode,
                    EpisodeTitle = episodeTitle
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


        public async Task<MediaMetadata?> GetMovieMetadataAsync(string title)
        {
            // Check cache first
            var cached = await _cache.GetCachedMetadataAsync(title, "Movie");
            if (cached != null)
            {
                _log($"[Cache] Found cached movie metadata for '{title}'");
                return cached;
            }

            _log($"Searching for movie: '{title}'");
            string searchTitle = title;
            int? year = null;

            var yearMatch = Regex.Match(title, @"^(.*?)(?:\s+(\d{4}))?$");
            if (yearMatch.Success)
            {
                searchTitle = yearMatch.Groups[1].Value.Trim();
                if (int.TryParse(yearMatch.Groups[2].Value, out int parsedYear))
                {
                    year = parsedYear;
                }
            }

            using var client = new HttpClient();
            try
            {
                var searchUrl = $"https://api.themoviedb.org/3/search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(searchTitle)}";
                if (year.HasValue)
                    searchUrl += $"&year={year.Value}";

                _log($"Requesting: {searchUrl}");
                var searchResponse = await client.GetAsync(searchUrl);
                if (searchResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    _log("Movie search returned 404 Not Found.");
                    return null;
                }
                searchResponse.EnsureSuccessStatusCode();

                var searchJson = await searchResponse.Content.ReadAsStringAsync();
                var searchData = JObject.Parse(searchJson);
                var first = searchData["results"]?.FirstOrDefault();

                if (first == null)
                {
                    _log("No movie found.");
                    return null;
                }

                int movieId = first["id"]?.Value<int>() ?? 0;
                string movieTitle = first["title"]?.ToString() ?? "Unknown";
                string posterPath = first["poster_path"]?.ToString() ?? "";

                _log($"Movie found: {movieTitle} (ID: {movieId})");

                var detailsUrl = $"https://api.themoviedb.org/3/movie/{movieId}?api_key={_apiKey}";
                _log($"Fetching details: {detailsUrl}");

                var detailsResponse = await client.GetAsync(detailsUrl);
                if (detailsResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    _log("Movie details returned 404 Not Found.");
                    return null;
                }
                detailsResponse.EnsureSuccessStatusCode();

                var detailsJson = await detailsResponse.Content.ReadAsStringAsync();
                var details = JObject.Parse(detailsJson);
                var releaseDateStr = details["release_date"]?.ToString();
               
                int? releaseYear = null;

                if (DateTime.TryParse(releaseDateStr, out var releaseDate))
                {
                    releaseYear = releaseDate.Year;
                }

                var metadata = new MediaMetadata
                {
                    Title = movieTitle,
                    Type = "Movie",
                    Year = releaseYear,
                    Synopsis = details["overview"]?.ToString() ?? "",
                    Rating = "N/A",
                    PG = details["adult"]?.ToObject<bool>() == true ? "18+" : "PG-13",
                    PosterUrl = string.IsNullOrWhiteSpace(posterPath) ? "" : "https://image.tmdb.org/t/p/w500" + posterPath
                };

                string normalizedKey = NormalizeTitleKey(metadata.Title);
                await _cache.SaveMetadataAsync(normalizedKey, metadata);
                return metadata;
            }
            catch (HttpRequestException e)
            {
                _log($"HTTP request failed: {e.Message}");
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

        private async Task<(string title, string overview, string rating, string poster)> GetTvShowDetailsAsync(int tvId)
        {
            using var client = new HttpClient();
            var url = $"https://api.themoviedb.org/3/tv/{tvId}?api_key={_apiKey}";
            _log($"Fetching TV details: {url}");

            var json = await client.GetStringAsync(url);
            var data = JObject.Parse(json);

            string rating = data["content_ratings"]?["results"]?
                .FirstOrDefault(r => r["iso_3166_1"]?.ToString() == "US")?["rating"]?.ToString() ?? "N/A";

            return (
                title: data["name"]?.ToString(),
                overview: data["overview"]?.ToString(),
                rating: rating,
                poster: "https://image.tmdb.org/t/p/w500" + data["poster_path"]?.ToString()
            );
        }
        private string NormalizeTitleKey(string title)
        {
            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "") // remove punctuation
                        .Trim(); // remove surrounding whitespace
        }

    }


}
