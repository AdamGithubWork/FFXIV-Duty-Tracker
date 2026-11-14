using MentorRouletteTracker.DutyTracking;

namespace MentorRouletteTracker.Storage;

public sealed class HistoryData
{
    public List<DutyRecord> Records { get; init; } = new();
    public List<SeenPlayer> SeenPlayers { get; init; } = new();
    public DutyRecord? PendingDuty { get; set; }
    internal Dictionary<DutyRecord, string> RecordIds { get; } = new(ReferenceEqualityComparer.Instance);
}

internal readonly record struct DutyRow(
    DateTime Timestamp,
    DateTime? CheckpointUtc,
    string DutyName,
    string DutyType,
    bool JoinedInProgress,
    int WipeCount,
    int LalafellCount,
    bool Completed,
    string PlayerJob,
    string RecorderName,
    string RecorderWorld,
    TimeSpan TimeInDuty,
    string Note,
    int? PvPDeaths);

internal readonly record struct PlayerRow(
    string Name,
    string Job,
    string Race,
    string Gender,
    string World,
    string Status,
    int DeathCount,
    int? PvPScoreboardDeaths);

internal sealed record DutyWrite(string Id, DutyRow Record, IReadOnlyList<PlayerRow> Party, bool Pending)
{
    public static DutyWrite Capture(string id, DutyRecord record, bool pending)
    {
        var row = new DutyRow(
            record.Timestamp,
            record.CheckpointUtc,
            record.DutyName ?? string.Empty,
            record.DutyType ?? string.Empty,
            record.JoinedInProgress,
            record.WipeCount,
            record.LalafellCount,
            record.Completed,
            record.PlayerJob ?? string.Empty,
            record.RecorderName ?? string.Empty,
            record.RecorderWorld ?? string.Empty,
            record.TimeInDuty,
            record.Note ?? string.Empty,
            record.PvPDeaths);
        var party = record.Party.Select(player => new PlayerRow(
            player.Name ?? string.Empty,
            player.Job ?? string.Empty,
            player.Race ?? string.Empty,
            player.Gender ?? string.Empty,
            player.World ?? string.Empty,
            player.Status ?? string.Empty,
            player.DeathCount,
            player.PvPScoreboardDeaths)).ToArray();
        return new DutyWrite(id, row, party, pending);
    }

    public bool SameValues(DutyWrite other)
    {
        // DateTime equality compares ticks but leaves out the local/UTC kind.
        return Record == other.Record &&
            Record.Timestamp.Kind == other.Record.Timestamp.Kind &&
            Record.CheckpointUtc?.Kind == other.Record.CheckpointUtc?.Kind &&
            Pending == other.Pending && Party.SequenceEqual(other.Party);
    }

    public DutyRecord ToRecord()
    {
        return new DutyRecord
        {
            Timestamp = Record.Timestamp,
            CheckpointUtc = Record.CheckpointUtc,
            DutyName = Record.DutyName,
            DutyType = Record.DutyType,
            JoinedInProgress = Record.JoinedInProgress,
            WipeCount = Record.WipeCount,
            LalafellCount = Record.LalafellCount,
            Completed = Record.Completed,
            PlayerJob = Record.PlayerJob,
            RecorderName = Record.RecorderName,
            RecorderWorld = Record.RecorderWorld,
            TimeInDuty = Record.TimeInDuty,
            Note = Record.Note,
            PvPDeaths = Record.PvPDeaths,
            Party = Party.Select(player => new PartyMemberSnapshot
            {
                Name = player.Name,
                Job = player.Job,
                Race = player.Race,
                Gender = player.Gender,
                World = player.World,
                Status = player.Status,
                DeathCount = player.DeathCount,
                PvPScoreboardDeaths = player.PvPScoreboardDeaths
            }).ToList()
        };
    }
}

internal sealed record HistoryWrite(List<DutyWrite> Duties, string SeenPlayersJson, bool PendingOnly = false);
