using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MentorRouletteTracker.Storage;

public sealed record HistoryImportResult(string SourceSha256, int HistoricalDuties,
    int PendingDuties, int PlayerSnapshots, int VerifiedValues, bool AlreadyImported);

public sealed class LegacyHistoryImporter(SqliteHistoryStore store)
{
    public HistoryImportResult ImportBackup(string backupPath, string expectedSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        if (string.Equals(Path.GetFullPath(backupPath), store.DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The source backup and target database must be separate files.", nameof(backupPath));
        if (expectedSha256 == null || expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Enter the backup's SHA-256 checksum.", nameof(expectedSha256));

        var bytes = File.ReadAllBytes(backupPath);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The backup checksum does not match.");

        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var records = root.GetProperty("Records");
        if (records.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The backup must contain a Records array.");

        var duties = new List<ImportDuty>();
        int ordinal = 0;
        foreach (var record in records.EnumerateArray())
            duties.Add(ParseDuty(record, hash, ordinal++, pending: false));
        var historicalCount = duties.Count;
        if (root.TryGetProperty("PendingDuty", out var pending) && pending.ValueKind != JsonValueKind.Null)
            duties.Add(ParseDuty(pending, hash, -1, pending: true));
        int pendingCount = duties.Count - historicalCount;
        int playerCount = duties.Sum(duty => duty.Players.Count);
        var seenJson = SqliteHistoryStore.LegacySeenJson(root);

        store.Initialize();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = store.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true, Pooling = false, DefaultTimeout = 5
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        bool alreadyImported;
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM legacy_imports WHERE source_sha256 = $hash;";
            check.Parameters.AddWithValue("$hash", hash);
            alreadyImported = (long)check.ExecuteScalar()! == 1;
        }

        if (!alreadyImported)
        {
            using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT (SELECT COUNT(*) FROM legacy_imports) + (SELECT COUNT(*) FROM duty_runs);";
            if ((long)check.ExecuteScalar()! != 0)
                throw new InvalidDataException("This database already contains history from another source.");

            Insert(connection, transaction, "legacy_imports", new()
            {
                ["source_sha256"] = hash, ["source_bytes"] = bytes,
                ["imported_at_utc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["historical_duties"] = (long)historicalCount, ["pending_duties"] = (long)pendingCount,
                ["player_snapshots"] = (long)playerCount
            });
            foreach (var duty in duties)
            {
                Insert(connection, transaction, "duty_runs", duty.Values);
                foreach (var player in duty.Players)
                    Insert(connection, transaction, "duty_players", player);
            }
            using var cache = connection.CreateCommand();
            cache.Transaction = transaction;
            cache.CommandText = "UPDATE history_state SET seen_players_json=$seen WHERE id=1;";
            cache.Parameters.AddWithValue("$seen", seenJson);
            cache.ExecuteNonQuery();
        }

        int verified = Verify(connection, transaction, hash, bytes, duties, historicalCount, pendingCount, playerCount, seenJson);
        transaction.Commit();
        return new(hash, historicalCount, pendingCount, playerCount, verified, alreadyImported);
    }

    private sealed record ImportDuty(Dictionary<string, object?> Values, List<Dictionary<string, object?>> Players);

    private static ImportDuty ParseDuty(JsonElement record, string hash, int ordinal, bool pending)
    {
        // Include the position so identical runs keep separate IDs.
        string id = $"legacy:{hash}:{ordinal}";
        string? checkpointUtc = null;
        if (record.TryGetProperty("CheckpointUtc", out var checkpoint) && checkpoint.ValueKind != JsonValueKind.Null)
        {
            checkpointUtc = DateTime.Parse(checkpoint.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                .ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }
        var values = new Dictionary<string, object?>
        {
            ["id"] = id, ["timestamp"] = SqliteHistoryStore.UtcTimestamp(RequiredString(record, "Timestamp")),
            ["duty_name"] = RequiredString(record, "DutyName"), ["duty_type"] = RequiredString(record, "DutyType"),
            ["joined_in_progress"] = OptionalBoolean(record, "JoinedInProgress") ? 1L : 0L,
            ["wipe_count"] = OptionalNumber(record, "WipeCount") ?? 0L,
            ["lalafell_count"] = OptionalNumber(record, "LalafellCount") ?? 0L,
            ["completed"] = record.GetProperty("Completed").GetBoolean() ? 1L : 0L,
            ["player_job"] = OptionalString(record, "PlayerJob"),
            ["recorder_name"] = OptionalString(record, "RecorderName"),
            ["recorder_world"] = OptionalString(record, "RecorderWorld"),
            ["duration_ticks"] = TimeSpan.Parse(RequiredString(record, "TimeInDuty"), CultureInfo.InvariantCulture).Ticks,
            ["note"] = OptionalString(record, "Note"), ["legacy_pvp_deaths"] = OptionalNumber(record, "PvPDeaths"),
            ["is_pending"] = pending ? 1L : 0L, ["checkpoint_utc"] = checkpointUtc,
            ["legacy_import_id"] = hash, ["legacy_record_index"] = (long)ordinal
        };
        var players = new List<Dictionary<string, object?>>();
        var party = record.GetProperty("Party");
        if (party.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Every duty must have a Party array.");
        foreach (var player in party.EnumerateArray())
        {
            players.Add(new()
            {
                ["duty_id"] = id, ["party_index"] = (long)players.Count,
                ["name"] = RequiredString(player, "Name"), ["world"] = RequiredString(player, "World"),
                ["job"] = OptionalString(player, "Job"), ["race"] = RequiredString(player, "Race"),
                ["gender"] = RequiredString(player, "Gender"), ["status"] = OptionalString(player, "Status"),
                ["death_count"] = player.GetProperty("DeathCount").GetInt64(),
                ["pvp_scoreboard_deaths"] = OptionalNumber(player, "PvPScoreboardDeaths")
            });
        }
        return new(values, players);
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new InvalidDataException($"{name} is null.");

    private static string OptionalString(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? string.Empty : value.GetString()!;

    private static bool OptionalBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null && value.GetBoolean();

    private static long? OptionalNumber(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null : value.GetInt64();

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction,
        string table, Dictionary<string, object?> values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var parameters = values.Select((entry, index) =>
        {
            string name = $"$p{index}";
            command.Parameters.AddWithValue(name, entry.Value ?? DBNull.Value);
            return name;
        }).ToArray();
        command.CommandText = $"INSERT INTO {table} ({string.Join(',', values.Keys)}) VALUES ({string.Join(',', parameters)});";
        command.ExecuteNonQuery();
    }

    private static int Verify(SqliteConnection connection, SqliteTransaction transaction,
        string hash, byte[] sourceBytes, List<ImportDuty> duties, int historicalCount, int pendingCount, int playerCount, string seenJson)
    {
        using var archive = connection.CreateCommand();
        archive.Transaction = transaction;
        archive.CommandText = "SELECT source_bytes, historical_duties, pending_duties, player_snapshots FROM legacy_imports WHERE source_sha256=$hash;";
        archive.Parameters.AddWithValue("$hash", hash);
        using (var reader = archive.ExecuteReader())
        {
            if (!reader.Read() || !((byte[])reader[0]).SequenceEqual(sourceBytes) ||
                reader.GetInt64(1) != historicalCount || reader.GetInt64(2) != pendingCount || reader.GetInt64(3) != playerCount)
                throw new InvalidDataException("The archived source does not match the verified backup.");
        }

        using var count = connection.CreateCommand();
        count.Transaction = transaction;
        count.CommandText = "SELECT COUNT(*) FROM duty_runs WHERE legacy_import_id=$hash;";
        count.Parameters.AddWithValue("$hash", hash);
        if ((long)count.ExecuteScalar()! != duties.Count)
            throw new InvalidDataException("The imported duty count does not match the backup.");

        using var cache = connection.CreateCommand();
        cache.Transaction = transaction;
        cache.CommandText = "SELECT seen_players_json FROM history_state WHERE id=1;";
        if (!Equals(cache.ExecuteScalar(), seenJson))
            throw new InvalidDataException("The stored seen-player cache differs from the backup.");
        int verified = 5;
        foreach (var duty in duties)
        {
            using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT * FROM duty_runs WHERE id=$id;";
            select.Parameters.AddWithValue("$id", duty.Values["id"]!);
            using (var reader = select.ExecuteReader())
            {
                if (!reader.Read()) throw new InvalidDataException("An imported duty is missing.");
                verified += Compare(reader, duty.Values);
            }
            select.CommandText = "SELECT * FROM duty_players WHERE duty_id=$id ORDER BY party_index;";
            using var players = select.ExecuteReader();
            int index = 0;
            while (players.Read())
            {
                if (index >= duty.Players.Count) throw new InvalidDataException("Unexpected extra player snapshots.");
                verified += Compare(players, duty.Players[index++]);
            }
            if (index != duty.Players.Count) throw new InvalidDataException("Imported player snapshots are missing.");
        }
        return verified;
    }

    private static int Compare(SqliteDataReader reader, Dictionary<string, object?> expected)
    {
        foreach (var (column, value) in expected)
        {
            var actual = reader[column];
            if (!Equals(actual is DBNull ? null : actual, value))
                throw new InvalidDataException($"The imported {column} value does not match the backup.");
        }
        return expected.Count;
    }
}
