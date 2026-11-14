
using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private void DrawStatisticsTab()
        {
            var now = DateTime.Now;

            var records = config.History.Records
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
                "StatisticsContent",
                Vector2.Zero,
                false,
                ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.Text("Player Analysis");
                ImGui.TextDisabled(
                    "Player statistics from completed duties matching global filters.");
                ImGui.TextDisabled("Death averages use encounters with known death counts only.");
                ImGui.Separator();
                ImGui.Spacing();

                if (records.Count == 0)
                {
                    ImGui.TextDisabled("No completed duties match the selected filters.");
                    ImGui.EndChild();
                    return;
                }

                DrawSelectedPlayerStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                var playerRecords = BuildStatisticsPlayerRecords(records);
                DrawRaceStatistics(records, playerRecords, true);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawGenderStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawJobStatistics(records, playerRecords, true);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawStatusStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawWorldStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawWorldStatistics(records, groupByDatacenter: true);
            }

            ImGui.EndChild();
        }

        private void DrawSelectedPlayerStatistics(List<DutyRecord> records)
        {
            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            ImGui.Text("Players in Matching Duties");
            ImGui.Spacing();

            var matchingPlayers = records
                .SelectMany(record => record.Party.Where(player =>
                    config.IncludeSelfInStatistics ||
                    !IsRecordingPlayer(record, player, localPlayerName)))
                .ToList();

            var matchingCounts = records
                .Select(record => record.Party.Count(player =>
                    config.IncludeSelfInStatistics ||
                    !IsRecordingPlayer(record, player, localPlayerName)))
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

        private static int? EffectiveDeaths(
            DutyRecord record,
            PartyMemberSnapshot player)
        {
            return record.DutyType == "PvP"
                ? player.PvPScoreboardDeaths
                : player.DeathCount;
        }

        private static Dictionary<PartyMemberSnapshot, DutyRecord>
            BuildStatisticsPlayerRecords(List<DutyRecord> records)
        {
            var lookup = new Dictionary<PartyMemberSnapshot, DutyRecord>(
                ReferenceEqualityComparer.Instance);

            foreach (var record in records)
            {
                foreach (var player in record.Party)
                {
                    lookup.TryAdd(player, record);
                }
            }

            return lookup;
        }

        private void DrawRaceStatistics(
            List<DutyRecord> records,
            Dictionary<PartyMemberSnapshot, DutyRecord>? playerRecords = null,
            bool applyPlayerFilters = false)
        {
            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            playerRecords ??= BuildStatisticsPlayerRecords(records);

            var characters = records
                .SelectMany(record => record.Party.Where(player =>
                    config.IncludeSelfInStatistics ||
                    !IsRecordingPlayer(record, player, localPlayerName)))
                .Where(x =>
                    (!applyPlayerFilters || MatchesPlayerFilters(x)) &&
                    !string.IsNullOrWhiteSpace(x.Race) &&
                    x.Race != "Unknown")
                .ToList();

            ImGui.Text("Race Statistics");
            ImGui.Spacing();

            var raceStats = characters
                .GroupBy(x => x.Race)
                .Select(x => new
                {
                    Race = x.Key,
                    Seen = x.Count(),
                    Deaths = x.Sum(y => SummaryDeaths(y, playerRecords) ?? 0),
                    AverageDeaths = x.Average(y => SummaryDeaths(y, playerRecords))
                })
                .OrderBy(x => x.Race)
                .ToList();

            if (raceStats.Count == 0)
            {
                ImGui.TextDisabled("No race data has been recorded yet.");
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
                                .ThenBy(x => x.Race).ToList()
                            : raceStats.OrderByDescending(x => x.Seen)
                                .ThenBy(x => x.Race).ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x => x.Deaths)
                                .ThenBy(x => x.Race).ToList()
                            : raceStats.OrderByDescending(x => x.Deaths)
                                .ThenBy(x => x.Race).ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceStats.OrderBy(x =>
                                x.AverageDeaths)
                                .ThenBy(x => x.Race).ToList()
                            : raceStats.OrderByDescending(x =>
                                x.AverageDeaths)
                                .ThenBy(x => x.Race).ToList(),

                        _ => raceStats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in raceStats)
                {
                    var deathsPerSeen = stat.AverageDeaths;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Race);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Deaths.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(deathsPerSeen?.ToString("0.00") ?? "-");
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

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
                    Deaths = x.Sum(y => SummaryDeaths(y, playerRecords) ?? 0),
                    AverageDeaths = x.Average(y => SummaryDeaths(y, playerRecords))
                })
                .OrderBy(x => x.Race)
                .ThenBy(x => x.Gender)
                .ToList();

            if (raceGenderStats.Count == 0)
            {
                ImGui.TextDisabled("No race or gender data has been recorded yet.");
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
                                .ThenBy(x => x.Gender).ToList()
                            : raceGenderStats.OrderByDescending(x => x.Race)
                                .ThenBy(x => x.Gender).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Gender)
                                .ThenBy(x => x.Race).ToList()
                            : raceGenderStats.OrderByDescending(x => x.Gender)
                                .ThenBy(x => x.Race).ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Seen)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender).ToList()
                            : raceGenderStats.OrderByDescending(x => x.Seen)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender).ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x => x.Deaths)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender).ToList()
                            : raceGenderStats.OrderByDescending(x => x.Deaths)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender).ToList(),

                        4 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? raceGenderStats.OrderBy(x =>
                                x.AverageDeaths)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender).ToList()
                            : raceGenderStats.OrderByDescending(x =>
                                x.AverageDeaths)
                                .ThenBy(x => x.Race)
                                .ThenBy(x => x.Gender).ToList(),

                        _ => raceGenderStats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in raceGenderStats)
                {
                    var deathsPerSeen = stat.AverageDeaths;

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
                    ImGui.TextUnformatted(deathsPerSeen?.ToString("0.00") ?? "-");
                }

                ImGui.EndTable();
            }
        }

        private void DrawGenderStatistics(List<DutyRecord> records)
        {
            ImGui.Text("Gender Statistics");
            ImGui.Spacing();

            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            var stats = records
                .SelectMany(record => record.Party.Select(player => new
                {
                    Record = record,
                    Player = player
                }))
                .Where(x =>
                    (config.IncludeSelfInStatistics || !IsRecordingPlayer(x.Record, x.Player, localPlayerName)) &&
                    MatchesPlayerFilters(x.Player) &&
                    !string.IsNullOrWhiteSpace(x.Player.Gender) &&
                    x.Player.Gender != "Unknown")
                .GroupBy(x => x.Player.Gender)
                .Select(group => new
                {
                    Gender = group.Key,
                    Seen = group.Count(),
                    Deaths = group.Sum(x => EffectiveDeaths(x.Record, x.Player) ?? 0),
                    AverageDeaths = group.Average(x => EffectiveDeaths(x.Record, x.Player))
                })
                .OrderBy(x => x.Gender)
                .ToList();

            if (stats.Count == 0)
            {
                ImGui.TextDisabled("No gender data matches the current filters.");
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
                            ? stats.OrderBy(x => x.Gender).ToList()
                            : stats.OrderByDescending(x => x.Gender).ToList(),

                        1 => ascending
                            ? stats.OrderBy(x => x.Seen).ToList()
                            : stats.OrderByDescending(x => x.Seen).ToList(),

                        2 => ascending
                            ? stats.OrderBy(x => x.Deaths).ToList()
                            : stats.OrderByDescending(x => x.Deaths).ToList(),

                        3 => ascending
                            ? stats.OrderBy(x => x.AverageDeaths).ToList()
                            : stats.OrderByDescending(x => x.AverageDeaths).ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Gender);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Deaths.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        stat.AverageDeaths?.ToString("0.00") ?? "-");
                }

                ImGui.EndTable();
            }
        }

        private void DrawJobStatistics(
            List<DutyRecord> records,
            Dictionary<PartyMemberSnapshot, DutyRecord>? playerRecords = null,
            bool applyPlayerFilters = false)
        {
            ImGui.Text("Job Statistics");
            ImGui.Spacing();

            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            playerRecords ??= BuildStatisticsPlayerRecords(records);

            var jobStats = records
                .SelectMany(record => record.Party.Where(player =>
                    config.IncludeSelfInStatistics ||
                    !IsRecordingPlayer(record, player, localPlayerName)))
                .Where(x =>
                    (!applyPlayerFilters || MatchesPlayerFilters(x)) &&
                    !string.IsNullOrWhiteSpace(x.Job) &&
                    x.Job != "Unknown")
                .GroupBy(x => x.Job)
                .OrderBy(x => x.Key)
                .ToList();

            if (jobStats.Count == 0)
            {
                ImGui.TextDisabled("No job data has been recorded yet.");
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
                        Deaths = x.Sum(y => SummaryDeaths(y, playerRecords) ?? 0),
                        AverageDeaths = x.Average(y => SummaryDeaths(y, playerRecords))
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
                                x => x.AverageDeaths)
                                .ThenBy(x => x.Job).ToList()
                            : stats.OrderByDescending(
                                x => x.AverageDeaths)
                                .ThenBy(x => x.Job).ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    var deathsPerSeen = stat.AverageDeaths;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Job);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Seen.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(stat.Deaths.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(deathsPerSeen?.ToString("0.00") ?? "-");
                }

                ImGui.EndTable();
            }
        }

        private void DrawStatusStatistics(List<DutyRecord> records)
        {
            ImGui.Text("Player Status Distribution");
            ImGui.Spacing();

            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            var encounters = records
                .SelectMany(record => record.Party.Select(player => new
                {
                    Record = record,
                    Player = player
                }))
                .Where(x =>
                    (config.IncludeSelfInStatistics || !IsRecordingPlayer(x.Record, x.Player, localPlayerName)) &&
                    MatchesPlayerFilters(x.Player))
                .ToList();

            var stats = encounters
                .GroupBy(x =>
                    string.IsNullOrWhiteSpace(x.Player.Status)
                        ? "Unknown"
                        : x.Player.Status)
                .Select(group => new
                {
                    Status = group.Key,
                    Encounters = group.Count(),
                    Deaths = group.Sum(x => EffectiveDeaths(x.Record, x.Player) ?? 0),
                    AverageDeaths = group.Average(x => EffectiveDeaths(x.Record, x.Player))
                })
                .OrderByDescending(x => x.Encounters)
                .ToList();

            if (stats.Count == 0)
            {
                ImGui.TextDisabled("No player status data matches the current filters.");
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
                            ? stats.OrderBy(x => x.Status).ToList()
                            : stats.OrderByDescending(x => x.Status).ToList(),

                        1 or 2 => ascending
                            ? stats.OrderBy(x => x.Encounters).ToList()
                            : stats.OrderByDescending(x => x.Encounters).ToList(),

                        3 => ascending
                            ? stats.OrderBy(x => x.AverageDeaths).ToList()
                            : stats.OrderByDescending(x => x.AverageDeaths).ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Status);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Encounters.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(
                        $"{(double)stat.Encounters / encounters.Count * 100:F1}%");

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        stat.AverageDeaths?.ToString("F2") ?? "-");
                }

                ImGui.EndTable();
            }
        }

        private void DrawWorldStatistics(List<DutyRecord> records, bool groupByDatacenter = false)
        {
            ImGui.Text(groupByDatacenter
                ? "Player World Distribution by Datacenter"
                : "Player World Distribution");
            ImGui.Spacing();

            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            var encounters = records
                .SelectMany(record => record.Party.Select(player => new
                {
                    Record = record,
                    Player = player
                }))
                .Where(x =>
                    (config.IncludeSelfInStatistics || !IsRecordingPlayer(x.Record, x.Player, localPlayerName)) &&
                    MatchesPlayerFilters(x.Player) &&
                    !string.IsNullOrWhiteSpace(x.Player.World) &&
                    x.Player.World != "Unknown")
                .ToList();

            var stats = encounters
                .GroupBy(x => groupByDatacenter
                    ? worldDatacenters.GetValueOrDefault(x.Player.World, "Unknown")
                    : x.Player.World)
                .Select(group => new
                {
                    World = group.Key,
                    Encounters = group.Count(),
                    Deaths = group.Sum(x => EffectiveDeaths(x.Record, x.Player) ?? 0),
                    AverageDeaths = group.Average(x => EffectiveDeaths(x.Record, x.Player))
                })
                .OrderByDescending(x => x.Encounters)
                .ToList();

            if (stats.Count == 0)
            {
                ImGui.TextDisabled("No player world data matches the current filters.");
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
                            ? stats.OrderBy(x => x.World).ToList()
                            : stats.OrderByDescending(x => x.World).ToList(),

                        1 or 2 => ascending
                            ? stats.OrderBy(x => x.Encounters).ToList()
                            : stats.OrderByDescending(x => x.Encounters).ToList(),

                        3 => ascending
                            ? stats.OrderBy(x => x.AverageDeaths).ToList()
                            : stats.OrderByDescending(x => x.AverageDeaths).ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.World);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Encounters.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(
                        $"{(double)stat.Encounters / encounters.Count * 100:F1}%");

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(
                        stat.AverageDeaths?.ToString("F2") ?? "-");
                }

                ImGui.EndTable();
            }
        }
    }
}
