using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MentorRouletteTracker.DutyTracking;

namespace MentorRouletteTracker.Storage;

public sealed partial class SqliteHistoryStore
{
    private Dictionary<string, DutyWrite> savedDuties = new();
    private string seenFingerprint = "[]";
    private long revision;
    private string? pendingDutyId;
    internal long Revision => revision;

    public HistoryData LoadHistory()
    {
        if (!File.Exists(DatabasePath))
            throw new FileNotFoundException("The history database is missing.", DatabasePath);
        Initialize();
        using var connection = OpenExisting();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA quick_check;";
        if (!Equals(command.ExecuteScalar(), "ok")) throw new InvalidDataException("SQLite history integrity check failed.");
        command.CommandText = "SELECT seen_players_json, revision FROM history_state WHERE id=1;";
        List<SeenPlayer> seen;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidDataException("History state is missing.");
            seen = JsonSerializer.Deserialize<List<SeenPlayer>>(reader.GetString(0))
                ?? throw new InvalidDataException("Seen-player cache is invalid.");
            revision = reader.GetInt64(1);
        }
        var data = new HistoryData { SeenPlayers = seen };
        var identities = new Dictionary<string, DutyRecord>();
        var pendingIds = new HashSet<string>();
        command.CommandText = "SELECT * FROM duty_runs ORDER BY rowid;";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var record = new DutyRecord
                {
                    Timestamp = DateTime.Parse((string)reader["timestamp"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime(),
                    CheckpointUtc = reader["checkpoint_utc"] is DBNull
                        ? null
                        : DateTime.Parse((string)reader["checkpoint_utc"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
                    DutyName = (string)reader["duty_name"], DutyType = (string)reader["duty_type"],
                    JoinedInProgress = (long)reader["joined_in_progress"] != 0,
                    WipeCount = checked((int)(long)reader["wipe_count"]), LalafellCount = checked((int)(long)reader["lalafell_count"]),
                    Completed = (long)reader["completed"] != 0, PlayerJob = (string)reader["player_job"],
                    RecorderName = (string)reader["recorder_name"], RecorderWorld = (string)reader["recorder_world"],
                    TimeInDuty = TimeSpan.FromTicks((long)reader["duration_ticks"]), Note = (string)reader["note"],
                    PvPDeaths = reader["legacy_pvp_deaths"] is DBNull ? null : checked((int)(long)reader["legacy_pvp_deaths"])
                };
                string id = (string)reader["id"];
                bool pending = (long)reader["is_pending"] != 0;
                identities.Add(id, record);
                data.RecordIds.Add(record, id);
                if (pending)
                {
                    pendingIds.Add(id);
                    if (data.PendingDuty != null) throw new InvalidDataException("Multiple active duties found.");
                    data.PendingDuty = record;
                }
                else data.Records.Add(record);
            }
        }
        command.CommandText = "SELECT * FROM duty_players ORDER BY duty_id, party_index;";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (!identities.TryGetValue((string)reader["duty_id"], out var record))
                    throw new InvalidDataException("A player snapshot has no duty.");
                record.Party.Add(new PartyMemberSnapshot
                {
                    Name = (string)reader["name"], World = (string)reader["world"], Job = (string)reader["job"],
                    Race = (string)reader["race"], Gender = (string)reader["gender"], Status = (string)reader["status"],
                    DeathCount = checked((int)(long)reader["death_count"]),
                    PvPScoreboardDeaths = reader["pvp_scoreboard_deaths"] is DBNull ? null : checked((int)(long)reader["pvp_scoreboard_deaths"])
                });
            }
        }
        transaction.Commit();
        savedDuties = identities.ToDictionary(entry => entry.Key, entry =>
            DutyWrite.Capture(entry.Key, entry.Value, pendingIds.Contains(entry.Key)));
        seenFingerprint = JsonSerializer.Serialize(data.SeenPlayers);
        pendingDutyId = data.PendingDuty == null ? null : data.RecordIds[data.PendingDuty];
        return data;
    }

    internal static HistoryWrite Capture(HistoryData data)
    {
        var rows = new List<DutyWrite>();
        void Add(DutyRecord record, bool pending)
        {
            if (!data.RecordIds.TryGetValue(record, out var id))
                data.RecordIds.Add(record, id = Guid.NewGuid().ToString("N"));
            rows.Add(DutyWrite.Capture(id, record, pending));
        }
        foreach (var record in data.Records)
            Add(record, false);
        if (data.PendingDuty != null)
            Add(data.PendingDuty, true);
        if (rows.Select(row => row.Id).Distinct().Count() != rows.Count)
            throw new InvalidDataException("The same duty object is present more than once.");
        var retained = new HashSet<DutyRecord>(data.Records, ReferenceEqualityComparer.Instance);
        if (data.PendingDuty != null) retained.Add(data.PendingDuty);
        foreach (var removed in data.RecordIds.Keys.Where(record => !retained.Contains(record)).ToArray())
            data.RecordIds.Remove(removed);
        return new(rows, JsonSerializer.Serialize(data.SeenPlayers));
    }

    internal static HistoryWrite CapturePending(HistoryData data)
    {
        var rows = new List<DutyWrite>();
        if (data.PendingDuty != null)
        {
            if (!data.RecordIds.TryGetValue(data.PendingDuty, out var id))
            {
                id = Guid.NewGuid().ToString("N");
                data.RecordIds.Add(data.PendingDuty, id);
            }
            rows.Add(DutyWrite.Capture(id, data.PendingDuty, true));
        }
        return new HistoryWrite(rows, string.Empty, PendingOnly: true);
    }

    internal void SaveHistory(HistoryWrite snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenExisting();
        cancellationToken.ThrowIfCancellationRequested();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM history_state WHERE id=1;";
        if ((long?)command.ExecuteScalar() != revision)
            throw new IOException("Another database session changed the history.");

        var desired = snapshot.PendingOnly
            ? new Dictionary<string, DutyWrite>(savedDuties)
            : new Dictionary<string, DutyWrite>();
        if (snapshot.PendingOnly && pendingDutyId != null)
            desired.Remove(pendingDutyId);
        foreach (var row in snapshot.Duties)
            desired.Add(row.Id, row);
        var deleted = savedDuties.Keys.Except(desired.Keys).ToArray();
        var changed = snapshot.Duties.Where(row => !savedDuties.TryGetValue(row.Id, out var previous) || !row.SameValues(previous)).ToArray();
        var seenJson = snapshot.PendingOnly ? seenFingerprint : snapshot.SeenPlayersJson;
        bool seenChanged = seenFingerprint != seenJson;
        if (deleted.Length == 0 && changed.Length == 0 && !seenChanged) { transaction.Commit(); return; }

        foreach (string id in deleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Execute(connection, transaction, "DELETE FROM duty_runs WHERE id=$id;", new() { ["id"] = id });
        }
        foreach (var row in changed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = row.Record;
            var fields = new Dictionary<string, object?>
            {
                ["id"] = row.Id, ["timestamp"] = record.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["duty_name"] = record.DutyName ?? string.Empty, ["duty_type"] = record.DutyType ?? string.Empty,
                ["joined_in_progress"] = record.JoinedInProgress ? 1 : 0, ["wipe_count"] = record.WipeCount,
                ["lalafell_count"] = record.LalafellCount, ["completed"] = record.Completed ? 1 : 0,
                ["player_job"] = record.PlayerJob ?? string.Empty, ["recorder_name"] = record.RecorderName ?? string.Empty,
                ["recorder_world"] = record.RecorderWorld ?? string.Empty, ["duration_ticks"] = record.TimeInDuty.Ticks,
                ["note"] = record.Note ?? string.Empty, ["legacy_pvp_deaths"] = record.PvPDeaths,
                ["is_pending"] = row.Pending ? 1 : 0,
                ["checkpoint_utc"] = record.CheckpointUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            };
            var columns = fields.Keys.ToArray();
            Execute(connection, transaction,
                $"INSERT INTO duty_runs ({string.Join(',', columns)}) VALUES ({string.Join(',', columns.Select(column => '$' + column))}) " +
                $"ON CONFLICT(id) DO UPDATE SET {string.Join(',', columns.Where(column => column != "id").Select(column => $"{column}=excluded.{column}"))};", fields);
            Execute(connection, transaction, "DELETE FROM duty_players WHERE duty_id=$id;", new() { ["id"] = row.Id });
            for (int index = 0; index < row.Party.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var player = row.Party[index];
                Execute(connection, transaction, """
                    INSERT INTO duty_players(duty_id,party_index,name,world,job,race,gender,status,death_count,pvp_scoreboard_deaths)
                    VALUES ($duty,$idx,$name,$world,$job,$race,$gender,$status,$deaths,$scoreboard);
                    """, new()
                {
                    ["duty"] = row.Id, ["idx"] = index, ["name"] = player.Name ?? string.Empty, ["world"] = player.World ?? string.Empty,
                    ["job"] = player.Job ?? string.Empty, ["race"] = player.Race ?? string.Empty, ["gender"] = player.Gender ?? string.Empty,
                    ["status"] = player.Status ?? string.Empty, ["deaths"] = player.DeathCount, ["scoreboard"] = player.PvPScoreboardDeaths
                });
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        Execute(connection, transaction, "UPDATE history_state SET revision=revision+1,seen_players_json=$seen WHERE id=1;",
            new() { ["seen"] = seenJson });
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        revision++;
        savedDuties = desired;
        seenFingerprint = seenJson;
        pendingDutyId = snapshot.Duties.SingleOrDefault(row => row.Pending)?.Id;
    }

    internal SqliteConnection OpenExisting()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true,
            Pooling = false, DefaultTimeout = 5
        }.ToString());
        try
        {
            connection.Open();
            ConfigureConnection(connection);
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static void ConfigureConnection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA synchronous = FULL;";
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, Dictionary<string, object?> parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue('$' + name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}
