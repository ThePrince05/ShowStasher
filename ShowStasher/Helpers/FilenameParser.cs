﻿using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShowStasher.Helpers
{


    public static class FilenameParser
    {
        private static readonly Regex SeasonEpisodeRegex = new(
         @"\b[Ss](\d{1,2})[Ee](\d{1,2})\b|\b(\d{1,2})[xX](\d{1,2})\b",
         RegexOptions.Compiled);

        // Catch "- 02" as anime fallback if needed
        private static readonly Regex DashEpisodeRegex = new(
            @"-\s*(\d{1,2})$", RegexOptions.Compiled);

        private static readonly Regex YearRegex = new(
        @"\b(19|20)\d{2}\b", RegexOptions.Compiled);

        // new: match "v2", "v10", etc. even when glued to digits
        private static readonly Regex VersionGarbageRegex = new(
            @"v\d+\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        private static readonly string[] KnownGarbageWords = {
            // Resolutions
            "1080p", "720p", "480p", "2160p", "4K",

            // Source and Release Groups
            "WEBRip", "WEB-DL", "BluRay", "BRRip", "BDRip", "DVDRip", "HDTV", "CAM", "TS", "TC", "SCREENER",
            "NF", "AMZN", "HMAX", "DSNP", "HULU", "iT", "ATVP", "PCOK", "STAN", "MUBI", "PLAY", "BONE",

            // Encoding and Codecs
            "x264", "x265", "HEVC", "AVC", "H.264", "H.265", "10bit", "8bit",

            // Audio Formats
            "DDP5.1", "DDP5.1", "DD5.1", "AAC","AAC2.0", "TrueHD", "Atmos", "Opus", "DTS-HD", "MA", "FLAC", "MP3", "OGG",

            // Miscellaneous
            "YIFY", "RARBG", "PSA", "GalaxyRG", "Joy", "Subbed", "Dual Audio", "Multi Sub", "Subs", "FanDub", "FanSub", "EngDub", "JapDub", "CMRG","UHD", "IAMABLE", "VXT", "FLUX", "DLWP", "REMASTERED",

            // Version and Edition Tags
            "Remux", "REPACK", "PROPER", "LIMITED", "EXTENDED", "UNRATED", "Director's Cut",

            // Video Quality
            "HDR", "HDR10", "HDR10+", "SDR", "DV", "DoVi", "DolbyVision",

            // Language Markers
            "H", "English", "AMZN", "GERMAN"

        };


        public static ParsedMediaInfo Parse(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // Extract year from original filename
            int? year = null;
            Match yearMatch = YearRegex.Match(fileName);
            if (yearMatch.Success)
                year = int.Parse(yearMatch.Value);

            // Remove version tokens (e.g., "v2")
            var cleaned = Regex.Replace(fileName, @"\bv\d+\b", "", RegexOptions.IgnoreCase);
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

            string title = match.Success && match.Index >= 0 && match.Index <= cleaned.Length
                ? cleaned.Substring(0, match.Index).Trim()
                : cleaned;

            // Clean trailing dashes/dots/spaces
            title = Regex.Replace(title, @"[-._\s]+$", "").Trim();

            // Remove embedded year from title (e.g. "Anaconda 1997")
            if (year.HasValue)
            {
                title = Regex.Replace(title, $@"\b{year.Value}\b", "", RegexOptions.IgnoreCase).Trim();
            }

            // Anime detection
            string lower = fileName.ToLowerInvariant();
            bool isAnime = lower.Contains("anime") ||
                           lower.Contains("fansub") ||
                           lower.Contains("subbed") ||
                           lower.Contains("japan") ||
                           lower.Contains("crunchyroll") ||
                           Regex.IsMatch(lower, @"\[.*?\]") ||
                           Regex.IsMatch(lower, @"\b(cr|funimation|hidive|bd|bluray)\b", RegexOptions.IgnoreCase) ||
                           Regex.IsMatch(lower, @"\b(ep|s\d+e\d+)\b", RegexOptions.IgnoreCase) && lower.Contains("720p") && lower.Contains("hevc");

            string type = (season.HasValue && episode.HasValue) ? "Series" : "Movie";

            return new ParsedMediaInfo
            {
                Title = title,
                Season = season,
                Episode = episode,
                Type = type,
                Year = year
            };
        }




        public static string CleanFilenamePreservingTokens(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "";

            // 1. Remove parenthesis content (and the parentheses)
            name = Regex.Replace(name, @"\([^)]*\)", "", RegexOptions.IgnoreCase);

            // 2. Replace dots and underscores with spaces
            name = name.Replace('.', ' ').Replace('_', ' ');

            // 3. Remove bracketed groups [like this]
            name = Regex.Replace(name, @"\[[^\]]+\]", "", RegexOptions.IgnoreCase);

            // 4. Remove known garbage words
            var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            tokens = tokens.Where(t => !KnownGarbageWords.Contains(t)).ToList();

            // 5. Remove version tokens like v1, v2, v3
            tokens = tokens.Where(t => !VersionGarbageRegex.IsMatch(t)).ToList();

            // Helper to check if token is a valid numeric token to keep
            bool IsValidToken(string token)
            {
                // Allow 4-digit years like 1999 or 2025
                if (Regex.IsMatch(token, @"^(19|20)\d{2}$"))
                    return true;

                // Allow season/episode codes like S01E01 or 01x05
                if (Regex.IsMatch(token, @"^S\d{1,2}E\d{1,2}$", RegexOptions.IgnoreCase))
                    return true;
                if (Regex.IsMatch(token, @"^\d{1,2}x\d{1,2}$", RegexOptions.IgnoreCase))
                    return true;

                // Allow pure numbers like "86", "12"
                if (Regex.IsMatch(token, @"^\d+$"))
                    return true;

                // Allow decimal numbers like "3.10"
                if (Regex.IsMatch(token, @"^\d+\.\d+$"))
                    return true;

                return false;
            }

            // 6. Remove tokens with digits that are not valid (except the above)
            tokens = tokens.Where(t => !Regex.IsMatch(t, @"\d") || IsValidToken(t)).ToList();

            // 7. Join tokens with a single space and trim
            string cleaned = string.Join(" ", tokens).Trim();

            return cleaned;
        }
    }



    }
