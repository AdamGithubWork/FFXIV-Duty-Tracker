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
        public List<uint> TrackedContentTypeIds { get; set; } = new();
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