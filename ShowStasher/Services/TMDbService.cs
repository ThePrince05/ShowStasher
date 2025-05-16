using System;
using System.Collections.Generic;
using System.Configuration;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ShowStasher.MVVM.Models;

namespace ShowStasher.Services
{
    public class TMDbService
    {
        private readonly string _apiKey;

        public TMDbService(string apiKey)
        {
            _apiKey = apiKey;
        }

        public async Task<MediaMetadata?> GetSeriesMetadataAsync(string title, int? season = null, int? episode = null)
        {
            var tvId = await GetTvShowIdAsync(title);
            if (tvId == null) return null;

            string episodeTitle = null;

            if (season.HasValue && episode.HasValue)
            {
                episodeTitle = await GetEpisodeTitleAsync(tvId.Value, season.Value, episode.Value);
            }

            // Basic series details
            var details = await GetTvShowDetailsAsync(tvId.Value);

            return new MediaMetadata
            {
                Title = details.title,
                Type = "Series",
                Synopsis = details.overview,
                Rating = "N/A", // Optional: fetch via certifications
                PG = details.rating,
                PosterUrl = details.poster,
                Season = season,
                Episode = episode,
                EpisodeTitle = episodeTitle
            };
        }

        private async Task<int?> GetTvShowIdAsync(string title)
        {
            using var client = new HttpClient();
            var url = $"https://api.themoviedb.org/3/search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(title)}";

            var json = await client.GetStringAsync(url);
            var data = JObject.Parse(json);
            var first = data["results"]?.FirstOrDefault();
            return first?["id"]?.Value<int>();
        }

        private async Task<string?> GetEpisodeTitleAsync(int tvId, int season, int episode)
        {
            using var client = new HttpClient();
            var url = $"https://api.themoviedb.org/3/tv/{tvId}/season/{season}/episode/{episode}?api_key={_apiKey}";

            var json = await client.GetStringAsync(url);
            var data = JObject.Parse(json);
            return data["name"]?.ToString();
        }
        public async Task<MediaMetadata?> GetMovieMetadataAsync(string title)
        {
            using var client = new HttpClient();

            // 1. Search for movie
            var searchUrl = $"https://api.themoviedb.org/3/search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(title)}";
            var searchJson = await client.GetStringAsync(searchUrl);
            var searchData = JObject.Parse(searchJson);
            var first = searchData["results"]?.FirstOrDefault();
            if (first == null) return null;

            int movieId = first["id"]?.Value<int>() ?? 0;
            string posterPath = first["poster_path"]?.ToString();
            string movieTitle = first["title"]?.ToString();

            // 2. Get movie details
            var detailsUrl = $"https://api.themoviedb.org/3/movie/{movieId}?api_key={_apiKey}";
            var detailsJson = await client.GetStringAsync(detailsUrl);
            var details = JObject.Parse(detailsJson);

            string synopsis = details["overview"]?.ToString();
            string rating = "N/A"; // Optional: parse from certification endpoint if needed
            string pg = details["adult"]?.ToObject<bool>() == true ? "18+" : "PG-13";

            return new MediaMetadata
            {
                Title = movieTitle,
                Type = "Movie",
                Synopsis = synopsis,
                Rating = rating,
                PG = pg,
                PosterUrl = "https://image.tmdb.org/t/p/w500" + posterPath
            };
        }

        private async Task<(string title, string overview, string rating, string poster)> GetTvShowDetailsAsync(int tvId)
        {
            using var client = new HttpClient();
            var url = $"https://api.themoviedb.org/3/tv/{tvId}?api_key={_apiKey}";

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
    }

}
