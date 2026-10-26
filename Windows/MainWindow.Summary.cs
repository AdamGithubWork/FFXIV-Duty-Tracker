using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private int? SummaryDeaths(PartyMemberSnapshot player)
        {
            var record = config.Records.FirstOrDefault(r =>
                r.Party.Any(p => ReferenceEquals(p, player)));
            return record == null ? null : EffectiveDeaths(record, player);
        }


        private void DrawSummaryTab()
        {
            // Lifetime records, completely independent of global filters.
            var records = config.Records.ToList();

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
                    .Where(player =>
                        config.IncludeSelfInStatistics ||
                        player.Name != localPlayerName)
                    .ToList();

                int uniquePlayers = players
                    .Select(player => (player.Name, player.World))
                    .Distinct()
                    .Count();

                int uniqueWorlds = players
                    .Select(player => player.World)
                    .Where(world =>
                        !string.IsNullOrWhiteSpace(world) &&
                        world != "Unknown")
                    .Distinct()
                    .Count();

                // Overview
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

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawSummaryStatusStatistics(players);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawSummaryWorldStatistics(players);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Reuse existing statistics with unfiltered records.
                DrawRaceStatistics(records);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawJobStatistics(records);
            }

            ImGui.EndChild();
        }


        private void DrawSummaryStatusStatistics(
            List<PartyMemberSnapshot> players)
        {
            ImGui.Text("Player Status Distribution");
            ImGui.Spacing();

            var stats = players
                .GroupBy(player =>
                    string.IsNullOrWhiteSpace(player.Status)
                        ? "Unknown"
                        : player.Status)
                .Select(group => new
                {
                    Status = group.Key,
                    Encounters = group.Count(),
                    Deaths = group.Sum(player => SummaryDeaths(player) ?? 0)
                })
                .OrderByDescending(stat => stat.Encounters)
                .ToList();

            if (stats.Count == 0)
            {
                ImGui.TextDisabled("No player status data available.");
                return;
            }

            if (ImGui.BeginTable(
                "summaryStatusStats",
                4,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Status");
                ImGui.TableSetupColumn("Encounters");
                ImGui.TableSetupColumn("Percentage");
                ImGui.TableSetupColumn("Deaths / Encounter");
                ImGui.TableHeadersRow();

                foreach (var stat in stats)
                {
                    double percentage = players.Count == 0
                        ? 0
                        : (double)stat.Encounters / players.Count * 100;

                    double deathsPerEncounter = stat.Encounters == 0
                        ? 0
                        : (double)stat.Deaths / stat.Encounters;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.Status);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Encounters.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted($"{percentage:F1}%");

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted($"{deathsPerEncounter:F2}");
                }

                ImGui.EndTable();
            }
        }


        private void DrawSummaryWorldStatistics(
            List<PartyMemberSnapshot> players)
        {
            ImGui.Text("Player World Distribution");
            ImGui.Spacing();

            var validPlayers = players
                .Where(player =>
                    !string.IsNullOrWhiteSpace(player.World) &&
                    player.World != "Unknown")
                .ToList();

            var stats = validPlayers
                .GroupBy(player => player.World)
                .Select(group => new
                {
                    World = group.Key,
                    Encounters = group.Count(),
                    Deaths = group.Sum(player => SummaryDeaths(player) ?? 0)
                })
                .OrderByDescending(stat => stat.Encounters)
                .ToList();

            if (stats.Count == 0)
            {
                ImGui.TextDisabled("No player world data available.");
                return;
            }

            if (ImGui.BeginTable(
                "summaryWorldStats",
                4,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn("World");
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

                        1 => ascending
                            ? stats.OrderBy(x => x.Encounters).ToList()
                            : stats.OrderByDescending(x => x.Encounters).ToList(),

                        2 => ascending
                            ? stats.OrderBy(x => x.Encounters).ToList()
                            : stats.OrderByDescending(x => x.Encounters).ToList(),

                        3 => ascending
                            ? stats.OrderBy(x =>
                                (double)x.Deaths / x.Encounters).ToList()
                            : stats.OrderByDescending(x =>
                                (double)x.Deaths / x.Encounters).ToList(),

                        _ => stats
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var stat in stats)
                {
                    double percentage =
                        (double)stat.Encounters / validPlayers.Count * 100;

                    double deathsPerEncounter =
                        (double)stat.Deaths / stat.Encounters;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(stat.World);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(stat.Encounters.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted($"{percentage:F1}%");

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted($"{deathsPerEncounter:F2}");
                }

                ImGui.EndTable();
            }
        }


        private static void DrawStatisticRow(
            string statistic,
            string value)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(statistic);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(value);
        }

    }
}
