namespace MentorRouletteTracker.DutyTracking
{
    [Serializable]
    public class PartyMemberSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public string Job { get; set; } = string.Empty;
        public string Race { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string World { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int DeathCount { get; set; }
        public int? PvPScoreboardDeaths { get; set; }
        public string PvPTeam { get; set; } = string.Empty;
        public bool? PvPIsEnemy { get; set; }
        public long? PvPDamageDealt { get; set; }
        public int? PvPKills { get; set; }
        public int? PvPAssists { get; set; }
        public int? PvPPlacement { get; set; }
    }
}
