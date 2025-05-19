using Microsoft.Data.Sqlite;
using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.Services
{

    public class MetadataCacheService
    {
        private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "ShowStasher");

        private static readonly string DbFile = Path.Combine(AppDataFolder, "metadataCache.db");
        private readonly string ConnectionString;

        public MetadataCacheService()
        {
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
                Title TEXT NOT NULL,
                Type TEXT NOT NULL,
                Synopsis TEXT,
                Rating TEXT,
                PG TEXT,
                PosterUrl TEXT,
                Season INTEGER,
                Episode INTEGER,
                EpisodeTitle TEXT,
                PRIMARY KEY (Title, Type, Season, Episode)
            )";
            tableCmd.ExecuteNonQuery();
        }

        public async Task<MediaMetadata?> GetCachedMetadataAsync(string title, string type, int? season = null, int? episode = null)
        {
            title = title.Trim().ToLowerInvariant();
            type = type.Trim().ToLowerInvariant();

            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText =
            @"
        SELECT Title, Type, Synopsis, Rating, PG, PosterUrl, Season, Episode, EpisodeTitle
        FROM MediaMetadataCache
        WHERE Title = $title AND Type = $type
              AND ((Season IS NULL AND $season IS NULL) OR Season = $season)
              AND ((Episode IS NULL AND $episode IS NULL) OR Episode = $episode)
        ";

            selectCmd.Parameters.AddWithValue("$title", title);
            selectCmd.Parameters.AddWithValue("$type", type);
            selectCmd.Parameters.AddWithValue("$season", season.HasValue ? season.Value : DBNull.Value);
            selectCmd.Parameters.AddWithValue("$episode", episode.HasValue ? episode.Value : DBNull.Value);

            using var reader = await selectCmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new MediaMetadata
                {
                    Title = reader.GetString(0),
                    Type = reader.GetString(1),
                    Synopsis = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Rating = reader.IsDBNull(3) ? null : reader.GetString(3),
                    PG = reader.IsDBNull(4) ? null : reader.GetString(4),
                    PosterUrl = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Season = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    Episode = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    EpisodeTitle = reader.IsDBNull(8) ? null : reader.GetString(8),
                };
            }

            return null;
        }

        public async Task SaveMetadataAsync(MediaMetadata metadata)
        {
            metadata.Title = metadata.Title?.Trim().ToLowerInvariant();
            metadata.Type = metadata.Type?.Trim().ToLowerInvariant();

            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var insertCmd = connection.CreateCommand();
            insertCmd.CommandText =
            @"
            INSERT INTO MediaMetadataCache (
                Title, Type, Synopsis, Rating, PG, PosterUrl, Season, Episode, EpisodeTitle)
            VALUES (
                $title, $type, $synopsis, $rating, $pg, $posterUrl, $season, $episode, $episodeTitle)
            ON CONFLICT(Title, Type, Season, Episode) DO UPDATE SET
                Synopsis = excluded.Synopsis,
                Rating = excluded.Rating,
                PG = excluded.PG,
                PosterUrl = excluded.PosterUrl,
                EpisodeTitle = excluded.EpisodeTitle
            ";

            insertCmd.Parameters.AddWithValue("$title", metadata.Title);
            insertCmd.Parameters.AddWithValue("$type", metadata.Type);
            insertCmd.Parameters.AddWithValue("$synopsis", (object?)metadata.Synopsis ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$rating", (object?)metadata.Rating ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$pg", (object?)metadata.PG ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$posterUrl", (object?)metadata.PosterUrl ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$season", metadata.Season.HasValue ? metadata.Season.Value : DBNull.Value);
            insertCmd.Parameters.AddWithValue("$episode", metadata.Episode.HasValue ? metadata.Episode.Value : DBNull.Value);
            insertCmd.Parameters.AddWithValue("$episodeTitle", (object?)metadata.EpisodeTitle ?? DBNull.Value);

            await insertCmd.ExecuteNonQueryAsync();
        }
    }


}
