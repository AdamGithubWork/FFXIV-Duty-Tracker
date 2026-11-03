using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private void DrawDutyAnalysisTab()
        {
            // Match the existing Statistics duty selection and global filters.
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
                    4 => x.Timestamp >= customStartDate.Date &&
                         x.Timestamp < customEndDate.Date.AddDays(1),
                    _ => true
                })
                .Where(x => MatchesDutyPlayerFilters(x))
                .Where(x => MatchesGlobalDutySearch(x))
                .OrderBy(x => x.Timestamp)
                .ToList();

            if (ImGui.BeginChild(
                "DutyAnalysisContent",
                Vector2.Zero,
                false,
                ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.Text("Duty Analysis");
                ImGui.TextDisabled("Compare completed duties using global filters.");
                ImGui.Separator();
                ImGui.Spacing();

                if (records.Count == 0)
                {
                    ImGui.TextDisabled("No completed duties match the selected filters.");
                }
                else
                {
                    DrawDutySpecificDetails(records);
                }
            }

            ImGui.EndChild();
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
                                .Sum(p => EffectiveDeaths(y, p) ?? 0)),

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
    }
}
