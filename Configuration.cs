using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace MentorRouletteTracker
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        public List<DutyRecord> Records { get; set; } = new();

        public List<SeenPlayer> SeenPlayers { get; set; } = new();
        public bool TrackAllianceMembers { get; set; } = false;
        public List<uint> TrackedContentTypeIds { get; set; } = new()
        {
            2,  // Dungeons
            3,  // Guildhests
            4,  // Trials
            5,  // Raids
            6,  // PvP
            21, // Deep Dungeons
            28, // Ultimate Raids
            30, // V&C Dungeon Finder
            37  // Chaotic Alliance Raid
        };
        public bool IncludeSelfInStatistics { get; set; } = false;
        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public DutyRecord? PendingDuty { get; set; }

        public void Initialize(IDalamudPluginInterface pi)
        {
            this.pluginInterface = pi;
        }

        public void Save()
        {
            this.pluginInterface?.SavePluginConfig(this);
        }
    }
}