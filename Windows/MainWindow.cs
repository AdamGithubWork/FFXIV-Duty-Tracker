
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow : Window, IDisposable
    {
        private readonly Configuration config;
        private readonly DutyTrackerService tracker;
        private readonly string exportDirectory;
        private readonly IObjectTable objectTable;
        private readonly Dictionary<string, string> worldDatacenters = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> selectedPlayerRaces = new();
        private readonly HashSet<string> selectedPlayerGenders = new();
        private readonly HashSet<string> selectedPlayerStatuses = new();
        private readonly HashSet<string> selectedPlayerJobs = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> selectedPlayerWorlds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> selectedDutyTypes = new();
        private readonly HashSet<PvP.PvpMode> selectedPvpModes = new();

        private string globalPlayerSearchText = string.Empty;
        private string globalDutySearchText = string.Empty;
        private int selectedTimeRange = 0;
        private int minimumEncounters = 1;
        private DateTime customStartDate = DateTime.Today.AddDays(-30);
        private DateTime customEndDate = DateTime.Today;

        private DutyRecord? selectedRecord;
        private bool showDetailWindow;

        private string? dutyHistoryFilter;
        private bool dutyHistoryFullRunsOnly;
        private bool openDutyHistory;
        private bool openSettings;

        private DutyRecord? recordPendingDeletion;
        private bool openDeleteConfirmation;

        private DutyRecord? recordToEdit;
        private bool openEditRecord;
        private int editWipeCount;
        private bool editJoinedInProgress;
        private string editError = string.Empty;

        private DutyRecord? noteRecord = null;
        private bool showNoteWindow = false;
        private string noteDraft = string.Empty;

        private int selectedHeatmapMetric = 0;
        private int appearanceColorCount;

        public MainWindow(
            Configuration config,
            DutyTrackerService tracker,
            string exportDirectory,
            IObjectTable objectTable)
            : base("Noumenon###MentorRouletteTrackerMain")
        {
            this.config = config;
            this.tracker = tracker;
            this.exportDirectory = exportDirectory;
            this.objectTable = objectTable;

            var datacenters = new (string Name, string[] Worlds)[]
            {
                ("Aether", ["Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren"]),
                ("Crystal", ["Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera"]),
                ("Dynamis", ["Cuchulainn", "Golem", "Halicarnassus", "Kraken", "Maduin", "Marilith", "Rafflesia", "Seraph"]),
                ("Primal", ["Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros"])
            };

            foreach (var datacenter in datacenters)
                foreach (var world in datacenter.Worlds)
                    worldDatacenters[world] = datacenter.Name;

            Size = new Vector2(800, 520);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        private static void DrawSectionHeading(string title) =>
            ImGui.TextColored(WindowAppearance.Accent, title);

        public override void PreDraw()
        {
            appearanceColorCount = WindowAppearance.Push(config);
        }

        public override void PostDraw()
        {
            if (appearanceColorCount > 0)
                ImGui.PopStyleColor(appearanceColorCount);
            appearanceColorCount = 0;
        }

        public void Dispose()
        {
            showDetailWindow = false;
            showNoteWindow = false;
            showPlayerHistory = false;
            selectedRecord = null;
            noteRecord = null;
            SaveAppearanceChanges();
        }

        public override void OnClose() => SaveAppearanceChanges();

        public void OpenSettings()
        {
            openSettings = true;
            openDutyHistory = false;
            IsOpen = true;
        }

        public void OpenLastDuty()
        {
            if (!config.StorageAvailable)
            {
                IsOpen = true;
                return;
            }

            selectedRecord = config.History.Records.MaxBy(record => record.Timestamp);
            showDetailWindow = selectedRecord != null;
            if (selectedRecord == null)
            {
                openSettings = false;
                dutyHistoryFilter = null;
                dutyHistoryFullRunsOnly = false;
                openDutyHistory = true;
                IsOpen = true;
            }
        }

        public void OpenDutyHistory(string dutyName, bool fullRunsOnly = false)
        {
            openSettings = false;
            if (string.IsNullOrWhiteSpace(dutyName))
                return;

            dutyHistoryFilter = dutyName;
            dutyHistoryFullRunsOnly = fullRunsOnly;
            openDutyHistory = true;

            // Opening a duty from the timer resets unrelated filters.
            globalPlayerSearchText = string.Empty;
            globalDutySearchText = string.Empty;
            selectedDutyTypes.Clear();
            selectedPvpModes.Clear();
            selectedPlayerRaces.Clear();
            selectedPlayerGenders.Clear();
            selectedPlayerStatuses.Clear();
            selectedPlayerJobs.Clear();
            selectedPlayerWorlds.Clear();
            selectedTimeRange = 0;
            minimumEncounters = 1;

            IsOpen = true;
        }

        public void DrawIndependentWindows()
        {
            if (!config.StorageAvailable)
                return;
            if (selectedRecord != null && !config.History.Records.Contains(selectedRecord))
            {
                selectedRecord = null;
                showDetailWindow = false;
            }
            if (noteRecord != null && !config.History.Records.Contains(noteRecord))
            {
                noteRecord = null;
                showNoteWindow = false;
            }
            if (!showPlayerHistory && !showDetailWindow && !showNoteWindow)
                return;
            var colorCount = WindowAppearance.Push(config);
            try
            {
                if (showPlayerHistory)
                {
                    var selected = GetPeopleRows(GetPlayerViewData()).FirstOrDefault(player =>
                        player.Name == historyPlayerName && player.World == historyPlayerWorld);
                    DrawPlayerHistory(selected);
                }
                DrawDetailWindow();
                DrawNoteWindow();
            }
            finally
            {
                if (colorCount > 0)
                    ImGui.PopStyleColor(colorCount);
            }
        }

        public override void Draw()
        {
            if (!config.StorageAvailable)
            {
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Duty history couldn't be loaded");
                ImGui.TextWrapped(config.StorageError ?? "History storage is unavailable.");
                ImGui.TextUnformatted("Recording is paused.");
                ImGui.Spacing();
                ImGui.TextWrapped("If the database is missing, unload Noumenon and restore it from a backup. Then reload the plugin.");
                ImGui.TextWrapped($"Database: {Path.Combine(exportDirectory, "history.sqlite")}");
                ImGui.TextWrapped("For other storage errors, check the Dalamud log before restoring files.");
                return;
            }

            if (ImGui.BeginTabBar("MainTabs"))
            {
                try
                {
                    var recordsTabFlags = openDutyHistory
                        ? ImGuiTabItemFlags.SetSelected
                        : ImGuiTabItemFlags.None;

                    if (ImGui.BeginTabItem("Records", recordsTabFlags))
                    {
                        try
                        {
                            DrawFilterPanel("Records");
                            DrawRecordsTab();
                        }
                        finally
                        {
                            ImGui.EndTabItem();
                        }
                    }

                    openDutyHistory = false;

                    if (ImGui.BeginTabItem("Statistics"))
                    {
                        try
                        {
                            DrawFilterPanel("Statistics", encounterFilter: true);
                            DrawStatisticsTab();
                        }
                        finally
                        {
                            ImGui.EndTabItem();
                        }
                    }

                    if (ImGui.BeginTabItem("Duty Analysis"))
                    {
                        try
                        {
                            DrawFilterPanel("Duty Analysis");
                            DrawDutyAnalysisTab();
                        }
                        finally
                        {
                            ImGui.EndTabItem();
                        }
                    }

                    if (ImGui.BeginTabItem("Summary"))
                    {
                        try
                        {
                            DrawFilterPanel("Summary", playerFilters: false);
                            DrawSummaryTab();
                        }
                        finally
                        {
                            ImGui.EndTabItem();
                        }
                    }

                    if (ImGui.BeginTabItem("People"))
                    {
                        try
                        {
                            DrawFilterPanel("People", encounterFilter: true);
                            DrawPeopleTab();
                        }
                        finally
                        {
                            ImGui.EndTabItem();
                        }
                    }

                    var settingsTabFlags = openSettings ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                    if (ImGui.BeginTabItem("Settings", settingsTabFlags))
                    {
                        try
                        {
                            openSettings = false;
                            DrawSettingsTab();
                        }
                        finally
                        {
                            ImGui.EndTabItem();
                        }
                    }
                }
                finally
                {
                    ImGui.EndTabBar();
                }
            }

            DrawResetSettingsConfirmation();
            if (openEditRecord)
            {
                ImGui.OpenPopup("EditDutyRecord");
                openEditRecord = false;
            }
            DrawEditRecordWindow();
            if (openDeleteConfirmation)
            {
                ImGui.OpenPopup("ConfirmDeleteRecord");
                openDeleteConfirmation = false;
            }
            DrawDeleteConfirmation();
            if (appearanceSettingsDirty && !ImGui.IsAnyItemActive())
                SaveAppearanceChanges();
        }
    }
}
