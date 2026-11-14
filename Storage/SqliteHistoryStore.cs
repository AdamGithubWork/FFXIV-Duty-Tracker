using Microsoft.Data.Sqlite;

namespace MentorRouletteTracker.Storage
{
    public sealed partial class SqliteHistoryStore
    {
        public const int CurrentSchemaVersion = 8;
        private const int ApplicationId = 0x44555459;

        public string DatabasePath { get; }

        public SqliteHistoryStore(string databasePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
            DatabasePath = Path.GetFullPath(databasePath);
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
            ConfigureConnection(connection);

            var applicationId = ReadInteger(connection, null, "PRAGMA application_id;");
            var version = ReadInteger(connection, null, "PRAGMA user_version;");
            if (applicationId == ApplicationId && version is 6 or 7)
                BackupBeforeUpgrade(connection);

            using (var transaction = connection.BeginTransaction())
            {
                applicationId = ReadInteger(connection, transaction, "PRAGMA application_id;");
                version = ReadInteger(connection, transaction, "PRAGMA user_version;");
                if (applicationId == 0 && version == 0 &&
                    ReadInteger(connection, transaction, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';") == 0)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = Schema;
                    command.ExecuteNonQuery();
                    command.CommandText = $"PRAGMA application_id = {ApplicationId}; PRAGMA user_version = {CurrentSchemaVersion};";
                    command.ExecuteNonQuery();
                }
                else
                {
                    if (applicationId != ApplicationId || version is not (6 or 7 or CurrentSchemaVersion))
                        throw new InvalidDataException("The history database version is not supported.");

                    if (version is 6 or 7)
                    {
                        using var migration = connection.CreateCommand();
                        migration.Transaction = transaction;
                        migration.CommandText = """
                            DROP INDEX idx_legacy_record;
                            ALTER TABLE duty_runs DROP COLUMN legacy_import_id;
                            ALTER TABLE duty_runs DROP COLUMN legacy_record_index;
                            DROP TABLE legacy_imports;
                            DROP TABLE IF EXISTS upload_queue;
                            PRAGMA user_version = 8;
                            """;
                        migration.ExecuteNonQuery();
                    }
                }

                var tableCount = ReadInteger(connection, transaction, """
                    SELECT COUNT(*) FROM sqlite_master
                    WHERE type='table' AND name IN ('duty_runs','duty_players','history_state');
                    """);
                if (tableCount != 3 || ReadInteger(connection, transaction, "SELECT COUNT(*) FROM history_state WHERE id=1;") != 1)
                    throw new InvalidDataException("History database tables or state are missing.");
                transaction.Commit();
            }

            using var settings = connection.CreateCommand();
            settings.CommandText = "PRAGMA journal_mode = WAL;";
            settings.ExecuteNonQuery();
        }

        private void BackupBeforeUpgrade(SqliteConnection connection)
        {
            var directory = Path.Combine(Path.GetDirectoryName(DatabasePath)!, "backups");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"history-before-v8-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.sqlite");
            using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
            backup.Open();
            ConfigureConnection(backup);
            connection.BackupDatabase(backup);
        }

        private static int ReadInteger(SqliteConnection connection, SqliteTransaction? transaction, string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private const string Schema = """
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

            CREATE TABLE history_state (
                id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                revision INTEGER NOT NULL DEFAULT 0
            ) STRICT;
            INSERT INTO history_state(id) VALUES(1);

            CREATE INDEX idx_duty_runs_timestamp ON duty_runs(timestamp);
            CREATE INDEX idx_duty_runs_type_completed ON duty_runs(duty_type, completed);
            CREATE UNIQUE INDEX idx_single_pending_duty ON duty_runs(is_pending) WHERE is_pending = 1;
            CREATE INDEX idx_duty_players_identity ON duty_players(name, world);
            """;
    }
}
