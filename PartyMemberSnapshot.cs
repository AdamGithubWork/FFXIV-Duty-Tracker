using System;

namespace MentorRouletteTracker
{
    [Serializable]
    // Information about a party member from a specific duty
    public class PartyMemberSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public string Job { get; set; } = string.Empty;
        public string Race { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string World { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int DeathCount { get; set; }
    }
}