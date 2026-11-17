
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private readonly ConditionalWeakTable<DutyRecord, string> recordUiIds = new();

        private string GetRecordUiId(DutyRecord record) =>
            recordUiIds.GetValue(record, _ => Guid.NewGuid().ToString("N"));

        private void DrawRecordsTab()
        {
            if (!string.IsNullOrWhiteSpace(dutyHistoryFilter))
            {
                ImGui.TextUnformatted(
                    $"Duty history: {dutyHistoryFilter}" + (dutyHistoryFullRunsOnly ? " (full runs only)" : ""));

                ImGui.SameLine();

                if (ImGui.Button("Show all duties"))
                {
                    dutyHistoryFilter = null;
                    dutyHistoryFullRunsOnly = false;
                }

                ImGui.Separator();
                ImGui.Spacing();
            }

            ImGui.TextDisabled(tracker.Armed
                ? "Recording is on."
                : "Recording is off. You can turn it back on in Settings.");
            ImGui.Spacing();

            ImGui.TextDisabled(
                "Click a duty for details. Right-click to edit or delete. Click a header to sort.");

            ImGui.Spacing();

            if (ImGui.BeginTabBar("DutyRecordTabs"))
            {
                try
                {
                    var completedTabFlags = openDutyHistory
                        ? ImGuiTabItemFlags.SetSelected
                        : ImGuiTabItemFlags.None;

                    if (ImGui.BeginTabItem("Completed Duties", completedTabFlags))
                    {
                        try
                        {
                            DrawRecordsTable(completed: true);
                        }
                        finally
                        {
                            ImGui.EndTabItem();
                        }
                    }

                    if (ImGui.BeginTabItem("Incomplete Duties"))
                    {
                        try
                        {
                            DrawRecordsTable(completed: false);
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
        }

        private void DrawRecordsTable(bool completed)
        {
            var now = DateTime.Now;

            var records = config.History.Records
                .Where(x => x.Completed == completed)
                .Where(x => MatchesDutyHistory(x))
                .Where(x => MatchesDutyFilters(x, now))
                .Where(x => MatchesDutyPlayerFilters(x))
                .ToList();

            if (records.Count == 0)
            {
                ImGui.TextDisabled(completed
                    ? "No completed duties with these filters."
                    : "No incomplete duties with these filters.");
                return;
            }

            Dictionary<DutyRecord, DutyPlayerCounts> playerCounts = new(ReferenceEqualityComparer.Instance);
            DutyPlayerCounts Counts(DutyRecord record)
            {
                if (!playerCounts.TryGetValue(record, out var counts))
                {
                    counts = GetDutyPlayerCounts(record);
                    playerCounts.Add(record, counts);
                }
                return counts;
            }

            var recordRowHeight = ImGui.GetTextLineHeight() + TableCellVerticalPadding * 2;
            var tableHeight = Math.Min(Math.Max(1, ImGui.GetContentRegionAvail().Y),
                recordRowHeight * (records.Count + 2) + ImGui.GetStyle().ScrollbarSize);
            if (BeginConfiguredTable("Records", completed ? "completedRecords" : "incompleteRecords", verticalScroll: true, height: tableHeight))
            {
                try
                {
                    SetupConfiguredColumns("Records", scrollable: true);

                    var sortSpecs = ImGui.TableGetSortSpecs();

                    if (sortSpecs.SpecsCount > 0 && IsTableColumnVisible("Records", ConfiguredSortColumn(sortSpecs.Specs.ColumnIndex)))
                    {
                        var spec = sortSpecs.Specs;
                        records = SortTableRows(records, record => ConfiguredSortColumn(spec.ColumnIndex) switch
                        {
                            0 => record.DutyName,
                            1 => DutyClassification.DisplayType(record),
                            2 => record.PlayerJob,
                            3 => record.Timestamp,
                            4 => record.TimeInDuty,
                            5 => record.JoinedInProgress,
                            6 => record.WipeCount,
                            7 => Counts(record).Lalafell,
                            8 => record.Completed,
                            9 => Counts(record).Sprouts,
                            10 => Counts(record).Mentors,
                            11 => Counts(record).Players,
                            12 => Counts(record).Deaths,
                            13 => record.PvPPlacement,
                            14 => RecorderPvpPlayer(record)?.PvPDamageDealt,
                            15 => RecorderPvpPlayer(record)?.PvPKills,
                            16 => RecorderPvpPlayer(record)?.PvPAssists,
                            17 => RecorderPvpPlayer(record)?.PvPDamageTaken,
                            18 => RecorderPvpPlayer(record)?.PvPHealingDone,
                            _ => record.DutyName
                        }, spec.SortDirection == ImGuiSortDirection.Ascending, record => record.DutyName);
                        sortSpecs.SpecsDirty = false;
                    }
                    else
                    {
                        records = records.OrderByDescending(record => record.Timestamp).ToList();
                    }

                    foreach (var r in records)
                    {
                        var id = GetRecordUiId(r);

                        ImGui.TableNextRow();

                        SetConfiguredColumnIndex(0);

                        if (ImGui.Selectable(
                                $"{r.DutyName}###record{id}",
                                false,
                                ImGuiSelectableFlags.SpanAllColumns))
                        {
                            selectedRecord = r;
                            showDetailWindow = true;
                        }

                        if (ImGui.BeginPopupContextItem($"RecordContext{id}"))
                        {
                            try
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
                            }
                            finally
                            {
                                ImGui.EndPopup();
                            }
                        }

                        DrawTableCell(1, DutyClassification.DisplayType(r));
                        DrawTableCell(2, r.PlayerJob);
                        DrawTableCell(3, r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                        DrawTableCell(4, FormatDuration(r.TimeInDuty));
                        DrawTableCell(5, r.JoinedInProgress ? "Yes" : "No");
                        DrawTableCell(6, r.WipeCount.ToString());
                        if (SetConfiguredColumnIndex(7))
                            ImGui.TextUnformatted(Counts(r).Lalafell.ToString());
                        DrawTableCell(8, r.Completed ? "Yes" : "No");
                        if (SetConfiguredColumnIndex(9))
                            ImGui.TextUnformatted(Counts(r).Sprouts.ToString());
                        if (SetConfiguredColumnIndex(10))
                            ImGui.TextUnformatted(Counts(r).Mentors.ToString());
                        if (SetConfiguredColumnIndex(11))
                            ImGui.TextUnformatted(Counts(r).Players.ToString());
                        if (SetConfiguredColumnIndex(12))
                            ImGui.TextUnformatted(Counts(r).Deaths?.ToString() ?? "-");
                        DrawTableCell(13, r.DutyType == "PvP" ? PvpPlacementLabel(r.PvPPlacement) : "-");
                        DrawTableCell(14, RecorderPvpPlayer(r)?.PvPDamageDealt?.ToString("N0") ?? "-");
                        DrawTableCell(15, RecorderPvpPlayer(r)?.PvPKills?.ToString() ?? "-");
                        DrawTableCell(16, RecorderPvpPlayer(r)?.PvPAssists?.ToString() ?? "-");
                        DrawTableCell(17, RecorderPvpPlayer(r)?.PvPDamageTaken?.ToString("N0") ?? "-");
                        DrawTableCell(18, RecorderPvpPlayer(r)?.PvPHealingDone?.ToString("N0") ?? "-");
                    }
                }
                finally
                {
                    EndStyledTable();
                }
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

            try
            {
                if (recordToEdit == null)
                {
                    ImGui.CloseCurrentPopup();
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
                        editError = "Wipes can't be negative.";
                    }
                    else if (editWipeCount > 99)
                    {
                        editError = "Keep wipes between 0 and 99.";
                    }
                    else
                    {
                        recordToEdit.WipeCount = editWipeCount;
                        recordToEdit.JoinedInProgress = editJoinedInProgress;

                        config.History.Save();

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
            }
            finally
            {
                ImGui.EndPopup();
            }
        }

        private void DrawDeleteConfirmation()
        {
            if (!ImGui.BeginPopupModal(
                    "ConfirmDeleteRecord",
                    ImGuiWindowFlags.AlwaysAutoResize))
            {
                return;
            }

            try
            {
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
                        "This deletes the run and the party details saved with it.");

                    ImGui.Spacing();

                    if (ImGui.Button("Delete"))
                    {
                        var record = recordPendingDeletion;

                        config.History.Records.Remove(record);

                        config.History.Save();

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
            }
            finally
            {
                ImGui.EndPopup();
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

            try
            {
                if (ImGui.Begin(
                        $"Duty Details - {r.DutyName}###DutyDetails",
                        ref isOpen))
                {
                    ImGui.TextColored(
                        WindowAppearance.Accent,
                        r.DutyName);

                    ImGui.Separator();

                    ImGui.Text($"Type: {DutyClassification.DisplayType(r)}");
                    ImGui.Text($"Your job: {r.PlayerJob}");
                    if (r.DutyType == "PvP")
                        ImGui.Text($"Your team's placement: {PvpPlacementLabel(r.PvPPlacement)}");

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

                    ImGui.Text(r.DutyType == "PvP" ? $"PvP records ({r.Party.Count})" : $"Party ({r.Party.Count})");
                    if (r.PvPRosterComplete.HasValue)
                    {
                        ImGui.PushTextWrapPos();
                        try
                        {
                            ImGui.TextDisabled(r.PvPRosterComplete.Value
                                ? r.TrackPvpEnemies ? "PvP Tracker roster imported. Some character details may be unknown."
                                    : "PvP Tracker teammate roster imported. Opposing members weren't tracked for this duty."
                                : "Waiting for the full PvP Tracker scoreboard.");
                        }
                        finally
                        {
                            ImGui.PopTextWrapPos();
                        }
                    }
                    if (r.PvPRosterComplete == true && (!r.PvPCombatStatsComplete || !r.PvPResultsComplete || !r.PvPExtendedStatsComplete))
                        ImGui.TextDisabled("Checking PvP Tracker for missing scores, teams, and results.");

                    ImGui.Spacing();

                    if (r.DutyType == "PvP")
                    {
                        DrawPvpRecords(r);
                    }
                    else if (r.Party.Count > 0)
                    {
                        var localPlayerName =
                            objectTable.LocalPlayer?.Name.TextValue;

                        var sortedParty = r.Party
                            .OrderBy(x => GetJobRoleOrder(x.Job))
                            .ThenBy(x => x.Name)
                            .ToList();

                        if (BeginStyledTable(
                                "partyDetail",
                                7,
                                ImGuiTableFlags.Borders |
                                ImGuiTableFlags.RowBg |
                                ImGuiTableFlags.Resizable |
                                ImGuiTableFlags.SizingStretchProp |
                                ImGuiTableFlags.Sortable))
                        {
                            try
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

                                    sortedParty = ConfiguredSortColumn(spec.ColumnIndex) switch
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
                                            ? sortedParty.OrderBy(GetPlayerStatus).ToList()
                                            : sortedParty.OrderByDescending(GetPlayerStatus).ToList(),

                                        6 => ascending
                                            ? sortedParty.OrderBy(x => !EffectiveDeaths(r, x).HasValue).ThenBy(x => EffectiveDeaths(r, x)).ToList()
                                            : sortedParty.OrderBy(x => !EffectiveDeaths(r, x).HasValue).ThenByDescending(x => EffectiveDeaths(r, x)).ToList(),

                                        _ => sortedParty
                                    };

                                    sortSpecs.SpecsDirty = false;
                                }

                                foreach (var member in sortedParty)
                                {
                                    ImGui.TableNextRow();

                                    ImGui.TableSetColumnIndex(0);

                                    if (IsRecordingPlayer(r, member, localPlayerName))
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
                                    ImGui.TextUnformatted(GetPlayerStatus(member));

                                    ImGui.TableSetColumnIndex(6);

                                    if (IsRecordingPlayer(r, member, localPlayerName) &&
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
                            }
                            finally
                            {
                                EndStyledTable();
                            }
                        }
                    }
                    else
                    {
                        ImGui.TextDisabled(
                            "No party details were recorded for this run.");
                    }

                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    if (ImGuiComponents.IconButton("EditDutyNote", FontAwesomeIcon.Edit))
                    {
                        noteRecord = r;
                        noteDraft = r.Note ?? string.Empty;
                        showNoteWindow = true;
                    }

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Edit note");

                    ImGui.SameLine();
                    ImGui.TextWrapped($"Notes: {r.Note}");
                }
            }
            finally
            {
                ImGui.End();
            }

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

            try
            {
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
                        config.History.Save();
                        isOpen = false;
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Cancel"))
                    {
                        isOpen = false;
                    }
                }
            }
            finally
            {
                ImGui.End();
            }

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
                "PLD" => 0,
                "WAR" => 0,
                "DRK" => 0,
                "GNB" => 0,

                "WHM" => 1,
                "SCH" => 1,
                "AST" => 1,
                "SGE" => 1,

                "MNK" => 2,
                "DRG" => 2,
                "NIN" => 2,
                "SAM" => 2,
                "RPR" => 2,
                "VPR" => 2,

                "BRD" => 3,
                "MCH" => 3,
                "DNC" => 3,

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
