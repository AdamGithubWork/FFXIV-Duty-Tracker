using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;

namespace MentorRouletteTracker.Storage;

/// <summary>
/// Creates and validates the history database schema and supports loading/saving through
/// HistorySession. It never reads or modifies the legacy Configuration JSON file.
/// Database operations are synchronous and are run on the session's dedicated writer.
/// </summary>
public sealed partial class SqliteHistoryStore
{
    public const int CurrentSchemaVersion = 3;
    private const int ApplicationId = 0x44555459; // DUTY

    public string DatabasePath { get; }

    public SqliteHistoryStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        var extension = Path.GetExtension(DatabasePath).ToLowerInvariant();
        if (extension is not (".db" or ".sqlite" or ".sqlite3"))
            throw new ArgumentException("Choose a database file, not an existing configuration or export.", nameof(databasePath));
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        connection.Open();

        // Create the tables and version marker together, or roll back everything.
        using (var transaction = connection.BeginTransaction())
        {
            var applicationId = ReadInteger(connection, transaction, "PRAGMA application_id;");
            var version = ReadInteger(connection, transaction, "PRAGMA user_version;");
            if (applicationId == ApplicationId && version is >= 1 and <= CurrentSchemaVersion)
            {
                // Reopening an existing supported database must preserve all records.
                var tableCount = ReadInteger(connection, transaction, """
                    SELECT COUNT(*) FROM sqlite_master
                    WHERE type = 'table' AND name IN ('duty_runs', 'duty_players', 'upload_queue');
                    """);
                if (tableCount != 3)
                    throw new InvalidDataException("History database tables are missing. Restore a backup before proceeding.");
            }
            else
            {
                var existingTables = ReadInteger(connection, transaction, """
                    SELECT COUNT(*) FROM sqlite_master
                    WHERE type = 'table' AND name NOT LIKE 'sqlite_%';
                    """);
                if (applicationId != 0 || version != 0 || existingTables != 0)
                    throw new InvalidDataException("Unrecognized history database or unsupported schema version. No migration was attempted.");

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = InitialSchema;
                command.ExecuteNonQuery();
                command.CommandText = $"PRAGMA application_id = {ApplicationId}; PRAGMA user_version = 1;";
                command.ExecuteNonQuery();
                version = 1;
            }

            if (version == 1)
            {
                using var migration = connection.CreateCommand();
                migration.Transaction = transaction;
                migration.CommandText = ImportSchema;
                migration.ExecuteNonQuery();
                migration.CommandText = "PRAGMA user_version = 2;";
                migration.ExecuteNonQuery();
                version = 2;
            }

            if (ReadInteger(connection, transaction, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='legacy_imports';") != 1)
                throw new InvalidDataException("The legacy import archive is missing. No history was imported.");

            if (version == 2)
            {
                using var migration = connection.CreateCommand();
                migration.Transaction = transaction;
                migration.CommandText = """
                    ALTER TABLE duty_runs ADD COLUMN history_order INTEGER NOT NULL DEFAULT 0;
                    UPDATE duty_runs SET history_order = COALESCE(legacy_record_index, rowid);
                    CREATE TABLE history_state (
                        id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                        revision INTEGER NOT NULL DEFAULT 0,
                        seen_players_json TEXT NOT NULL DEFAULT '[]'
                    ) STRICT;
                    INSERT INTO history_state(id) VALUES (1);
                    """;
                migration.ExecuteNonQuery();
                migration.CommandText = "SELECT source_bytes FROM legacy_imports LIMIT 1;";
                if (migration.ExecuteScalar() is byte[] source)
                {
                    using var archive = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(source).TrimStart('\uFEFF'));
                    migration.CommandText = "UPDATE history_state SET seen_players_json=$seen WHERE id=1;";
                    migration.Parameters.AddWithValue("$seen", LegacySeenJson(archive.RootElement));
                    migration.ExecuteNonQuery();
                }
                migration.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
                migration.ExecuteNonQuery();
            }
            if (ReadInteger(connection, transaction, "SELECT COUNT(*) FROM history_state WHERE id=1;") != 1)
                throw new InvalidDataException("The database history state is missing.");
            transaction.Commit();
        }

        using var settings = connection.CreateCommand();
        settings.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL;";
        settings.ExecuteNonQuery();
    }

