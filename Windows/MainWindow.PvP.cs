using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;

namespace MentorRouletteTracker;

public partial class MainWindow
{
    private static PartyMemberSnapshot? RecorderPvpPlayer(DutyRecord record) => record.DutyType == "PvP"
        ? record.Party.FirstOrDefault(player => player.Name == record.RecorderName && player.World == record.RecorderWorld) : null;

    private static long? SumPvpScores(IEnumerable<long?> scores)
    {
        var known = scores.Where(score => score.HasValue).ToArray();
        return known.Length == 0 ? null : known.Sum(score => score!.Value);
    }

    private static string PvpPlacementLabel(int? placement) => placement switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd", _ => "Unknown"
    };

    private static string PvpSideLabel(PartyMemberSnapshot player) => player.PvPIsEnemy switch
    {
        false => "Teammate", true => "Enemy", _ => "Unknown"
    };

    private void DrawPvpPlayers(DutyRecord record)
    {
        var teammates = record.Party.Count(player => player.PvPIsEnemy == false);
        var enemies = record.Party.Count(player => player.PvPIsEnemy == true);
        var unknown = record.Party.Count - teammates - enemies;
        ImGui.Text($"Teammates: {teammates}    Enemies: {enemies}    Side unknown: {unknown}");
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled("Team labels and scores come from PvPStats. '-' means the score isn't available. Choose columns in Settings > Table columns > PvP players.");
        ImGui.PopTextWrapPos();
        if (record.Party.Count == 0)
        {
            ImGui.TextDisabled("No players have been recorded yet.");
            return;
        }
        if (!BeginConfiguredTable("PvP players", "pvpPlayerDetail", height: ImGui.GetTextLineHeightWithSpacing() * 14, verticalScroll: true))
            return;
        SetupConfiguredColumns("PvP players", scrollable: true);
        var specs = ImGui.TableGetSortSpecs();
        var column = specs.SpecsCount > 0 && IsTableColumnVisible("PvP players", specs.Specs.ColumnIndex)
            ? specs.Specs.ColumnIndex : 3;
        var ascending = specs.SpecsCount == 0 || specs.Specs.SortDirection == ImGuiSortDirection.Ascending;
        var players = SortTableRows(record.Party, player => column switch
        {
            0 => player.Name, 1 => player.World, 2 => player.Job,
            3 => player.PvPIsEnemy.HasValue ? (player.PvPIsEnemy.Value ? 1 : 0) : (int?)null,
            4 => string.IsNullOrEmpty(player.PvPTeam) ? null : player.PvPTeam,
            5 => player.PvPDamageDealt, 6 => player.PvPKills, 7 => EffectiveDeaths(record, player),
            8 => player.PvPAssists, 9 => player.Race, 10 => player.Gender, 11 => GetPlayerStatus(player),
            12 => player.PvPPlacement,
            _ => player.Name
        }, ascending, player => player.Name);
        specs.SpecsDirty = false;
        foreach (var player in players)
        {
            ImGui.TableNextRow();
            var self = player.Name == record.RecorderName && player.World == record.RecorderWorld;
            DrawTableCell(0, player.Name + (self ? " (You)" : ""));
            DrawTableCell(1, player.World);
            DrawTableCell(2, player.Job);
            DrawTableCell(3, self ? "You" : PvpSideLabel(player));
            DrawTableCell(4, string.IsNullOrEmpty(player.PvPTeam) ? "Unknown" : player.PvPTeam);
            DrawTableCell(5, player.PvPDamageDealt?.ToString("N0") ?? "-");
            DrawTableCell(6, player.PvPKills?.ToString() ?? "-");
            DrawTableCell(7, EffectiveDeaths(record, player)?.ToString() ?? "-");
            DrawTableCell(8, player.PvPAssists?.ToString() ?? "-");
            DrawTableCell(9, player.Race);
            DrawTableCell(10, player.Gender);
            DrawTableCell(11, GetPlayerStatus(player));
            DrawTableCell(12, PvpPlayerResultLabel(record, player));
        }
        EndStyledTable();
    }
}
