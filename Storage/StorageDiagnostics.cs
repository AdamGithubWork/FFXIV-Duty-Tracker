#if DEBUG
using MentorRouletteTracker.DutyTracking;

namespace MentorRouletteTracker.Storage;

public static class StorageDiagnostics
{
    public static void Run(string directory)
    {
        var path = Path.Combine(Path.GetFullPath(directory), "storage-check-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var store = new SqliteHistoryStore(path);
            store.Initialize();
            using var session = new HistorySession(store);
            session.Records.Add(new DutyRecord
            {
                DutyName = "SQLite self-test", Note = "original",
                Party = new() { new PartyMemberSnapshot { Name = "Self-test", World = "Test", PvPScoreboardDeaths = null } }
            });
            session.Flush();
            var loaded = new SqliteHistoryStore(path).LoadHistory();
            if (loaded.Records.Count != 1 || loaded.Records[0].Party[0].PvPScoreboardDeaths != null)
                throw new InvalidDataException("SQLite insert/load check failed.");
            session.Records[0].Note = "edited";
            session.Flush();
            if (new SqliteHistoryStore(path).LoadHistory().Records[0].Note != "edited")
                throw new InvalidDataException("SQLite edit check failed.");
            session.Records.Clear();
            session.Flush();
            if (new SqliteHistoryStore(path).LoadHistory().Records.Count != 0)
                throw new InvalidDataException("SQLite delete check failed.");
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}
#endif