    internal static string LegacySeenJson(JsonElement root)
    {
        if (!root.TryGetProperty("SeenPlayers", out var seen)) return "[]";
        if (seen.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The legacy seen-player cache must be an array.");
        return seen.GetRawText();
    }

    private static int ReadInteger(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private const string InitialSchema = """
        CREATE TABLE duty_runs (
            id TEXT NOT NULL PRIMARY KEY,
            timestamp TEXT NOT NULL,
            duty_name TEXT NOT NULL,
            duty_type TEXT NOT NULL DEFAULT '',
            joined_in_progress INTEGER NOT NULL DEFAULT 0 CHECK (joined_in_progress IN (0, 1)),
            wipe_count INTEGER NOT NULL DEFAULT 0,
            lalafell_count INTEGER NOT NULL DEFAULT 0,
            completed INTEGER NOT NULL DEFAULT 0 CHECK (completed IN (0, 1)),
            player_job TEXT NOT NULL DEFAULT '',
            recorder_name TEXT NOT NULL DEFAULT '',
            recorder_world TEXT NOT NULL DEFAULT '',
            duration_ticks INTEGER NOT NULL DEFAULT 0,
            note TEXT NOT NULL DEFAULT '',
            legacy_pvp_deaths INTEGER,
            is_pending INTEGER NOT NULL DEFAULT 0 CHECK (is_pending IN (0, 1)),
            checkpoint_utc TEXT,
            CHECK (is_pending = 0 OR completed = 0)
        ) STRICT;

        CREATE TABLE duty_players (
            duty_id TEXT NOT NULL REFERENCES duty_runs(id) ON DELETE CASCADE,
            party_index INTEGER NOT NULL CHECK (party_index >= 0),
            name TEXT NOT NULL,
            world TEXT NOT NULL DEFAULT '',
            job TEXT NOT NULL DEFAULT '',
            race TEXT NOT NULL DEFAULT '',
            gender TEXT NOT NULL DEFAULT '',
            status TEXT NOT NULL DEFAULT '',
            death_count INTEGER NOT NULL DEFAULT 0,
            pvp_scoreboard_deaths INTEGER,
            PRIMARY KEY (duty_id, party_index)
        ) STRICT;

        -- Reserved for a future opt-in upload implementation. No uploader exists yet.
        CREATE TABLE upload_queue (
            submission_id TEXT NOT NULL PRIMARY KEY,
            duty_id TEXT NOT NULL UNIQUE REFERENCES duty_runs(id) ON DELETE CASCADE,
            payload_version INTEGER NOT NULL,
            payload_json TEXT NOT NULL,
            queued_at_utc TEXT NOT NULL,
            next_attempt_utc TEXT,
            attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0)
        ) STRICT;

        CREATE INDEX idx_duty_runs_timestamp ON duty_runs(timestamp);
        CREATE INDEX idx_duty_runs_type_completed ON duty_runs(duty_type, completed);
        CREATE UNIQUE INDEX idx_single_pending_duty ON duty_runs(is_pending) WHERE is_pending = 1;
        CREATE INDEX idx_duty_players_identity ON duty_players(name, world);
        CREATE INDEX idx_upload_queue_retry ON upload_queue(next_attempt_utc);
        """;

    private const string ImportSchema = """
        CREATE TABLE legacy_imports (
            source_sha256 TEXT NOT NULL PRIMARY KEY,
            source_bytes BLOB NOT NULL,
            imported_at_utc TEXT NOT NULL,
            historical_duties INTEGER NOT NULL,
            pending_duties INTEGER NOT NULL,
            player_snapshots INTEGER NOT NULL
        ) STRICT;

        ALTER TABLE duty_runs ADD COLUMN legacy_import_id TEXT REFERENCES legacy_imports(source_sha256);
        ALTER TABLE duty_runs ADD COLUMN legacy_record_index INTEGER;
        CREATE UNIQUE INDEX idx_legacy_record ON duty_runs(legacy_import_id, legacy_record_index)
            WHERE legacy_import_id IS NOT NULL;
        """;
}
