using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private void DrawFilterCheckboxes(
            IEnumerable<string> options,
            HashSet<string> selected)
        {
            var items = options.ToList();

            if (items.Count == 0)
            {
                ImGui.TextDisabled("No options yet.");
                return;
            }

            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float checkboxSize = ImGui.GetFrameHeight();

            ImGui.PushID(selected.GetHashCode());

            for (int i = 0; i < items.Count; i++)
            {
                string option = items[i];
                bool isSelected = selected.Contains(option);

                if (ImGui.Checkbox(option, ref isSelected))
                {
                    if (isSelected)
                        selected.Add(option);
                    else
                        selected.Remove(option);
                }

                if (i < items.Count - 1)
                {
                    string nextOption = items[i + 1];

                    float nextWidth =
                        checkboxSize +
                        ImGui.GetStyle().ItemInnerSpacing.X +
                        ImGui.CalcTextSize(nextOption).X;

                    if (ImGui.GetContentRegionAvail().X >=
                        nextWidth + spacing)
                    {
                        ImGui.SameLine();
                    }
                }
            }

            ImGui.PopID();
        }


        private bool MatchesPlayerFilters(PartyMemberSnapshot player)
        {
            return
                (selectedPlayerRaces.Count == 0 ||
                 selectedPlayerRaces.Contains(player.Race)) &&

                (selectedPlayerGenders.Count == 0 ||
                 selectedPlayerGenders.Contains(player.Gender)) &&

                (selectedPlayerStatuses.Count == 0 ||
                 selectedPlayerStatuses.Contains(player.Status)) &&

                MatchesGlobalPlayerSearch(player);
        }


        private bool MatchesDutyPlayerFilters(DutyRecord record)
        {
            bool hasPlayerFilters =
                selectedPlayerRaces.Count > 0 ||
                selectedPlayerGenders.Count > 0 ||
                selectedPlayerStatuses.Count > 0 ||
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
