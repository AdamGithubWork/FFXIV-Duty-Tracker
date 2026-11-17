using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.PvP;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private bool filtersExpanded;
        private readonly Dictionary<string, DateFields> dateInputs = new();

        private sealed class DateFields
        {
            public int Year;
            public int Month;
            public int Day;
            public DateTime AppliedDate;

            public DateFields(DateTime date)
            {
                Year = date.Year;
                Month = date.Month;
                Day = date.Day;
                AppliedDate = date.Date;
            }
        }

        private void ClearFilters()
        {
            dateInputs.Clear();
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
            dutyHistoryFilter = null;
            dutyHistoryFullRunsOnly = false;
        }

        private void DrawFilterPanel(string tab, bool playerFilters = true, bool encounterFilter = false)
        {
            static int Active(HashSet<string> selection) => selection.Count > 0 ? 1 : 0;
            var activeCount = Active(selectedDutyTypes) +
                (selectedPvpModes.Count > 0 ? 1 : 0) +
                (string.IsNullOrWhiteSpace(globalDutySearchText) ? 0 : 1) +
                (selectedTimeRange == 0 ? 0 : 1);
            if (playerFilters)
            {
                activeCount += Active(selectedPlayerRaces) + Active(selectedPlayerGenders) +
                    Active(selectedPlayerStatuses) + Active(selectedPlayerJobs) + Active(selectedPlayerWorlds) +
                    (string.IsNullOrWhiteSpace(globalPlayerSearchText) ? 0 : 1) +
                    (encounterFilter && minimumEncounters > 1 ? 1 : 0);
            }

            var countText = activeCount == 0 ? "No filters" : $"{activeCount} active";
            ImGui.SetNextItemOpen(filtersExpanded, ImGuiCond.Always);
            filtersExpanded = ImGui.CollapsingHeader($"Filters — {tab} ({countText})###GlobalFilters");
            if (!filtersExpanded)
                return;

            var height = Math.Min(ImGui.GetFontSize() * 16f,
                Math.Max(ImGui.GetFrameHeight() * 4f, ImGui.GetContentRegionAvail().Y * 0.5f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10));
            try
            {
                try
                {
                    if (ImGui.BeginChild("FilterPanel", new Vector2(0, height), true))
                    {
                        if (ImGui.Button("Clear filters"))
                            ClearFilters();
                        ImGui.PushTextWrapPos();
                        try
                        {
                            ImGui.TextDisabled(playerFilters
                                ? "Filters apply across data tabs. Select multiple options in a dropdown to include any of them."
                                : "Only duty and date filters apply to Summary.");
                        }
                        finally
                        {
                            ImGui.PopTextWrapPos();
                        }
                        ImGui.Spacing();

                        var columns = playerFilters && ImGui.GetContentRegionAvail().X >= ImGui.GetFontSize() * 38f ? 2 : 1;
                        if (ImGui.BeginTable("FilterGroups", columns, ImGuiTableFlags.SizingStretchSame))
                        {
                            try
                            {
                                ImGui.TableNextColumn();
                                DrawDutyFilters();
                                if (playerFilters)
                                {
                                    ImGui.TableNextColumn();
                                    DrawPlayerFilters(encounterFilter);
                                }
                            }
                            finally
                            {
                                ImGui.EndTable();
                            }
                        }
                    }
                }
                finally
                {
                    ImGui.EndChild();
                }
            }
            finally
            {
                ImGui.PopStyleVar();
            }
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, WindowAppearance.FilterBackground);
            try
            {
                ImGui.PushStyleColor(ImGuiCol.Border, WindowAppearance.Accent);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 4));
                try
                {
                    try
                    {
                        if (ImGui.BeginChild("FilterBoundary", new Vector2(0, ImGui.GetTextLineHeight() + 10), true,
                            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                            ImGui.TextUnformatted($"End of filters — {tab} below");
                    }
                    finally
                    {
                        ImGui.EndChild();
                    }
                }
                finally
                {
                    ImGui.PopStyleVar();
                }
            }
            finally
            {
                ImGui.PopStyleColor(2);
            }
            ImGui.Spacing();
        }

        private void DrawDutyFilters()
        {
            DrawSectionHeading("Duties & dates");
            ImGui.Separator();
            ImGui.TextUnformatted("Duty name");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##GlobalDutySearch", "Search duties...", ref globalDutySearchText, 100);
            ImGui.Spacing();

            var dutyTypes = config.History.Records.Select(DutyClassification.DutyType)
                .Where(type => !string.IsNullOrWhiteSpace(type));
            DrawDutyTypeFilter(dutyTypes);

            ImGui.TextUnformatted("Date range");
            ImGui.SetNextItemWidth(-1);
            string[] timeRanges = ["All Time", "Last 7 Days", "Last 30 Days", "Last 90 Days", "Custom Range"];
            ImGui.Combo("##GlobalTimeRange", ref selectedTimeRange, timeRanges, timeRanges.Length);
            if (selectedTimeRange == 4)
            {
                DrawFilterDate("Start date", ref customStartDate);
                DrawFilterDate("End date", ref customEndDate);
                if (customStartDate > customEndDate)
                    ImGui.TextWrapped("The start date can't be after the end date.");
            }
        }

        private static readonly (PvpMode Mode, string Label)[] PvpModeOptions =
        [
            (PvpMode.Frontline, "Frontline"),
            (PvpMode.CrystallineConflict, "Crystalline Conflict"),
            (PvpMode.RivalWings, "Rival Wings")
        ];

        private static readonly string[] OrderedDutyTypes =
        [
            "Dungeons", "Trials", "Extreme Trials", "Raids", "Savage Raids", "Ultimate Raids",
            "Alliance Raids", "PvP", "Guildhests"
        ];

        private void TogglePvpMode(PvpMode mode)
        {
            if (selectedDutyTypes.Contains("PvP") && selectedPvpModes.Count == 0)
                selectedPvpModes.UnionWith(PvpModeOptions.Select(option => option.Mode));
            if (!selectedPvpModes.Remove(mode))
                selectedPvpModes.Add(mode);
            if (selectedPvpModes.Count == 0)
                selectedDutyTypes.Remove("PvP");
            else
                selectedDutyTypes.Add("PvP");
        }

        private void DrawDutyTypeFilter(IEnumerable<string> dutyTypes)
        {
            ImGui.TextUnformatted("Duty types");
            ImGui.SetNextItemWidth(-1);
            var preview = selectedDutyTypes.Count switch
            {
                0 => "All duty types",
                1 when selectedDutyTypes.Contains("PvP") && selectedPvpModes.Count == 1 =>
                    PvpModeOptions.First(option => selectedPvpModes.Contains(option.Mode)).Label,
                1 when selectedDutyTypes.Contains("PvP") && selectedPvpModes.Count > 1 => $"PvP ({selectedPvpModes.Count} modes)",
                1 => selectedDutyTypes.Single(),
                _ => $"{selectedDutyTypes.Count} selected"
            };
            if (!ImGui.BeginCombo("##DutyTypesFilter", preview, ImGuiComboFlags.HeightLarge))
            {
                ImGui.Spacing();
                return;
            }
            try
            {
                var allTypes = selectedDutyTypes.Count == 0;
                if (ImGui.Checkbox("All duty types", ref allTypes))
                {
                    selectedDutyTypes.Clear();
                    selectedPvpModes.Clear();
                }
                ImGui.Separator();
                ImGui.TextDisabled("Duty types");
                var additional = dutyTypes.Concat(selectedDutyTypes).Except(OrderedDutyTypes, StringComparer.Ordinal)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase);
                foreach (var dutyType in OrderedDutyTypes.Concat(additional).ToList())
                {
                    if (dutyType != "PvP")
                    {
                        DrawFilterOption(dutyType, selectedDutyTypes);
                        continue;
                    }
                    var pvpDisabled = BeginPvpControl();
                    try
                    {
                        var pvpSelected = selectedDutyTypes.Contains("PvP");
                        if (ImGui.Checkbox("PvP", ref pvpSelected))
                        {
                            if (!selectedDutyTypes.Remove("PvP"))
                                selectedDutyTypes.Add("PvP");
                            selectedPvpModes.Clear();
                        }
                        ImGui.Indent();
                        try
                        {
                            ImGui.TextDisabled("PvP modes");
                            foreach (var (mode, label) in PvpModeOptions)
                            {
                                var selected = selectedDutyTypes.Contains("PvP") &&
                                    (selectedPvpModes.Count == 0 || selectedPvpModes.Contains(mode));
                                if (ImGui.Checkbox(label, ref selected))
                                    TogglePvpMode(mode);
                            }
                        }
                        finally
                        {
                            ImGui.Unindent();
                        }
                    }
                    finally
                    {
                        EndPvpControl(pvpDisabled);
                    }
                }
            }
            finally
            {
                ImGui.EndCombo();
            }
            ImGui.Spacing();
        }

        private void DrawPlayerFilters(bool encounterFilter)
        {
            DrawSectionHeading("Players");
            ImGui.Separator();
            ImGui.TextUnformatted("Player name or home world");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##GlobalPlayerSearch", "Search players...", ref globalPlayerSearchText, 100);
            ImGui.Spacing();

            DrawJobAndWorldFilters();
            ImGui.Spacing();
            DrawSimpleFilter("Race", "All races",
                new[] { "Hyur", "Elezen", "Lalafell", "Miqo'te", "Roegadyn", "Au Ra", "Hrothgar", "Viera" },
                selectedPlayerRaces);
            DrawSimpleFilter("Gender", "All genders", new[] { "Male", "Female" }, selectedPlayerGenders);
            DrawSimpleFilter("Status", "All statuses", GetStatusFilterOptions(), selectedPlayerStatuses);

            if (encounterFilter)
            {
                ImGui.TextUnformatted("Minimum encounters");
                ImGui.SetNextItemWidth(-1);
                ImGui.SliderInt("##MinimumEncounters", ref minimumEncounters, 1, 100, "%d", ImGuiSliderFlags.AlwaysClamp);
                ImGui.PushTextWrapPos();
                try
                {
                    ImGui.TextDisabled("Set how many matching completed duties a player needs to appear in. Missing death counts still count toward encounters.");
                }
                finally
                {
                    ImGui.PopTextWrapPos();
                }
            }
        }

        private static void DrawSimpleFilter(
            string label, string allLabel, IEnumerable<string> options, HashSet<string> selected)
        {
            ImGui.TextUnformatted(label);
            ImGui.SetNextItemWidth(-1);
            var preview = selected.Count switch
            {
                0 => allLabel,
                1 => selected.Single(),
                _ => $"{selected.Count} selected"
            };
            if (ImGui.BeginCombo($"##{label}Filter", preview, ImGuiComboFlags.HeightLarge))
            {
                try
                {
                    ImGui.PushID(label);
                    try
                    {
                        if (ImGui.Selectable(allLabel, selected.Count == 0, ImGuiSelectableFlags.DontClosePopups))
                            selected.Clear();
                        ImGui.Separator();
                        foreach (var option in options.Concat(selected).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToList())
                            DrawFilterOption(option, selected);
                    }
                    finally
                    {
                        ImGui.PopID();
                    }
                }
                finally
                {
                    ImGui.EndCombo();
                }
            }
            ImGui.Spacing();
        }

        private void DrawFilterDate(string label, ref DateTime date)
        {
            if (!dateInputs.TryGetValue(label, out var input) || input.AppliedDate != date.Date)
                dateInputs[label] = input = new DateFields(date);
            ImGui.PushID(label);
            try
            {
                ImGui.TextUnformatted(label);
                if (ImGui.BeginTable("DateFields", 3, ImGuiTableFlags.SizingStretchSame))
                {
                    try
                    {
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled("Year");
                        ImGui.SetNextItemWidth(-1);
                        ImGui.InputInt("##Year", ref input.Year, 0, 0);
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled("Month");
                        ImGui.SetNextItemWidth(-1);
                        ImGui.InputInt("##Month", ref input.Month, 0, 0);
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled("Day");
                        ImGui.SetNextItemWidth(-1);
                        ImGui.InputInt("##Day", ref input.Day, 0, 0);
                    }
                    finally
                    {
                        ImGui.EndTable();
                    }
                }
            }
            finally
            {
                ImGui.PopID();
            }
            if (IsValidDate(input.Year, input.Month, input.Day))
            {
                date = new DateTime(input.Year, input.Month, input.Day);
                input.AppliedDate = date;
            }
            else
                ImGui.TextDisabled("Enter a valid date to apply this filter.");
        }
    }
}
