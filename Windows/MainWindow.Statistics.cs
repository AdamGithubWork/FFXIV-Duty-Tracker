
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

            if (ImGui.BeginChild(
                "StatisticsContent",
                Vector2.Zero,
                false,
                ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.Text("Player stats");
                DrawStatisticsCustomization();
                ImGui.PushTextWrapPos();
                ImGui.TextDisabled(
                    "Players from completed duties that match your filters.");
                ImGui.TextDisabled("Droughts count duties where no players match your filters.");
                ImGui.TextDisabled("Death totals include known counts only. Averages skip unknown counts. '-' means no known counts.");
                ImGui.PopTextWrapPos();
                ImGui.Separator();
                ImGui.Spacing();

                if (records.Count == 0)
                {
                    ImGui.TextDisabled("No completed duties match your filters.");
                    ImGui.EndChild();
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
                Section(config.ShowGenderStatistics, () => DrawGenderStatistics(data));
                Section(config.ShowJobStatistics, () => DrawJobStatistics(data));
                Section(config.ShowStatusStatistics, () => DrawStatusStatistics(data));
                Section(config.ShowWorldStatistics, () => DrawWorldStatistics(data));
                Section(config.ShowDatacenterStatistics, () => DrawWorldStatistics(data, groupByDatacenter: true));

                if (!anyVisible)
                    ImGui.TextDisabled("No statistics selected. Choose sections in Customize statistics.");
            }

            ImGui.EndChild();
        }

        private void DrawStatisticsCustomization()
        {
            ImGui.SetNextItemWidth(Math.Min(240f, ImGui.GetContentRegionAvail().X));
            if (!ImGui.BeginCombo("##StatisticsVisibility", "Customize statistics"))
                return;

            var changed = false;
            void Option(string label, bool visible, Action<bool> update)
            {
                if (ImGui.Checkbox(label, ref visible))
                {
                    update(visible);
                    changed = true;
                }
            }

            Option("Overview", config.ShowStatisticsOverview, value => config.ShowStatisticsOverview = value);
            Option("Race", config.ShowRaceStatistics, value => config.ShowRaceStatistics = value);
            Option("Gender", config.ShowGenderStatistics, value => config.ShowGenderStatistics = value);
            Option("Job", config.ShowJobStatistics, value => config.ShowJobStatistics = value);
            Option("Status", config.ShowStatusStatistics, value => config.ShowStatusStatistics = value);
            Option("Home world", config.ShowWorldStatistics, value => config.ShowWorldStatistics = value);
            Option("Datacenter", config.ShowDatacenterStatistics, value => config.ShowDatacenterStatistics = value);

            if (changed)
                config.Save();
            ImGui.EndCombo();
        }

        private void DrawSelectedPlayerStatistics(PlayerViewData data)
        {
            ImGui.Text("Players in these duties");
            ImGui.Spacing();
            var overview = data.Overview;

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

                ImGui.EndTable();
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
            ImGui.Text("Players by race");
            ImGui.Spacing();
            var raceStats = GetPlayerStatistics(data, PlayerStatistic.Race);

            if (raceStats.Count == 0)
            {
                ImGui.TextDisabled("No races match your filters.");
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
                            ? raceStats.OrderBy(x => x.Name).ToList()
                            : raceStats.OrderByDescending(x => x.Name).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x => x.Seen)
                                .ThenBy(x => x.Name).ToList()
                            : raceStats.OrderByDescending(x => x.Seen)
                                .ThenBy(x => x.Name).ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x => !x.Deaths.HasValue).ThenBy(x => x.Deaths)
                                .ThenBy(x => x.Name).ToList()
                            : raceStats.OrderBy(x => !x.Deaths.HasValue).ThenByDescending(x => x.Deaths)
                                .ThenBy(x => x.Name).ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x => !x.AverageDeaths.HasValue).ThenBy(x => x.AverageDeaths)
                                .ThenBy(x => x.Name).ToList()
                            : raceStats.OrderBy(x => !x.AverageDeaths.HasValue).ThenByDescending(x => x.AverageDeaths)
                                .ThenBy(x => x.Name).ToList(),

                        _ => raceStats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in raceStats)
                {
                    var deathsPerSeen = stat.AverageDeaths;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Name);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Deaths?.ToString() ?? "-");

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(deathsPerSeen?.ToString("0.00") ?? "-");
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Players by race and gender");
            ImGui.Spacing();

            var raceGenderStats = GetPlayerStatistics(data, PlayerStatistic.RaceAndGender);

            if (raceGenderStats.Count == 0)
            {
                ImGui.TextDisabled("No race and gender data matches your filters.");
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
                            ? raceGenderStats.OrderBy(x => x.Name)
                                .ThenBy(x => x.Detail).ToList()
                            : raceGenderStats.OrderByDescending(x => x.Name)
                                .ThenBy(x => x.Detail).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Detail)
                                .ThenBy(x => x.Name).ToList()
                            : raceGenderStats.OrderByDescending(x => x.Detail)
                                .ThenBy(x => x.Name).ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Seen)
                                .ThenBy(x => x.Name)
                                .ThenBy(x => x.Detail).ToList()
                            : raceGenderStats.OrderByDescending(x => x.Seen)
                                .ThenBy(x => x.Name)
                                .ThenBy(x => x.Detail).ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => !x.Deaths.HasValue).ThenBy(x => x.Deaths)
                                .ThenBy(x => x.Name)
                                .ThenBy(x => x.Detail).ToList()
                            : raceGenderStats.OrderBy(x => !x.Deaths.HasValue).ThenByDescending(x => x.Deaths)
                                .ThenBy(x => x.Name)
                                .ThenBy(x => x.Detail).ToList(),

                        4 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => !x.AverageDeaths.HasValue).ThenBy(x => x.AverageDeaths)
                                .ThenBy(x => x.Name)
                                .ThenBy(x => x.Detail).ToList()
                            : raceGenderStats.OrderBy(x => !x.AverageDeaths.HasValue).ThenByDescending(x => x.AverageDeaths)
                                .ThenBy(x => x.Name)
                                .ThenBy(x => x.Detail).ToList(),

                        _ => raceGenderStats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in raceGenderStats)
                {
                    var deathsPerSeen = stat.AverageDeaths;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Name);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Detail);

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(stat.Deaths?.ToString() ?? "-");

                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(deathsPerSeen?.ToString("0.00") ?? "-");
                }

                ImGui.EndTable();
            }
        }

        private void DrawGenderStatistics(PlayerViewData data)
        {
            ImGui.Text("Players by gender");
            ImGui.Spacing();
            var stats = GetPlayerStatistics(data, PlayerStatistic.Gender);

            if (stats.Count == 0)
            {
                ImGui.TextDisabled("No gender data matches your filters.");
                return;
            }

            if (ImGui.BeginTable(
                "genderStats",
                4,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn("Gender");
                ImGui.TableSetupColumn("Seen");
                ImGui.TableSetupColumn("Deaths");
                ImGui.TableSetupColumn("Deaths / Seen");
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
                            ? stats.OrderBy(x => x.Name).ToList()
                            : stats.OrderByDescending(x => x.Name).ToList(),

                        1 => ascending
                            ? stats.OrderBy(x => x.Seen).ToList()
                            : stats.OrderByDescending(x => x.Seen).ToList(),

                        2 => ascending
                            ? stats.OrderBy(x => !x.Deaths.HasValue).ThenBy(x => x.Deaths).ToList()
                            : stats.OrderBy(x => !x.Deaths.HasValue).ThenByDescending(x => x.Deaths).ToList(),

                        3 => ascending
                            ? stats.OrderBy(x => !x.AverageDeaths.HasValue).ThenBy(x => x.AverageDeaths).ToList()
                            : stats.OrderBy(x => !x.AverageDeaths.HasValue).ThenByDescending(x => x.AverageDeaths).ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Name);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Deaths?.ToString() ?? "-");

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        stat.AverageDeaths?.ToString("0.00") ?? "-");
                }

                ImGui.EndTable();
            }
        }

        private void DrawJobStatistics(PlayerViewData data)
        {
            ImGui.Text("Players by job");
            ImGui.Spacing();
            var jobStats = GetPlayerStatistics(data, PlayerStatistic.Job);

            if (jobStats.Count == 0)
            {
                ImGui.TextDisabled("No jobs match your filters.");
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

                var stats = jobStats;

                var sortSpecs = ImGui.TableGetSortSpecs();

                if (sortSpecs.SpecsCount > 0)
                {
                    var spec = sortSpecs.Specs;

                    stats = spec.ColumnIndex switch
                    {
                        0 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.Name).ToList()
                            : stats.OrderByDescending(x => x.Name).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.Seen).ThenBy(x => x.Name).ToList()
                            : stats.OrderByDescending(x => x.Seen).ThenBy(x => x.Name).ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => !x.Deaths.HasValue).ThenBy(x => x.Deaths).ThenBy(x => x.Name).ToList()
                            : stats.OrderBy(x => !x.Deaths.HasValue).ThenByDescending(x => x.Deaths).ThenBy(x => x.Name).ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => !x.AverageDeaths.HasValue).ThenBy(x => x.AverageDeaths)
                                .ThenBy(x => x.Name).ToList()
                            : stats.OrderBy(x => !x.AverageDeaths.HasValue).ThenByDescending(x => x.AverageDeaths)
                                .ThenBy(x => x.Name).ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    var deathsPerSeen = stat.AverageDeaths;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Name);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Deaths?.ToString() ?? "-");

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(deathsPerSeen?.ToString("0.00") ?? "-");
                }

                ImGui.EndTable();
            }
        }

        private void DrawStatusStatistics(PlayerViewData data)
        {
            ImGui.Text("Players by status");
            ImGui.Spacing();
            var stats = GetPlayerStatistics(data, PlayerStatistic.Status);

            if (stats.Count == 0)
            {
                ImGui.TextDisabled("No player statuses match your filters.");
                return;
            }

            if (ImGui.BeginTable(
                "statsStatusDistribution",
                4,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn("Status");
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
                            ? stats.OrderBy(x => x.Name).ToList()
                            : stats.OrderByDescending(x => x.Name).ToList(),

                        1 or 2 => ascending
                            ? stats.OrderBy(x => x.Seen).ToList()
                            : stats.OrderByDescending(x => x.Seen).ToList(),

                        3 => ascending
                            ? stats.OrderBy(x => !x.AverageDeaths.HasValue).ThenBy(x => x.AverageDeaths).ToList()
                            : stats.OrderBy(x => !x.AverageDeaths.HasValue).ThenByDescending(x => x.AverageDeaths).ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Name);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(
                        $"{(double)stat.Seen / data.Encounters.Count * 100:F1}%");

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        stat.AverageDeaths?.ToString("F2") ?? "-");
                }

                ImGui.EndTable();
            }
        }

        private void DrawWorldStatistics(PlayerViewData data, bool groupByDatacenter = false)
        {
            ImGui.Text(groupByDatacenter
                ? "Players by datacenter"
                : "Players by home world");
            ImGui.Spacing();
            var statistic = groupByDatacenter ? PlayerStatistic.Datacenter : PlayerStatistic.World;
            var stats = GetPlayerStatistics(data, statistic);
            var encounterCount = stats.Sum(row => row.Seen);

            if (stats.Count == 0)
            {
                ImGui.TextDisabled(groupByDatacenter
                    ? "No datacenters match your filters."
                    : "No home worlds match your filters.");
                return;
            }

            if (ImGui.BeginTable(
                groupByDatacenter ? "statsDatacenterDistribution" : "statsWorldDistribution",
                4,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn(groupByDatacenter ? "Datacenter" : "World");
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
                            ? stats.OrderBy(x => x.Name).ToList()
                            : stats.OrderByDescending(x => x.Name).ToList(),

                        1 or 2 => ascending
                            ? stats.OrderBy(x => x.Seen).ToList()
                            : stats.OrderByDescending(x => x.Seen).ToList(),

                        3 => ascending
                            ? stats.OrderBy(x => !x.AverageDeaths.HasValue).ThenBy(x => x.AverageDeaths).ToList()
                            : stats.OrderBy(x => !x.AverageDeaths.HasValue).ThenByDescending(x => x.AverageDeaths).ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Name);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(
                        $"{(double)stat.Seen / encounterCount * 100:F1}%");

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        stat.AverageDeaths?.ToString("F2") ?? "-");
                }

                ImGui.EndTable();
            }
        }
    }
}
