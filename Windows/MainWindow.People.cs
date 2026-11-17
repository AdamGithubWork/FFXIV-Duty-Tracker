
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
            public DateTime FirstSeen => Encounters[^1].Record.Timestamp;
            public DateTime LastSeen => Encounters[0].Record.Timestamp;
            public required int DifferentDuties { get; init; }
            public required int DeathlessEncounters { get; init; }
            public required int SproutEncounters { get; init; }
            public required int MentorEncounters { get; init; }
            public long? PvpDamage { get; init; }
            public long? PvpKills { get; init; }
            public long? PvpAssists { get; init; }
            public int TeammateEncounters { get; init; }
            public int EnemyEncounters { get; init; }
            public PvpStatistics Pvp { get; init; } = new();
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
                    var pvp = BuildPvpStatistics(entries);
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
                        TotalDeaths = TotalKnownDeaths(entries.Select(x => EffectiveDeaths(x.Record, x.Player))),
                        DifferentDuties = entries.Select(entry => entry.Record.DutyName).Distinct().Count(),
                        DeathlessEncounters = entries.Count(entry => EffectiveDeaths(entry.Record, entry.Player) == 0),
                        SproutEncounters = entries.Count(entry => GetPlayerStatus(entry.Player) == "Sprout"),
                        MentorEncounters = entries.Count(entry => IsMentor(entry.Player)),
                        Pvp = pvp,
                        PvpDamage = pvp.Damage,
                        PvpKills = pvp.Kills,
                        PvpAssists = pvp.Assists,
                        TeammateEncounters = entries.Count(entry => entry.Record.DutyType == "PvP" && entry.Player.PvPIsEnemy == false),
                        EnemyEncounters = entries.Count(entry => entry.Record.DutyType == "PvP" && entry.Player.PvPIsEnemy == true)
                    };
                })
                .ToList();

            data.People = people;
            return people;
        }

        private void DrawPeopleTab()
        {
            DrawSectionHeading("Players you've met");
            ImGui.Spacing();
            var data = GetPlayerViewData();
            var people = GetPeopleRows(data);

            int uniqueLalafell = people.Count(x => x.Race == "Lalafell");
            ImGui.Text($"Unique players: {people.Count}");
            ImGui.SameLine();
            ImGui.TextDisabled($"({uniqueLalafell} Lalafell)");
            ImGui.Spacing();
            ImGui.PushTextWrapPos();
            try
            {
                ImGui.TextDisabled("Click a player to view recorded duties with them. Click a header to sort.");
                ImGui.TextDisabled("'-' means unknown.");
            }
            finally
            {
                ImGui.PopTextWrapPos();
            }
            ImGui.Spacing();

            if (people.Count == 0)
                ImGui.TextDisabled("No players match these filters. Clear filters or lower minimum encounters to see more players.");

            if (people.Count > 0 && BeginConfiguredTable("People", "peopleTable", verticalScroll: true))
            {
                SetupConfiguredColumns("People", scrollable: true);

                var sortSpecs = ImGui.TableGetSortSpecs();
                var sortColumn = sortSpecs.SpecsCount > 0 && IsTableColumnVisible("People", ConfiguredSortColumn(sortSpecs.Specs.ColumnIndex))
                    ? ConfiguredSortColumn(sortSpecs.Specs.ColumnIndex) : 0;
                var direction = sortSpecs.SpecsCount > 0 && sortColumn == ConfiguredSortColumn(sortSpecs.Specs.ColumnIndex)
                    ? sortSpecs.Specs.SortDirection : ImGuiSortDirection.Ascending;
                if (sortSpecs.SpecsDirty || data.PeopleSort != (sortColumn, (int)direction))
                {
                    people = SortTableRows(people, player => sortColumn switch
                    {
                        0 => player.Name,
                        1 => player.World,
                        2 => player.Race,
                        3 => player.Gender,
                        4 => player.TimesSeen,
                        5 => player.TotalDeaths,
                        6 => player.AverageDeaths,
                        7 => player.FirstSeen,
                        8 => player.LastSeen,
                        9 => player.DifferentDuties,
                        10 => player.KnownDeathEncounters,
                        11 => player.DeathlessEncounters,
                        12 => player.SproutEncounters,
                        13 => player.MentorEncounters,
                        14 => player.PvpDamage,
                        15 => player.PvpKills,
                        16 => player.PvpAssists,
                        17 => player.TeammateEncounters,
                        18 => player.EnemyEncounters,
                        >= 19 and <= 29 => PvpStatisticValue(player.Pvp, sortColumn - 16),
                        _ => player.Name
                    }, direction == ImGuiSortDirection.Ascending, player => player.Name);
                    data.People = people;
                    data.PeopleSort = (sortColumn, (int)direction);
                    sortSpecs.SpecsDirty = false;
                }

                for (int i = 0; i < people.Count; i++)
                {
                    var player = people[i];
                    ImGui.TableNextRow();
                    SetConfiguredColumnIndex(0);
                    if (ImGui.Selectable($"{player.Name}###person{player.Name}@{player.World}"))
                    {
                        historyPlayerName = player.Name;
                        historyPlayerWorld = player.World;
                        showPlayerHistory = true;
                    }
                    DrawTableCell(1, player.World);
                    DrawTableCell(2, player.Race);
                    DrawTableCell(3, player.Gender);
                    DrawTableCell(4, player.TimesSeen.ToString());
                    DrawTableCell(5, player.TotalDeaths?.ToString() ?? "-");
                    DrawTableCell(6, player.AverageDeaths?.ToString("F2") ?? "-");
                    DrawTableCell(7, player.FirstSeen.ToString("yyyy-MM-dd HH:mm"));
                    DrawTableCell(8, player.LastSeen.ToString("yyyy-MM-dd HH:mm"));
                    DrawTableCell(9, player.DifferentDuties.ToString());
                    DrawTableCell(10, player.KnownDeathEncounters.ToString());
                    DrawTableCell(11, player.DeathlessEncounters.ToString());
                    DrawTableCell(12, player.SproutEncounters.ToString());
                    DrawTableCell(13, player.MentorEncounters.ToString());
                    DrawTableCell(14, player.PvpDamage?.ToString("N0") ?? "-");
                    DrawTableCell(15, player.PvpKills?.ToString("N0") ?? "-");
                    DrawTableCell(16, player.PvpAssists?.ToString("N0") ?? "-");
                    DrawTableCell(17, player.TeammateEncounters.ToString());
                    DrawTableCell(18, player.EnemyEncounters.ToString());
                    DrawPvpStatisticCells(19, player.Pvp, includeScores: false);
                }
                EndStyledTable();
            }

        }

        private void DrawPlayerHistory(PeopleTableRow? selected)
        {
            ImGui.SetNextWindowSize(new Vector2(760, 440), ImGuiCond.FirstUseEver);
            bool isOpen = showPlayerHistory;
            try
            {
                if (ImGui.Begin("Player Duty History###DutyTrackerPlayerHistory", ref isOpen))
                {
                    ImGui.TextUnformatted($"{historyPlayerName}@{historyPlayerWorld}");
                    ImGui.TextDisabled("Duties recorded with this player. Filters still apply.");
                    ImGui.Separator();
                    ImGui.Spacing();

                    if (selected == null || selected.Encounters.Count == 0)
                    {
                        ImGui.TextDisabled("No duties with this player match these filters.");
                    }
                    else
                    {
                        ImGui.Text($"Encounters: {selected.TimesSeen}    Known death counts: {selected.KnownDeathEncounters}    Total deaths: {selected.TotalDeaths?.ToString() ?? "-"}    Average: {selected.AverageDeaths?.ToString("F2") ?? "-"}");
                        ImGui.Spacing();

                        if (BeginStyledTable(
                            "playerHistoryTable", 5,
                            ImGuiTableFlags.Borders |
                            ImGuiTableFlags.RowBg |
                            ImGuiTableFlags.ScrollY |
                            ImGuiTableFlags.Resizable |
                            ImGuiTableFlags.SizingStretchProp))
                        {
                            try
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
                                    }
                                    ImGui.TableSetColumnIndex(1);
                                    ImGui.TextUnformatted(DutyClassification.DisplayType(entry.Record));
                                    ImGui.TableSetColumnIndex(2);
                                    ImGui.TextUnformatted(entry.Record.Timestamp.ToString("yyyy-MM-dd HH:mm"));
                                    ImGui.TableSetColumnIndex(3);
                                    ImGui.TextUnformatted(entry.Player.Job);
                                    ImGui.TableSetColumnIndex(4);
                                    ImGui.TextUnformatted(EffectiveDeaths(entry.Record, entry.Player)?.ToString() ?? "-");
                                }
                            }
                            finally
                            {
                                EndStyledTable();
                            }
                        }
                    }
                }
            }
            finally
            {
                ImGui.End();
            }
            showPlayerHistory = isOpen;
        }
    }
}
