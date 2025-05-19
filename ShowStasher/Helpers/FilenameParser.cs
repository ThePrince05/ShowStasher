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
            // Resolutions
            "1080p", "720p", "480p", "2160p", "4K",

            // Source and Release Groups
            "WEBRip", "WEB-DL", "BluRay", "BRRip", "BDRip", "DVDRip", "HDTV", "CAM", "TS", "TC", "SCREENER",
            "NF", "AMZN", "HMAX", "DSNP", "HULU", "iT", "ATVP", "PCOK", "STAN", "MUBI", "PLAY",

            // Encoding and Codecs
            "x264", "x265", "HEVC", "AVC", "H.264", "H.265", "10bit", "8bit",

            // Audio Formats
            "DDP5.1", "DDP5.1", "DD5.1", "AAC","AAC2.0", "TrueHD", "Atmos", "Opus", "DTS-HD", "MA", "FLAC", "MP3", "OGG",

            // Miscellaneous
            "YIFY", "RARBG", "PSA", "GalaxyRG", "Joy", "Subbed", "Dual Audio", "Multi Sub", "Subs", "FanDub", "FanSub", "EngDub", "JapDub",

            // Version and Edition Tags
            "Remux", "REPACK", "PROPER", "LIMITED", "EXTENDED", "UNRATED", "Director's Cut",

            // Video Quality
            "HDR", "HDR10", "HDR10+", "SDR", "DV", "DoVi", "DolbyVision",

            // Language Markers
            "H", "English", "AMZN"
               
        };


        // new: match "v2", "v10", etc. even when glued to digits
        private static readonly Regex VersionGarbageRegex = new(
            @"v\d+\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        public static ParsedFileInfo Parse(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var cleaned = Regex.Replace(fileName,
            @"\bv\d+\b",
            "",
            RegexOptions.IgnoreCase);

            cleaned = CleanFilenamePreservingTokens(cleaned);

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
            // 1. Replace parenthesis, . and _ with spaces
            name = Regex.Replace(name, @"\([^)]*\)", "", RegexOptions.IgnoreCase);

            name = name.Replace('.', ' ').Replace('_', ' ');

            // 2. Remove bracketed groups [LikeThis]
            name = Regex.Replace(name, @"\[[^\]]+\]", "", RegexOptions.IgnoreCase);

            // 3. Remove known garbage words (1080p, WEBRip, etc.)
            foreach (var word in KnownGarbageWords)
            {
                name = Regex.Replace(
                    name,
                    $@"\b{Regex.Escape(word)}\b",
                    "",
                    RegexOptions.IgnoreCase);
            }

            // 4. Remove version tokens like v1, v2, v10 (standalone, word-boundary)
            name = VersionGarbageRegex.Replace(name, "");

            // 5. Remove any token containing digits
            //    except 4-digit years and except SxxEyy or xxXyy patterns
            name = Regex.Replace(
                name,
                @"\b(?!\d{4}\b)(?![sS]\d{1,2}[eE]\d{1,2}\b)(?!\d{1,2}[xX]\d{1,2}\b)\w*\d+\w*\b",
                "",
                RegexOptions.IgnoreCase);

            // 6. Collapse multiple spaces into one and trim
            return Regex.Replace(name, @"\s{2,}", " ").Trim();
        }

    }



}
