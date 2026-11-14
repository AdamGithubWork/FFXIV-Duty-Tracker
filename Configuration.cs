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
        [NonSerialized]
        private string? storageError;

        internal HistorySession History => history ?? throw new InvalidOperationException("History is not initialized.");
        internal bool StorageAvailable => history != null;
        internal string? StorageError => storageError;

        public void Initialize(IDalamudPluginInterface pi, Action<Exception>? reportStorageError = null, bool createDatabase = false)
        {
            if (history != null)
                throw new InvalidOperationException("History has already been initialized.");
            pluginInterface = pi;
            var path = Path.Combine(pi.GetPluginConfigDirectory(), "history.sqlite");
            storageError = null;
            try
            {
                var store = new SqliteHistoryStore(path);
                if (createDatabase && !File.Exists(path))
                    store.Initialize();
                history = new HistorySession(store, reportStorageError);
            }
            catch (Exception ex)
            {
                storageError = ex is FileNotFoundException
                    ? "Couldn't find history.sqlite."
                    : $"Couldn't load duty history: {ex.Message}";
                reportStorageError?.Invoke(ex);
            }
        }

        public void Save()
        {
            pluginInterface?.SavePluginConfig(this);
        }

        public void DisposeHistory() => history?.Dispose();
    }
}
