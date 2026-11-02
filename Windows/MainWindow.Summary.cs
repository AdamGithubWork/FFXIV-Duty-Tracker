using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        // Retained for the existing Race and Job tables in MainWindow.Statistics.cs.
        private int? SummaryDeaths(
            PartyMemberSnapshot player,
            Dictionary<PartyMemberSnapshot, DutyRecord> playerRecords)
        {
            return playerRecords.TryGetValue(player, out var record)
                ? EffectiveDeaths(record, player)
                : null;
        }

        private void DrawSummaryTab()
        {
            // Lifetime overview only. Global filters never apply here.
            var records = config.Records;

            if (ImGui.BeginChild(
                "SummaryContent",
                Vector2.Zero,
                false,
                ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.Text("Lifetime Summary");
                ImGui.TextDisabled("All recorded duties. Global filters do not apply.");
                ImGui.Separator();
                ImGui.Spacing();

                if (records.Count == 0)
                {
                    ImGui.TextDisabled("No duty records available.");
                    ImGui.EndChild();
                    return;
                }

                var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;
                var players = records
                    .SelectMany(record => record.Party)
                    .Where(player => config.IncludeSelfInStatistics || player.Name != localPlayerName)
                    .ToList();

                int uniquePlayers = players
                    .Select(player => (player.Name, player.World))
                    .Distinct()
                    .Count();

                int uniqueWorlds = players
                    .Select(player => player.World)
                    .Where(world => !string.IsNullOrWhiteSpace(world) && world != "Unknown")
                    .Distinct()
                    .Count();

                ImGui.Text("Overview");
                ImGui.Spacing();

                if (ImGui.BeginTable(
                    "summaryOverview",
                    2,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("Statistic");
                    ImGui.TableSetupColumn("Value");

                    DrawStatisticRow("Recorded Duties", records.Count.ToString());
                    DrawStatisticRow("Player Encounters", players.Count.ToString());
                    DrawStatisticRow("Unique Players", uniquePlayers.ToString());
                    DrawStatisticRow("Unique Worlds", uniqueWorlds.ToString());

                    ImGui.EndTable();
                }
            }

            ImGui.EndChild();
        }

        private static void DrawStatisticRow(string statistic, string value)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(statistic);
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(value);
        }
    }
}
