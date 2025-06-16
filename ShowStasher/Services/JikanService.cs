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
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using static ShowStasher.MVVM.ViewModels.MainViewModel;

namespace ShowStasher.Services
{
    public class JikanService
    {
        private readonly HttpClient _httpClient;
        private readonly Action<string, AppLogLevel> _log;
        private readonly SqliteDbService _dbService;

        public JikanService(SqliteDbService cache, Action<string, AppLogLevel> log)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.jikan.moe/v4/")
            };
            _dbService = cache;
            _log = log;
        }

        public async Task<MediaMetadata?> GetAnimeMetadataAsync(string title, int? season, int? episode)
        {
            var cached = await _dbService.GetCachedMetadataAsync(title, "Series", season, episode);
            if (cached != null)
            {
                _log($"Cache hit for anime '{title}', episode {episode ?? 0}.", AppLogLevel.Success);
                return cached;
            }

            _log($"Searching anime '{title}' via Jikan...", AppLogLevel.Info);
            var anime = await SearchAnimeByTitleAsync(title);
            if (anime == null)
            {
                _log($"No anime match found for '{title}'.", AppLogLevel.Warning);
                return null;
            }

            string englishTitle = anime.Titles?.FirstOrDefault(t => t.Type == "English")?.Title ?? anime.Title;
            string normalizedTitle = NormalizeTitleKey(englishTitle);
            string synopsis = anime.Synopsis ?? "No synopsis available.";
            string posterUrl = anime.Images?.Jpg?.ImageUrl ?? "";
            string pgRating = anime.Rating ?? "N/A";
            int animeId = anime.MalId;

            int fetchMalId = animeId;
            if (season.HasValue && season.Value > 1)
            {
                fetchMalId = await GetSeasonMalIdAsync(animeId, season.Value);
                _log($"Resolved Season {season.Value} MAL ID: {fetchMalId}.", AppLogLevel.Info);
            }

            _log($"Fetching episodes for Anime ID {fetchMalId}...", AppLogLevel.Info);

            var episodes = await FetchAllEpisodesAsync(fetchMalId);
            MediaMetadata? requestedEpisodeMetadata = null;

            foreach (var ep in episodes)
            {
                if (string.IsNullOrWhiteSpace(ep.Title) || ep.EpisodeId == 0)
                    continue;

                var metadata = new MediaMetadata
                {
                    Title = englishTitle,
                    Type = "Series",
                    Synopsis = synopsis,
                    PosterUrl = posterUrl,
                    PG = pgRating,
                    Rating = null,
                    Season = season ?? 1,
                    Episode = ep.EpisodeId,
                    EpisodeTitle = ep.Title
                };

                await _dbService.SaveMetadataAsync(normalizedTitle, metadata);
                _log($"Cached episode {metadata.Episode}: {metadata.EpisodeTitle}.", AppLogLevel.Success);

                if (episode.HasValue && ep.EpisodeId == episode.Value)
                    requestedEpisodeMetadata = metadata;
            }

            if (requestedEpisodeMetadata != null)
            {
                _log($"Found and cached episode {episode.Value} for '{englishTitle}'.", AppLogLevel.Success);
                return requestedEpisodeMetadata;
            }

            _log($"Requested episode not found or not specified. Returning base metadata.", AppLogLevel.Info);

            var genericMetadata = new MediaMetadata
            {
                Title = englishTitle,
                Type = "Series",
                Synopsis = synopsis,
                PosterUrl = posterUrl,
                PG = pgRating,
                Rating = null,
                Season = null,
                Episode = null
            };

            await _dbService.SaveMetadataAsync(normalizedTitle, genericMetadata);
            _log($"Saved base metadata for anime '{englishTitle}'.", AppLogLevel.Success);
            return genericMetadata;
        }


        private string NormalizeTitleKey(string title)
        {
            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "") // remove punctuation
                        .Trim(); // remove surrounding whitespace
        }
        private async Task<JikanAnimeData?> SearchAnimeByTitleAsync(string title)
        {
            try
            {
                string query = Uri.EscapeDataString(title);
                var response = await _httpClient.GetFromJsonAsync<JikanAnimeSearchResponse>(
                    $"https://api.jikan.moe/v4/anime?q={query}&limit=5");

                var animeList = response?.Data;

                if (animeList == null || animeList.Count == 0)
                {
                    _log($"No results found for \"{title}\".", AppLogLevel.Warning);
                    return null;
                }

                var bestMatch = animeList
                    .FirstOrDefault(a =>
                        string.Equals(a.Title, title, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a.TitleEnglish, title, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a.TitleJapanese, title, StringComparison.OrdinalIgnoreCase)) ?? animeList.First();

                _log($"Found anime '{bestMatch.Title}' (ID: {bestMatch.MalId}).", AppLogLevel.Success);
                return bestMatch;
            }
            catch (Exception ex)
            {
                _log($"SearchAnimeByTitleAsync error: {ex.Message}", AppLogLevel.Error);
                return null;
            }
        }


        private async Task<List<JikanEpisode>> FetchAllEpisodesAsync(int malId)
        {
            var allEpisodes = new List<JikanEpisode>();
            int currentPage = 1;
            bool hasNextPage;

            do
            {
                try
                {
                    string url = $"https://api.jikan.moe/v4/anime/{malId}/episodes?page={currentPage}";
                    var response = await _httpClient.GetFromJsonAsync<JikanEpisodeListResponse>(url);

                    if (response?.Data != null)
                    {
                        allEpisodes.AddRange(response.Data);
                        _log($"Fetched page {currentPage} with {response.Data.Count} episodes.", AppLogLevel.Info);
                    }

                    hasNextPage = response?.Pagination?.HasNextPage ?? false;
                    currentPage++;
                }
                catch (Exception ex)
                {
                    _log($"Error fetching episodes page {currentPage}: {ex.Message}", AppLogLevel.Error);
                    break;
                }

                // Be kind to the API
                await Task.Delay(300);
            }
            while (hasNextPage);

            _log($"Total episodes fetched: {allEpisodes.Count}.", AppLogLevel.Success);
            return allEpisodes;
        }

        private async Task<int> GetSeasonMalIdAsync(int baseMalId, int season)
        {
            var relResponse = await _httpClient.GetFromJsonAsync<JikanRelationResponse>(
                $"https://api.jikan.moe/v4/anime/{baseMalId}/relations");
            if (relResponse?.Data == null)
                throw new Exception($"No relations for anime ID {baseMalId}");

            var seRel = relResponse.Data
                .FirstOrDefault(r => r.RelationType.Equals("Sequel", StringComparison.OrdinalIgnoreCase)
                                     && r.Entry.Any(e => e.Name.Contains($"Season {season}")));
            if (seRel != null)
                return seRel.Entry.First().MalId;

            throw new Exception($"Season {season} not found in relations of anime {baseMalId}");
        }




    }
}
