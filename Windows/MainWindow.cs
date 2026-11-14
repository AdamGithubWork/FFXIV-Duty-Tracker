
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
        private readonly HashSet<string> selectedDutyTypes = new();

        private string globalPlayerSearchText = string.Empty;
        private string globalDutySearchText = string.Empty;
        private int selectedTimeRange = 0;
        private DateTime customStartDate = DateTime.Today.AddDays(-30);
        private DateTime customEndDate = DateTime.Today;

        private DutyRecord? selectedRecord;
        private bool showDetailWindow;

        private string? dutyHistoryFilter;
        private bool openDutyHistory;

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

        private string lastExportMessage = string.Empty;

        private int selectedHeatmapMetric = 0;

        public MainWindow(
            Configuration config,
            DutyTrackerService tracker,
            string exportDirectory,
            IObjectTable objectTable)
            : base("Duty Tracker###MentorRouletteTrackerMain")
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

        public void Dispose()
        {
        }

        public void OpenDutyHistory(string dutyName)
        {
            if (string.IsNullOrWhiteSpace(dutyName))
                return;

            dutyHistoryFilter = dutyName;
            openDutyHistory = true;

            // Opening a duty from the timer resets unrelated filters.
            globalPlayerSearchText = string.Empty;
            globalDutySearchText = string.Empty;
            selectedDutyTypes.Clear();
            selectedPlayerRaces.Clear();
            selectedPlayerGenders.Clear();
            selectedPlayerStatuses.Clear();
            selectedTimeRange = 0;

            IsOpen = true;
        }

        public override void Draw()
        {
            if (!config.StorageAvailable)
            {
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Duty history couldn't be loaded");
                ImGui.TextWrapped(config.StorageError ?? "History storage is unavailable.");
                ImGui.TextUnformatted("Recording is paused.");
                ImGui.Spacing();
                ImGui.TextWrapped("If the database is missing, unload Duty Tracker and restore it from a backup. Then reload the plugin.");
                ImGui.TextWrapped($"Database: {Path.Combine(exportDirectory, "history.sqlite")}");
                ImGui.TextWrapped("For other storage errors, check the Dalamud log before restoring files.");
                return;
            }

            int activeFilterCount =
                selectedDutyTypes.Count +
                selectedPlayerRaces.Count +
                selectedPlayerGenders.Count +
                selectedPlayerStatuses.Count +
                (string.IsNullOrWhiteSpace(globalPlayerSearchText) ? 0 : 1) +
                (string.IsNullOrWhiteSpace(globalDutySearchText) ? 0 : 1) +
                (selectedTimeRange == 0 ? 0 : 1);

            string filterHeader = activeFilterCount == 0
                ? "Filters"
                : $"Filters ({activeFilterCount} active)";

            if (ImGui.CollapsingHeader(filterHeader + "###GlobalFilters"))
            {
                ImGui.Spacing();

                ImGui.Text("Find players");

                ImGui.SetNextItemWidth(
                    Math.Min(300f, ImGui.GetContentRegionAvail().X));

                ImGui.InputText(
                    "##GlobalPlayerSearch",
                    ref globalPlayerSearchText,
                    100);

                ImGui.TextDisabled("Player name or home world.");
                ImGui.Spacing();

                ImGui.Text("Find duties");

                ImGui.SetNextItemWidth(
                    Math.Min(300f, ImGui.GetContentRegionAvail().X));

                ImGui.InputText(
                    "##GlobalDutySearch",
                    ref globalDutySearchText,
                    100);

                ImGui.TextDisabled("Duty name.");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Duty Types");

                ImGui.SameLine();

                ImGui.TextDisabled(
                    selectedDutyTypes.Count == 0
                        ? "(All)"
                        : $"({selectedDutyTypes.Count} selected)");

                ImGui.Spacing();

                var dutyTypes = config.History.Records
                    .Select(record => record.DutyType)
                    .Where(type => !string.IsNullOrWhiteSpace(type))
                    .Concat(new[] { "Extreme Trials", "Savage Raids" })
                    .Distinct()
                    .OrderBy(type => type)
                    .ToList();

                DrawFilterCheckboxes(
                    dutyTypes,
                    selectedDutyTypes);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                int activePlayerFilters =
                    selectedPlayerRaces.Count +
                    selectedPlayerGenders.Count +
                    selectedPlayerStatuses.Count;

                ImGui.Text("Player filters");
                ImGui.SameLine();

                ImGui.TextDisabled(
                    activePlayerFilters == 0
                        ? "(All)"
                        : $"({activePlayerFilters} selected)");

                ImGui.Spacing();

                ImGui.Text("Race");
                ImGui.SameLine();

                ImGui.TextDisabled(
                    selectedPlayerRaces.Count == 0
                        ? "(All)"
                        : $"({selectedPlayerRaces.Count} selected)");

                string[] races =
                {
                    "Hyur",
                    "Elezen",
                    "Lalafell",
                    "Miqo'te",
                    "Roegadyn",
                    "Au Ra",
                    "Hrothgar",
                    "Viera"
                };

                DrawFilterCheckboxes(
                    races,
                    selectedPlayerRaces);

                ImGui.Spacing();

                ImGui.Text("Gender");
                ImGui.SameLine();

                ImGui.TextDisabled(
                    selectedPlayerGenders.Count == 0
                        ? "(All)"
                        : $"({selectedPlayerGenders.Count} selected)");

                string[] genders =
                {
                    "Male",
                    "Female"
                };

                DrawFilterCheckboxes(
                    genders,
                    selectedPlayerGenders);

                ImGui.Spacing();

                ImGui.Text("Status");
                ImGui.SameLine();

                ImGui.TextDisabled(
                    selectedPlayerStatuses.Count == 0
                        ? "(All)"
                        : $"({selectedPlayerStatuses.Count} selected)");

                string[] statuses =
                {
                    "Battle Mentor",
                    "Trade Mentor",
                    "Sprout",
                    "Party Member",
                    "Returner"
                };

                DrawFilterCheckboxes(
                    statuses,
                    selectedPlayerStatuses);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Time Range");

                string[] timeRanges =
                {
                    "All Time",
                    "Last 7 Days",
                    "Last 30 Days",
                    "Last 90 Days",
                    "Custom Range"
                };

                ImGui.SetNextItemWidth(
                    Math.Min(220f, ImGui.GetContentRegionAvail().X));

                ImGui.Combo(
                    "##GlobalTimeRange",
                    ref selectedTimeRange,
                    timeRanges,
                    timeRanges.Length);

                if (selectedTimeRange == 4)
                {
                    ImGui.Spacing();

                    int startYear = customStartDate.Year;
                    int startMonth = customStartDate.Month;
                    int startDay = customStartDate.Day;

                    int endYear = customEndDate.Year;
                    int endMonth = customEndDate.Month;
                    int endDay = customEndDate.Day;

                    ImGui.Text("Start Date");

                    ImGui.SetNextItemWidth(110);
                    ImGui.InputInt("Year##GlobalStart", ref startYear);

                    ImGui.SetNextItemWidth(110);
                    ImGui.InputInt("Month##GlobalStart", ref startMonth);

                    ImGui.SetNextItemWidth(110);
                    ImGui.InputInt("Day##GlobalStart", ref startDay);

                    ImGui.Spacing();

                    ImGui.Text("End Date");

                    ImGui.SetNextItemWidth(110);
                    ImGui.InputInt("Year##GlobalEnd", ref endYear);

                    ImGui.SetNextItemWidth(110);
                    ImGui.InputInt("Month##GlobalEnd", ref endMonth);

                    ImGui.SetNextItemWidth(110);
                    ImGui.InputInt("Day##GlobalEnd", ref endDay);

                    if (IsValidDate(startYear, startMonth, startDay))
                    {
                        customStartDate = new DateTime(
                            startYear, startMonth, startDay);
                    }

                    if (IsValidDate(endYear, endMonth, endDay))
                    {
                        customEndDate = new DateTime(
                            endYear, endMonth, endDay);
                    }

                    if (customStartDate > customEndDate)
                    {
                        ImGui.TextDisabled(
                            "The start date can't be after the end date.");
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.Button("Clear filters"))
                {
                    globalPlayerSearchText = string.Empty;
                    globalDutySearchText = string.Empty;
                    selectedDutyTypes.Clear();
                    selectedPlayerRaces.Clear();
                    selectedPlayerGenders.Clear();
                    selectedPlayerStatuses.Clear();
                    selectedTimeRange = 0;

                    dutyHistoryFilter = null;
                }

                ImGui.Dummy(new Vector2(0, 8));
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0, 8));
            }

            ImGui.Spacing();

            if (ImGui.BeginTabBar("MainTabs"))
            {
                var recordsTabFlags = openDutyHistory
                    ? ImGuiTabItemFlags.SetSelected
                    : ImGuiTabItemFlags.None;

                if (ImGui.BeginTabItem("Records", recordsTabFlags))
                {
                    DrawRecordsTab();
                    ImGui.EndTabItem();
                }

                openDutyHistory = false;

                if (ImGui.BeginTabItem("Statistics"))
                {
                    DrawStatisticsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Duty Analysis"))
                {
                    DrawDutyAnalysisTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Summary"))
                {
                    DrawSummaryTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("People"))
                {
                    DrawPeopleTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Settings"))
                {
                    DrawSettingsTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            DrawDetailWindow();
            DrawNoteWindow();

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
        }
    }
}
