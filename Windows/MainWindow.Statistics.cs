
using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private void DrawStatisticsTab()
        {
            var data = GetPlayerViewData();
            var records = data.Records;

            try
            {
                if (ImGui.BeginChild(
                    "StatisticsContent",
                    Vector2.Zero,
                    false,
                    ImGuiWindowFlags.HorizontalScrollbar))
                {
                    ImGui.Text("Player stats");
                    ImGui.PushTextWrapPos();
                    try
                    {
                        ImGui.TextDisabled("Completed duties matching the filters. '-' means unknown.");
                    }
                    finally
                    {
                        ImGui.PopTextWrapPos();
                    }
                    ImGui.Separator();
                    ImGui.Spacing();

                    if (records.Count == 0)
                    {
                        ImGui.TextDisabled("No completed duties with these filters.");
                        return;
                    }

                    var anyVisible = false;
                    void Section(bool visible, Action draw)
                    {
                        if (!visible)
                            return;
                        if (anyVisible)
                        {
                            ImGui.Spacing();
                            ImGui.Separator();
                            ImGui.Spacing();
                        }
                        draw();
                        anyVisible = true;
                    }

                    Section(config.ShowStatisticsOverview, () => DrawSelectedPlayerStatistics(data));
                    Section(config.ShowRaceStatistics, () => DrawRaceStatistics(data));
                    Section(config.ShowGenderStatistics, () => DrawPlayerStatisticTable(data, PlayerStatistic.Gender));
                    Section(config.ShowJobStatistics, () => DrawPlayerStatisticTable(data, PlayerStatistic.Job));
                    Section(config.ShowStatusStatistics, () => DrawPlayerStatisticTable(data, PlayerStatistic.Status));
                    Section(config.ShowWorldStatistics, () => DrawPlayerStatisticTable(data, PlayerStatistic.World));
                    Section(config.ShowDatacenterStatistics, () => DrawPlayerStatisticTable(data, PlayerStatistic.Datacenter));

                    if (!anyVisible)
                        ImGui.TextDisabled("No stats selected. Choose which sections to show in Settings.");
                }
            }
            finally
            {
                ImGui.EndChild();
            }
        }

        private void DrawSelectedPlayerStatistics(PlayerViewData data)
        {
            DrawSectionHeading("Players in these duties");
            ImGui.Spacing();
            var overview = data.Overview;

            if (BeginStyledTable(
                "lalaStats",
                2,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchProp))
            {
                try
                {
                    ImGui.TableSetupColumn("Statistic");
                    ImGui.TableSetupColumn("Value");

                    DrawStatisticRow(
                        "Unique Players Seen",
                        overview.UniquePlayers.ToString());

                    DrawStatisticRow(
                        "Total Encounters",
                        overview.TotalEncounters.ToString());

                    DrawStatisticRow(
                        "Duties With Matching Players",
                        overview.DutiesWithPlayers.ToString());

                    DrawStatisticRow(
                        "Duties Since Last Encounter",
                        overview.CurrentDrought.ToString());

                    DrawStatisticRow(
                        "Longest Encounter Drought",
                        $"{overview.LongestDrought} duties");
                }
                finally
                {
                    EndStyledTable();
                }
            }
        }

        private static int? EffectiveDeaths(
            DutyRecord record,
            PartyMemberSnapshot player)
        {
            return record.DutyType == "PvP"
                ? player.PvPScoreboardDeaths
                : player.DeathCount;
        }

        private static int? TotalKnownDeaths(IEnumerable<int?> counts)
        {
            var known = counts.Where(count => count.HasValue).ToList();
            return known.Count == 0 ? null : known.Sum(count => count.GetValueOrDefault());
        }

        private void DrawRaceStatistics(PlayerViewData data)
        {
            DrawPlayerStatisticTable(data, PlayerStatistic.Race);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawPlayerStatisticTable(data, PlayerStatistic.RaceAndGender);
        }

        private void DrawPlayerStatisticTable(PlayerViewData data, PlayerStatistic statistic)
        {
            var (title, label, tableId) = statistic switch
            {
                PlayerStatistic.Race => ("Players by race", "Race", "raceStats"),
                PlayerStatistic.RaceAndGender => ("Players by race and gender", "Race", "raceGenderStats"),
                PlayerStatistic.Gender => ("Players by gender", "Gender", "genderStats"),
                PlayerStatistic.Job => ("Players by job", "Job", "jobStats"),
                PlayerStatistic.Status => ("Players by status", "Status", "statsStatusDistribution"),
                PlayerStatistic.World => ("Players by home world", "World", "statsWorldDistribution"),
                PlayerStatistic.Datacenter => ("Players by datacenter", "Datacenter", "statsDatacenterDistribution"),
                _ => throw new ArgumentOutOfRangeException(nameof(statistic))
            };
            DrawSectionHeading(title);
            ImGui.Spacing();
            var rows = GetPlayerStatistics(data, statistic);
            if (rows.Count == 0)
            {
                ImGui.TextDisabled("No players match these filters in this section.");
                return;
            }

            var key = StatisticTableKey(statistic);
            var detail = statistic == PlayerStatistic.RaceAndGender;
            var total = rows.Sum(row => row.Seen);
            if (BeginConfiguredTable(key, tableId, showDetail: detail))
            {
                try
                {
                    SetupConfiguredColumns(key, label, detail);
                    var sortSpecs = ImGui.TableGetSortSpecs();
                    if (sortSpecs.SpecsCount > 0 && IsTableColumnVisible(key, ConfiguredSortColumn(sortSpecs.Specs.ColumnIndex)) &&
                        (ConfiguredSortColumn(sortSpecs.Specs.ColumnIndex) != 1 || detail))
                    {
                        var spec = sortSpecs.Specs;
                        rows = SortTableRows(rows, row => ConfiguredSortColumn(spec.ColumnIndex) switch
                        {
                            0 => row.Name,
                            1 => row.Detail,
                            2 or 5 => row.Seen,
                            3 => row.Deaths,
                            4 => row.AverageDeaths,
                            >= 6 and <= 19 => PvpStatisticValue(row.Pvp, ConfiguredSortColumn(spec.ColumnIndex) - 6, averages: true),
                            _ => row.Name
                        }, spec.SortDirection == ImGuiSortDirection.Ascending, row => row.Name + " " + row.Detail);
                        sortSpecs.SpecsDirty = false;
                    }

                    foreach (var row in rows)
                    {
                        ImGui.TableNextRow();
                        DrawTableCell(0, row.Name);
                        DrawTableCell(1, row.Detail);
                        DrawTableCell(2, row.Seen.ToString());
                        DrawTableCell(3, row.Deaths?.ToString() ?? "-");
                        DrawTableCell(4, row.AverageDeaths?.ToString("F2") ?? "-");
                        DrawTableCell(5, $"{(double)row.Seen / total * 100:F1}%");
                        DrawPvpStatisticCells(6, row.Pvp, averages: true);
                    }
                }
                finally
                {
                    EndStyledTable();
                }
            }
        }
    }
}
