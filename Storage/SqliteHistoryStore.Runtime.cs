using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MentorRouletteTracker.DutyTracking;

namespace MentorRouletteTracker.Storage;

public sealed partial class SqliteHistoryStore
{
    private Dictionary<string, string> fingerprints = new();
    private string seenFingerprint = "[]";
    private long revision;

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
        var orders = new Dictionary<string, (bool Pending, int Order)>();
        command.CommandText = "SELECT * FROM duty_runs ORDER BY history_order, rowid;";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var record = new DutyRecord
                {
                    Timestamp = DateTime.Parse((string)reader["timestamp"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
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
                orders.Add(id, (pending, checked((int)(long)reader["history_order"])));
                data.RecordIds.Add(record, id);
                if (pending)
                {
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
        fingerprints = identities.ToDictionary(entry => entry.Key, entry =>
            new DutyWrite(entry.Key, JsonSerializer.Serialize(entry.Value), orders[entry.Key].Pending, orders[entry.Key].Order).Fingerprint);
        seenFingerprint = JsonSerializer.Serialize(data.SeenPlayers);
        return data;
    }

    internal static HistoryWrite Capture(HistoryData data)
    {
        var rows = new List<DutyWrite>();
        void Add(DutyRecord record, bool pending, int order)
        {
            if (!data.RecordIds.TryGetValue(record, out var id))
                data.RecordIds.Add(record, id = Guid.NewGuid().ToString("N"));
            rows.Add(new(id, JsonSerializer.Serialize(record), pending, order));
        }
        for (int i = 0; i < data.Records.Count; i++) Add(data.Records[i], false, i);
        if (data.PendingDuty != null) Add(data.PendingDuty, true, -1);
        if (rows.Select(row => row.Id).Distinct().Count() != rows.Count)
            throw new InvalidDataException("The same duty object is present more than once.");
        var retained = data.Records.ToHashSet();
        if (data.PendingDuty != null) retained.Add(data.PendingDuty);
        foreach (var removed in data.RecordIds.Keys.Where(record => !retained.Contains(record)).ToArray())
            data.RecordIds.Remove(removed);
        return new(rows, JsonSerializer.Serialize(data.SeenPlayers));
    }

    internal void SaveHistory(HistoryWrite snapshot)
    {
        using var connection = OpenExisting();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM history_state WHERE id=1;";
        if ((long?)command.ExecuteScalar() != revision)
            throw new IOException("Another database session changed the history.");

        var desired = snapshot.Duties.ToDictionary(row => row.Id, row => row.Fingerprint);
        var deleted = fingerprints.Keys.Except(desired.Keys).ToArray();
        var changed = snapshot.Duties.Where(row => !fingerprints.TryGetValue(row.Id, out var previous) || previous != row.Fingerprint).ToArray();
        bool seenChanged = seenFingerprint != snapshot.SeenPlayersJson;
        if (deleted.Length == 0 && changed.Length == 0 && !seenChanged) { transaction.Commit(); return; }

        foreach (string id in deleted)
            Execute(connection, transaction, "DELETE FROM duty_runs WHERE id=$id;", new() { ["id"] = id });
        foreach (var row in changed)
        {
            var record = JsonSerializer.Deserialize<DutyRecord>(row.Json) ?? throw new InvalidDataException("Invalid duty snapshot.");
            var fields = new Dictionary<string, object?>
            {
                ["id"] = row.Id, ["timestamp"] = record.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                ["duty_name"] = record.DutyName, ["duty_type"] = record.DutyType,
                ["joined_in_progress"] = record.JoinedInProgress ? 1 : 0, ["wipe_count"] = record.WipeCount,
                ["lalafell_count"] = record.LalafellCount, ["completed"] = record.Completed ? 1 : 0,
                ["player_job"] = record.PlayerJob, ["recorder_name"] = record.RecorderName,
                ["recorder_world"] = record.RecorderWorld, ["duration_ticks"] = record.TimeInDuty.Ticks,
                ["note"] = record.Note, ["legacy_pvp_deaths"] = record.PvPDeaths,
                ["is_pending"] = row.Pending ? 1 : 0, ["history_order"] = row.Order
            };
            var columns = fields.Keys.ToArray();
            Execute(connection, transaction,
                $"INSERT INTO duty_runs ({string.Join(',', columns)}) VALUES ({string.Join(',', columns.Select(column => '$' + column))}) " +
                $"ON CONFLICT(id) DO UPDATE SET {string.Join(',', columns.Where(column => column != "id").Select(column => $"{column}=excluded.{column}"))};", fields);
            Execute(connection, transaction, "DELETE FROM duty_players WHERE duty_id=$id;", new() { ["id"] = row.Id });
            for (int index = 0; index < record.Party.Count; index++)
            {
                var player = record.Party[index];
                Execute(connection, transaction, """
                    INSERT INTO duty_players(duty_id,party_index,name,world,job,race,gender,status,death_count,pvp_scoreboard_deaths)
                    VALUES ($duty,$idx,$name,$world,$job,$race,$gender,$status,$deaths,$scoreboard);
                    """, new()
                {
                    ["duty"] = row.Id, ["idx"] = index, ["name"] = player.Name, ["world"] = player.World,
                    ["job"] = player.Job, ["race"] = player.Race, ["gender"] = player.Gender,
                    ["status"] = player.Status, ["deaths"] = player.DeathCount, ["scoreboard"] = player.PvPScoreboardDeaths
                });
            }
        }
        Execute(connection, transaction, "UPDATE history_state SET revision=revision+1,seen_players_json=$seen WHERE id=1;",
            new() { ["seen"] = snapshot.SeenPlayersJson });
        transaction.Commit();
        revision++;
        fingerprints = desired;
        seenFingerprint = snapshot.SeenPlayersJson;
    }

    private SqliteConnection OpenExisting()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true,
            Pooling = false, DefaultTimeout = 5
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
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
