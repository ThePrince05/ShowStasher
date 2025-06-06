using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ShowStasher.MVVM.Models;
using System.Net.Http;
using Newtonsoft.Json;
using ShowStasher.Helpers;
using System.Text.RegularExpressions;

namespace ShowStasher.Services
{
    public class JikanService
    {
        private readonly HttpClient _httpClient;
        private readonly Action<string> _log;
        private readonly MetadataCacheService _cache;

        public JikanService(MetadataCacheService cache, Action<string> log)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.jikan.moe/v4/")
            };
            _cache = cache;
            _log = log;
        }

        public async Task<MediaMetadata?> GetAnimeMetadataAsync(string title, int? season = null, int? episode = null)
        {
            // ✅ Check cache first
            var cached = await _cache.GetCachedMetadataAsync(title, "Anime", season, episode);
            if (cached != null)
            {
                _log($"[Cache] Found cached anime metadata for '{title}' S{season}E{episode}");
                return cached;
            }

            _log($"Searching for anime: '{title}'");

            var searchResult = await SearchAnimeByTitle(title);
            if (searchResult == null)
            {
                _log($"No anime found for title: '{title}'");
                return null;
            }

            var animeId = searchResult.MalId;
            _log($"Found anime '{searchResult.Title}' with MAL ID {animeId}");

            string episodeTitle = null;
            if (episode.HasValue)
            {
                _log($"Fetching episode {episode.Value} for anime ID {animeId}");
                episodeTitle = await FetchAnimeEpisodeTitle(animeId, episode.Value);
                _log($"Episode title: {episodeTitle ?? "Not found"}");
            }

            var metadata = new MediaMetadata
            {
                Title = searchResult.Title,
                Type = "Anime",
                Synopsis = searchResult.Synopsis,
                Rating = null,
                PG = searchResult.Rating,
                PosterUrl = searchResult.ImageUrl,
                Season = season,
                Episode = episode,
                EpisodeTitle = episodeTitle
            };

            // ✅ Save to cache
            string normalizedKey = NormalizeTitleKey(metadata.Title);
            await _cache.SaveMetadataAsync(normalizedKey, metadata);

            return metadata;
        }

        private async Task<JikanAnimeSearchResult?> SearchAnimeByTitle(string title)
        {
            var url = $"anime?q={Uri.EscapeDataString(title)}&limit=1";
            _log($"Requesting: {url}");

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _log($"Failed to search anime. Status: {(int)response.StatusCode} - {response.ReasonPhrase}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var searchResponse = JsonConvert.DeserializeObject<JikanAnimeSearchResponse>(json);
            return searchResponse?.Data?.FirstOrDefault();
        }

        private async Task<string?> FetchAnimeEpisodeTitle(int animeId, int episodeNumber)
        {
            var url = $"anime/{animeId}/episodes/{episodeNumber}";
            _log($"Requesting episode info: {url}");

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _log($"Failed to fetch episode title. Status: {(int)response.StatusCode} - {response.ReasonPhrase}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            dynamic data = JsonConvert.DeserializeObject(json);
            return data?.data?.title as string;
        }

        private string NormalizeTitleKey(string title)
        {
            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "") // remove punctuation
                        .Trim(); // remove surrounding whitespace
        }

    }
}
