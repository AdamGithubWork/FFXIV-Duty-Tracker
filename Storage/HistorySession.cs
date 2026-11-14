using System.Text.Json;
using MentorRouletteTracker.DutyTracking;

namespace MentorRouletteTracker.Storage;

public sealed class HistorySession : IDisposable
{
    private readonly HistoryData data;
    private readonly SqliteHistoryStore? store;
    private readonly Action<HistoryData>? legacySave;
    private readonly Action<Exception>? reportError;
    private readonly object gate = new();
    private readonly string? recoveryPath;
    private HistoryWrite? pending;
    private Task worker = Task.CompletedTask;
    private bool running;
    private bool disposed;
    private Exception? lastError;

    public List<DutyRecord> Records => data.Records;
    public List<SeenPlayer> SeenPlayers => data.SeenPlayers;
    public DutyRecord? PendingDuty { get => data.PendingDuty; set => data.PendingDuty = value; }

    public HistorySession(HistoryData data, Action<HistoryData> legacySave)
    {
        this.data = data;
        this.legacySave = legacySave;
    }

    public HistorySession(SqliteHistoryStore store, Action<Exception>? reportError = null)
    {
        this.store = store;
        this.reportError = reportError;
        var directory = Path.GetDirectoryName(store.DatabasePath)!;
        var prefix = Path.GetFileNameWithoutExtension(store.DatabasePath) + "-recovery-";
        if (Directory.Exists(directory) && Directory.EnumerateFiles(directory, prefix + "*.json").Any())
            throw new InvalidDataException("A recovery file needs to be reviewed before loading history.");
        recoveryPath = Path.Combine(directory, prefix + Guid.NewGuid().ToString("N") + ".json");
        data = Task.Run(store.LoadHistory).GetAwaiter().GetResult();
    }

    public void Save()
    {
        lock (gate)
            ObjectDisposedException.ThrowIf(disposed, this);
        if (legacySave != null) { legacySave(data); return; }
        var snapshot = SqliteHistoryStore.Capture(data);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // The latest snapshot includes earlier edits, so only keep one queued save.
            pending = snapshot;
            if (!running)
            {
                running = true;
                worker = Task.Run(WritePending);
            }
        }
    }

    private void WritePending()
    {
        while (true)
        {
            HistoryWrite snapshot;
            lock (gate)
            {
                if (pending == null) { running = false; return; }
                snapshot = pending;
                pending = null;
            }
            try
            {
                store!.SaveHistory(snapshot);
                if (File.Exists(recoveryPath)) File.Delete(recoveryPath!);
                lock (gate) lastError = null;
            }
            catch (Exception ex)
            {
                Exception failure;
                try
                {
                    WriteRecovery(snapshot);
                    failure = new IOException($"SQLite history was not saved. A recovery copy is at {recoveryPath}.", ex);
                }
                catch (Exception backupError)
                {
                    failure = new AggregateException("Could not save history or its recovery copy.", ex, backupError);
                }
                lock (gate) lastError = failure;
                try { reportError?.Invoke(failure); }
                catch { /* A logging failure must not terminate the history writer. */ }
            }
        }
    }

    private void WriteRecovery(HistoryWrite snapshot)
    {
        var temporaryPath = recoveryPath + ".tmp";
        using (var stream = File.Create(temporaryPath))
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Records");
            writer.WriteStartArray();
            foreach (var row in snapshot.Duties.Where(row => !row.Pending)) writer.WriteRawValue(row.Json);
            writer.WriteEndArray();
            writer.WritePropertyName("PendingDuty");
            var active = snapshot.Duties.SingleOrDefault(row => row.Pending);
            if (active == null) writer.WriteNullValue(); else writer.WriteRawValue(active.Json);
            writer.WritePropertyName("SeenPlayers");
            writer.WriteRawValue(snapshot.SeenPlayersJson);
            writer.WriteEndObject();
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporaryPath, recoveryPath!, overwrite: true);
    }

    public void Flush()
    {
        Save();
        if (legacySave != null) return;
        while (true)
        {
            Task current;
            lock (gate) current = worker;
            current.GetAwaiter().GetResult();
            lock (gate)
            {
                if (running || pending != null) continue;
                if (lastError != null) throw new IOException("Could not finish saving history. Check the recovery file and plugin log.", lastError);
                return;
            }
        }
    }

    public void Dispose()
    {
        lock (gate) if (disposed) return;
        try { Flush(); }
        finally { lock (gate) disposed = true; }
    }
}
