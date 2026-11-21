using Microsoft.Data.Sqlite;

namespace MentorRouletteTracker.Storage;

public sealed partial class SqliteHistoryStore
{
    internal string BackupDirectory => Path.Combine(Path.GetDirectoryName(DatabasePath)!, "backups");

    internal string CreateBackup(bool beforeRestore = false)
    {
        Directory.CreateDirectory(BackupDirectory);
        var kind = beforeRestore ? "before-restore" : "manual";
        var path = Path.Combine(BackupDirectory, $"history-{kind}-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.sqlite");
        var temporaryPath = path + ".tmp";
        try
        {
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5
            }.ToString()))
            using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 5
            }.ToString()))
            {
                source.Open();
                backup.Open();
                source.BackupDatabase(backup);
                using var command = backup.CreateCommand();
                command.CommandText = "PRAGMA journal_mode = DELETE;";
                command.ExecuteNonQuery();
                command.CommandText = "PRAGMA quick_check;";
                if (!Equals(command.ExecuteScalar(), "ok"))
                    throw new InvalidDataException("The history backup failed its integrity check.");
            }
            File.Move(temporaryPath, path);
            return path;
        }
        catch
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try { File.Delete(temporaryPath + suffix); }
                catch { }
            }
            throw;
        }
    }

    internal static HistoryData ReadBackup(string path)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Noumenon-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var copyPath = Path.Combine(directory, "history.sqlite");
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5
            }.ToString()))
            using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = copyPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
            }.ToString()))
            {
                source.Open();
                var version = ReadInteger(source, null, "PRAGMA user_version;");
                if (ReadInteger(source, null, "PRAGMA application_id;") != ApplicationId ||
                    version is not (6 or 7 or 8 or 9 or 10 or 11 or 12 or CurrentSchemaVersion))
                    throw new InvalidDataException("Choose a supported Noumenon history backup.");
                copy.Open();
                source.BackupDatabase(copy);
            }

            // Upgrade the temporary copy, leaving the chosen backup untouched.
            var restored = new SqliteHistoryStore(copyPath).LoadHistory();
            if (restored.PendingDuty is { } unfinished)
            {
                if (unfinished.CheckpointUtc is { } checkpoint)
                {
                    var duration = checkpoint - unfinished.Timestamp.ToUniversalTime();
                    unfinished.TimeInDuty = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
                }
                restored.Records.Add(unfinished);
                restored.PendingDuty = null;
            }
            return restored;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch { }
        }
    }
}
