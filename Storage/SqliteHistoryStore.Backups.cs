using Microsoft.Data.Sqlite;

namespace MentorRouletteTracker.Storage;

public sealed partial class SqliteHistoryStore
{
    internal string BackupDirectory => Path.Combine(Path.GetDirectoryName(DatabasePath)!, "backups");

    internal string CreateBackup()
    {
        Directory.CreateDirectory(BackupDirectory);
        var path = Path.Combine(BackupDirectory, $"history-manual-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.sqlite");
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
}
