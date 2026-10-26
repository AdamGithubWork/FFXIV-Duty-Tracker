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
        private void DrawPeopleTab()
        {
            ImGui.Text("People Seen");
            ImGui.Spacing();
            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;

            var now = DateTime.Now;

            var people = config.Records
                .Where(record =>
                    selectedDutyTypes.Count == 0 ||
                    selectedDutyTypes.Contains(record.DutyType))
                .Where(record => selectedTimeRange switch
                {
                    1 => record.Timestamp >= now.AddDays(-7),
                    2 => record.Timestamp >= now.AddDays(-30),
                    3 => record.Timestamp >= now.AddDays(-90),
                    4 => record.Timestamp >= customStartDate.Date &&
                         record.Timestamp < customEndDate.Date.AddDays(1),
                    _ => true
                })
                .SelectMany(record => record.Party)
                .Where(player =>
                    config.IncludeSelfInStatistics ||
                    player.Name != localPlayerName)
                .Where(MatchesPlayerFilters)
                .Where(player => MatchesGlobalPlayerSearch(player))
                .GroupBy(player => new { player.Name, player.World })
                .Select(group => new SeenPlayer
                {
                    Name = group.Key.Name,
                    World = group.Key.World,
                    Race = group.Select(x => x.Race)
                        .FirstOrDefault(x => x != "Unknown") ?? "Unknown",
                    Gender = group.Select(x => x.Gender)
                        .FirstOrDefault(x => x != "Unknown") ?? "Unknown",
                    TimesSeen = group.Count()
                })
                .ToList();

            var uniquePeople = people.Count;

            var uniqueLalafell = people.Count(
                x => x.Race == "Lalafell");

            ImGui.Text($"Unique people seen: {uniquePeople}");

            ImGui.SameLine();

            ImGui.TextDisabled(
                $"({uniqueLalafell} Lalafell)");

            ImGui.Spacing();

            ImGui.TextDisabled(
                "Click column headers to sort.");

            ImGui.Spacing();

            if (ImGui.BeginTable(
                    "peopleTable",
                    5,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn(
                    "Name",
                    ImGuiTableColumnFlags.PreferSortAscending);
                ImGui.TableSetupColumn("World");
                ImGui.TableSetupColumn("Race");
                ImGui.TableSetupColumn("Gender");

                ImGui.TableSetupColumn(
                    "Times Seen",
                    ImGuiTableColumnFlags.PreferSortDescending);

                ImGui.TableHeadersRow();

                var sortSpecs = ImGui.TableGetSortSpecs();

                if (sortSpecs.SpecsCount > 0)
                {
                    var spec = sortSpecs.Specs;

                    people = spec.ColumnIndex switch
                    {
                        0 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.Name).ToList()
                            : people.OrderByDescending(x => x.Name).ToList(),

                        1 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.World).ThenBy(x => x.Name).ToList()
                            : people.OrderByDescending(x => x.World).ThenBy(x => x.Name).ToList(),

                        2 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.Race).ThenBy(x => x.Name).ToList()
                            : people.OrderByDescending(x => x.Race).ThenBy(x => x.Name).ToList(),

                        3 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.Gender).ThenBy(x => x.Name).ToList()
                            : people.OrderByDescending(x => x.Gender).ThenBy(x => x.Name).ToList(),

                        4 => spec.SortDirection == ImGuiSortDirection.Ascending
                            ? people.OrderBy(x => x.TimesSeen).ThenBy(x => x.Name).ToList()
                            : people.OrderByDescending(x => x.TimesSeen).ThenBy(x => x.Name).ToList(),

                        _ => people
                    };

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var player in people)
                {
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(player.Name);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(player.World);

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(player.Race);

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(player.Gender);

                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(
                        player.TimesSeen.ToString());
                }

                ImGui.EndTable();
            }
        }

    }
}
