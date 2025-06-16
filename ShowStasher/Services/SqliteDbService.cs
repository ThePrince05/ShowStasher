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
using static ShowStasher.MVVM.ViewModels.MainViewModel;

namespace ShowStasher.Services
{

    public class SqliteDbService
    {
        private readonly Action<string, AppLogLevel> _log;
        private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ShowStasher");

        private static readonly string DbFile = Path.Combine(AppDataFolder, "Database.db");
        private readonly string ConnectionString;

        public SqliteDbService(Action<string, AppLogLevel> log)
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
            );

            CREATE TABLE IF NOT EXISTS History (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            OriginalFileName TEXT NOT NULL,
            NewFileName TEXT NOT NULL,
            SourcePath TEXT NOT NULL,
            DestinationPath TEXT NOT NULL,
            MovedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Settings (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL
            );

        ";
            tableCmd.ExecuteNonQuery();

        }

        public async Task<MediaMetadata?> GetCachedMetadataAsync(string title, string type, int? year = null, int? season = null, int? episode = null)
        {
            string normalizedTitle = NormalizeTitleKey(title);
            type = type.Trim().ToLowerInvariant();

            _log($"Looking up cache for Title='{normalizedTitle}', Type='{type}', Year={year}, Season={season}, Episode={episode}", AppLogLevel.Debug);

            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText =
            @"
            SELECT Title, Type, Year, Synopsis, ""Cast"", Rating, PG, PosterUrl, Season, Episode, EpisodeTitle
            FROM MediaMetadataCache
            WHERE Title = $title AND Type = $type
                  AND Season = $season AND Episode = $episode
            ";

            int seasonValue = season ?? SentinelValue;
            int episodeValue = episode ?? SentinelValue;

            selectCmd.Parameters.AddWithValue("$title", normalizedTitle);
            selectCmd.Parameters.AddWithValue("$type", type);
            selectCmd.Parameters.AddWithValue("$year", year ?? (object)DBNull.Value);
            selectCmd.Parameters.AddWithValue("$season", seasonValue);
            selectCmd.Parameters.AddWithValue("$episode", episodeValue);

            using var reader = await selectCmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                _log($"Cache hit for Title='{normalizedTitle}', Type='{type}', Season={seasonValue}, Episode={episodeValue}", AppLogLevel.Success);

                return new MediaMetadata
                {
                    Title = reader.GetString(0),
                    Type = reader.GetString(1),
                    Year = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    Synopsis = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Cast = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Rating = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    PG = reader.IsDBNull(6) ? null : reader.GetString(6),
                    PosterUrl = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Season = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    Episode = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    EpisodeTitle = reader.IsDBNull(10) ? null : reader.GetString(10),
                };
            }

            _log($"Cache miss for Title='{normalizedTitle}', Type='{type}', Season={seasonValue}, Episode={episodeValue}", AppLogLevel.Debug);
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

            _log($"Saved metadata for '{normalizedKey}', Type='{metadata.Type}', Season={seasonValue}, Episode={episodeValue} (Rows affected: {affected})", AppLogLevel.Success);
        }

        public async Task SaveMoveHistoryAsync(MoveHistory history)
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            string sql = @"
        INSERT INTO History 
            (OriginalFileName, NewFileName, SourcePath, DestinationPath, MovedAt)
        VALUES 
            (@OriginalFileName, @NewFileName, @SourcePath, @DestinationPath, @MovedAt);";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@OriginalFileName", history.OriginalFileName);
            command.Parameters.AddWithValue("@NewFileName", history.NewFileName);
            command.Parameters.AddWithValue("@SourcePath", history.SourcePath);
            command.Parameters.AddWithValue("@DestinationPath", history.DestinationPath);
            command.Parameters.AddWithValue("@MovedAt", history.MovedAt.ToString("o")); // ISO format

            await command.ExecuteNonQueryAsync();
        }

        public Task DeleteMoveHistoryAsync(int id)
        {
            // Run the blocking DB calls on a ThreadPool thread
            return Task.Run(() =>
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open(); // synchronous, so on background thread
                using var command = new SqliteCommand("DELETE FROM History WHERE Id = @id", connection);
                command.Parameters.AddWithValue("@id", id);
                command.ExecuteNonQuery(); // synchronous delete
            });
        }


        public Task ClearAllHistoryAsync()
        {
            return Task.Run(() =>
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open(); // synchronous, on background thread
                using var command = new SqliteCommand("DELETE FROM History", connection);
                command.ExecuteNonQuery(); // synchronous delete
            });
        }


        public async Task<List<MoveHistory>> GetAllMoveHistoryAsync()
        {
            var list = new List<MoveHistory>();

            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM History ORDER BY MovedAt DESC";
            using var command = new SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                list.Add(new MoveHistory
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    OriginalFileName = reader.GetString(reader.GetOrdinal("OriginalFileName")),
                    NewFileName = reader.GetString(reader.GetOrdinal("NewFileName")),
                    SourcePath = reader.GetString(reader.GetOrdinal("SourcePath")),
                    DestinationPath = reader.GetString(reader.GetOrdinal("DestinationPath")),
                    MovedAt = reader.GetDateTime(reader.GetOrdinal("MovedAt"))
                });
            }

            return list;
        }

        private string NormalizeTitleKey(string title)
        {
            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "") // remove punctuation
                        .Trim(); // remove surrounding whitespace
        }

        public async Task<string?> GetSettingAsync(string key)
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM Settings WHERE Key = @key";
            command.Parameters.AddWithValue("@key", key);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString();
        }

        public async Task SaveOrUpdateSettingAsync(string key, string value)
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
                command.CommandText = @"
            INSERT INTO Settings (Key, Value)
            VALUES (@key, @value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
         ";

            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", value);

            await command.ExecuteNonQueryAsync();
        }


    }
}
