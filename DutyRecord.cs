namespace MentorRouletteTracker
{
    // Defines information stored about a duty
    public class DutyRecord
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string DutyName { get; set; } = string.Empty;
        public string DutyType { get; set; } = string.Empty;
        public bool JoinedInProgress { get; set; }
        public int WipeCount { get; set; }
        public int LalafellCount { get; set; }
        public bool Completed { get; set; }
        public string PlayerJob { get; set; } = string.Empty;
        public TimeSpan TimeInDuty { get; set; }
        public List<PartyMemberSnapshot> Party { get; set; } = new();
        public string Note { get; set; } = string.Empty;
        public int? PvPDeaths { get; set; }
    }
}