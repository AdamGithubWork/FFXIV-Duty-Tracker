
using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private void DrawDutyAnalysisTab()
        {
            var records = FilterDutyRecords()
                .OrderBy(x => x.Timestamp)
                .ToList();

            if (ImGui.BeginChild(
                "DutyAnalysisContent",
                Vector2.Zero,
                false,
                ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.Text("Duty Analysis");
                ImGui.TextDisabled("Completed duties matching the filters above.");
                ImGui.Separator();
                ImGui.Spacing();

                if (records.Count == 0)
                {
                    ImGui.TextDisabled("No completed duties with these filters.");
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
            DrawSectionHeading("Overview");
            ImGui.Spacing();

            if (BeginStyledTable(
                "dutyOverview",
                2,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Statistic");
                ImGui.TableSetupColumn("Value");

                DrawStatisticRow("Completed Duties", records.Count.ToString());
                var fullRuns = records.Where(record => !record.JoinedInProgress).ToList();
                DrawStatisticRow("Average Duty Length (full runs)",
                    fullRuns.Count == 0
                        ? "N/A"
                        : FormatDuration(TimeSpan.FromSeconds(
                            fullRuns.Average(record => record.TimeInDuty.TotalSeconds))));
                DrawStatisticRow("Average Wipes / Duty",
                    records.Average(x => x.WipeCount).ToString("F2"));
                DrawStatisticRow("Joined In Progress",
                    records.Count(x => x.JoinedInProgress).ToString());

                EndStyledTable();
            }
        }

        private void DrawDutyTypeStatistics(List<DutyRecord> records)
        {
            DrawSectionHeading("Duties by Type");

            ImGui.Spacing();

            var typeCounts = records
                .GroupBy(DutyClassification.DisplayType)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key)
                .ToList();

            if (BeginStyledTable(
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

                EndStyledTable();
            }
        }

        private void DrawMostCommonDuty(List<DutyRecord> records)
        {
            DrawSectionHeading("Most Common Duty");

            ImGui.Spacing();

            var mostCommon = records
                .GroupBy(x => x.DutyName)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key)
                .First();

            if (BeginStyledTable(
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

                EndStyledTable();
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

            DrawSectionHeading("Duties by Day and Time");
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

            if (BeginStyledTable(
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

                var maxValue = Math.Max(
                    1.0,
                    dutiesByDayAndTime.Max(x =>
                        selectedHeatmapMetric == 0
                            ? x.Count
                            : x.AverageDuration));

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

                EndStyledTable();
            }
        }

        private double? AverageKnownDeathsPerRun(IEnumerable<DutyRecord> records)
        {
            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;
            return records.Select(record =>
            {
                var deaths = record.Party
                    .Where(player => !string.IsNullOrWhiteSpace(player.Name) && MatchesPlayerFilters(player) &&
                        (config.IncludeSelfInStatistics || !IsRecordingPlayer(record, player, localPlayerName)))
                    .Select(player => EffectiveDeaths(record, player))
                    .ToList();

                return deaths.Count > 0 && deaths.All(count => count.HasValue)
                    ? (double?)deaths.Sum(count => count.GetValueOrDefault())
                    : null;
            }).Average();
        }

        private sealed record DutyAnalysisRow(
            string Duty, int Runs, int? Deaths, double? AverageDeaths, int TotalWipes,
            TimeSpan AverageTime, int InProgress, int Sprouts, int Mentors, int Players, int Lalafell)
        {
            public double WipesPerRun => Runs == 0 ? 0 : (double)TotalWipes / Runs;
            public PvpStatistics Pvp { get; init; } = new();
        }

        private List<DutyAnalysisRow> BuildDutyAnalysisRows(IEnumerable<DutyRecord> records)
        {
            return records.GroupBy(record => record.DutyName).OrderBy(group => group.Key).Select(group =>
            {
                var runs = group.ToList();
                var counts = runs.Select(GetDutyPlayerCounts).ToList();
                return new DutyAnalysisRow(group.Key, runs.Count,
                    TotalKnownDeaths(counts.Select(count => count.Deaths)), AverageKnownDeathsPerRun(runs),
                    runs.Sum(record => record.WipeCount), TimeSpan.FromTicks((long)runs.Average(record => record.TimeInDuty.Ticks)),
                    runs.Count(record => record.JoinedInProgress), counts.Sum(count => count.Sprouts),
                    counts.Sum(count => count.Mentors), counts.Sum(count => count.Players), counts.Sum(count => count.Lalafell))
                {
                    Pvp = BuildPvpStatistics(runs.Where(record => record.DutyType == "PvP")
                        .Select(record => (Record: record, Player: RecorderPvpPlayer(record)))
                        .Where(entry => entry.Player != null)
                        .Select(entry => new PlayerEncounter { Record = entry.Record, Player = entry.Player! }))
                };
            }).ToList();
        }

        private void DrawDutySpecificDetails(List<DutyRecord> records)
        {
            DrawSectionHeading("Stats by duty");
            ImGui.PushTextWrapPos();
            ImGui.TextDisabled("Choose columns in Settings. '-' means unknown.");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            var stats = BuildDutyAnalysisRows(records);
            if (stats.Count == 0)
            {
                ImGui.TextDisabled("No duties with these filters.");
                return;
            }

            var rowHeight = ImGui.GetTextLineHeight() + TableCellVerticalPadding * 2;
            var height = rowHeight * (Math.Min(stats.Count, 12) + 2) + ImGui.GetStyle().ScrollbarSize;
            if (BeginConfiguredTable("Duty Analysis", "dutySpecificDetails", verticalScroll: true, height: height))
            {
                SetupConfiguredColumns("Duty Analysis", scrollable: true);
                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsCount > 0 && IsTableColumnVisible("Duty Analysis", ConfiguredSortColumn(sortSpecs.Specs.ColumnIndex)))
                {
                    var spec = sortSpecs.Specs;
                    stats = SortTableRows(stats, row => ConfiguredSortColumn(spec.ColumnIndex) switch
                    {
                        0 => row.Duty,
                        1 => row.Runs,
                        2 => row.Deaths,
                        3 => row.AverageDeaths,
                        4 => row.TotalWipes,
                        5 => row.WipesPerRun,
                        6 => row.AverageTime,
                        7 => row.InProgress,
                        8 => row.Sprouts,
                        9 => row.Mentors,
                        10 => row.Players,
                        11 => row.Lalafell,
                        >= 12 and <= 25 => PvpStatisticValue(row.Pvp, ConfiguredSortColumn(spec.ColumnIndex) - 12),
                        _ => row.Duty
                    }, spec.SortDirection == ImGuiSortDirection.Ascending, row => row.Duty);
                    sortSpecs.SpecsDirty = false;
                }
                foreach (var row in stats)
                {
                    ImGui.TableNextRow();
                    DrawTableCell(0, row.Duty);
                    DrawTableCell(1, row.Runs.ToString());
                    DrawTableCell(2, row.Deaths?.ToString() ?? "-");
                    DrawTableCell(3, row.AverageDeaths?.ToString("F2") ?? "-");
                    DrawTableCell(4, row.TotalWipes.ToString());
                    DrawTableCell(5, row.WipesPerRun.ToString("F2"));
                    DrawTableCell(6, FormatDuration(row.AverageTime));
                    DrawTableCell(7, row.InProgress.ToString());
                    DrawTableCell(8, row.Sprouts.ToString());
                    DrawTableCell(9, row.Mentors.ToString());
                    DrawTableCell(10, row.Players.ToString());
                    DrawTableCell(11, row.Lalafell.ToString());
                    DrawPvpStatisticCells(12, row.Pvp);
                }
                EndStyledTable();
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
