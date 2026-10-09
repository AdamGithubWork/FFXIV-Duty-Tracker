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

        /// <summary>If true, only logs runs entered via Duty Roulette (Mentor).</summary>
        public bool MentorRouletteOnly { get; set; } = true;

        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

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