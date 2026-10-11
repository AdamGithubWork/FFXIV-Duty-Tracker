using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace MentorRouletteTracker
{
    public class MainWindow : Window, IDisposable
    {
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

                if (ImGui.BeginTabItem("People"))
                {
                    DrawPeopleTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            DrawDetailWindow();
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

                var records = config.Records
                    .Select((record, index) => new
                    {
                        Record = record,
                        Index = index
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

            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            var people = config.Records
                .SelectMany(x => x.Party)
                .Where(x => x.Name != localPlayerName)
                .GroupBy(x => x.Name);

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
                    Name = group.Key,
                    Race = race,
                    Gender = gender,
                    TimesSeen = group.Count()
                });
            }
        }

        private void DrawStatisticsTab()
        {
            var records = config.Records
                .Where(x => x.Completed)
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

                DrawLalafellStatistics(records);

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

                DrawRaceStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawJobStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawDutySpecificDetails(records);

                ImGui.Spacing();
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

        private void DrawLalafellStatistics(List<DutyRecord> records)
        {
            ImGui.Text("Lalafell Statistics");

            ImGui.Spacing();

            var currentDrought = 0;

            for (var i = records.Count - 1; i >= 0; i--)
            {
                if (records[i].LalafellCount > 0)
                    break;

                currentDrought++;
            }

            var longestDrought = 0;
            var drought = 0;

            foreach (var record in records)
            {
                if (record.LalafellCount == 0)
                {
                    drought++;

                    longestDrought =
                        Math.Max(longestDrought, drought);
                }
                else
                {
                    drought = 0;
                }
            }

            var totalLalafell =
                records.Sum(x => x.LalafellCount);

            var dutiesWithLalafell =
                records.Count(x => x.LalafellCount > 0);

            var uniqueLalafell =
                config.SeenPlayers.Count(
                    x => x.Race == "Lalafell");

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
                    "Unique Lalafell Seen",
                    uniqueLalafell.ToString());

                DrawStatisticRow(
                    "Total Lalafell Encounters",
                    totalLalafell.ToString());

                DrawStatisticRow(
                    "Duties With Lalafell",
                    dutiesWithLalafell.ToString());

                DrawStatisticRow(
                    "Duties Since Last Lalafell",
                    currentDrought.ToString());

                DrawStatisticRow(
                    "Longest Lalafell Drought",
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
                                    !string.IsNullOrWhiteSpace(p.Name) &&
                                    p.Name != objectTable.LocalPlayer?.Name.TextValue)
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
                    x.Name != localPlayerName &&
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
                    x.Name != localPlayerName &&
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

        private void DrawPeopleTab()
        {
            ImGui.Text("People Seen");

            if (ImGui.Button("Rebuild people history"))
            {
                RebuildSeenPlayers();
                config.Save();
            }

            ImGui.Spacing();

            var uniquePeople = config.SeenPlayers.Count;

            var uniqueLalafell = config.SeenPlayers.Count(
                x => x.Race == "Lalafell");

            ImGui.Text($"Unique people seen: {uniquePeople}");

            ImGui.SameLine();

            ImGui.TextDisabled(
                $"({uniqueLalafell} Lalafell)");

            ImGui.Spacing();

            ImGui.TextDisabled(
                "Click column headers to sort.");

            ImGui.Spacing();

            if (config.SeenPlayers.Count == 0)
            {
                ImGui.TextDisabled(
                    "No players have been recorded yet.");

                return;
            }

            if (ImGui.BeginTable(
                    "peopleTable",
                    4,
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

                ImGui.TableSetupColumn("Race");
                ImGui.TableSetupColumn("Gender");

                ImGui.TableSetupColumn(
                    "Times Seen",
                    ImGuiTableColumnFlags.PreferSortDescending);

                ImGui.TableHeadersRow();

                var people = config.SeenPlayers.ToList();

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
                            ? people.OrderBy(x => x.Race)
                                .ThenBy(x => x.Name)
                                .ToList()
                            : people.OrderByDescending(x => x.Race)
                                .ThenBy(x => x.Name)
                                .ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.Gender)
                                .ThenBy(x => x.Name)
                                .ToList()
                            : people.OrderByDescending(x => x.Gender)
                                .ThenBy(x => x.Name)
                                .ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.TimesSeen)
                                .ThenBy(x => x.Name)
                                .ToList()
                            : people.OrderByDescending(x => x.TimesSeen)
                                .ThenBy(x => x.Name)
                                .ToList(),

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
                    ImGui.TextUnformatted(player.Race);

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(player.Gender);

                    ImGui.TableSetColumnIndex(3);
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
                            5,
                            ImGuiTableFlags.Borders |
                            ImGuiTableFlags.RowBg |
                            ImGuiTableFlags.Resizable |
                            ImGuiTableFlags.SizingStretchProp))
                    {
                        ImGui.TableSetupColumn("Name");
                        ImGui.TableSetupColumn("Job");
                        ImGui.TableSetupColumn("Race");
                        ImGui.TableSetupColumn("Gender");
                        ImGui.TableSetupColumn("Deaths");

                        ImGui.TableHeadersRow();

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
                            ImGui.TextUnformatted(member.Job);

                            ImGui.TableSetColumnIndex(2);
                            ImGui.TextUnformatted(member.Race);

                            ImGui.TableSetColumnIndex(3);
                            ImGui.TextUnformatted(member.Gender);

                            ImGui.TableSetColumnIndex(4);

                            if (member.Name == localPlayerName)
                            {
                                ImGui.TextUnformatted("-");
                            }
                            else
                            {
                                ImGui.TextUnformatted(
                                    member.DeathCount.ToString());
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
            }

            ImGui.End();

            showDetailWindow = isOpen;

            if (!showDetailWindow)
            {
                selectedRecord = null;
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
    }
}