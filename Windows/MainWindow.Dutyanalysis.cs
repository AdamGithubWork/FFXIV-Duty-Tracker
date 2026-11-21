
using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private DutyAnalysisData? dutyAnalysisData;
        private string? dutyAnalysisFilterKey;
        private long dutyAnalysisHistoryVersion = -1;
        private int dutyAnalysisRecordCount = -1;
        private DateTime nextDutyAnalysisRefresh = DateTime.MinValue;

        private sealed record DutyCount(string Key, int Count);
        private sealed record DutyHeatmapCell(DayOfWeek Day, string TimePeriod, int Count, double AverageDuration);
        private sealed record DutyAnalysisData(
            int CompletedDuties, TimeSpan? AverageFullRunTime, double AverageWipes, int JoinedInProgress,
            List<DutyCount> TypeCounts, DutyCount? MostCommon, List<DutyHeatmapCell> Heatmap,
            List<DutyAnalysisRow> Rows);

        private DutyAnalysisData GetDutyAnalysisData()
        {
            var filterKey = GetPlayerViewFilterKey();
            var now = DateTime.UtcNow;
            if (dutyAnalysisData != null &&
                dutyAnalysisHistoryVersion == config.History.ChangeVersion &&
                dutyAnalysisRecordCount == config.History.Records.Count &&
                dutyAnalysisFilterKey == filterKey &&
                (selectedTimeRange is not (>= 1 and <= 3) || now < nextDutyAnalysisRefresh))
                return dutyAnalysisData;

            var records = FilterDutyRecords().OrderBy(record => record.Timestamp).ToList();
            var fullRuns = records.Where(record => !record.JoinedInProgress).ToList();
            var typeCounts = records.GroupBy(DutyClassification.DisplayType)
                .Select(group => new DutyCount(group.Key, group.Count()))
                .OrderByDescending(group => group.Count).ThenBy(group => group.Key).ToList();
            var mostCommon = records.GroupBy(record => record.DutyName)
                .Select(group => new DutyCount(group.Key, group.Count()))
                .OrderByDescending(group => group.Count).ThenBy(group => group.Key).FirstOrDefault();
            var heatmap = records.GroupBy(record => (record.Timestamp.DayOfWeek, Period: GetTimePeriod(record.Timestamp)))
                .Select(group => new DutyHeatmapCell(group.Key.DayOfWeek, group.Key.Period,
                    group.Count(), group.Average(record => record.TimeInDuty.TotalMinutes))).ToList();

            dutyAnalysisData = new DutyAnalysisData(
                records.Count,
                fullRuns.Count == 0 ? null : TimeSpan.FromSeconds(fullRuns.Average(record => record.TimeInDuty.TotalSeconds)),
                records.Count == 0 ? 0 : records.Average(record => record.WipeCount),
                records.Count(record => record.JoinedInProgress), typeCounts, mostCommon, heatmap,
                BuildDutyAnalysisRows(records));
            dutyAnalysisFilterKey = filterKey;
            dutyAnalysisHistoryVersion = config.History.ChangeVersion;
            dutyAnalysisRecordCount = config.History.Records.Count;
            nextDutyAnalysisRefresh = now.AddSeconds(1);
            return dutyAnalysisData;
        }

        private void DrawDutyAnalysisTab()
        {
            var data = GetDutyAnalysisData();

            try
            {
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

                    if (data.CompletedDuties == 0)
                    {
                        ImGui.TextDisabled("No completed duties with these filters.");
                    }
                    else
                    {
                        DrawDutyOverview(data);
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();

                        DrawDutyTypeStatistics(data.TypeCounts);
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();

                        DrawMostCommonDuty(data.MostCommon!);
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();

                        DrawDutiesHeatmap(data.Heatmap);
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();

                        DrawDutySpecificDetails(data.Rows);
                    }
                }
            }
            finally
            {
                ImGui.EndChild();
            }
        }

        private void DrawDutyOverview(DutyAnalysisData data)
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
                try
                {
                    ImGui.TableSetupColumn("Statistic");
                    ImGui.TableSetupColumn("Value");

                    DrawStatisticRow("Completed Duties", data.CompletedDuties.ToString());
                    DrawStatisticRow("Average Duty Length (full runs)",
                        data.AverageFullRunTime.HasValue ? FormatDuration(data.AverageFullRunTime.Value) : "N/A");
                    DrawStatisticRow("Average Wipes / Duty", data.AverageWipes.ToString("F2"));
                    DrawStatisticRow("Joined In Progress", data.JoinedInProgress.ToString());
                }
                finally
                {
                    EndStyledTable();
                }
            }
        }

        private void DrawDutyTypeStatistics(List<DutyCount> typeCounts)
        {
            DrawSectionHeading("Duties by Type");

            ImGui.Spacing();

            if (BeginStyledTable(
                "dutyTypeStats",
                2,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchProp))
            {
                try
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
                            group.Count.ToString());
                    }
                }
                finally
                {
                    EndStyledTable();
                }
            }
        }

        private void DrawMostCommonDuty(DutyCount mostCommon)
        {
            DrawSectionHeading("Most Common Duty");

            ImGui.Spacing();

            if (BeginStyledTable(
                "mostCommonDuty",
                2,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchProp))
            {
                try
                {
                    ImGui.TableSetupColumn("Duty");
                    ImGui.TableSetupColumn("Times Completed");

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(mostCommon.Key);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(
                        mostCommon.Count.ToString());
                }
                finally
                {
                    EndStyledTable();
                }
            }
        }

        private void DrawDutiesHeatmap(List<DutyHeatmapCell> dutiesByDayAndTime)
        {
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
                try
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

                                try
                                {
                                    ImGui.TextUnformatted($"{day} - {period}");
                                    ImGui.Separator();
                                    ImGui.TextUnformatted($"Duties: {count}");

                                    if (selectedHeatmapMetric == 1)
                                    {
                                        ImGui.TextUnformatted(
                                            $"Average Duration: {value:F1} minutes");
                                    }
                                }
                                finally
                                {
                                    ImGui.EndTooltip();
                                }
                            }
                        }
                    }
                }
                finally
                {
                    EndStyledTable();
                }
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
            TimeSpan AverageTime, int InProgress, int Sprouts, int Mentors, int Players, int TargetPlayers)
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
                    counts.Sum(count => count.Mentors), counts.Sum(count => count.Players), counts.Sum(count => count.TargetPlayers))
                {
                    Pvp = BuildPvpStatistics(runs.Where(record => record.DutyType == "PvP")
                        .Select(record => (Record: record, Player: RecorderPvpPlayer(record)))
                        .Where(entry => entry.Player != null)
                        .Select(entry => new PlayerEncounter { Record = entry.Record, Player = entry.Player! }))
                };
            }).ToList();
        }

        private void DrawDutySpecificDetails(List<DutyAnalysisRow> stats)
        {
            DrawSectionHeading("Stats by duty");
            ImGui.PushTextWrapPos();
            try
            {
                ImGui.TextDisabled("Choose columns in Settings. '-' means unknown.");
            }
            finally
            {
                ImGui.PopTextWrapPos();
            }
            ImGui.Spacing();
            if (stats.Count == 0)
            {
                ImGui.TextDisabled("No duties with these filters.");
                return;
            }

            var rowHeight = ImGui.GetTextLineHeight() + TableCellVerticalPadding * 2;
            var height = rowHeight * (Math.Min(stats.Count, 12) + 2) + ImGui.GetStyle().ScrollbarSize;
            if (BeginConfiguredTable("Duty Analysis", "dutySpecificDetails", verticalScroll: true, height: height))
            {
                try
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
                            11 => row.TargetPlayers,
                            >= 12 and <= 28 => PvpStatisticValue(row.Pvp, ConfiguredSortColumn(spec.ColumnIndex) - 12),
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
                        DrawTableCell(11, row.TargetPlayers.ToString());
                        DrawPvpStatisticCells(12, row.Pvp);
                    }
                }
                finally
                {
                    EndStyledTable();
                }
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
