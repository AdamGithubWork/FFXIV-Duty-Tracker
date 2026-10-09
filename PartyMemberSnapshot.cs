using System;

namespace MentorRouletteTracker
{
    /// <summary>
    /// A snapshot of one party member's name, job, race, gender, and deaths
    /// for a single duty run.
    /// </summary>
    [Serializable]
    public class PartyMemberSnapshot
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Job abbreviation, e.g. "WAR", "WHM".</summary>
        public string Job { get; set; } = string.Empty;

        /// <summary>Race display name, e.g. "Lalafell".</summary>
        public string Race { get; set; } = string.Empty;

        /// <summary>Gender display name, e.g. "Male" or "Female".</summary>
        public string Gender { get; set; } = string.Empty;

        /// <summary>Number of times this player died during the duty.</summary>
        public int DeathCount { get; set; }
    }
}