using MentorRouletteTracker.DutyTracking;
using System.Text.Json;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private PlayerViewData? playerViewData;
        private string? playerViewFilterKey;
        private long playerViewHistoryVersion = -1;
        private int playerViewRecordCount = -1;
        private DateTime nextPlayerViewRefresh = DateTime.MinValue;

        private sealed class PlayerViewData
        {
            public required List<DutyRecord> Records { get; init; }
            public required List<PlayerEncounter> Encounters { get; init; }
            public required PlayerOverview Overview { get; init; }
            public List<PeopleTableRow>? People { get; set; }
            public (int Column, int Direction)? PeopleSort { get; set; }
            public List<PlayerEncounter>? StatisticsEncounters { get; set; }
            public readonly Dictionary<PlayerStatistic, List<PlayerStatisticRow>> Statistics = new();
        }

        private sealed record PlayerOverview(
            int UniquePlayers, int TotalEncounters, int DutiesWithPlayers,
            int CurrentDrought, int LongestDrought);

        private enum PlayerStatistic
        {
            Race,
            RaceAndGender,
            Gender,
            Job,
            Status,
            World,
            Datacenter
        }

        private sealed record PlayerStatisticRow(
            string Name, string Detail, int Seen, int? Deaths, double? AverageDeaths)
        {
            public PvpStatistics Pvp { get; init; } = new();
        }

        private string GetPlayerViewFilterKey() => JsonSerializer.Serialize(new
            {
                DutyTypes = selectedDutyTypes.Order(StringComparer.Ordinal),
                PvpModes = selectedPvpModes.Order(),
                Races = selectedPlayerRaces.Order(StringComparer.Ordinal),
                Genders = selectedPlayerGenders.Order(StringComparer.Ordinal),
                Statuses = selectedPlayerStatuses.Order(StringComparer.Ordinal),
                Jobs = selectedPlayerJobs.Order(StringComparer.Ordinal),
                Worlds = selectedPlayerWorlds.Order(StringComparer.Ordinal),
                DutySearch = globalDutySearchText.Trim(),
                PlayerSearch = globalPlayerSearchText.Trim(),
                selectedTimeRange,
                StartDate = customStartDate.Date,
                EndDate = customEndDate.Date,
                minimumEncounters,
                config.IncludeSelfInStatistics,
                EncounterTargetRace,
                EncounterTargetGender,
                LocalPlayer = objectTable.LocalPlayer?.Name.TextValue
            });

        private PlayerViewData GetPlayerViewData()
        {
            var filterKey = GetPlayerViewFilterKey();
            var now = DateTime.UtcNow;
            if (playerViewData != null &&
                playerViewHistoryVersion == config.History.ChangeVersion &&
                playerViewRecordCount == config.History.Records.Count &&
                playerViewFilterKey == filterKey &&
                (selectedTimeRange is not (>= 1 and <= 3) || now < nextPlayerViewRefresh))
                return playerViewData;

            var records = FilterDutyRecords(applyPlayerFilters: false).ToList();
            var encounters = GetMatchingPlayerEncounters(records);
            var matchingCounts = encounters.GroupBy(encounter => encounter.Record)
                .ToDictionary(group => group.Key, group => group.Count());
            var orderedRecords = records.OrderBy(record => record.Timestamp).ToList();
            var targetDuties = encounters.Where(encounter => MatchesEncounterTarget(encounter.Player))
                .Select(encounter => encounter.Record).ToHashSet();
            var currentDrought = 0;
            var longestDrought = 0;
            foreach (var record in orderedRecords)
            {
                currentDrought = targetDuties.Contains(record) ? 0 : currentDrought + 1;
                longestDrought = Math.Max(longestDrought, currentDrought);
            }

            playerViewData = new PlayerViewData
            {
                Records = orderedRecords,
                Encounters = encounters,
                Overview = new PlayerOverview(
                    encounters.Select(encounter => (encounter.Player.Name, encounter.Player.World)).Distinct().Count(),
                    encounters.Count, matchingCounts.Count, currentDrought, longestDrought)
            };
            playerViewFilterKey = filterKey;
            playerViewHistoryVersion = config.History.ChangeVersion;
            playerViewRecordCount = config.History.Records.Count;
            // Rolling date filters must still age out duties without a save or filter change.
            nextPlayerViewRefresh = now.AddSeconds(1);
            return playerViewData;
        }

        private List<PlayerStatisticRow> GetPlayerStatistics(PlayerViewData data, PlayerStatistic statistic)
        {
            if (data.Statistics.TryGetValue(statistic, out var cached))
                return cached;

            static bool Known(string value) => !string.IsNullOrWhiteSpace(value) && value != "Unknown";

            data.StatisticsEncounters ??= data.Encounters.OrderBy(encounter => encounter.Record.Timestamp).ToList();
            var encounters = data.StatisticsEncounters
                .Where(encounter => statistic switch
                {
                    PlayerStatistic.Race => Known(encounter.Player.Race),
                    PlayerStatistic.RaceAndGender => Known(encounter.Player.Race) && Known(encounter.Player.Gender),
                    PlayerStatistic.Gender => Known(encounter.Player.Gender),
                    PlayerStatistic.Job => Known(encounter.Player.Job),
                    PlayerStatistic.World or PlayerStatistic.Datacenter => Known(encounter.Player.World),
                    _ => true
                });

            var rows = encounters.GroupBy(encounter =>
            {
                var player = encounter.Player;
                return statistic switch
                {
                    PlayerStatistic.Race => (player.Race, ""),
                    PlayerStatistic.RaceAndGender => (player.Race, player.Gender),
                    PlayerStatistic.Gender => (player.Gender, ""),
                    PlayerStatistic.Job => (player.Job, ""),
                    PlayerStatistic.Status => (GetPlayerStatus(player), ""),
                    PlayerStatistic.World => (player.World, ""),
                    PlayerStatistic.Datacenter => (worldDatacenters.GetValueOrDefault(player.World, "Unknown"), ""),
                    _ => throw new ArgumentOutOfRangeException(nameof(statistic))
                };
            }).Select(group => new PlayerStatisticRow(
                group.Key.Item1, group.Key.Item2, group.Count(),
                TotalKnownDeaths(group.Select(encounter => EffectiveDeaths(encounter.Record, encounter.Player))),
                group.Average(encounter => EffectiveDeaths(encounter.Record, encounter.Player)))
                { Pvp = BuildPvpStatistics(group) });

            var result = statistic is PlayerStatistic.Status or PlayerStatistic.World or PlayerStatistic.Datacenter
                ? rows.OrderByDescending(row => row.Seen).ToList()
                : rows.OrderBy(row => row.Name).ThenBy(row => row.Detail).ToList();
            data.Statistics.Add(statistic, result);
            return result;
        }
    }
}
