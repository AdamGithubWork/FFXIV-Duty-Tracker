using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private void ClearFilters()
        {
            globalPlayerSearchText = string.Empty;
            globalDutySearchText = string.Empty;
            selectedDutyTypes.Clear();
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
            if (!ImGui.CollapsingHeader($"Filters — {tab} ({countText})###GlobalFilters"))
                return;

            var height = Math.Min(ImGui.GetFontSize() * 16f,
                Math.Max(ImGui.GetFrameHeight() * 4f, ImGui.GetContentRegionAvail().Y * 0.5f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10));
            if (ImGui.BeginChild("FilterPanel", new Vector2(0, height), true))
            {
                if (ImGui.Button("Clear filters"))
                    ClearFilters();
                ImGui.PushTextWrapPos();
                ImGui.TextDisabled(playerFilters
                    ? "Filters apply across data tabs. Select multiple options in a dropdown to include any of them."
                    : "Only duty and date filters apply to Summary.");
                ImGui.PopTextWrapPos();
                ImGui.Spacing();

                var columns = playerFilters && ImGui.GetContentRegionAvail().X >= ImGui.GetFontSize() * 38f ? 2 : 1;
                if (ImGui.BeginTable("FilterGroups", columns, ImGuiTableFlags.SizingStretchSame))
                {
                    ImGui.TableNextColumn();
                    DrawDutyFilters();
                    if (playerFilters)
                    {
                        ImGui.TableNextColumn();
                        DrawPlayerFilters(encounterFilter);
                    }
                    ImGui.EndTable();
                }
            }
            ImGui.EndChild();
            ImGui.PopStyleVar();
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

            var dutyTypes = config.History.Records.Select(record => record.DutyType)
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Concat(new[] { "Extreme Trials", "Savage Raids" });
            DrawSimpleFilter("Duty types", "All duty types", dutyTypes, selectedDutyTypes);

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
                ImGui.TextDisabled("Set how many matching completed duties a player needs to appear in. Missing death counts still count toward encounters.");
                ImGui.PopTextWrapPos();
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
                ImGui.PushID(label);
                if (ImGui.Selectable(allLabel, selected.Count == 0, ImGuiSelectableFlags.DontClosePopups))
                    selected.Clear();
                ImGui.Separator();
                foreach (var option in options.Concat(selected).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToList())
                    DrawFilterOption(option, selected);
                ImGui.PopID();
                ImGui.EndCombo();
            }
            ImGui.Spacing();
        }

        private static void DrawFilterDate(string label, ref DateTime date)
        {
            var year = date.Year;
            var month = date.Month;
            var day = date.Day;
            ImGui.PushID(label);
            ImGui.TextUnformatted(label);
            if (ImGui.BeginTable("DateFields", 3, ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableNextColumn();
                ImGui.TextDisabled("Year");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputInt("##Year", ref year, 0, 0);
                ImGui.TableNextColumn();
                ImGui.TextDisabled("Month");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputInt("##Month", ref month, 0, 0);
                ImGui.TableNextColumn();
                ImGui.TextDisabled("Day");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputInt("##Day", ref day, 0, 0);
                ImGui.EndTable();
            }
            ImGui.PopID();
            if (IsValidDate(year, month, day))
                date = new DateTime(year, month, day);
        }
    }
}
