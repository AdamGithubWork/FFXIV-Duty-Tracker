using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private string[] statusFilterOptions = Array.Empty<string>();
        private long statusFilterHistoryVersion = -1;
        private int statusFilterRecordCount = -1;

        private IEnumerable<string> GetStatusFilterOptions()
        {
            if (statusFilterHistoryVersion != config.History.ChangeVersion ||
                statusFilterRecordCount != config.History.Records.Count)
            {
                statusFilterOptions = new[]
                    { "Mentor", "Trade Mentor", "Sprout", "Party Member", "Returner" }
                    .Concat(config.History.Records.SelectMany(record => record.Party)
                        .Select(GetPlayerStatus))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                statusFilterHistoryVersion = config.History.ChangeVersion;
                statusFilterRecordCount = config.History.Records.Count;
            }

            return statusFilterOptions.Concat(selectedPlayerStatuses)
                .Where(status => status != "Unknown")
                .Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase);
        }

        private static string GetPlayerStatus(PartyMemberSnapshot player) => player.Status switch
        {
            "Battle Mentor" or "PvE Mentor" or "PvP Mentor" or "Mentor" => "Mentor",
            _ => string.IsNullOrWhiteSpace(player.Status) ? "Unknown" : player.Status
        };

        private IEnumerable<DutyRecord> FilterDutyRecords(bool? completed = true, bool applyPlayerFilters = true)
        {
            var now = DateTime.Now;
            return config.History.Records
                .Where(record => !completed.HasValue || record.Completed == completed.Value)
                .Where(record => MatchesDutyFilters(record, now))
                .Where(record => !applyPlayerFilters || MatchesDutyPlayerFilters(record));
        }

        private bool MatchesDutyHistory(DutyRecord record)
        {
            return dutyHistoryFilter == null || (dutyHistoryFullRunsOnly
                ? DutyTimerWindow.IsFullRunForDuty(record, dutyHistoryFilter)
                : string.Equals(record.DutyName, dutyHistoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        private bool MatchesDutyFilters(DutyRecord record, DateTime now)
        {
            return (selectedDutyTypes.Count == 0 || selectedDutyTypes.Contains(record.DutyType)) &&
                MatchesGlobalDutySearch(record) &&
                (selectedTimeRange switch
                {
                    1 => record.Timestamp >= now.AddDays(-7),
                    2 => record.Timestamp >= now.AddDays(-30),
                    3 => record.Timestamp >= now.AddDays(-90),
                    4 => record.Timestamp.Date >= customStartDate.Date &&
                         record.Timestamp.Date <= customEndDate.Date,
                    _ => true
                });
        }

        private bool MatchesPlayerFilters(PartyMemberSnapshot player)
        {
            return
                (selectedPlayerRaces.Count == 0 ||
                 selectedPlayerRaces.Contains(player.Race)) &&

                (selectedPlayerGenders.Count == 0 ||
                 selectedPlayerGenders.Contains(player.Gender)) &&

                (selectedPlayerStatuses.Count == 0 ||
                 selectedPlayerStatuses.Contains(GetPlayerStatus(player))) &&

                (selectedPlayerJobs.Count == 0 || selectedPlayerJobs.Contains(player.Job)) &&
                (selectedPlayerWorlds.Count == 0 || selectedPlayerWorlds.Contains(player.World)) &&

                MatchesGlobalPlayerSearch(player);
        }


        private bool MatchesDutyPlayerFilters(DutyRecord record)
        {
            bool hasPlayerFilters =
                selectedPlayerRaces.Count > 0 ||
                selectedPlayerGenders.Count > 0 ||
                selectedPlayerStatuses.Count > 0 ||
                selectedPlayerJobs.Count > 0 ||
                selectedPlayerWorlds.Count > 0 ||
                !string.IsNullOrWhiteSpace(globalPlayerSearchText);

            if (!hasPlayerFilters)
                return true;

            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            return record.Party.Any(player =>
                (config.IncludeSelfInStatistics ||
                 !IsRecordingPlayer(record, player, localPlayerName)) &&
                MatchesPlayerFilters(player));
        }


        private static bool IsRecordingPlayer(
            DutyRecord record, PartyMemberSnapshot player, string? localPlayerName)
        {
            if (string.IsNullOrEmpty(record.RecorderName) ||
                string.IsNullOrEmpty(record.RecorderWorld))
                return player.Name == localPlayerName;

            return player.Name == record.RecorderName &&
                   player.World == record.RecorderWorld;
        }

        private static bool IsValidDate(int year, int month, int day)
        {
            return year >= 1 && year <= 9999 &&
                   month >= 1 && month <= 12 &&
                   day >= 1 &&
                   day <= DateTime.DaysInMonth(year, month);
        }


        private bool MatchesGlobalPlayerSearch(PartyMemberSnapshot player)
        {
            var search = globalPlayerSearchText.Trim();

            return string.IsNullOrEmpty(search) ||
                player.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                player.World.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                $"{player.Name}@{player.World}".Contains(
                    search, StringComparison.OrdinalIgnoreCase);
        }
        private bool MatchesGlobalDutySearch(DutyRecord record)
        {
            var search = globalDutySearchText.Trim();

            return string.IsNullOrEmpty(search) ||
                record.DutyName.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase);
        }

    }
}
