using Microsoft.Data.Sqlite;
using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.Services
{
    public class MetadataCacheService
    {
        private const string DbFile = "metadataCache.db";
        private const string ConnectionString = $"Data Source={DbFile}";

        public MetadataCacheService()
        {
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
                PRIMARY KEY (Title, Type)
            )";
            tableCmd.ExecuteNonQuery();
        }

        // Next: implement methods below

        public async Task<MediaMetadata?> GetCachedMetadataAsync(string title, string type, int? season = null, int? episode = null)
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();

            // Build dynamic query
            selectCmd.CommandText =
            @"
    SELECT Title, Type, Synopsis, Rating, PG, PosterUrl, Season, Episode
    FROM MediaMetadataCache
    WHERE Title = $title AND Type = $type
    " + (season.HasValue ? "AND Season = $season " : "") + (episode.HasValue ? "AND Episode = $episode" : "");

            selectCmd.Parameters.AddWithValue("$title", title);
            selectCmd.Parameters.AddWithValue("$type", type);

            if (season.HasValue)
                selectCmd.Parameters.AddWithValue("$season", season.Value);
            if (episode.HasValue)
                selectCmd.Parameters.AddWithValue("$episode", episode.Value);

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
                };
            }

            return null;
        }


        public async Task SaveMetadataAsync(MediaMetadata metadata)
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var insertCmd = connection.CreateCommand();
            insertCmd.CommandText =
            @"
            INSERT INTO MediaMetadataCache (Title, Type, Synopsis, Rating, PG, PosterUrl, Season, Episode)
            VALUES ($title, $type, $synopsis, $rating, $pg, $posterUrl, $season, $episode)
            ON CONFLICT(Title, Type) DO UPDATE SET
              Synopsis = excluded.Synopsis,
              Rating = excluded.Rating,
              PG = excluded.PG,
              PosterUrl = excluded.PosterUrl,
              Season = excluded.Season,
              Episode = excluded.Episode
            ";
            insertCmd.Parameters.AddWithValue("$title", metadata.Title);
            insertCmd.Parameters.AddWithValue("$type", metadata.Type);
            insertCmd.Parameters.AddWithValue("$synopsis", (object?)metadata.Synopsis ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$rating", (object?)metadata.Rating ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$pg", (object?)metadata.PG ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$posterUrl", (object?)metadata.PosterUrl ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$season", (object?)metadata.Season ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$episode", (object?)metadata.Episode ?? DBNull.Value);

            await insertCmd.ExecuteNonQueryAsync();
        }

    }
}
