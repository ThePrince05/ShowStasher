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

namespace ShowStasher.Services
{
    public class JikanService
    {
        private readonly HttpClient _httpClient;
        private readonly Action<string> _log;
        private readonly MetadataCacheService _metadataCacheService;

        public JikanService(MetadataCacheService cache, Action<string> log)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.jikan.moe/v4/")
            };
            _metadataCacheService = cache;
            _log = log;
        }

        public async Task<MediaMetadata?> GetAnimeMetadataAsync(string title, int? season, int? episode)
        {
            var cached = await _metadataCacheService.GetCachedMetadataAsync(title, "Series", season, episode);
            if (cached != null)
            {
                _log($"[Cache Hit] Anime '{title}' episode {episode} found.");
                return cached;
            }

            _log($"[Jikan] Searching for anime: '{title}'...");
            var anime = await SearchAnimeByTitleAsync(title);
            if (anime == null)
            {
                _log($"[Jikan] No match found for '{title}'.");
                return null;
            }

            // 🆕 Extract English title (fallback to original title if missing)
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
                _log($"[Jikan] Resolved Season {season.Value} MAL ID: {fetchMalId}");
            }

            _log($"[Jikan] Using title: {englishTitle}");
            _log($"[Jikan] Fetching ALL episodes for Anime ID {animeId}...");

            var episodes = await FetchAllEpisodesAsync(fetchMalId);
            MediaMetadata? requestedEpisodeMetadata = null;

            foreach (var ep in episodes)
            {
                if (string.IsNullOrWhiteSpace(ep.Title) || ep.EpisodeId == 0)
                    continue;

                var metadata = new MediaMetadata
                {
                    Title = englishTitle,
                    Type = "Series", // Unified type
                    Synopsis = synopsis,
                    PosterUrl = posterUrl,
                    PG = pgRating,
                    Rating = null,
                    Season = season ?? 1,
                    Episode = ep.EpisodeId,
                    EpisodeTitle = ep.Title
                };

                await _metadataCacheService.SaveMetadataAsync(normalizedTitle, metadata);
                _log($"[Jikan] Cached: Episode {metadata.Episode} - {metadata.EpisodeTitle}");

                if (episode.HasValue && ep.EpisodeId == episode.Value)
                {
                    requestedEpisodeMetadata = metadata;
                }
            }

            if (requestedEpisodeMetadata != null)
            {
                _log($"[Success] Found and cached episode {episode.Value} for '{englishTitle}'.");
                return requestedEpisodeMetadata;
            }

            _log($"[Info] Requested episode not found or not specified, returning base metadata.");

            // ⬇️ Updated fallback metadata to also use the English title
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

            await _metadataCacheService.SaveMetadataAsync(normalizedTitle, genericMetadata);
            _log($"[Generic] Saved base metadata for anime '{englishTitle}'");
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
                    _log($"[Jikan] No results found for \"{title}\".");
                    return null;
                }

                var bestMatch = animeList
                    .FirstOrDefault(a =>
                        string.Equals(a.Title, title, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a.TitleEnglish, title, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a.TitleJapanese, title, StringComparison.OrdinalIgnoreCase)) ?? animeList.First();

                _log($"[Jikan] Found anime: \"{bestMatch.Title}\" (ID: {bestMatch.MalId})");
                return bestMatch;
            }
            catch (Exception ex)
            {
                _log($"[Jikan] SearchAnimeByTitleAsync error: {ex.Message}");
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
                        _log($"[Jikan] Fetched page {currentPage} with {response.Data.Count} episodes.");
                    }

                    hasNextPage = response?.Pagination?.HasNextPage ?? false;
                    currentPage++;
                }
                catch (Exception ex)
                {
                    _log($"[Jikan] Error fetching episodes (page {currentPage}): {ex.Message}");
                    break;
                }

                // Optional: be nice to the API
                await Task.Delay(300);
            }
            while (hasNextPage);

            _log($"[Jikan] Total episodes fetched: {allEpisodes.Count}");
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
