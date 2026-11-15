
using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private bool showPlayerHistory;
        private string? historyPlayerName;
        private string? historyPlayerWorld;

        private sealed class PlayerEncounter
        {
            public required DutyRecord Record { get; init; }
            public required PartyMemberSnapshot Player { get; init; }
        }

        private sealed class PeopleTableRow
        {
            public required string Name { get; init; }
            public required string World { get; init; }
            public required string Race { get; init; }
            public required string Gender { get; init; }
            public required List<PlayerEncounter> Encounters { get; init; }
            public int TimesSeen => Encounters.Count;
            public required int? TotalDeaths { get; init; }
            public required int KnownDeathEncounters { get; init; }
            public double? AverageDeaths => KnownDeathEncounters == 0 ? null : (double?)TotalDeaths / KnownDeathEncounters;
        }

        private List<PlayerEncounter> GetMatchingPlayerEncounters(IEnumerable<DutyRecord> records)
        {
            var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;
            var encounters = records
                .SelectMany(record => record.Party.Select(player => new PlayerEncounter
                {
                    Record = record,
                    Player = player
                }))
                .Where(encounter =>
                    (config.IncludeSelfInStatistics ||
                     !IsRecordingPlayer(encounter.Record, encounter.Player, localPlayerName)) &&
                    MatchesPlayerFilters(encounter.Player))
                .ToList();
            if (minimumEncounters <= 1)
                return encounters;

            var qualifiedPlayers = encounters
                .GroupBy(encounter => (encounter.Player.Name, encounter.Player.World))
                .Where(group => group.Select(encounter => encounter.Record)
                    .Distinct(ReferenceEqualityComparer.Instance).Count() >= minimumEncounters)
                .Select(group => group.Key).ToHashSet();
            return encounters.Where(encounter =>
                qualifiedPlayers.Contains((encounter.Player.Name, encounter.Player.World))).ToList();
        }

        private List<PeopleTableRow> GetPeopleRows(PlayerViewData data)
        {
            if (data.People != null)
                return data.People;

            var people = data.Encounters
                .GroupBy(x => (x.Player.Name, x.Player.World))
                .Select(group =>
                {
                    var entries = group.ToList();
                    return new PeopleTableRow
                    {
                        Name = group.Key.Name,
                        World = group.Key.World,
                        Race = entries.Select(x => x.Player.Race)
                            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x != "Unknown") ?? "Unknown",
                        Gender = entries.Select(x => x.Player.Gender)
                            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x != "Unknown") ?? "Unknown",
                        Encounters = entries.OrderByDescending(entry => entry.Record.Timestamp).ToList(),
                        KnownDeathEncounters = entries.Count(entry => EffectiveDeaths(entry.Record, entry.Player).HasValue),
                        TotalDeaths = TotalKnownDeaths(entries.Select(x => EffectiveDeaths(x.Record, x.Player)))
                    };
                })
                .ToList();

            data.People = people;
            return people;
        }

        private void DrawPeopleTab()
        {
            ImGui.Text("Players you've met");
            ImGui.Spacing();
            var data = GetPlayerViewData();
            var people = GetPeopleRows(data);

            int uniqueLalafell = people.Count(x => x.Race == "Lalafell");
            ImGui.Text($"Unique players: {people.Count}");
            ImGui.SameLine();
            ImGui.TextDisabled($"({uniqueLalafell} Lalafell)");
            ImGui.Spacing();
            ImGui.PushTextWrapPos();
            ImGui.TextDisabled("Click a player to see your duties together. Click a column to sort.");
            ImGui.TextDisabled("Death totals include known counts only. Averages skip unknown counts. '-' means no known counts.");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            if (people.Count == 0)
                ImGui.TextDisabled("No players match your filters. Try clearing filters or lowering minimum encounters.");

            if (people.Count > 0 && ImGui.BeginTable(
                "peopleTable",
                7,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.PreferSortAscending | ImGuiTableColumnFlags.WidthStretch, 2f);
                ImGui.TableSetupColumn("World");
                ImGui.TableSetupColumn("Race");
                ImGui.TableSetupColumn("Gender");
                ImGui.TableSetupColumn("Times Seen", ImGuiTableColumnFlags.PreferSortDescending);
                ImGui.TableSetupColumn("Total Deaths");
                ImGui.TableSetupColumn("Deaths / Encounter");
                ImGui.TableHeadersRow();

                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsCount > 0 &&
                    (sortSpecs.SpecsDirty || data.PeopleSort !=
                        (sortSpecs.Specs.ColumnIndex, (int)sortSpecs.Specs.SortDirection)))
                {
                    var spec = sortSpecs.Specs;
                    bool ascending = spec.SortDirection == ImGuiSortDirection.Ascending;
                    people = spec.ColumnIndex switch
                    {
                        0 => ascending ? people.OrderBy(x => x.Name).ToList() : people.OrderByDescending(x => x.Name).ToList(),
                        1 => ascending ? people.OrderBy(x => x.World).ThenBy(x => x.Name).ToList() : people.OrderByDescending(x => x.World).ThenBy(x => x.Name).ToList(),
                        2 => ascending ? people.OrderBy(x => x.Race).ThenBy(x => x.Name).ToList() : people.OrderByDescending(x => x.Race).ThenBy(x => x.Name).ToList(),
                        3 => ascending ? people.OrderBy(x => x.Gender).ThenBy(x => x.Name).ToList() : people.OrderByDescending(x => x.Gender).ThenBy(x => x.Name).ToList(),
                        4 => ascending ? people.OrderBy(x => x.TimesSeen).ThenBy(x => x.Name).ToList() : people.OrderByDescending(x => x.TimesSeen).ThenBy(x => x.Name).ToList(),
                        5 => ascending ? people.OrderBy(x => !x.TotalDeaths.HasValue).ThenBy(x => x.TotalDeaths).ThenBy(x => x.Name).ToList() : people.OrderBy(x => !x.TotalDeaths.HasValue).ThenByDescending(x => x.TotalDeaths).ThenBy(x => x.Name).ToList(),
                        6 => ascending ? people.OrderBy(x => !x.AverageDeaths.HasValue).ThenBy(x => x.AverageDeaths).ThenBy(x => x.Name).ToList() : people.OrderBy(x => !x.AverageDeaths.HasValue).ThenByDescending(x => x.AverageDeaths).ThenBy(x => x.Name).ToList(),
                        _ => people
                    };
                    data.People = people;
                    data.PeopleSort = (spec.ColumnIndex, (int)spec.SortDirection);
                    sortSpecs.SpecsDirty = false;
                }

                for (int i = 0; i < people.Count; i++)
                {
                    var player = people[i];
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Selectable($"{player.Name}###person{player.Name}@{player.World}"))
                    {
                        historyPlayerName = player.Name;
                        historyPlayerWorld = player.World;
                        showPlayerHistory = true;
                    }
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(player.World);
                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(player.Race);
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(player.Gender);
                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(player.TimesSeen.ToString());
                    ImGui.TableSetColumnIndex(5);
                    ImGui.TextUnformatted(player.TotalDeaths?.ToString() ?? "-");
                    ImGui.TableSetColumnIndex(6);
                    ImGui.TextUnformatted(player.AverageDeaths?.ToString("F2") ?? "-");
                }
                ImGui.EndTable();
            }

            if (showPlayerHistory)
            {
                var selected = people.FirstOrDefault(x =>
                    x.Name == historyPlayerName && x.World == historyPlayerWorld);
                DrawPlayerHistory(selected);
            }
        }

        private void DrawPlayerHistory(PeopleTableRow? selected)
        {
            ImGui.SetNextWindowSize(new Vector2(760, 440), ImGuiCond.FirstUseEver);
            bool isOpen = showPlayerHistory;
            if (ImGui.Begin("Player Duty History###DutyTrackerPlayerHistory", ref isOpen))
            {
                ImGui.TextUnformatted($"{historyPlayerName}@{historyPlayerWorld}");
                ImGui.TextDisabled("Duties that match your filters.");
                ImGui.Separator();
                ImGui.Spacing();

                if (selected == null || selected.Encounters.Count == 0)
                {
                    ImGui.TextDisabled("No encounters match your filters.");
                }
                else
                {
                    ImGui.Text($"Encounters: {selected.TimesSeen}    Known death counts: {selected.KnownDeathEncounters}    Total deaths: {selected.TotalDeaths?.ToString() ?? "-"}    Average: {selected.AverageDeaths?.ToString("F2") ?? "-"}");
                    ImGui.Spacing();

                    if (ImGui.BeginTable(
                        "playerHistoryTable", 5,
                        ImGuiTableFlags.Borders |
                        ImGuiTableFlags.RowBg |
                        ImGuiTableFlags.ScrollY |
                        ImGuiTableFlags.Resizable |
                        ImGuiTableFlags.SizingStretchProp))
                    {
                        ImGui.TableSetupColumn("Duty");
                        ImGui.TableSetupColumn("Type");
                        ImGui.TableSetupColumn("Started");
                        ImGui.TableSetupColumn("Job");
                        ImGui.TableSetupColumn("Deaths");
                        ImGui.TableHeadersRow();

                        var history = selected.Encounters;

                        for (int i = 0; i < history.Count; i++)
                        {
                            var entry = history[i];
                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            if (ImGui.Selectable($"{entry.Record.DutyName}###history{GetRecordUiId(entry.Record)}"))
                            {
                                selectedRecord = entry.Record;
                                showDetailWindow = true;
                                isOpen = false;
                            }
                            ImGui.TableSetColumnIndex(1);
                            ImGui.TextUnformatted(entry.Record.DutyType);
                            ImGui.TableSetColumnIndex(2);
                            ImGui.TextUnformatted(entry.Record.Timestamp.ToString("yyyy-MM-dd HH:mm"));
                            ImGui.TableSetColumnIndex(3);
                            ImGui.TextUnformatted(entry.Player.Job);
                            ImGui.TableSetColumnIndex(4);
                            ImGui.TextUnformatted(EffectiveDeaths(entry.Record, entry.Player)?.ToString() ?? "-");
                        }
                        ImGui.EndTable();
                    }
                }
            }
            ImGui.End();
            showPlayerHistory = isOpen;
        }
    }
}
