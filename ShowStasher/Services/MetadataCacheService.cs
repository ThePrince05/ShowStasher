using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShowStasher.Services
{

    public class MetadataCacheService
    {
        private readonly Action<string> _log;
        private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ShowStasher");

        private static readonly string DbFile = Path.Combine(AppDataFolder, "Database.db");
        private readonly string ConnectionString;

        public MetadataCacheService(Action<string> log)
        {
            _log = log;

            // Ensure the directory exists
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }

            ConnectionString = $"Data Source={DbFile}";

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var tableCmd = connection.CreateCommand();
            tableCmd.CommandText =
            @"
           CREATE TABLE IF NOT EXISTS MediaMetadataCache (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Title TEXT NOT NULL,
            Type TEXT NOT NULL,
            Year INTEGER,
            Synopsis TEXT,
            Cast TEXT,
            Rating INTEGER,
            PG TEXT,
            PosterUrl TEXT,
            Season INTEGER,
            Episode INTEGER,
            EpisodeTitle TEXT,
            UNIQUE(Title, Type, Season, Episode)
        )
        ";
            tableCmd.ExecuteNonQuery();

        }

        public async Task<MediaMetadata?> GetCachedMetadataAsync(string title, string type, int? year = null, int? season = null, int? episode = null)
        {
            string normalizedTitle = NormalizeTitleKey(title); // 🔑 Use normalized key!
            type = type.Trim().ToLowerInvariant();

            _log($"[DEBUG-LOOKUP] Looking for Title='{normalizedTitle}', Type='{type}', Year={year}, Season={season}, Episode={episode}");

            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText =
            @"
            SELECT Title, Type, Year, Synopsis, Rating, PG, PosterUrl, Season, Episode, EpisodeTitle
            FROM MediaMetadataCache
            WHERE Title = $title AND Type = $type
                  AND Season = $season AND Episode = $episode
            ";

            int seasonValue = season ?? -1;
            int episodeValue = episode ?? -1;

            selectCmd.Parameters.AddWithValue("$title", normalizedTitle);
            selectCmd.Parameters.AddWithValue("$type", type);
            selectCmd.Parameters.AddWithValue("$year", year ?? (object)DBNull.Value); // Year can stay nullable
            selectCmd.Parameters.AddWithValue("$season", seasonValue);
            selectCmd.Parameters.AddWithValue("$episode", episodeValue);

            using var reader = await selectCmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new MediaMetadata
                {
                    Title = reader.GetString(0),
                    Type = reader.GetString(1),
                    Year = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    Synopsis = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Rating = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    PG = reader.IsDBNull(5) ? null : reader.GetString(5),
                    PosterUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Season = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    Episode = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    EpisodeTitle = reader.IsDBNull(9) ? null : reader.GetString(9),
                };
            }

            _log($"[DEBUG-LOOKUP] No match found in cache for Title='{normalizedTitle}', Type='{type}', Season={season}, Episode={episode}");
            return null;
        }


        private const int SentinelValue = -1;

        public async Task SaveMetadataAsync(string normalizedKey, MediaMetadata metadata)
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            int seasonValue = metadata.Season ?? SentinelValue;
            int episodeValue = metadata.Episode ?? SentinelValue;

            var insertCmd = connection.CreateCommand();
            insertCmd.CommandText =
            insertCmd.CommandText =
            @"
            INSERT OR REPLACE INTO MediaMetadataCache 
            (Title, Type, Year, Synopsis, Rating, PG, PosterUrl, Season, Episode, EpisodeTitle, Cast)
            VALUES
            ($title, $type, $year, $synopsis, $rating, $pg, $posterUrl, $season, $episode, $episodeTitle, $cast)
            ";

            insertCmd.Parameters.AddWithValue("$title", normalizedKey);
            insertCmd.Parameters.AddWithValue("$type", metadata.Type.Trim().ToLowerInvariant());
            insertCmd.Parameters.AddWithValue("$year", metadata.Year ?? (object)DBNull.Value);
            insertCmd.Parameters.AddWithValue("$synopsis", metadata.Synopsis ?? (object)DBNull.Value);
            insertCmd.Parameters.AddWithValue("$cast", metadata.Cast ?? (object)DBNull.Value);
            insertCmd.Parameters.AddWithValue("$rating", metadata.Rating ?? (object)DBNull.Value);
            insertCmd.Parameters.AddWithValue("$pg", metadata.PG ?? (object)DBNull.Value);
            insertCmd.Parameters.AddWithValue("$posterUrl", metadata.PosterUrl ?? (object)DBNull.Value);
            insertCmd.Parameters.AddWithValue("$season", seasonValue);
            insertCmd.Parameters.AddWithValue("$episode", episodeValue);
            insertCmd.Parameters.AddWithValue("$episodeTitle", metadata.EpisodeTitle ?? (object)DBNull.Value);

            int affected = await insertCmd.ExecuteNonQueryAsync();

            _log($"[CACHE] Metadata saved for '{normalizedKey}' Type='{metadata.Type}', Season={seasonValue}, Episode={episodeValue} (Rows affected: {affected})");
        }



        private string NormalizeTitleKey(string title)
        {
            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "") // remove punctuation
                        .Trim(); // remove surrounding whitespace
        }

    }
}
