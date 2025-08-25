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
            LookupKey TEXT,
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
            UNIQUE(LookupKey, Type, Season, Episode)
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

        public async Task<MediaMetadata?> GetCachedMetadataAsync(string lookupKey, string type, int? year = null, int? season = null, int? episode = null)
        {
            try
            {
                _log($"[DB] Enter GetCachedMetadataAsync: lookupKey='{lookupKey}', type='{type}', year={year}, season={season}, episode={episode}", AppLogLevel.Info);
                string normalizedLookupKey = NormalizeTitleKey(lookupKey);
                type = type.Trim().ToLowerInvariant();
                _log($"[DB] Normalized lookupKey='{normalizedLookupKey}', type='{type}'", AppLogLevel.Debug);
                using var connection = new SqliteConnection(ConnectionString);
                await connection.OpenAsync();
                _log("[DB] Connection opened for GetCachedMetadataAsync", AppLogLevel.Debug);

                var selectCmd = connection.CreateCommand();
                selectCmd.CommandText =
                @"
                SELECT LookupKey, Title, Type, Year, Synopsis, [Cast], Rating, PG, PosterUrl, Season, Episode, EpisodeTitle
                FROM MediaMetadataCache
                WHERE LookupKey = $lookupKey AND Type = $type
                      AND Season = $season AND Episode = $episode
                ";

                int seasonValue = season ?? SentinelValue;
                int episodeValue = episode ?? SentinelValue;

                selectCmd.Parameters.AddWithValue("$lookupKey", normalizedLookupKey);
                selectCmd.Parameters.AddWithValue("$type", type);
                selectCmd.Parameters.AddWithValue("$year", year ?? (object)DBNull.Value);
                selectCmd.Parameters.AddWithValue("$season", seasonValue);
                selectCmd.Parameters.AddWithValue("$episode", episodeValue);

                using var reader = await selectCmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    _log($"Cache hit for LookupKey='{normalizedLookupKey}', Type='{type}', Season={seasonValue}, Episode={episodeValue}", AppLogLevel.Success);

                    return new MediaMetadata
                    {
                        LookupKey = reader.IsDBNull(0) ? null : reader.GetString(0),
                        Title = reader.IsDBNull(1) ? null : reader.GetString(1),
                        Type = reader.GetString(2),
                        Year = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                        Synopsis = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Cast = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Rating = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        PG = reader.IsDBNull(7) ? null : reader.GetString(7),
                        PosterUrl = reader.IsDBNull(8) ? null : reader.GetString(8),
                        Season = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                        Episode = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                        EpisodeTitle = reader.IsDBNull(11) ? null : reader.GetString(11),
                    };
                }

                _log($"Cache miss for LookupKey='{normalizedLookupKey}', Type='{type}', Season={seasonValue}, Episode={episodeValue}", AppLogLevel.Debug);
                _log("[DB] Connection closed for GetCachedMetadataAsync", AppLogLevel.Debug);
                return null;
            }
            catch (Exception ex)
            {
                _log($"[DB][ERROR] Exception in GetCachedMetadataAsync: {ex.Message}\n{ex}", AppLogLevel.Error);
                return null;
            }
        }

        private const int SentinelValue = -1;


        public async Task SaveMetadataAsync(string lookupKey, MediaMetadata metadata)
        {
            try
            {
                _log($"[DB] Enter SaveMetadataAsync: lookupKey='{lookupKey}', metadata.Title='{metadata.Title}', metadata.Type='{metadata.Type}'", AppLogLevel.Info);
                using var connection = new SqliteConnection(ConnectionString);
                await connection.OpenAsync();
                _log("[DB] Connection opened for SaveMetadataAsync", AppLogLevel.Debug);

                int seasonValue = metadata.Season ?? SentinelValue;
                int episodeValue = metadata.Episode ?? SentinelValue;

                var insertCmd = connection.CreateCommand();
                insertCmd.CommandText =
                @"
                INSERT OR REPLACE INTO MediaMetadataCache 
                (LookupKey, Title, Type, Year, Synopsis, Rating, PG, PosterUrl, Season, Episode, EpisodeTitle, Cast)
                VALUES
                ($lookupKey, $title, $type, $year, $synopsis, $rating, $pg, $posterUrl, $season, $episode, $episodeTitle, $cast)
                ";

                insertCmd.Parameters.AddWithValue("$lookupKey", lookupKey);
                insertCmd.Parameters.AddWithValue("$title", metadata.Title ?? (object)DBNull.Value);
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

                // Log SQL command and parameters for debugging
                var paramDump = new StringBuilder();
                foreach (SqliteParameter p in insertCmd.Parameters)
                {
                    paramDump.AppendLine($"  {p.ParameterName}: '{p.Value}'");
                }
                _log($"[SQL DEBUG] Command: {insertCmd.CommandText}\nParameters:\n{paramDump}", AppLogLevel.Debug);

                int affected = await insertCmd.ExecuteNonQueryAsync();
                _log($"[DB] Connection closed for SaveMetadataAsync", AppLogLevel.Debug);
                _log($"Saved metadata for LookupKey='{lookupKey}', Title='{metadata.Title}', Type='{metadata.Type}', Season={seasonValue}, Episode={episodeValue} (Rows affected: {affected})", AppLogLevel.Success);
            }
            catch (Exception ex)
            {
                _log($"[DB][ERROR] Exception in SaveMetadataAsync: {ex.Message}\n{ex}", AppLogLevel.Error);
            }
        }

        public async Task SaveMoveHistoryAsync(MoveHistory history)
        {
            try
            {
                _log($"[DB] Enter SaveMoveHistoryAsync: OriginalFileName='{history.OriginalFileName}', NewFileName='{history.NewFileName}'", AppLogLevel.Info);
                using var connection = new SqliteConnection(ConnectionString);
                await connection.OpenAsync();
                _log("[DB] Connection opened for SaveMoveHistoryAsync", AppLogLevel.Debug);

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
                _log("[DB] Connection closed for SaveMoveHistoryAsync", AppLogLevel.Debug);
            }
            catch (Exception ex)
            {
                _log($"[DB][ERROR] Exception in SaveMoveHistoryAsync: {ex.Message}\n{ex}", AppLogLevel.Error);
            }
        }

        public Task DeleteMoveHistoryAsync(int id)
        {
            _log($"[DB] Enter DeleteMoveHistoryAsync: id={id}", AppLogLevel.Info);
            return Task.Run(() =>
            {
                try
                {
                    using var connection = new SqliteConnection(ConnectionString);
                    connection.Open();
                    _log("[DB] Connection opened for DeleteMoveHistoryAsync", AppLogLevel.Debug);
                    using var command = new SqliteCommand("DELETE FROM History WHERE Id = @id", connection);
                    command.Parameters.AddWithValue("@id", id);
                    command.ExecuteNonQuery(); // synchronous delete
                    _log("[DB] Connection closed for DeleteMoveHistoryAsync", AppLogLevel.Debug);
                }
                catch (Exception ex)
                {
                    _log($"[DB][ERROR] Exception in DeleteMoveHistoryAsync: {ex.Message}\n{ex}", AppLogLevel.Error);
                }
            });
        }

        public Task ClearAllHistoryAsync()
        {
            _log("[DB] Enter ClearAllHistoryAsync", AppLogLevel.Info);
            return Task.Run(() =>
            {
                try
                {
                    using var connection = new SqliteConnection(ConnectionString);
                    connection.Open();
                    _log("[DB] Connection opened for ClearAllHistoryAsync", AppLogLevel.Debug);
                    using var command = new SqliteCommand("DELETE FROM History", connection);
                    command.ExecuteNonQuery(); // synchronous delete
                    _log("[DB] Connection closed for ClearAllHistoryAsync", AppLogLevel.Debug);
                }
                catch (Exception ex)
                {
                    _log($"[DB][ERROR] Exception in ClearAllHistoryAsync: {ex.Message}\n{ex}", AppLogLevel.Error);
                }
            });
        }

        public async Task<List<MoveHistory>> GetAllMoveHistoryAsync()
        {
            _log("[DB] Enter GetAllMoveHistoryAsync", AppLogLevel.Info);
            var list = new List<MoveHistory>();
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                await connection.OpenAsync();
                _log("[DB] Connection opened for GetAllMoveHistoryAsync", AppLogLevel.Debug);

                var sql = "SELECT * FROM History ORDER BY MovedAt DESC";
                using var command = new SqliteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    list.Add(new MoveHistory
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        OriginalFileName = reader["OriginalFileName"]?.ToString(),
                        NewFileName = reader["NewFileName"]?.ToString(),
                        SourcePath = reader["SourcePath"]?.ToString(),
                        DestinationPath = reader["DestinationPath"]?.ToString(),
                        MovedAt = DateTime.Parse(reader["MovedAt"]?.ToString() ?? DateTime.MinValue.ToString())
                    });
                }

                _log($"[DB] Loaded {list.Count} move history records.", AppLogLevel.Info);
                _log("[DB] Connection closed for GetAllMoveHistoryAsync", AppLogLevel.Debug);
                return list;
            }
            catch (Exception ex)
            {
                _log($"[DB][ERROR] Exception in GetAllMoveHistoryAsync: {ex.Message}\n{ex}", AppLogLevel.Error);
                return list;
            }
        }

        private string NormalizeTitleKey(string title)
        {
            return Regex.Replace(title.ToLowerInvariant(), @"[^\w\s]", "") // remove punctuation
                        .Trim(); // remove surrounding whitespace
        }

        public async Task<string?> GetSettingAsync(string key)
        {
            try
            {
                _log($"[DB] Enter GetSettingAsync: key='{key}'", AppLogLevel.Info);
                using var connection = new SqliteConnection(ConnectionString);
                await connection.OpenAsync();
                _log("[DB] Connection opened for GetSettingAsync", AppLogLevel.Debug);

                var command = connection.CreateCommand();
                command.CommandText = "SELECT Value FROM Settings WHERE Key = @key";
                command.Parameters.AddWithValue("@key", key);

                var result = await command.ExecuteScalarAsync();
                _log("[DB] Connection closed for GetSettingAsync", AppLogLevel.Debug);
                return result?.ToString();
            }
            catch (Exception ex)
            {
                _log($"[DB][ERROR] Exception in GetSettingAsync: {ex.Message}\n{ex}", AppLogLevel.Error);
                return null;
            }
        }

        public async Task SaveOrUpdateSettingAsync(string key, string value)
        {
            try
            {
                _log($"[DB] Enter SaveOrUpdateSettingAsync: key='{key}', value='{value}'", AppLogLevel.Info);
                using var connection = new SqliteConnection(ConnectionString);
                await connection.OpenAsync();
                _log("[DB] Connection opened for SaveOrUpdateSettingAsync", AppLogLevel.Debug);

                var command = connection.CreateCommand();
                    command.CommandText = @"
                INSERT INTO Settings (Key, Value)
                VALUES (@key, @value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
             ";

                command.Parameters.AddWithValue("@key", key);
                command.Parameters.AddWithValue("@value", value);

                await command.ExecuteNonQueryAsync();
                _log("[DB] Connection closed for SaveOrUpdateSettingAsync", AppLogLevel.Debug);
            }
            catch (Exception ex)
            {
                _log($"[DB][ERROR] Exception in SaveOrUpdateSettingAsync: {ex.Message}\n{ex}", AppLogLevel.Error);
            }
        }


    }
}
