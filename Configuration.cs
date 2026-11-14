
using Dalamud.Configuration;
using Dalamud.Plugin;
using MentorRouletteTracker.DutyTracking;
using MentorRouletteTracker.Storage;
using System.Text.Json;

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
        public bool UseSqliteHistory { get; set; } = false;

        [NonSerialized]
        private HistorySession? history;
        [NonSerialized]
        private bool sqliteHistoryActive;

        internal HistorySession History => history ?? throw new InvalidOperationException("History is not initialized.");

        public bool ShouldSerializeRecords() => !sqliteHistoryActive;
        public bool ShouldSerializeSeenPlayers() => !sqliteHistoryActive;
        public bool ShouldSerializePendingDuty() => !sqliteHistoryActive;

        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pi, Action<Exception>? reportStorageError = null)
        {
            if (history != null) throw new InvalidOperationException("History has already been initialized.");
            this.pluginInterface = pi;
            if (UseSqliteHistory)
            {
                var session = new HistorySession(new SqliteHistoryStore(
                    Path.Combine(pi.GetPluginConfigDirectory(), "history.sqlite")), reportStorageError);
                foreach (var record in Records.Concat(PendingDuty == null ? Enumerable.Empty<DutyRecord>() : new[] { PendingDuty }))
                {
                    record.Timestamp = record.Timestamp.ToUniversalTime().ToLocalTime();
                    record.CheckpointUtc = record.CheckpointUtc?.ToUniversalTime();
                }
                if ((Records.Count > 0 || PendingDuty != null) &&
                    (JsonSerializer.Serialize(Records) != JsonSerializer.Serialize(session.Records) ||
                     JsonSerializer.Serialize(PendingDuty) != JsonSerializer.Serialize(session.PendingDuty) ||
                     JsonSerializer.Serialize(SeenPlayers) != JsonSerializer.Serialize(session.SeenPlayers)))
                    throw new InvalidDataException("The database history does not match the JSON history. Import the latest backup before switching to SQLite.");
                history = session;
                sqliteHistoryActive = true;
                Records = new();
                SeenPlayers = new();
                PendingDuty = null;
            }
            else
            {
                history = new HistorySession(new HistoryData
                {
                    Records = Records, SeenPlayers = SeenPlayers, PendingDuty = PendingDuty
                }, state =>
                {
                    Records = state.Records;
                    SeenPlayers = state.SeenPlayers;
                    PendingDuty = state.PendingDuty;
                    pluginInterface.SavePluginConfig(this);
                });
            }
        }

        public void Save()
        {
            if (history == null) { pluginInterface?.SavePluginConfig(this); return; }
            if (UseSqliteHistory != sqliteHistoryActive)
                throw new InvalidOperationException("Import your history and restart the plugin to change storage modes.");
            history.Save();
            if (sqliteHistoryActive) pluginInterface?.SavePluginConfig(this);
        }

        public void DisposeHistory() => history?.Dispose();
    }
}
