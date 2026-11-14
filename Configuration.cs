using Dalamud.Configuration;
using Dalamud.Plugin;
using MentorRouletteTracker.Storage;

namespace MentorRouletteTracker
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;
        public List<uint> TrackedContentTypeIds { get; set; } = new();
        public List<uint> TimerVisibleContentTypeIds { get; set; } = new();
        public bool TrackAllianceMembers { get; set; }
        public bool IncludeSelfInStatistics { get; set; }

        [NonSerialized]
        private HistorySession? history;
        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        internal HistorySession History => history ?? throw new InvalidOperationException("History is not initialized.");

        public void Initialize(IDalamudPluginInterface pi, Action<Exception>? reportStorageError = null, bool createDatabase = false)
        {
            if (history != null)
                throw new InvalidOperationException("History has already been initialized.");
            pluginInterface = pi;
            var path = Path.Combine(pi.GetPluginConfigDirectory(), "history.sqlite");
            var store = new SqliteHistoryStore(path);
            if (createDatabase && !File.Exists(path))
                store.Initialize();
            history = new HistorySession(store, reportStorageError);
        }

        public void Save()
        {
            pluginInterface?.SavePluginConfig(this);
        }

        public void DisposeHistory() => history?.Dispose();
    }
}
