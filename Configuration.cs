
using Dalamud.Configuration;
using Dalamud.Plugin;
using MentorRouletteTracker.DutyTracking;

namespace MentorRouletteTracker
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        public List<uint> TrackedContentTypeIds { get; set; } = new();
        public List<uint> TimerVisibleContentTypeIds { get; set; } = new();

        public bool TrackAllianceMembers { get; set; } = false;
        public bool IncludeSelfInStatistics { get; set; } = false;

        public List<DutyRecord> Records { get; set; } = new();
        public List<SeenPlayer> SeenPlayers { get; set; } = new();
        public DutyRecord? PendingDuty { get; set; }

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
