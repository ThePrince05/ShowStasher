using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShowStasher.Helpers
{
   
        public class ParsedFileInfo
        {
            public string Title { get; set; } = string.Empty;
            public int? Season { get; set; }
            public int? Episode { get; set; }
            public string Type { get; set; } = "Unknown"; // "Movie", "Series", or "Anime"
        }

    public static class FilenameParser
    {
        private static readonly Regex SeasonEpisodeRegex = new(
            @"\b[Ss](\d{1,2})[Ee](\d{1,2})\b|\b(\d{1,2})[xX](\d{1,2})\b",
            RegexOptions.Compiled);

        // Catch "- 02" as anime fallback if needed
        private static readonly Regex DashEpisodeRegex = new(
            @"-\s*(\d{1,2})$", RegexOptions.Compiled);

        private static readonly string[] KnownGarbageWords = {
        "1080p","720p","480p","WEBRip","WEB","BluRay","BRRip",
        "x264","x265","HEVC","H.264","H.265","10bit","8bit",
        "DVDRip","HDRip","Dual Audio","Multi Sub","Subs","AC3",
        "DTS","PSA","YIFY","RARBG","HDTV","NF"
    };

        public static ParsedFileInfo Parse(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var cleaned = CleanFilenamePreservingTokens(fileName);

            int? season = null, episode = null;
            Match match = SeasonEpisodeRegex.Match(cleaned);

            if (match.Success)
            {
                if (match.Groups[1].Success && match.Groups[2].Success)
                {
                    season = int.Parse(match.Groups[1].Value);
                    episode = int.Parse(match.Groups[2].Value);
                }
                else if (match.Groups[3].Success && match.Groups[4].Success)
                {
                    season = int.Parse(match.Groups[3].Value);
                    episode = int.Parse(match.Groups[4].Value);
                }
            }
            else
            {
                match = DashEpisodeRegex.Match(cleaned);
                if (match.Success)
                {
                    season = 1;
                    episode = int.Parse(match.Groups[1].Value);
                }
            }

            string title;
            if (match.Success && match.Index >= 0 && match.Index <= cleaned.Length)
            {
                title = cleaned.Substring(0, match.Index).Trim();
            }
            else
            {
                title = cleaned;
            }

            title = Regex.Replace(title, @"[-._\s]+$", "").Trim();

            // 🎌 Anime detection based on keywords or patterns
            string lower = fileName.ToLowerInvariant();
            bool isAnime = lower.Contains("anime") ||
                           lower.Contains("fansub") ||
                           lower.Contains("subbed") ||
                           lower.Contains("japan") ||
                           lower.Contains("crunchyroll") ||
                           Regex.IsMatch(lower, @"\[.*?\]") || // [SubGroup] etc.
                           Regex.IsMatch(lower, @"\b(cr|funimation|hidive|bd|bluray)\b", RegexOptions.IgnoreCase) ||
                           Regex.IsMatch(lower, @"\b(ep|s\d+e\d+)\b", RegexOptions.IgnoreCase) && lower.Contains("720p") && lower.Contains("hevc");

            string type;
            if (season.HasValue && episode.HasValue)
                type = "Series";
            else
                type = "Movie"; 

            return new ParsedFileInfo
            {
                Title = title,
                Season = season,
                Episode = episode,
                Type = type
            };
        }


        private static string CleanFilenamePreservingTokens(string name)
        {
            // Replace . and _ with spaces
            name = name.Replace('.', ' ').Replace('_', ' ');

            // Remove bracketed groups [LikeThis]
            name = Regex.Replace(name, @"\[[^\]]+\]", "", RegexOptions.IgnoreCase);

            // Remove known garbage words
            foreach (var word in KnownGarbageWords)
            {
                name = Regex.Replace(
                    name,
                    $@"\b{Regex.Escape(word)}\b",
                    "",
                    RegexOptions.IgnoreCase);
            }

            // ✋ Remove any token containing digits
            // except 4-digit years and except SxxEyy/xxXyy patterns
            // \b(?!\d{4}\b)(?![sS]\d{1,2}[eE]\d{1,2}\b)(?!\d{1,2}[xX]\d{1,2}\b)\w*\d+\w*\b
            name = Regex.Replace(
                name,
                @"\b(?!\d{4}\b)(?![sS]\d{1,2}[eE]\d{1,2}\b)(?!\d{1,2}[xX]\d{1,2}\b)\w*\d+\w*\b",
                "",
                RegexOptions.IgnoreCase);  // Negative lookahead :contentReference[oaicite:0]{index=0}

            // Collapse spaces
            return Regex.Replace(name, @"\s{2,}", " ").Trim();
        }
    }





}
