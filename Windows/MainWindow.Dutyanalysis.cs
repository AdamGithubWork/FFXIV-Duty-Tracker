
using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private void DrawDutyAnalysisTab()
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
                ImGui.TextDisabled("Completed duties matching global filters.");
                ImGui.Separator();
                ImGui.Spacing();

                if (records.Count == 0)
                {
                    ImGui.TextDisabled("No completed duties match the selected filters.");
                }
                else
                {
                    DrawDutyOverview(records);
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

                    DrawDutiesHeatmap(records);
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    DrawDutySpecificDetails(records);
                }
            }

            ImGui.EndChild();
        }

        private void DrawDutyOverview(List<DutyRecord> records)
        {
            ImGui.Text("Duty Overview");
            ImGui.Spacing();

            if (ImGui.BeginTable(
                "dutyOverview",
                2,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Statistic");
                ImGui.TableSetupColumn("Value");

                DrawStatisticRow("Completed Duties", records.Count.ToString());
                DrawStatisticRow("Average Duty Length",
                    FormatDuration(TimeSpan.FromSeconds(
                        records.Average(x => x.TimeInDuty.TotalSeconds))));
                DrawStatisticRow("Average Wipes / Duty",
                    records.Average(x => x.WipeCount).ToString("F2"));
                DrawStatisticRow("Joined In Progress",
                    records.Count(x => x.JoinedInProgress).ToString());

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

        private void DrawDutiesHeatmap(List<DutyRecord> records)
        {
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
                            cellColor = new Vector4(
                                0.08f,
                                0.25f + intensity * 0.45f,
                                0.12f + intensity * 0.20f,
                                1.0f);
                        }
                        else
                        {
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

        private double? AverageKnownDeathsPerRun(IEnumerable<DutyRecord> records)
        {
            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;
            return records.Select(record =>
            {
                var deaths = record.Party
                    .Where(player => !string.IsNullOrWhiteSpace(player.Name) &&
                        (config.IncludeSelfInStatistics || !IsRecordingPlayer(record, player, localPlayerName)))
                    .Select(player => EffectiveDeaths(record, player))
                    .ToList();

                return deaths.Count > 0 && deaths.All(count => count.HasValue)
                    ? (double?)deaths.Sum(count => count.GetValueOrDefault())
                    : null;
            }).Average();
        }

        private void DrawDutySpecificDetails(List<DutyRecord> records)
        {
            ImGui.Text("Duty Specific Details");
            ImGui.TextDisabled("Deaths / Run uses runs with known deaths for every included player.");

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
                        AverageDeaths = AverageKnownDeathsPerRun(x),

                        Deaths = x.Sum(y =>
                            y.Party
                                .Where(p =>
                                    !string.IsNullOrWhiteSpace(p.Name) &&
                                    (config.IncludeSelfInStatistics ||
                                     !IsRecordingPlayer(y, p, objectTable.LocalPlayer?.Name.TextValue)))
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
                                x.AverageDeaths)
                                .ThenBy(x => x.Duty)
                                .ToList()
                            : stats.OrderByDescending(x =>
                                x.AverageDeaths)
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
                    var deathsPerRun = stat.AverageDeaths;

                    var wipesPerRun = stat.Runs == 0
                        ? 0
                        : (double)stat.TotalWipes / stat.Runs;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Duty);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Runs.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Deaths.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(deathsPerRun?.ToString("0.00") ?? "-");

                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(stat.TotalWipes.ToString());

                    ImGui.TableSetColumnIndex(5);
                    ImGui.TextUnformatted(wipesPerRun.ToString("0.00"));

                    ImGui.TableSetColumnIndex(6);
                    ImGui.TextUnformatted(FormatDuration(stat.AverageTime));

                    ImGui.TableSetColumnIndex(7);
                    ImGui.TextUnformatted(stat.InProgress.ToString());
                }

                ImGui.EndTable();
            }
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
