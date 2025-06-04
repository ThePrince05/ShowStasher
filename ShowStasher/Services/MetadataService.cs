using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShowStasher.Services
{
    public class MetadataService
    {
        private readonly TMDbService _tmdbService;
        private readonly JikanService _jikanService;
        private readonly MetadataCacheService _cacheService;

        public MetadataService(TMDbService tmdbService, JikanService jikanService, MetadataCacheService cacheService)
        {
            _tmdbService = tmdbService;
            _jikanService = jikanService;
            _cacheService = cacheService;
        }

        public async Task<MediaMetadata?> GetMetadataAsync(string title, string type, int? season = null, int? episode = null)
        {
            // Try cache first
            var cached = await _cacheService.GetCachedMetadataAsync(title, type, season, episode);
            if (cached != null) return cached;

            MediaMetadata? metadata = null;

            if (type.ToLower() == "anime")
            {
                metadata = await _jikanService.GetAnimeMetadataAsync(title, season, episode);
            }
            else if (type.ToLower() == "series")
            {
                metadata = await _tmdbService.GetSeriesMetadataAsync(title, season, episode);
            }
            else if (type.ToLower() == "movie")
            {
                metadata = await _tmdbService.GetMovieMetadataAsync(title);
            }

            if (metadata != null)
            {
                string normalizedKey = NormalizeTitleKey(metadata.Title);
                await _cacheService.SaveMetadataAsync(normalizedKey, metadata);
            }

            return metadata;
        }

        private string NormalizeTitleKey(string title)
        {
            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "") // remove punctuation
                        .Trim(); // remove surrounding whitespace
        }
    }

}
