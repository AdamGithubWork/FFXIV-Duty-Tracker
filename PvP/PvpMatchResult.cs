namespace MentorRouletteTracker.PvP;

public class PvpMatchResult
{
    public string MatchId { get; set; } = string.Empty;
    public DateTime DutyStartTime { get; set; }
    public bool IsFrontline { get; set; }
    public Dictionary<string, int> PlayerDeaths { get; set; } = new();
}
