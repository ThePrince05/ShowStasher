using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ShowStasher.MVVM.Models;
using System.Net.Http;
using Newtonsoft.Json;

namespace ShowStasher.Services
{
    public class JikanService
    {
        private readonly HttpClient _httpClient;

        public JikanService()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.jikan.moe/v4/")
            };
        }

        public async Task<MediaMetadata?> GetAnimeMetadataAsync(string title, int? season = null, int? episode = null)
        {
            // Search for anime
            var searchResult = await SearchAnimeByTitle(title);
            if (searchResult == null) return null;

            var animeId = searchResult.MalId;

            string episodeTitle = null;
            if (episode.HasValue)
            {
                var episodeData = await FetchAnimeEpisodeTitle(animeId, episode.Value);
                episodeTitle = episodeData;
            }

            return new MediaMetadata
            {
                Title = searchResult.Title,
                Type = "Anime",
                Synopsis = searchResult.Synopsis,
                Rating = "N/A",
                PG = searchResult.Rating,
                PosterUrl = searchResult.ImageUrl,
                Season = season,
                Episode = episode,
                EpisodeTitle = episodeTitle
            };
        }
        private async Task<string?> FetchAnimeEpisodeTitle(int animeId, int episodeNumber)
        {
            using var client = new HttpClient();
            var url = $"https://api.jikan.moe/v4/anime/{animeId}/episodes/{episodeNumber}";

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            dynamic data = JsonConvert.DeserializeObject(json);
            return data?.data?.title as string;
        }


    }
}
