using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private void DrawStatisticsTab()
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
                    4 => x.Timestamp >= customStartDate.Date && x.Timestamp < customEndDate.Date.AddDays(1),
                    _ => true
                })
                .Where(x => MatchesDutyPlayerFilters(x))
                .OrderBy(x => x.Timestamp)
                .ToList();
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
            if (ImGui.BeginChild(
                    "StatisticsContent",
                    Vector2.Zero,
                    false,
                    ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.Text("Mentor Roulette Statistics");

                ImGui.Spacing();

                ImGui.TextDisabled(
                    "Statistics based on completed duties.");

                ImGui.Spacing();

                ImGui.Separator();

                ImGui.Spacing();

                if (records.Count == 0)
                {
                    ImGui.TextDisabled(
                        "No completed duties have been recorded yet.");

                    ImGui.EndChild();
                    return;
                }

                DrawOverallStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawSelectedPlayerStatistics(records);

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

                DrawDutySpecificDetails(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

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
                                // Duty Count: darker green -> brighter green
                                cellColor = new Vector4(
                                    0.08f,
                                    0.25f + intensity * 0.45f,
                                    0.12f + intensity * 0.20f,
                                    1.0f);
                            }
                            else
                            {
                                // Average Duration: green -> yellow -> red
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

            ImGui.EndChild();
        }


        private void DrawOverallStatistics(List<DutyRecord> records)
        {
            ImGui.Text("Overall");

            ImGui.Spacing();

            var averageSeconds =
                records.Average(x => x.TimeInDuty.TotalSeconds);

            var fastest = records.MinBy(x => x.TimeInDuty);
            var longest = records.MaxBy(x => x.TimeInDuty);
            var mostLalafell = records.MaxBy(x => x.LalafellCount);

            if (ImGui.BeginTable(
                    "overallStats",
                    2,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Statistic");
                ImGui.TableSetupColumn("Value");

                DrawStatisticRow(
                    "Total Duties",
                    records.Count.ToString());

                DrawStatisticRow(
                    "Average Duty Length",
                    FormatDuration(
                        TimeSpan.FromSeconds(averageSeconds)));

                DrawStatisticRow(
                    "Fastest Duty",
                    fastest == null
                        ? "N/A"
                        : $"{FormatDuration(fastest.TimeInDuty)} — {fastest.DutyName}");

                DrawStatisticRow(
                    "Longest Duty",
                    longest == null
                        ? "N/A"
                        : $"{FormatDuration(longest.TimeInDuty)} — {longest.DutyName}");

                DrawStatisticRow(
                    "Most Lalafell in One Duty",
                    mostLalafell == null
                        ? "N/A"
                        : $"{mostLalafell.LalafellCount} — {mostLalafell.DutyName}");

                ImGui.EndTable();
            }
        }


        private void DrawSelectedPlayerStatistics(List<DutyRecord> records)
        {
            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            ImGui.Text("Players in Matching Duties");
            ImGui.Spacing();

            var matchingPlayers = records
                .SelectMany(x => x.Party)
                .Where(x =>
                    config.IncludeSelfInStatistics ||
                    x.Name != localPlayerName)
                .ToList();

            var matchingCounts = records
                .Select(record => record.Party.Count(player =>
                    config.IncludeSelfInStatistics ||
                    player.Name != localPlayerName))
                .ToList();

            var currentDrought = 0;
            for (var i = matchingCounts.Count - 1; i >= 0; i--)
            {
                if (matchingCounts[i] > 0)
                    break;

                currentDrought++;
            }

            var longestDrought = 0;
            var drought = 0;

            foreach (var count in matchingCounts)
            {
                if (count == 0)
                {
                    drought++;
                    longestDrought = Math.Max(longestDrought, drought);
                }
                else
                {
                    drought = 0;
                }
            }

            var totalEncounters = matchingCounts.Sum();

            var dutiesWithMatchingPlayers =
                matchingCounts.Count(count => count > 0);

            var uniquePlayers = matchingPlayers
                .Select(x => (x.Name, x.World))
                .Distinct()
                .Count();

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
                    uniquePlayers.ToString());

                DrawStatisticRow(
                    "Total Encounters",
                    totalEncounters.ToString());

                DrawStatisticRow(
                    "Duties With Matching Players",
                    dutiesWithMatchingPlayers.ToString());

                DrawStatisticRow(
                    "Duties Since Last Encounter",
                    currentDrought.ToString());

                DrawStatisticRow(
                    "Longest Encounter Drought",
                    $"{longestDrought} duties");

                ImGui.EndTable();
            }
        }


        private static int? EffectiveDeaths(DutyRecord record, PartyMemberSnapshot player)
        {
            return record.DutyType == "PvP"
                ? player.PvPScoreboardDeaths
                : player.DeathCount;
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


        private void DrawRaceStatistics(List<DutyRecord> records)
        {
            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            var characters = records
                .SelectMany(x => x.Party)
                .Where(x =>
                    (config.IncludeSelfInStatistics || x.Name != localPlayerName) &&
                    !string.IsNullOrWhiteSpace(x.Race) &&
                    x.Race != "Unknown")
                .ToList();

            // ---------------------------------------------------------
            // Race Statistics
            // ---------------------------------------------------------

            ImGui.Text("Race Statistics");

            ImGui.Spacing();

            var raceStats = characters
                .GroupBy(x => x.Race)
                .Select(x => new
                {
                    Race = x.Key,
                    Seen = x.Count(),
                    Deaths = x.Sum(y => SummaryDeaths(y) ?? 0)
                })
                .OrderBy(x => x.Race)
                .ToList();

            if (raceStats.Count == 0)
            {
                ImGui.TextDisabled(
                    "No race data has been recorded yet.");
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
                            ? raceStats.OrderBy(x => x.Race).ToList()
                            : raceStats.OrderByDescending(x => x.Race).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x => x.Seen)
                                .ThenBy(x => x.Race)
                                .ToList()
                            : raceStats.OrderByDescending(x => x.Seen)
                                .ThenBy(x => x.Race)
                                .ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x => x.Deaths)
                                .ThenBy(x => x.Race)
                                .ToList()
                            : raceStats.OrderByDescending(x => x.Deaths)
                                .ThenBy(x => x.Race)
                                .ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x =>
                                x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Race)
                                .ToList()
                            : raceStats.OrderByDescending(x =>
                                x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Race)
                                .ToList(),

                        _ => raceStats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in raceStats)
                {
                    var deathsPerSeen = stat.Seen == 0
                        ? 0
                        : (double)stat.Deaths / stat.Seen;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Race);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Deaths.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        deathsPerSeen.ToString("0.00"));
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // ---------------------------------------------------------
            // Race / Gender Statistics
            // ---------------------------------------------------------

            ImGui.Text("Race / Gender Statistics");

            ImGui.Spacing();

            var raceGenderStats = characters
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Gender) &&
                    x.Gender != "Unknown")
                .GroupBy(x => new
                {
                    x.Race,
                    x.Gender
                })
                .Select(x => new
                {
                    Race = x.Key.Race,
                    Gender = x.Key.Gender,
                    Seen = x.Count(),
                    Deaths = x.Sum(y => SummaryDeaths(y) ?? 0)
                })
                .OrderBy(x => x.Race)
                .ThenBy(x => x.Gender)
                .ToList();

            if (raceGenderStats.Count == 0)
            {
                ImGui.TextDisabled(
                    "No race or gender data has been recorded yet.");

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
                            ? raceGenderStats.OrderBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList()
                            : raceGenderStats.OrderByDescending(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Gender)
                                .ThenBy(x => x.Race)
                                .ToList()
                            : raceGenderStats.OrderByDescending(x => x.Gender)
                                .ThenBy(x => x.Race)
                                .ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Seen)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList()
                            : raceGenderStats.OrderByDescending(x => x.Seen)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Deaths)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList()
                            : raceGenderStats.OrderByDescending(x => x.Deaths)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList(),

                        4 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x =>
                                x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList()
                            : raceGenderStats.OrderByDescending(x =>
                                x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender)
                                .ToList(),

                        _ => raceGenderStats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in raceGenderStats)
                {
                    var deathsPerSeen = stat.Seen == 0
                        ? 0
                        : (double)stat.Deaths / stat.Seen;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Race);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Gender);

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(stat.Deaths.ToString());

                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(
                        deathsPerSeen.ToString("0.00"));
                }

                ImGui.EndTable();
            }
        }


        private void DrawJobStatistics(List<DutyRecord> records)
        {
            ImGui.Text("Job Statistics");

            ImGui.Spacing();

            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            var jobStats = records
                .SelectMany(x => x.Party)
                .Where(x =>
                    (config.IncludeSelfInStatistics || x.Name != localPlayerName) &&
                    !string.IsNullOrWhiteSpace(x.Job) &&
                    x.Job != "Unknown")
                .GroupBy(x => x.Job)
                .OrderBy(x => x.Key)
                .ToList();

            if (jobStats.Count == 0)
            {
                ImGui.TextDisabled(
                    "No job data has been recorded yet.");

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

                var stats = jobStats
                    .Select(x => new
                    {
                        Job = x.Key,
                        Seen = x.Count(),
                        Deaths = x.Sum(y => SummaryDeaths(y) ?? 0)
                    })
                    .ToList();

                var sortSpecs = ImGui.TableGetSortSpecs();

                if (sortSpecs.SpecsCount > 0)
                {
                    var spec = sortSpecs.Specs;

                    stats = spec.ColumnIndex switch
                    {
                        0 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.Job).ToList()
                            : stats.OrderByDescending(x => x.Job).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.Seen).ThenBy(x => x.Job).ToList()
                            : stats.OrderByDescending(x => x.Seen).ThenBy(x => x.Job).ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(x => x.Deaths).ThenBy(x => x.Job).ToList()
                            : stats.OrderByDescending(x => x.Deaths).ThenBy(x => x.Job).ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? stats.OrderBy(
                                x => x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Job)
                                .ToList()
                            : stats.OrderByDescending(
                                x => x.Seen == 0
                                    ? 0
                                    : (double)x.Deaths / x.Seen)
                                .ThenBy(x => x.Job)
                                .ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    var deathsPerSeen = stat.Seen == 0
                        ? 0
                        : (double)stat.Deaths / stat.Seen;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Job);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(
                        stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(
                        stat.Deaths.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        deathsPerSeen.ToString("0.00"));
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
