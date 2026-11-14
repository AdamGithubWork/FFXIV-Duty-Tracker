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
    private HistoryWrite? pendingDuty;
    private HistoryWrite? recoverySnapshot;
    private bool retryFullSave;
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
        recoveryPath = Path.Combine(directory, prefix + Guid.NewGuid().ToString("N") + ".json");
        data = Task.Run(store.LoadHistory).GetAwaiter().GetResult();
        RecoverHistory(Directory.EnumerateFiles(directory, prefix + "*.json").ToArray());
        recoverySnapshot = SqliteHistoryStore.Capture(data);
    }

    private void RecoverHistory(string[] paths)
    {
        var recoveries = paths.Select(ReadRecovery).OrderBy(recovery => recovery.Revision).ToList();
        foreach (var group in recoveries.Where(recovery => recovery.Revision >= store!.Revision)
                     .GroupBy(recovery => recovery.Revision))
        {
            if (group.Select(recovery => JsonSerializer.Serialize(SqliteHistoryStore.Capture(recovery.Data)))
                .Distinct(StringComparer.Ordinal).Count() > 1)
                throw new InvalidDataException("Recovery files contain different edits for the same database revision.");
        }

        foreach (var recovery in recoveries)
        {
            if (recovery.Revision == null)
            {
                if (JsonSerializer.Serialize(recovery.Data) != JsonSerializer.Serialize(data))
                    throw new InvalidDataException($"Recovery file {recovery.Path} has no database revision. Its edits need review.");
                store!.SaveHistory(SqliteHistoryStore.Capture(data));
            }
            else if (recovery.Revision < store!.Revision)
            {
                // Keep older edits available for review without replacing newer history.
                var archivedPath = recovery.Path + ".stale";
                File.Move(recovery.Path, archivedPath, overwrite: false);
                try
                {
                    reportError?.Invoke(new InvalidDataException($"Skipped an older recovery file. A copy is at {archivedPath}."));
                }
                catch
                {
                    // Logging shouldn't prevent history from loading.
                }
                continue;
            }
            else
            {
                if (recovery.Revision != store!.Revision)
                    throw new InvalidDataException($"Recovery file {recovery.Path} belongs to a newer database revision.");

                store.SaveHistory(SqliteHistoryStore.Capture(recovery.Data));
                data.Records.Clear();
                data.Records.AddRange(recovery.Data.Records);
                data.PendingDuty = recovery.Data.PendingDuty;
                data.SeenPlayers.Clear();
                data.SeenPlayers.AddRange(recovery.Data.SeenPlayers);
                data.RecordIds.Clear();
                foreach (var (record, id) in recovery.Data.RecordIds)
                    data.RecordIds.Add(record, id);
            }

            File.Delete(recovery.Path);
        }
    }

    private static (string Path, long? Revision, HistoryData Data) ReadRecovery(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var records = root.GetProperty("Records").Deserialize<List<DutyRecord>>()
            ?? throw new InvalidDataException($"Recovery file {path} has no records array.");
        var players = root.GetProperty("SeenPlayers").Deserialize<List<SeenPlayer>>()
            ?? throw new InvalidDataException($"Recovery file {path} has no seen-player array.");
        var pendingDuty = root.GetProperty("PendingDuty").Deserialize<DutyRecord>();
        var recovered = new HistoryData { Records = records, SeenPlayers = players, PendingDuty = pendingDuty };
        foreach (var record in records.Concat(pendingDuty == null ? Enumerable.Empty<DutyRecord>() : new[] { pendingDuty }))
        {
            record.Timestamp = record.Timestamp.ToUniversalTime().ToLocalTime();
            record.CheckpointUtc = record.CheckpointUtc?.ToUniversalTime();
        }
        if (!root.TryGetProperty("BaseRevision", out var revision))
            return (path, null, recovered);

        var ids = root.GetProperty("DutyIds").Deserialize<List<string>>()
            ?? throw new InvalidDataException($"Recovery file {path} has no duty IDs.");
        if (ids.Count != records.Count)
            throw new InvalidDataException($"Recovery file {path} has a different number of records and IDs.");
        for (int i = 0; i < records.Count; i++)
            recovered.RecordIds.Add(records[i], ids[i]);
        if (pendingDuty != null)
            recovered.RecordIds.Add(pendingDuty, root.GetProperty("PendingDutyId").GetString()!);
        if (recovered.RecordIds.Values.Any(string.IsNullOrWhiteSpace) ||
            recovered.RecordIds.Values.Distinct(StringComparer.Ordinal).Count() != recovered.RecordIds.Count)
            throw new InvalidDataException($"Recovery file {path} contains missing or duplicate duty IDs.");

        return (path, revision.GetInt64(), recovered);
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
            pendingDuty = null;
            if (!running)
            {
                running = true;
                worker = Task.Run(WritePending);
            }
        }
    }

    public void SavePending()
    {
        lock (gate)
            ObjectDisposedException.ThrowIf(disposed, this);
        if (legacySave != null)
        {
            Save();
            return;
        }
        var snapshot = SqliteHistoryStore.CapturePending(data);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pendingDuty = snapshot;
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
            HistoryWrite? full;
            HistoryWrite? active;
            lock (gate)
            {
                if (pending == null && pendingDuty == null)
                {
                    running = false;
                    return;
                }
                full = pending;
                active = pendingDuty;
                pending = null;
                pendingDuty = null;
            }
            if (full != null)
                recoverySnapshot = full;
            if (active != null)
            {
                // Merge on the worker so the game thread only copies the active duty.
                recoverySnapshot = new HistoryWrite(
                    recoverySnapshot!.Duties.Where(row => !row.Pending).Concat(active.Duties).ToList(),
                    recoverySnapshot.SeenPlayersJson);
            }
            snapshot = full != null || retryFullSave ? recoverySnapshot! : active!;
            try
            {
                store!.SaveHistory(snapshot);
                if (File.Exists(recoveryPath)) File.Delete(recoveryPath!);
                retryFullSave = false;
                lock (gate) lastError = null;
            }
            catch (Exception ex)
            {
                retryFullSave = true;
                Exception failure;
                try
                {
                    WriteRecovery(recoverySnapshot!);
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
            writer.WriteNumber("BaseRevision", store!.Revision);
            writer.WritePropertyName("DutyIds");
            JsonSerializer.Serialize(writer, snapshot.Duties.Where(row => !row.Pending).Select(row => row.Id));
            writer.WriteString("PendingDutyId", snapshot.Duties.SingleOrDefault(row => row.Pending)?.Id);
            writer.WritePropertyName("Records");
            writer.WriteStartArray();
            foreach (var row in snapshot.Duties.Where(row => !row.Pending))
                JsonSerializer.Serialize(writer, row.ToRecord());
            writer.WriteEndArray();
            writer.WritePropertyName("PendingDuty");
            var active = snapshot.Duties.SingleOrDefault(row => row.Pending);
            if (active == null)
                writer.WriteNullValue();
            else
                JsonSerializer.Serialize(writer, active.ToRecord());
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
                if (running || pending != null || pendingDuty != null) continue;
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
