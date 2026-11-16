using Dalamud.Configuration;
using Dalamud.Plugin;
using MentorRouletteTracker.Storage;

namespace MentorRouletteTracker
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;
        public List<uint> TrackedContentTypeIds { get; set; } = [2, 3, 4, 5, 6, 21, 28, 30, 37];
        public List<uint> TimerVisibleContentTypeIds { get; set; } = [2, 3, 4, 5, 6];
        public bool TrackAllianceMembers { get; set; } = true;
        public bool TrackPvpEnemies { get; set; } = true;
        public bool IncludeSelfInStatistics { get; set; }
        public bool ShowStatisticsOverview { get; set; } = true;
        public bool ShowRaceStatistics { get; set; } = true;
        public bool ShowGenderStatistics { get; set; } = true;
        public bool ShowJobStatistics { get; set; } = true;
        public bool ShowStatusStatistics { get; set; } = true;
        public bool ShowWorldStatistics { get; set; } = true;
        public bool ShowDatacenterStatistics { get; set; } = true;
        public HashSet<string> HiddenMilestones { get; set; } = new();
        public Dictionary<string, HashSet<string>> TableColumns { get; set; } = new();
        public Dictionary<string, List<string>> TableColumnOrder { get; set; } = new();
        public uint? WindowBackgroundColor { get; set; }
        public uint? PanelBackgroundColor { get; set; }
        public uint? TableHeaderBackgroundColor { get; set; }
        public uint? TableRowBackgroundColor { get; set; }
        public uint? AlternateRowBackgroundColor { get; set; }
        public bool WrapTableHeaders { get; set; } = true;

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

        internal void ResetPreferences()
        {
            var defaults = new Configuration();
            foreach (var property in typeof(Configuration).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                if (property.CanRead && property.CanWrite)
                    property.SetValue(this, property.GetValue(defaults));
            Save();
        }

        internal bool UpgradePvpColumnSettings()
        {
            var changed = false;
            if (TableColumns != null && TableColumns.Remove("PvP players", out var visibility))
            {
                TableColumns.TryAdd("PvP records", visibility);
                changed = true;
            }
            if (TableColumnOrder != null && TableColumnOrder.Remove("PvP players", out var order))
            {
                TableColumnOrder.TryAdd("PvP records", order);
                changed = true;
            }
            return changed;
        }

        public void DisposeHistory() => history?.Dispose();
    }
}
