using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;

namespace MentorRouletteTracker
{
    public class MainWindow : Window, IDisposable
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

        private DutyRecord? recordToEdit;

        private bool openEditRecord;
        private int editWipeCount;
        private bool editJoinedInProgress;
        private string editError = string.Empty;
        private bool showNoteWindow = false;
        private string noteDraft = string.Empty;
        private DutyRecord? noteRecord = null;
        private string globalPlayerSearchText = string.Empty;
        private int selectedPeopleRace = 0;
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
                }

                // Separate Global Filters from the main tabs
                ImGui.Dummy(new Vector2(0, 8));

                ImGui.Separator();

                ImGui.Dummy(new Vector2(0, 8));

            }


            ImGui.Spacing();

            // Main Tabs
            if (ImGui.BeginTabBar("MainTabs"))
            {
                if (ImGui.BeginTabItem("Records"))
                {
                    DrawRecordsTab();
                    ImGui.EndTabItem();
                }

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

        private void DrawFilterCheckboxes(
            IEnumerable<string> options,
            HashSet<string> selected)
        {
            var items = options.ToList();

            if (items.Count == 0)
            {
                ImGui.TextDisabled("No options available.");
                return;
            }

            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float checkboxSize = ImGui.GetFrameHeight();

            ImGui.PushID(selected.GetHashCode());

            for (int i = 0; i < items.Count; i++)
            {
                string option = items[i];
                bool isSelected = selected.Contains(option);

                if (ImGui.Checkbox(option, ref isSelected))
                {
                    if (isSelected)
                        selected.Add(option);
                    else
                        selected.Remove(option);
                }

                if (i < items.Count - 1)
                {
                    string nextOption = items[i + 1];

                    float nextWidth =
                        checkboxSize +
                        ImGui.GetStyle().ItemInnerSpacing.X +
                        ImGui.CalcTextSize(nextOption).X;

                    if (ImGui.GetContentRegionAvail().X >=
                        nextWidth + spacing)
                    {
                        ImGui.SameLine();
                    }
                }
            }

            ImGui.PopID();
        }


        private bool MatchesPlayerFilters(PartyMemberSnapshot player)
        {
            return
                (selectedPlayerRaces.Count == 0 ||
                 selectedPlayerRaces.Contains(player.Race)) &&

                (selectedPlayerGenders.Count == 0 ||
                 selectedPlayerGenders.Contains(player.Gender)) &&

                (selectedPlayerStatuses.Count == 0 ||
                 selectedPlayerStatuses.Contains(player.Status)) &&

                MatchesGlobalPlayerSearch(player);
        }
        private bool MatchesDutyPlayerFilters(DutyRecord record)
        {
            bool hasPlayerFilters =
                selectedPlayerRaces.Count > 0 ||
                selectedPlayerGenders.Count > 0 ||
                selectedPlayerStatuses.Count > 0 ||
                !string.IsNullOrWhiteSpace(globalPlayerSearchText);

            if (!hasPlayerFilters)
                return true;

            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            return record.Party.Any(player =>
                (config.IncludeSelfInStatistics ||
                 player.Name != localPlayerName) &&
                MatchesPlayerFilters(player));
        }
        private void DrawRecordsTab()
        {
            var armed = tracker.Armed;

            if (ImGui.Checkbox("Tracking armed", ref armed))
            {
                tracker.Armed = armed;
            }

            ImGui.SameLine();

            ImGui.TextDisabled(
                "(pause this before duties you don't want logged)");

            ImGui.Spacing();

            if (ImGui.Button("Export CSV"))
            {
                var path = Path.Combine(
                    exportDirectory,
                    $"mentor_roulette_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                CsvExporter.Export(config.Records, path);

                lastExportMessage =
                    $"Exported {config.Records.Count} records to {path}";
            }

            ImGui.SameLine();

            if (ImGui.Button("Clear all records"))
            {
                ImGui.OpenPopup("ConfirmClear");
            }

            if (ImGui.BeginPopup("ConfirmClear"))
            {
                ImGui.Text(
                    "Delete all recorded runs? This can't be undone.");

                ImGui.Spacing();

                if (ImGui.Button("Yes, clear"))
                {
                    config.Records.Clear();
                    config.SeenPlayers.Clear();
                    config.Save();

                    selectedRecord = null;
                    showDetailWindow = false;

                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            if (!string.IsNullOrEmpty(lastExportMessage))
            {
                ImGui.Spacing();

                ImGui.TextColored(
                    new Vector4(0.4f, 1f, 0.4f, 1f),
                    lastExportMessage);
            }

            ImGui.Spacing();

            ImGui.TextDisabled(
                "Click a duty name to view full party details. Right-click a record to delete it. Click column headers to sort.");

            ImGui.Spacing();

            if (ImGui.BeginTable(
                    "records",
                    9,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn(
                    "Duty",
                    ImGuiTableColumnFlags.PreferSortAscending);

                ImGui.TableSetupColumn("Type");
                ImGui.TableSetupColumn("Job");
                ImGui.TableSetupColumn("Started");
                ImGui.TableSetupColumn("Time");

                ImGui.TableSetupColumn(
                    "Joined In Progress",
                    ImGuiTableColumnFlags.WidthFixed,
                    110f);

                ImGui.TableSetupColumn("Wipes");
                ImGui.TableSetupColumn("Lalafell");
                ImGui.TableSetupColumn("Completed");

                ImGui.TableHeadersRow();

                var now = DateTime.Now;

                var records = config.Records
                    .Select((record, index) => new
                    {
                        Record = record,
                        Index = index
                    })
                    .Where(x =>
                        selectedDutyTypes.Count == 0 ||
                        selectedDutyTypes.Contains(x.Record.DutyType))
                    .Where(x => MatchesDutyPlayerFilters(x.Record))
                    .Where(x => selectedTimeRange switch
                    {
                        1 => x.Record.Timestamp >= now.AddDays(-7),
                        2 => x.Record.Timestamp >= now.AddDays(-30),
                        3 => x.Record.Timestamp >= now.AddDays(-90),
                        4 => x.Record.Timestamp >= customStartDate.Date &&
                             x.Record.Timestamp < customEndDate.Date.AddDays(1),
                        _ => true
                    })
                    .ToList();

                var sortSpecs = ImGui.TableGetSortSpecs();

                if (sortSpecs.SpecsCount > 0)
                {
                    var spec = sortSpecs.Specs;

                    records = spec.ColumnIndex switch
                    {
                        0 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? records.OrderBy(x => x.Record.DutyName).ToList()
                            : records.OrderByDescending(x => x.Record.DutyName).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? records.OrderBy(x => x.Record.DutyType).ToList()
                            : records.OrderByDescending(x => x.Record.DutyType).ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? records.OrderBy(x => x.Record.PlayerJob).ToList()
                            : records.OrderByDescending(x => x.Record.PlayerJob).ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? records.OrderBy(x => x.Record.Timestamp).ToList()
                            : records.OrderByDescending(x => x.Record.Timestamp).ToList(),

                        4 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? records.OrderBy(x => x.Record.TimeInDuty).ToList()
                            : records.OrderByDescending(x => x.Record.TimeInDuty).ToList(),

                        5 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? records.OrderBy(x => x.Record.JoinedInProgress).ToList()
                            : records.OrderByDescending(x => x.Record.JoinedInProgress).ToList(),

                        6 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? records.OrderBy(x => x.Record.WipeCount).ToList()
                            : records.OrderByDescending(x => x.Record.WipeCount).ToList(),

                        7 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? records.OrderBy(x => x.Record.LalafellCount).ToList()
                            : records.OrderByDescending(x => x.Record.LalafellCount).ToList(),

                        8 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? records.OrderBy(x => x.Record.Completed).ToList()
                            : records.OrderByDescending(x => x.Record.Completed).ToList(),

                        _ => records
                    };

                    sortSpecs.SpecsDirty = false;
                }
                else
                {
                    records = records
                        .OrderByDescending(x => x.Record.Timestamp)
                        .ToList();
                }

                foreach (var item in records)
                {
                    var r = item.Record;
                    var i = item.Index;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);

                    if (ImGui.Selectable(
                            $"{r.DutyName}##record{i}",
                            false,
                            ImGuiSelectableFlags.SpanAllColumns))
                    {
                        selectedRecord = r;
                        showDetailWindow = true;
                    }

                    if (ImGui.BeginPopupContextItem($"RecordContext{i}"))
                    {
                        ImGui.TextUnformatted(r.DutyName);

                        ImGui.Separator();
                        if (ImGui.MenuItem("Edit record"))
                        {
                            StartEditingRecord(r);
                        }
                        if (ImGui.MenuItem("Delete record"))
                        {
                            recordPendingDeletion = r;
                            openDeleteConfirmation = true;
                        }
                        ImGui.EndPopup();
                    }

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(r.DutyType);

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(r.PlayerJob);

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));

                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(
                        FormatDuration(r.TimeInDuty));

                    ImGui.TableSetColumnIndex(5);
                    ImGui.TextUnformatted(
                        r.JoinedInProgress ? "Yes" : "No");

                    ImGui.TableSetColumnIndex(6);
                    ImGui.TextUnformatted(
                        r.WipeCount.ToString());

                    ImGui.TableSetColumnIndex(7);
                    ImGui.TextUnformatted(
                        r.LalafellCount.ToString());

                    ImGui.TableSetColumnIndex(8);
                    ImGui.TextUnformatted(
                        r.Completed ? "Yes" : "No");
                }

                ImGui.EndTable();
            }
        }

        private void StartEditingRecord(DutyRecord record)
        {
            recordToEdit = record;
            editWipeCount = record.WipeCount;
            editJoinedInProgress = record.JoinedInProgress;
            editError = string.Empty;
            openEditRecord = true;
        }

        private void DrawEditRecordWindow()
        {
            if (!ImGui.BeginPopupModal("EditDutyRecord", ImGuiWindowFlags.AlwaysAutoResize))
            {
                return;
            }
            if (recordToEdit == null)
            {
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
                return;
            }
            ImGui.Text("Edit duty record");
            ImGui.Separator();
            ImGui.InputInt("Wipes", ref editWipeCount);
            ImGui.Checkbox("Joined in progress", ref editJoinedInProgress);
            if (!string.IsNullOrEmpty(editError))
            {
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), editError);
            }
            ImGui.Separator();
            if (ImGui.Button("Save"))
            {
                if (editWipeCount < 0)
                {
                    editError = "Wipe count cannot be negative";
                }
                else if (editWipeCount > 99)
                {
                    editError = "Wipe count shouldn't be over 99";
                }
                else
                {
                    recordToEdit.WipeCount = editWipeCount;
                    recordToEdit.JoinedInProgress = editJoinedInProgress;
                    config.Save();
                    recordToEdit = null;
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                recordToEdit = null;
                editError = string.Empty;
                ImGui.CloseCurrentPopup() ;
            }
            ImGui.EndPopup() ;
        }
        private void DrawDeleteConfirmation()
        {
            if (!ImGui.BeginPopupModal(
                    "ConfirmDeleteRecord",
                    ImGuiWindowFlags.AlwaysAutoResize))
            {
                return;
            }

            if (recordPendingDeletion != null)
            {
                ImGui.Text("Delete this record?");

                ImGui.Spacing();

                ImGui.TextUnformatted(
                    recordPendingDeletion.DutyName);

                ImGui.TextDisabled(
                    recordPendingDeletion.Timestamp.ToString(
                        "yyyy-MM-dd HH:mm:ss"));

                ImGui.Spacing();

                ImGui.TextDisabled(
                    "This will remove the entire duty record.");

                ImGui.Spacing();

                if (ImGui.Button("Delete"))
                {
                    var record = recordPendingDeletion;

                    config.Records.Remove(record);

                    RebuildSeenPlayers();

                    config.Save();

                    if (selectedRecord == record)
                    {
                        selectedRecord = null;
                        showDetailWindow = false;
                    }

                    recordPendingDeletion = null;

                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel"))
                {
                    recordPendingDeletion = null;

                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndPopup();
        }

        private void RebuildSeenPlayers()
        {
            config.SeenPlayers.Clear();

            var people = config.Records.SelectMany(x => x.Party).GroupBy(x => new { x.Name, x.World });

            foreach (var group in people)
            {
                var race = group
                    .Select(x => x.Race)
                    .FirstOrDefault(x => x != "Unknown")
                    ?? "Unknown";

                var gender = group
                    .Select(x => x.Gender)
                    .FirstOrDefault(x => x != "Unknown")
                    ?? "Unknown";

                config.SeenPlayers.Add(new SeenPlayer
                {
                    Name = group.Key.Name,
                    World = group.Key.World,
                    Race = race,
                    Gender = gender,
                    TimesSeen = group.Count()
                });
            }
        }
        private static bool IsValidDate(int year, int month, int day)
        {
            return year >= 1 && year <= 9999 &&
                   month >= 1 && month <= 12 &&
                   day >= 1 &&
                   day <= DateTime.DaysInMonth(year, month);
        }
        private void DrawStatisticsTab()
        {
            var now = DateTime.Now;

            var records = config.Records
                .Where(x => x.Completed)
                .Where(x =>
                    selectedDutyTypes.Count == 0 ||
                    selectedDutyTypes.Contains(x.DutyType))
                .Where(x => selectedTimeRange switch
                {
                    1 => x.Timestamp >= now.AddDays(-7),
                    2 => x.Timestamp >= now.AddDays(-30),
                    3 => x.Timestamp >= now.AddDays(-90),
                    4 => x.Timestamp >= customStartDate.Date && x.Timestamp < customEndDate.Date.AddDays(1),
                    _ => true
                })
                .Where(x => MatchesDutyPlayerFilters(x))
                .OrderBy(x => x.Timestamp)
                .ToList();
            var dutiesByDayAndTime = records
                .GroupBy(record => new
                {
                    Day = record.Timestamp.DayOfWeek,
                    TimePeriod = GetTimePeriod(record.Timestamp)
                })
                .Select(group => new
                {
                    group.Key.Day,
                    group.Key.TimePeriod,
                    Count = group.Count(),
                    AverageDuration = group.Average(x => x.TimeInDuty.TotalMinutes)
                })
                .ToList();
            if (ImGui.BeginChild(
                    "StatisticsContent",
                    Vector2.Zero,
                    false,
                    ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.Text("Mentor Roulette Statistics");

                ImGui.Spacing();

                ImGui.TextDisabled(
                    "Statistics based on completed duties.");

                ImGui.Spacing();

                ImGui.Separator();

                ImGui.Spacing();

                if (records.Count == 0)
                {
                    ImGui.TextDisabled(
                        "No completed duties have been recorded yet.");

                    ImGui.EndChild();
                    return;
                }

                DrawOverallStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawSelectedPlayerStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawDutyTypeStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawMostCommonDuty(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawDutySpecificDetails(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Duties by Day and Time");
                ImGui.Separator();
                var heatmapMetrics = new[]
                {
                    "Duty Count",
                    "Average Duration"
                };

                ImGui.SetNextItemWidth(200);

                ImGui.Combo(
                    "Metric##Heatmap",
                    ref selectedHeatmapMetric,
                    heatmapMetrics,
                    heatmapMetrics.Length);

                ImGui.Spacing();
                var timePeriods = new[]
                {
                    "Late Night",
                    "Early Morning",
                    "Morning",
                    "Afternoon",
                    "Evening",
                    "Night"
                };

                var days = new[]
                {
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday,
                    DayOfWeek.Saturday,
                    DayOfWeek.Sunday
                };

                if (ImGui.BeginTable(
                    "dutiesHeatmap",
                    7,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("Day");

                    foreach (var period in timePeriods)
                    {
                        ImGui.TableSetupColumn(period);
                    }

                    ImGui.TableHeadersRow();

                    foreach (var day in days)
                    {
                        ImGui.TableNextRow();

                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted(day.ToString());

                        for (int i = 0; i < timePeriods.Length; i++)
                        {
                            var period = timePeriods[i];

                            var group = dutiesByDayAndTime
                                .FirstOrDefault(x => x.Day == day && x.TimePeriod == period);

                            var count = group?.Count ?? 0;

                            var value = selectedHeatmapMetric == 0
                                ? count
                                : group?.AverageDuration ?? 0;

                            ImGui.TableSetColumnIndex(i + 1);

                            var maxValue = Math.Max(
                                1.0,
                                dutiesByDayAndTime.Max(x =>
                                    selectedHeatmapMetric == 0
                                        ? x.Count
                                        : x.AverageDuration));

                            var intensity = (float)(value / maxValue);

                            Vector4 cellColor;

                            if (selectedHeatmapMetric == 0)
                            {
                                // Duty Count: darker green -> brighter green
                                cellColor = new Vector4(
                                    0.08f,
                                    0.25f + intensity * 0.45f,
                                    0.12f + intensity * 0.20f,
                                    1.0f);
                            }
                            else
                            {
                                // Average Duration: green -> yellow -> red
                                cellColor = new Vector4(
                                    0.15f + intensity * 0.65f,
                                    0.65f - intensity * 0.45f,
                                    0.12f,
                                    1.0f);
                            }

                            if (count > 0)
                            {
                                ImGui.TableSetBgColor(
                                    ImGuiTableBgTarget.CellBg,
                                    ImGui.ColorConvertFloat4ToU32(cellColor));
                            }

                            ImGui.TextUnformatted(
                                selectedHeatmapMetric == 0
                                    ? count.ToString()
                                    : count > 0
                                        ? $"{value:F1}m"
                                        : "—");
                            if (ImGui.IsItemHovered() && count > 0)
                            {
                                ImGui.BeginTooltip();

                                ImGui.TextUnformatted($"{day} - {period}");
                                ImGui.Separator();

                                ImGui.TextUnformatted($"Duties: {count}");

                                if (selectedHeatmapMetric == 1)
                                {
                                    ImGui.TextUnformatted(
                                        $"Average Duration: {value:F1} minutes");
                                }

                                ImGui.EndTooltip();
                            }
                        }
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.EndChild();
        }

        private void DrawOverallStatistics(List<DutyRecord> records)
        {
            ImGui.Text("Overall");

            ImGui.Spacing();

            var averageSeconds =
                records.Average(x => x.TimeInDuty.TotalSeconds);

            var fastest = records.MinBy(x => x.TimeInDuty);
            var longest = records.MaxBy(x => x.TimeInDuty);
            var mostLalafell = records.MaxBy(x => x.LalafellCount);

            if (ImGui.BeginTable(
                    "overallStats",
                    2,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Statistic");
                ImGui.TableSetupColumn("Value");

                DrawStatisticRow(
                    "Total Duties",
                    records.Count.ToString());

                DrawStatisticRow(
                    "Average Duty Length",
                    FormatDuration(
                        TimeSpan.FromSeconds(averageSeconds)));

                DrawStatisticRow(
                    "Fastest Duty",
                    fastest == null
                        ? "N/A"
                        : $"{FormatDuration(fastest.TimeInDuty)} — {fastest.DutyName}");

                DrawStatisticRow(
                    "Longest Duty",
                    longest == null
                        ? "N/A"
                        : $"{FormatDuration(longest.TimeInDuty)} — {longest.DutyName}");

                DrawStatisticRow(
                    "Most Lalafell in One Duty",
                    mostLalafell == null
                        ? "N/A"
                        : $"{mostLalafell.LalafellCount} — {mostLalafell.DutyName}");

                ImGui.EndTable();
            }
        }

        private void DrawSelectedPlayerStatistics(List<DutyRecord> records)
        {
            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            ImGui.Text("Players in Matching Duties");
            ImGui.Spacing();

            var matchingPlayers = records
                .SelectMany(x => x.Party)
                .Where(x =>
                    config.IncludeSelfInStatistics ||
                    x.Name != localPlayerName)
                .ToList();

            var matchingCounts = records
                .Select(record => record.Party.Count(player =>
                    config.IncludeSelfInStatistics ||
                    player.Name != localPlayerName))
                .ToList();

            var currentDrought = 0;
            for (var i = matchingCounts.Count - 1; i >= 0; i--)
            {
                if (matchingCounts[i] > 0)
                    break;

                currentDrought++;
            }

            var longestDrought = 0;
            var drought = 0;

            foreach (var count in matchingCounts)
            {
                if (count == 0)
                {
                    drought++;
                    longestDrought = Math.Max(longestDrought, drought);
                }
                else
                {
                    drought = 0;
                }
            }

            var totalEncounters = matchingCounts.Sum();

            var dutiesWithMatchingPlayers =
                matchingCounts.Count(count => count > 0);

            var uniquePlayers = matchingPlayers
                .Select(x => (x.Name, x.World))
                .Distinct()
                .Count();

            if (ImGui.BeginTable(
                    "lalaStats",
                    2,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Statistic");
                ImGui.TableSetupColumn("Value");

                DrawStatisticRow(
                    "Unique Players Seen",
                    uniquePlayers.ToString());

                DrawStatisticRow(
                    "Total Encounters",
                    totalEncounters.ToString());

                DrawStatisticRow(
                    "Duties With Matching Players",
                    dutiesWithMatchingPlayers.ToString());

                DrawStatisticRow(
                    "Duties Since Last Encounter",
                    currentDrought.ToString());

                DrawStatisticRow(
                    "Longest Encounter Drought",
                    $"{longestDrought} duties");

                ImGui.EndTable();
            }
        }

        private void DrawDutySpecificDetails(List<DutyRecord> records)
        {
            ImGui.Text("Duty Specific Details");

            ImGui.Spacing();

            var dutyStats = records
                .GroupBy(x => x.DutyName)
                .OrderBy(x => x.Key)
                .ToList();

            if (dutyStats.Count == 0)
            {
                ImGui.TextDisabled(
                    "No duty data has been recorded yet.");

                return;
            }

            if (ImGui.BeginTable(
                    "dutySpecificDetails",
                    8,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn(
                    "Duty",
                    ImGuiTableColumnFlags.PreferSortAscending);

                ImGui.TableSetupColumn("Runs");
                ImGui.TableSetupColumn("Deaths");
                ImGui.TableSetupColumn("Deaths / Run");
                ImGui.TableSetupColumn("Total Wipes");
                ImGui.TableSetupColumn("Wipes / Run");
                ImGui.TableSetupColumn("Avg Time");
                ImGui.TableSetupColumn("In Progress");

                ImGui.TableHeadersRow();

                var stats = dutyStats
                    .Select(x => new
                    {
                        Duty = x.Key,
                        Runs = x.Count(),

                        Deaths = x.Sum(y =>
                            y.Party
                                .Where(p =>
                                    !string.IsNullOrWhiteSpace(p.Name) && (config.IncludeSelfInStatistics ||
                                    p.Name != objectTable.LocalPlayer?.Name.TextValue))
                                .Sum(p => p.DeathCount)),

                        TotalWipes = x.Sum(y => y.WipeCount),

                        AverageTime = TimeSpan.FromTicks(
                            (long)x.Average(y => y.TimeInDuty.Ticks)),

                        InProgress = x.Count(y => y.JoinedInProgress)
                    })
                    .ToList();

                var sortSpecs = ImGui.TableGetSortSpecs();

                if (sortSpecs.SpecsCount > 0)
                {
                    var spec = sortSpecs.Specs;

                    stats = spec.ColumnIndex switch
                    {
                        0 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.Duty).ToList()
                            : stats.OrderByDescending(x => x.Duty).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.Runs).ThenBy(x => x.Duty).ToList()
                            : stats.OrderByDescending(x => x.Runs).ThenBy(x => x.Duty).ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.Deaths).ThenBy(x => x.Duty).ToList()
                            : stats.OrderByDescending(x => x.Deaths).ThenBy(x => x.Duty).ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x =>
                                x.Runs == 0
                                    ? 0
                                    : (double)x.Deaths / x.Runs)
                                .ThenBy(x => x.Duty)
                                .ToList()
                            : stats.OrderByDescending(x =>
                                x.Runs == 0
                                    ? 0
                                    : (double)x.Deaths / x.Runs)
                                .ThenBy(x => x.Duty)
                                .ToList(),

                        4 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.TotalWipes)
                                .ThenBy(x => x.Duty)
                                .ToList()
                            : stats.OrderByDescending(x => x.TotalWipes)
                                .ThenBy(x => x.Duty)
                                .ToList(),

                        5 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x =>
                                x.Runs == 0
                                    ? 0
                                    : (double)x.TotalWipes / x.Runs)
                                .ThenBy(x => x.Duty)
                                .ToList()
                            : stats.OrderByDescending(x =>
                                x.Runs == 0
                                    ? 0
                                    : (double)x.TotalWipes / x.Runs)
                                .ThenBy(x => x.Duty)
                                .ToList(),

                        6 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.AverageTime)
                                .ThenBy(x => x.Duty)
                                .ToList()
                            : stats.OrderByDescending(x => x.AverageTime)
                                .ThenBy(x => x.Duty)
                                .ToList(),

                        7 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.InProgress)
                                .ThenBy(x => x.Duty)
                                .ToList()
                            : stats.OrderByDescending(x => x.InProgress)
                                .ThenBy(x => x.Duty)
                                .ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    var deathsPerRun = stat.Runs == 0
                        ? 0
                        : (double)stat.Deaths / stat.Runs;

                    var wipesPerRun = stat.Runs == 0
                        ? 0
                        : (double)stat.TotalWipes / stat.Runs;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Duty);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(
                        stat.Runs.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(
                        stat.Deaths.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        deathsPerRun.ToString("0.00"));

                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(
                        stat.TotalWipes.ToString());

                    ImGui.TableSetColumnIndex(5);
                    ImGui.TextUnformatted(
                        wipesPerRun.ToString("0.00"));

                    ImGui.TableSetColumnIndex(6);
                    ImGui.TextUnformatted(
                        FormatDuration(stat.AverageTime));

                    ImGui.TableSetColumnIndex(7);
                    ImGui.TextUnformatted(
                        stat.InProgress.ToString());
                }

                ImGui.EndTable();
            }
        }

        private void DrawSummaryTab()
        {
            // Lifetime records, completely independent of global filters.
            var records = config.Records.ToList();

            if (ImGui.BeginChild(
                "SummaryContent",
                Vector2.Zero,
                false,
                ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.Text("Lifetime Summary");
                ImGui.TextDisabled("All recorded duties. Global filters do not apply.");
                ImGui.Separator();
                ImGui.Spacing();

                if (records.Count == 0)
                {
                    ImGui.TextDisabled("No duty records available.");
                    ImGui.EndChild();
                    return;
                }

                var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

                var players = records
                    .SelectMany(record => record.Party)
                    .Where(player =>
                        config.IncludeSelfInStatistics ||
                        player.Name != localPlayerName)
                    .ToList();

                int uniquePlayers = players
                    .Select(player => (player.Name, player.World))
                    .Distinct()
                    .Count();

                int uniqueWorlds = players
                    .Select(player => player.World)
                    .Where(world =>
                        !string.IsNullOrWhiteSpace(world) &&
                        world != "Unknown")
                    .Distinct()
                    .Count();

                // Overview
                ImGui.Text("Overview");
                ImGui.Spacing();

                if (ImGui.BeginTable(
                    "summaryOverview",
                    2,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("Statistic");
                    ImGui.TableSetupColumn("Value");

                    DrawStatisticRow("Recorded Duties", records.Count.ToString());
                    DrawStatisticRow("Player Encounters", players.Count.ToString());
                    DrawStatisticRow("Unique Players", uniquePlayers.ToString());
                    DrawStatisticRow("Unique Worlds", uniqueWorlds.ToString());

                    ImGui.EndTable();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawSummaryStatusStatistics(players);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawSummaryWorldStatistics(players);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Reuse existing statistics with unfiltered records.
                DrawRaceStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawJobStatistics(records);
            }

            ImGui.EndChild();
        }

        private void DrawSummaryStatusStatistics(
            List<PartyMemberSnapshot> players)
        {
            ImGui.Text("Player Status Distribution");
            ImGui.Spacing();

            var stats = players
                .GroupBy(player =>
                    string.IsNullOrWhiteSpace(player.Status)
                        ? "Unknown"
                        : player.Status)
                .Select(group => new
                {
                    Status = group.Key,
                    Encounters = group.Count(),
                    Deaths = group.Sum(player => player.DeathCount)
                })
                .OrderByDescending(stat => stat.Encounters)
                .ToList();

            if (stats.Count == 0)
            {
                ImGui.TextDisabled("No player status data available.");
                return;
            }

            if (ImGui.BeginTable(
                "summaryStatusStats",
                4,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Status");
                ImGui.TableSetupColumn("Encounters");
                ImGui.TableSetupColumn("Percentage");
                ImGui.TableSetupColumn("Deaths / Encounter");
                ImGui.TableHeadersRow();

                foreach (var stat in stats)
                {
                    double percentage = players.Count == 0
                        ? 0
                        : (double)stat.Encounters / players.Count * 100;

                    double deathsPerEncounter = stat.Encounters == 0
                        ? 0
                        : (double)stat.Deaths / stat.Encounters;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Status);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Encounters.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted($"{percentage:F1}%");

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted($"{deathsPerEncounter:F2}");
                }

                ImGui.EndTable();
            }
        }

        private void DrawSummaryWorldStatistics(
            List<PartyMemberSnapshot> players)
        {
            ImGui.Text("Player World Distribution");
            ImGui.Spacing();

            var validPlayers = players
                .Where(player =>
                    !string.IsNullOrWhiteSpace(player.World) &&
                    player.World != "Unknown")
                .ToList();

            var stats = validPlayers
                .GroupBy(player => player.World)
                .Select(group => new
                {
                    World = group.Key,
                    Encounters = group.Count(),
                    Deaths = group.Sum(player => player.DeathCount)
                })
                .OrderByDescending(stat => stat.Encounters)
                .ToList();

            if (stats.Count == 0)
            {
                ImGui.TextDisabled("No player world data available.");
                return;
            }

            if (ImGui.BeginTable(
                "summaryWorldStats",
                4,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn("World");
                ImGui.TableSetupColumn("Encounters");
                ImGui.TableSetupColumn("Percentage");
                ImGui.TableSetupColumn("Deaths / Encounter");
                ImGui.TableHeadersRow();

                var sortSpecs = ImGui.TableGetSortSpecs();

                if (sortSpecs.SpecsCount > 0)
                {
                    var spec = sortSpecs.Specs;
                    bool ascending =
                        spec.SortDirection == ImGuiSortDirection.Ascending;

                    stats = spec.ColumnIndex switch
                    {
                        0 => ascending
                            ? stats.OrderBy(x => x.World).ToList()
                            : stats.OrderByDescending(x => x.World).ToList(),

                        1 => ascending
                            ? stats.OrderBy(x => x.Encounters).ToList()
                            : stats.OrderByDescending(x => x.Encounters).ToList(),

                        2 => ascending
                            ? stats.OrderBy(x => x.Encounters).ToList()
                            : stats.OrderByDescending(x => x.Encounters).ToList(),

                        3 => ascending
                            ? stats.OrderBy(x =>
                                (double)x.Deaths / x.Encounters).ToList()
                            : stats.OrderByDescending(x =>
                                (double)x.Deaths / x.Encounters).ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    double percentage =
                        (double)stat.Encounters / validPlayers.Count * 100;

                    double deathsPerEncounter =
                        (double)stat.Deaths / stat.Encounters;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.World);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Encounters.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted($"{percentage:F1}%");

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted($"{deathsPerEncounter:F2}");
                }

                ImGui.EndTable();
            }
        }
        private void DrawDutyTypeStatistics(List<DutyRecord> records)
        {
            ImGui.Text("Duties by Type");

            ImGui.Spacing();

            var typeCounts = records
                .GroupBy(x => x.DutyType)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key)
                .ToList();

            if (ImGui.BeginTable(
                    "dutyTypeStats",
                    2,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Duty Type");
                ImGui.TableSetupColumn("Count");

                ImGui.TableHeadersRow();

                foreach (var group in typeCounts)
                {
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(group.Key);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(
                        group.Count().ToString());
                }

                ImGui.EndTable();
            }
        }

        private void DrawMostCommonDuty(List<DutyRecord> records)
        {
            ImGui.Text("Most Common Duty");

            ImGui.Spacing();

            var mostCommon = records
                .GroupBy(x => x.DutyName)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key)
                .First();

            if (ImGui.BeginTable(
                    "mostCommonDuty",
                    2,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Duty");
                ImGui.TableSetupColumn("Times Completed");

                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(mostCommon.Key);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(
                    mostCommon.Count().ToString());

                ImGui.EndTable();
            }
        }

        private void DrawRaceStatistics(List<DutyRecord> records)
        {
            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            var characters = records
                .SelectMany(x => x.Party)
                .Where(x =>
                    (config.IncludeSelfInStatistics || x.Name != localPlayerName) &&
                    !string.IsNullOrWhiteSpace(x.Race) &&
                    x.Race != "Unknown")
                .ToList();

            // ---------------------------------------------------------
            // Race Statistics
            // ---------------------------------------------------------

            ImGui.Text("Race Statistics");

            ImGui.Spacing();

            var raceStats = characters
                .GroupBy(x => x.Race)
                .Select(x => new
                {
                    Race = x.Key,
                    Seen = x.Count(),
                    Deaths = x.Sum(y => y.DeathCount)
                })
                .OrderBy(x => x.Race)
                .ToList();

            if (raceStats.Count == 0)
            {
                ImGui.TextDisabled(
                    "No race data has been recorded yet.");
            }
            else if (ImGui.BeginTable(
                         "raceStats",
                         4,
                         ImGuiTableFlags.Borders |
                         ImGuiTableFlags.RowBg |
                         ImGuiTableFlags.Resizable |
                         ImGuiTableFlags.SizingStretchProp |
                         ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn(
                    "Race",
                    ImGuiTableColumnFlags.PreferSortAscending);

                ImGui.TableSetupColumn("Seen");
                ImGui.TableSetupColumn("Deaths");
                ImGui.TableSetupColumn("Deaths / Seen");

                ImGui.TableHeadersRow();

                var sortSpecs = ImGui.TableGetSortSpecs();

                if (sortSpecs.SpecsCount > 0)
                {
                    var spec = sortSpecs.Specs;

                    raceStats = spec.ColumnIndex switch
                    {
                        0 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x => x.Race).ToList()
                            : raceStats.OrderByDescending(x => x.Race).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x => x.Seen)
                                .ThenBy(x => x.Race)
                                .ToList()
                            : raceStats.OrderByDescending(x => x.Seen)
                                .ThenBy(x => x.Race)
                                .ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x => x.Deaths)
                                .ThenBy(x => x.Race)
                                .ToList()
                            : raceStats.OrderByDescending(x => x.Deaths)
                                .ThenBy(x => x.Race)
                                .ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x =>
                                x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Race)
                                .ToList()
                            : raceStats.OrderByDescending(x =>
                                x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Race)
                                .ToList(),

                        _ => raceStats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in raceStats)
                {
                    var deathsPerSeen = stat.Seen == 0
                        ? 0
                        : (double)stat.Deaths / stat.Seen;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Race);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Deaths.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        deathsPerSeen.ToString("0.00"));
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // ---------------------------------------------------------
            // Race / Gender Statistics
            // ---------------------------------------------------------

            ImGui.Text("Race / Gender Statistics");

            ImGui.Spacing();

            var raceGenderStats = characters
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Gender) &&
                    x.Gender != "Unknown")
                .GroupBy(x => new
                {
                    x.Race,
                    x.Gender
                })
                .Select(x => new
                {
                    Race = x.Key.Race,
                    Gender = x.Key.Gender,
                    Seen = x.Count(),
                    Deaths = x.Sum(y => y.DeathCount)
                })
                .OrderBy(x => x.Race)
                .ThenBy(x => x.Gender)
                .ToList();

            if (raceGenderStats.Count == 0)
            {
                ImGui.TextDisabled(
                    "No race or gender data has been recorded yet.");

                return;
            }

            if (ImGui.BeginTable(
                    "raceGenderStats",
                    5,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn(
                    "Race",
                    ImGuiTableColumnFlags.PreferSortAscending);

                ImGui.TableSetupColumn("Gender");
                ImGui.TableSetupColumn("Seen");
                ImGui.TableSetupColumn("Deaths");
                ImGui.TableSetupColumn("Deaths / Seen");

                ImGui.TableHeadersRow();

                var sortSpecs = ImGui.TableGetSortSpecs();

                if (sortSpecs.SpecsCount > 0)
                {
                    var spec = sortSpecs.Specs;

                    raceGenderStats = spec.ColumnIndex switch
                    {
                        0 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList()
                            : raceGenderStats.OrderByDescending(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Gender)
                                .ThenBy(x => x.Race)
                                .ToList()
                            : raceGenderStats.OrderByDescending(x => x.Gender)
                                .ThenBy(x => x.Race)
                                .ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Seen)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList()
                            : raceGenderStats.OrderByDescending(x => x.Seen)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Deaths)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList()
                            : raceGenderStats.OrderByDescending(x => x.Deaths)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList(),

                        4 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x =>
                                x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList()
                            : raceGenderStats.OrderByDescending(x =>
                                x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList(),

                        _ => raceGenderStats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in raceGenderStats)
                {
                    var deathsPerSeen = stat.Seen == 0
                        ? 0
                        : (double)stat.Deaths / stat.Seen;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Race);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Gender);

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(stat.Deaths.ToString());

                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(
                        deathsPerSeen.ToString("0.00"));
                }

                ImGui.EndTable();
            }
        }

        private void DrawJobStatistics(List<DutyRecord> records)
        {
            ImGui.Text("Job Statistics");

            ImGui.Spacing();

            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            var jobStats = records
                .SelectMany(x => x.Party)
                .Where(x =>
                    (config.IncludeSelfInStatistics || x.Name != localPlayerName) &&
                    !string.IsNullOrWhiteSpace(x.Job) &&
                    x.Job != "Unknown")
                .GroupBy(x => x.Job)
                .OrderBy(x => x.Key)
                .ToList();

            if (jobStats.Count == 0)
            {
                ImGui.TextDisabled(
                    "No job data has been recorded yet.");

                return;
            }

            if (ImGui.BeginTable(
                    "jobStats",
                    4,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn(
                    "Job",
                    ImGuiTableColumnFlags.PreferSortAscending);

                ImGui.TableSetupColumn("Seen");
                ImGui.TableSetupColumn("Deaths");
                ImGui.TableSetupColumn("Deaths / Seen");

                ImGui.TableHeadersRow();

                var stats = jobStats
                    .Select(x => new
                    {
                        Job = x.Key,
                        Seen = x.Count(),
                        Deaths = x.Sum(y => y.DeathCount)
                    })
                    .ToList();

                var sortSpecs = ImGui.TableGetSortSpecs();

                if (sortSpecs.SpecsCount > 0)
                {
                    var spec = sortSpecs.Specs;

                    stats = spec.ColumnIndex switch
                    {
                        0 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.Job).ToList()
                            : stats.OrderByDescending(x => x.Job).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.Seen).ThenBy(x => x.Job).ToList()
                            : stats.OrderByDescending(x => x.Seen).ThenBy(x => x.Job).ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.Deaths).ThenBy(x => x.Job).ToList()
                            : stats.OrderByDescending(x => x.Deaths).ThenBy(x => x.Job).ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(
                                x => x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Job)
                                .ToList()
                            : stats.OrderByDescending(
                                x => x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Job)
                                .ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    var deathsPerSeen = stat.Seen == 0
                        ? 0
                        : (double)stat.Deaths / stat.Seen;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Job);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(
                        stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(
                        stat.Deaths.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        deathsPerSeen.ToString("0.00"));
                }

                ImGui.EndTable();
            }
        }
        private void DrawSettingsTab()
        {
            ImGui.Text("Duty Tracking Options");
            ImGui.TextDisabled("Check each duty type you would like recorded");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            var contentTypes = new (uint Id, string Name)[]
            {
                (2, "Dungeons"),
                (3, "Guildhests"),
                (4, "Trials"),
                (5, "Raids"),
                (6, "PvP"),
                (21, "Deep Dungeons"),
                (28, "Ultimate Raids"),
                (30, "V&C Dungeon Finder"),
                (37, "Chaotic Alliance Raid")
            };

            foreach (var contentType in contentTypes)
            {
                var isTracked =
                    config.TrackedContentTypeIds.Contains(contentType.Id);

                if (ImGui.Checkbox(contentType.Name, ref isTracked))
                {
                    if (isTracked)
                        config.TrackedContentTypeIds.Add(contentType.Id);
                    else
                        config.TrackedContentTypeIds.Remove(contentType.Id);

                    config.Save();
                }
            }
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("Player Tracking Options");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            bool includeSelf = config.IncludeSelfInStatistics;

            if (ImGui.Checkbox("Include yourself in statistics", ref includeSelf))
            {
                config.IncludeSelfInStatistics = includeSelf;
                config.Save();
            }
            bool trackAlliance = config.TrackAllianceMembers;

            if (ImGui.Checkbox("Track alliance members", ref trackAlliance))
            {
                config.TrackAllianceMembers = trackAlliance;
                config.Save();
            }
        }
        private bool MatchesGlobalPlayerSearch(PartyMemberSnapshot player)
        {
            var search = globalPlayerSearchText.Trim();

            return string.IsNullOrEmpty(search) ||
                player.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                player.World.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                $"{player.Name}@{player.World}".Contains(
                    search, StringComparison.OrdinalIgnoreCase);
        }
        private void DrawPeopleTab()
        {
            ImGui.Text("People Seen");
            ImGui.Spacing();
            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            var now = DateTime.Now;

            var people = config.Records
                .Where(record =>
                    selectedDutyTypes.Count == 0 ||
                    selectedDutyTypes.Contains(record.DutyType))
                .Where(record => selectedTimeRange switch
                {
                    1 => record.Timestamp >= now.AddDays(-7),
                    2 => record.Timestamp >= now.AddDays(-30),
                    3 => record.Timestamp >= now.AddDays(-90),
                    4 => record.Timestamp >= customStartDate.Date &&
                         record.Timestamp < customEndDate.Date.AddDays(1),
                    _ => true
                })
                .SelectMany(record => record.Party)
                .Where(player =>
                    config.IncludeSelfInStatistics ||
                    player.Name != localPlayerName)
                .Where(MatchesPlayerFilters)
                .Where(player => MatchesGlobalPlayerSearch(player))
                .GroupBy(player => new { player.Name, player.World })
                .Select(group => new SeenPlayer
                {
                    Name = group.Key.Name,
                    World = group.Key.World,
                    Race = group.Select(x => x.Race)
                        .FirstOrDefault(x => x != "Unknown") ?? "Unknown",
                    Gender = group.Select(x => x.Gender)
                        .FirstOrDefault(x => x != "Unknown") ?? "Unknown",
                    TimesSeen = group.Count()
                })
                .ToList();

            var uniquePeople = people.Count;

            var uniqueLalafell = people.Count(
                x => x.Race == "Lalafell");

            ImGui.Text($"Unique people seen: {uniquePeople}");

            ImGui.SameLine();

            ImGui.TextDisabled(
                $"({uniqueLalafell} Lalafell)");

            ImGui.Spacing();

            ImGui.TextDisabled(
                "Click column headers to sort.");

            ImGui.Spacing();

            if (ImGui.BeginTable(
                    "peopleTable",
                    5,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn(
                    "Name",
                    ImGuiTableColumnFlags.PreferSortAscending);
                ImGui.TableSetupColumn("World");
                ImGui.TableSetupColumn("Race");
                ImGui.TableSetupColumn("Gender");

                ImGui.TableSetupColumn(
                    "Times Seen",
                    ImGuiTableColumnFlags.PreferSortDescending);

                ImGui.TableHeadersRow();

                var sortSpecs = ImGui.TableGetSortSpecs();

                if (sortSpecs.SpecsCount > 0)
                {
                    var spec = sortSpecs.Specs;

                    people = spec.ColumnIndex switch
                    {
                        0 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.Name).ToList()
                            : people.OrderByDescending(x => x.Name).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.World).ThenBy(x => x.Name).ToList()
                            : people.OrderByDescending(x => x.World).ThenBy(x => x.Name).ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.Race).ThenBy(x => x.Name).ToList()
                            : people.OrderByDescending(x => x.Race).ThenBy(x => x.Name).ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.Gender).ThenBy(x => x.Name).ToList()
                            : people.OrderByDescending(x => x.Gender).ThenBy(x => x.Name).ToList(),

                        4 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.TimesSeen).ThenBy(x => x.Name).ToList()
                            : people.OrderByDescending(x => x.TimesSeen).ThenBy(x => x.Name).ToList(),

                        _ => people
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var player in people)
                {
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(player.Name);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(player.World);

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(player.Race);

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(player.Gender);

                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(
                        player.TimesSeen.ToString());
                }

                ImGui.EndTable();
            }
        }

        private static void DrawStatisticRow(
            string statistic,
            string value)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(statistic);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(value);
        }

        private void DrawDetailWindow()
        {
            if (!showDetailWindow || selectedRecord == null)
                return;

            var r = selectedRecord;

            ImGui.SetNextWindowSize(
                new Vector2(600, 450),
                ImGuiCond.FirstUseEver);

            var isOpen = showDetailWindow;

            if (ImGui.Begin(
                    $"Duty Details - {r.DutyName}###DutyDetails",
                    ref isOpen))
            {
                ImGui.TextColored(
                    new Vector4(1f, 0.85f, 0.4f, 1f),
                    r.DutyName);

                ImGui.Separator();

                ImGui.Text($"Type: {r.DutyType}");
                ImGui.Text($"Your job: {r.PlayerJob}");

                ImGui.Text(
                    $"Started: {r.Timestamp:yyyy-MM-dd HH:mm:ss}");

                ImGui.Text(
                    $"Time in duty: {FormatDuration(r.TimeInDuty)}");

                ImGui.Text(
                    $"Joined in progress: {(r.JoinedInProgress ? "Yes" : "No")}");

                ImGui.Text($"Wipes: {r.WipeCount}");
                ImGui.Text($"Lalafell count: {r.LalafellCount}");

                ImGui.Text(
                    $"Completed: {(r.Completed ? "Yes" : "No")}");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Party");

                ImGui.Spacing();

                if (r.Party.Count > 0)
                {
                    var localPlayerName =
                        objectTable.LocalPlayer?.Name.TextValue;

                    var sortedParty = r.Party
                        .OrderBy(x => GetJobRoleOrder(x.Job))
                        .ThenBy(x => x.Name)
                        .ToList();

                    if (ImGui.BeginTable(
                            "partyDetail",
                            7,
                            ImGuiTableFlags.Borders |
                            ImGuiTableFlags.RowBg |
                            ImGuiTableFlags.Resizable |
                            ImGuiTableFlags.SizingStretchProp |
                            ImGuiTableFlags.Sortable))
                    {
                        ImGui.TableSetupColumn("Name");
                        ImGui.TableSetupColumn("World");
                        ImGui.TableSetupColumn("Job");
                        ImGui.TableSetupColumn("Race");
                        ImGui.TableSetupColumn("Gender");
                        ImGui.TableSetupColumn("Status");
                        ImGui.TableSetupColumn("Deaths");

                        ImGui.TableHeadersRow();
                        var sortSpecs = ImGui.TableGetSortSpecs();

                        if (sortSpecs.SpecsCount > 0)
                        {
                            var spec = sortSpecs.Specs;
                            bool ascending = spec.SortDirection == ImGuiSortDirection.Ascending;

                            sortedParty = spec.ColumnIndex switch
                            {
                                0 => ascending
                                    ? sortedParty.OrderBy(x => x.Name).ToList()
                                    : sortedParty.OrderByDescending(x => x.Name).ToList(),

                                1 => ascending
                                    ? sortedParty.OrderBy(x => x.World).ToList()
                                    : sortedParty.OrderByDescending(x => x.World).ToList(),

                                2 => ascending
                                    ? sortedParty.OrderBy(x => x.Job).ToList()
                                    : sortedParty.OrderByDescending(x => x.Job).ToList(),

                                3 => ascending
                                    ? sortedParty.OrderBy(x => x.Race).ToList()
                                    : sortedParty.OrderByDescending(x => x.Race).ToList(),

                                4 => ascending
                                    ? sortedParty.OrderBy(x => x.Gender).ToList()
                                    : sortedParty.OrderByDescending(x => x.Gender).ToList(),

                                5 => ascending
                                    ? sortedParty.OrderBy(x => x.Status ?? "Unknown").ToList()
                                    : sortedParty.OrderByDescending(x => x.Status ?? "Unknown").ToList(),

                                6 => ascending
                                    ? sortedParty.OrderBy(x => x.DeathCount).ToList()
                                    : sortedParty.OrderByDescending(x => x.DeathCount).ToList(),

                                _ => sortedParty
                            };

                            sortSpecs.SpecsDirty = false;
                        }

                        foreach (var member in sortedParty)
                        {
                            ImGui.TableNextRow();

                            ImGui.TableSetColumnIndex(0);

                            if (member.Name == localPlayerName)
                            {
                                ImGui.TextUnformatted(
                                    $"{member.Name} (You)");
                            }
                            else
                            {
                                ImGui.TextUnformatted(member.Name);
                            }
                            ImGui.TableSetColumnIndex(1);
                            ImGui.TextUnformatted(member.World);
                            ImGui.TableSetColumnIndex(2);
                            ImGui.TextUnformatted(member.Job);

                            ImGui.TableSetColumnIndex(3);
                            ImGui.TextUnformatted(member.Race);

                            ImGui.TableSetColumnIndex(4);
                            ImGui.TextUnformatted(member.Gender);

                            ImGui.TableSetColumnIndex(5); ;
                            ImGui.TextUnformatted(member.Status);

                            ImGui.TableSetColumnIndex(6);

                            if (member.Name == localPlayerName && !config.IncludeSelfInStatistics)
                            {
                                ImGui.TextUnformatted("-");
                            }
                            else
                            {
                                ImGui.TextUnformatted(member.DeathCount.ToString());
                            }
                        }

                        ImGui.EndTable();
                    }
                }
                else
                {
                    ImGui.TextDisabled(
                        "No party details were captured for this run.");
                }
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.PushFont(UiBuilder.IconFont);

                if (ImGui.Button(FontAwesomeIcon.Edit.ToIconString()))
                {
                    noteRecord = r;
                    noteDraft = r.Note ?? string.Empty;
                    showNoteWindow = true;
                }

                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextWrapped($"Notes: {r.Note}");
            }

            ImGui.End();

            showDetailWindow = isOpen;

            if (!showDetailWindow)
            {
                selectedRecord = null;
            }
        }
        private void DrawNoteWindow()
        {
            if (!showNoteWindow || noteRecord == null)
                return;

            ImGui.SetNextWindowSize(
                new Vector2(450, 300),
                ImGuiCond.FirstUseEver);

            var isOpen = showNoteWindow;

            if (ImGui.Begin(
                $"Duty Notes - {noteRecord.DutyName}###DutyNotes",
                ref isOpen))
            {
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.InputTextMultiline(
                    "##NoteText",
                    ref noteDraft,
                    4096,
                    new Vector2(-1, 180));

                ImGui.Spacing();

                if (ImGui.Button("Save"))
                {
                    noteRecord.Note = noteDraft;
                    config.Save();
                    isOpen = false;
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel"))
                {
                    isOpen = false;
                }
            }

            ImGui.End();

            showNoteWindow = isOpen;

            if (!showNoteWindow)
            {
                noteRecord = null;
                noteDraft = string.Empty;
            }
        }
        private static int GetJobRoleOrder(string job)
        {
            return job switch
            {
                // Tanks
                "PLD" => 0,
                "WAR" => 0,
                "DRK" => 0,
                "GNB" => 0,

                // Healers
                "WHM" => 1,
                "SCH" => 1,
                "AST" => 1,
                "SGE" => 1,

                // Melee DPS
                "MNK" => 2,
                "DRG" => 2,
                "NIN" => 2,
                "SAM" => 2,
                "RPR" => 2,
                "VPR" => 2,

                // Physical Ranged DPS
                "BRD" => 3,
                "MCH" => 3,
                "DNC" => 3,

                // Magical Ranged DPS
                "BLM" => 4,
                "SMN" => 4,
                "RDM" => 4,
                "PCT" => 4,
                "BLU" => 4,

                // Unknown / future jobs go at the end.
                _ => 5
            };
        }

        private static string FormatDuration(TimeSpan span)
        {
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}h {span.Minutes}m"
                : $"{span.Minutes}m {span.Seconds}s";
        }
        private static string GetTimePeriod(DateTime timestamp)
        {
            return timestamp.Hour switch
            {
                < 4 => "Late Night",
                < 8 => "Early Morning",
                < 12 => "Morning",
                < 16 => "Afternoon",
                < 20 => "Evening",
                _ => "Night"
            };
        }
    }
}