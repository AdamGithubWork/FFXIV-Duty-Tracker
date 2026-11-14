namespace MentorRouletteTracker.PvP;

public readonly record struct PvpMatchReference(string MatchId, DateTime DutyStartTime, bool IsFrontline);

public class PvpMatchResult
{
    public string MatchId { get; set; } = string.Empty;
    public DateTime DutyStartTime { get; set; }
    public bool IsFrontline { get; set; }
    public Dictionary<string, int> PlayerDeaths { get; set; } = new();
}
