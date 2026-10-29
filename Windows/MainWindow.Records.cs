
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private void DrawRecordsTab()
        {
            // Duty history filter, set by DutyTimerWindow.
            if (!string.IsNullOrWhiteSpace(dutyHistoryFilter))
            {
                ImGui.TextUnformatted(
                    $"Showing history for: {dutyHistoryFilter}");

                ImGui.SameLine();

                if (ImGui.Button("Show All Duties"))
                {
                    dutyHistoryFilter = null;
                }

                ImGui.Separator();
                ImGui.Spacing();
            }

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
                    // Show only this duty when opened
                    // through the floating timer window.
                    .Where(x =>
                        dutyHistoryFilter == null ||
                        string.Equals(
                            x.Record.DutyName,
                            dutyHistoryFilter,
                            StringComparison.OrdinalIgnoreCase))
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
            if (!ImGui.BeginPopupModal(
                    "EditDutyRecord",
                    ImGuiWindowFlags.AlwaysAutoResize))
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
                ImGui.TextColored(
                    new Vector4(1f, 0.3f, 0.3f, 1f),
                    editError);
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
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
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

            var people = config.Records
                .SelectMany(x => x.Party)
                .GroupBy(x => new { x.Name, x.World });

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
                            bool ascending =
                                spec.SortDirection == ImGuiSortDirection.Ascending;

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
                                    ? sortedParty.OrderBy(x => EffectiveDeaths(r, x)).ToList()
                                    : sortedParty.OrderByDescending(x => EffectiveDeaths(r, x)).ToList(),

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

                            ImGui.TableSetColumnIndex(5);
                            ImGui.TextUnformatted(member.Status);

                            ImGui.TableSetColumnIndex(6);

                            if (member.Name == localPlayerName &&
                                !config.IncludeSelfInStatistics)
                            {
                                ImGui.TextUnformatted("-");
                            }
                            else
                            {
                                ImGui.TextUnformatted(
                                    EffectiveDeaths(r, member)?.ToString() ?? "-");
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
