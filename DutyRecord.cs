using System;
using System.Collections.Generic;

namespace MentorRouletteTracker
{
    /// <summary>
    /// One completed (or abandoned) duty run, matching the columns
    /// in the user's existing spreadsheet.
    /// </summary>
    [Serializable]
    public class DutyRecord
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>Display name of the duty, e.g. "The Tam-Tara Deepcroft".</summary>
        public string DutyName { get; set; } = string.Empty;

        /// <summary>Human-readable content type, e.g. "Normal Raid", "Trial", "Guildhest".</summary>
        public string DutyType { get; set; } = string.Empty;

        /// <summary>True if the duty was already in progress when the player joined it.</summary>
        public bool JoinedInProgress { get; set; }

        /// <summary>Number of wipes observed during this run.</summary>
        public int WipeCount { get; set; }

        /// <summary>Number of Lalafell characters present in the party for this run.</summary>
        public int LalafellCount { get; set; }

        /// <summary>Whether the duty was completed (vs. left early / plugin closed mid-run).</summary>
        public bool Completed { get; set; }

        /// <summary>Job abbreviation the local player was playing during this run, e.g. "WHM".</summary>
        public string PlayerJob { get; set; } = string.Empty;

        /// <summary>Wall-clock time spent from entering the duty to leaving/completing it.</summary>
        public TimeSpan TimeInDuty { get; set; }

        /// <summary>Snapshot of every party member seen during this run.</summary>
        public List<PartyMemberSnapshot> Party { get; set; } = new();
    }
}