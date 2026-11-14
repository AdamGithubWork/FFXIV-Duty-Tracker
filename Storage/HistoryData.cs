using MentorRouletteTracker.DutyTracking;

namespace MentorRouletteTracker.Storage;

public sealed class HistoryData
{
    public List<DutyRecord> Records { get; init; } = new();
    public List<SeenPlayer> SeenPlayers { get; init; } = new();
    public DutyRecord? PendingDuty { get; set; }
    internal Dictionary<DutyRecord, string> RecordIds { get; } = new();
}

internal sealed record DutyWrite(string Id, string Json, bool Pending, int Order)
{
    public string Fingerprint => $"{Pending}:{Order}:{Json}";
}

internal sealed record HistoryWrite(List<DutyWrite> Duties, string SeenPlayersJson);
