namespace MentorRouletteTracker.PvP;

public enum PvpMode
{
    Unknown,
    CrystallineConflict,
    Frontline,
    RivalWings
}

public readonly record struct PvpMatchReference(string MatchId, DateTime DutyStartTime, PvpMode Mode);

public sealed class PvpPlayerResult
{
    public string Name { get; init; } = string.Empty;
    public string World { get; init; } = string.Empty;
    public string Job { get; set; } = "Unknown";
    public int? Deaths { get; set; }
}

public class PvpMatchResult
{
    public string MatchId { get; set; } = string.Empty;
    public DateTime DutyStartTime { get; set; }
    public PvpMode Mode { get; set; }
    public string RecorderName { get; set; } = string.Empty;
    public string RecorderWorld { get; set; } = string.Empty;
    public int ExpectedPlayerCount { get; set; }
    public bool RosterComplete => ExpectedPlayerCount > 0 && Players.Count >= ExpectedPlayerCount;
    public Dictionary<string, PvpPlayerResult> Players { get; set; } = new(StringComparer.Ordinal);
}
