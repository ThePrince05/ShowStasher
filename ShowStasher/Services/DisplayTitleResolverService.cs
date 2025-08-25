using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ShowStasher.MVVM.ViewModels.MainViewModel;

namespace ShowStasher.Services
{
    public class DisplayTitleResolverService
    {
        private readonly TMDbService _tmdbService;
        private readonly JikanService _jikanService;
        private readonly Action<string, AppLogLevel> _log;

        public DisplayTitleResolverService(TMDbService tmdbService, JikanService jikanService, Action<string, AppLogLevel> log)
        {
            _tmdbService = tmdbService;
            _jikanService = jikanService;
            _log = log;
        }

        public async Task<string> ResolveDisplayTitleAsync(ParsedMediaInfo parsed, bool isOfflineMode)
        {
            string resultTitle;
            if (isOfflineMode || string.IsNullOrWhiteSpace(parsed?.Title))
            {
                resultTitle = parsed?.Title ?? string.Empty;
                _log($"[DisplayTitleResolver] Offline mode or empty title. Returning: '{resultTitle}'", AppLogLevel.Debug);
                return resultTitle;
            }

            if (parsed.Type?.Equals("Anime", StringComparison.OrdinalIgnoreCase) == true)
            {
                var animeTitle = await _jikanService.GetDisplayTitleAsync(parsed.Title, parsed.Year);
                if (!string.IsNullOrWhiteSpace(animeTitle))
                {
                    _log($"[DisplayTitleResolver] Jikan resolved: '{animeTitle}'", AppLogLevel.Debug);
                    return animeTitle;
                }
            }
            else
            {
                var tmdbTitle = await _tmdbService.GetDisplayTitleAsync(parsed.Title, parsed.Type, parsed.Year);
                if (!string.IsNullOrWhiteSpace(tmdbTitle))
                {
                    _log($"[DisplayTitleResolver] TMDb resolved: '{tmdbTitle}'", AppLogLevel.Debug);
                    return tmdbTitle;
                }
            }

            _log($"[DisplayTitleResolver] Fallback to filename: '{parsed.Title}'", AppLogLevel.Debug);
            return parsed.Title;
        }
    }
}
