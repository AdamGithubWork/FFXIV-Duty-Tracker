
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace MentorRouletteTracker
{
    public partial class MainWindow : Window, IDisposable
    {
        private readonly HashSet<string> selectedPlayerRaces = new();
        private readonly HashSet<string> selectedPlayerGenders = new();
        private readonly HashSet<string> selectedPlayerStatuses = new();
        private readonly HashSet<string> selectedDutyTypes = new();

        private int selectedTimeRange = 0;
        private DateTime customStartDate = DateTime.Today.AddDays(-30);
        private DateTime customEndDate = DateTime.Today;

        private readonly Configuration config;
        private readonly DutyTrackerService tracker;
        private readonly string exportDirectory;
        private readonly IObjectTable objectTable;

        private string lastExportMessage = string.Empty;
        private DutyRecord? selectedRecord;
        private DutyRecord? recordPendingDeletion;
        private bool openDeleteConfirmation;
        private bool showDetailWindow;

        // Duty history navigation
        private string? dutyHistoryFilter;
        private bool openDutyHistory;

        private DutyRecord? recordToEdit;
        private bool openEditRecord;
        private int editWipeCount;
        private bool editJoinedInProgress;
        private string editError = string.Empty;

        private bool showNoteWindow = false;
        private string noteDraft = string.Empty;
        private DutyRecord? noteRecord = null;

        private string globalPlayerSearchText = string.Empty;
        private int selectedHeatmapMetric = 0;

        public MainWindow(
            Configuration config,
            DutyTrackerService tracker,
            string exportDirectory,
            IObjectTable objectTable)
            : base("Mentor Roulette Tracker###MentorRouletteTrackerMain")
        {
            this.config = config;
            this.tracker = tracker;
            this.exportDirectory = exportDirectory;
            this.objectTable = objectTable;

            Size = new Vector2(800, 520);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public void Dispose()
        {
        }

        // Called by DutyTimerWindow when the average-time
        // button is clicked.
        public void OpenDutyHistory(string dutyName)
        {
            if (string.IsNullOrWhiteSpace(dutyName))
                return;

            dutyHistoryFilter = dutyName;
            openDutyHistory = true;

            // Show all historical runs for this duty,
            // regardless of previously selected global filters.
            globalPlayerSearchText = string.Empty;
            selectedDutyTypes.Clear();
            selectedPlayerRaces.Clear();
            selectedPlayerGenders.Clear();
            selectedPlayerStatuses.Clear();
            selectedTimeRange = 0;

            IsOpen = true;
        }

        public override void Draw()
        {
            int activeFilterCount =
                selectedDutyTypes.Count +
                selectedPlayerRaces.Count +
                selectedPlayerGenders.Count +
                selectedPlayerStatuses.Count +
                (string.IsNullOrWhiteSpace(globalPlayerSearchText) ? 0 : 1) +
                (selectedTimeRange == 0 ? 0 : 1);

            string filterHeader = activeFilterCount == 0
                ? "Global Filters"
                : $"Global Filters ({activeFilterCount} active)";

            if (ImGui.CollapsingHeader(filterHeader + "###GlobalFilters"))
            {
                ImGui.Spacing();

                // Player search
                ImGui.Text("Search Players");

                ImGui.SetNextItemWidth(
                    Math.Min(300f, ImGui.GetContentRegionAvail().X));

                ImGui.InputText(
                    "##GlobalPlayerSearch",
                    ref globalPlayerSearchText,
                    100);

                ImGui.TextDisabled("Search by player name or world.");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Duty Types
                ImGui.Text("Duty Types");

                ImGui.SameLine();

                ImGui.TextDisabled(
                    selectedDutyTypes.Count == 0
                        ? "(All)"
                        : $"({selectedDutyTypes.Count} selected)");

                ImGui.Spacing();

                var dutyTypes = config.Records
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

                // Player Filters
                int activePlayerFilters =
                    selectedPlayerRaces.Count +
                    selectedPlayerGenders.Count +
                    selectedPlayerStatuses.Count;

                ImGui.Text("Player Filters");
                ImGui.SameLine();

                ImGui.TextDisabled(
                    activePlayerFilters == 0
                        ? "(All)"
                        : $"({activePlayerFilters} selected)");

                ImGui.Spacing();

                // Race
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

                // Gender
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

                // Status
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

                // Time Range
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
                            "Start date must be before end date.");
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Clear filters
                if (ImGui.Button("Clear All Filters"))
                {
                    globalPlayerSearchText = string.Empty;
                    selectedDutyTypes.Clear();
                    selectedPlayerRaces.Clear();
                    selectedPlayerGenders.Clear();
                    selectedPlayerStatuses.Clear();
                    selectedTimeRange = 0;

                    // Also clear the duty history filter.
                    dutyHistoryFilter = null;
                }

                ImGui.Dummy(new Vector2(0, 8));
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0, 8));
            }

            ImGui.Spacing();

            // Main Tabs
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

            // Detail and popup windows
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
