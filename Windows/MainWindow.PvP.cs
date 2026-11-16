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
        ImGui.TextDisabled("Scores come from PvPStats. '-' means unknown.");
        ImGui.PopTextWrapPos();
        if (record.Party.Count == 0)
        {
            ImGui.TextDisabled("No players have been recorded yet.");
            return;
        }
        var rowHeight = ImGui.GetTextLineHeight() + TableCellVerticalPadding * 2;
        var desiredHeight = rowHeight * (Math.Min(record.Party.Count, 12) + 2) + ImGui.GetStyle().ScrollbarSize;
        var availableHeight = Math.Max(rowHeight * 3, ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() * 5);
        if (!BeginConfiguredTable("PvP players", "pvpPlayerDetailV2", height: Math.Min(desiredHeight, availableHeight), verticalScroll: true))
            return;
        SetupConfiguredColumns("PvP players", scrollable: true);
        var specs = ImGui.TableGetSortSpecs();
        var column = specs.SpecsCount > 0 && IsTableColumnVisible("PvP players", ConfiguredSortColumn(specs.Specs.ColumnIndex))
            ? ConfiguredSortColumn(specs.Specs.ColumnIndex) : 3;
        var ascending = specs.SpecsCount == 0 || specs.Specs.SortDirection == ImGuiSortDirection.Ascending;
        var players = SortTableRows(record.Party, player => column switch
        {
            0 => player.Name, 1 => player.World, 2 => player.Job,
            3 => player.PvPIsEnemy.HasValue ? (player.PvPIsEnemy.Value ? 1 : 0) : (int?)null,
            4 => player.PvPDamageDealt, 5 => player.PvPKills, 6 => EffectiveDeaths(record, player),
            7 => player.PvPAssists, 8 => player.Race, 9 => player.Gender, 10 => GetPlayerStatus(player),
            11 => player.PvPPlacement,
            12 => player.PvPDamageTaken,
            13 => player.PvPHealingDone,
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
            DrawTableCell(4, player.PvPDamageDealt?.ToString("N0") ?? "-");
            DrawTableCell(5, player.PvPKills?.ToString() ?? "-");
            DrawTableCell(6, EffectiveDeaths(record, player)?.ToString() ?? "-");
            DrawTableCell(7, player.PvPAssists?.ToString() ?? "-");
            DrawTableCell(8, player.Race);
            DrawTableCell(9, player.Gender);
            DrawTableCell(10, GetPlayerStatus(player));
            DrawTableCell(11, PvpPlayerResultLabel(record, player));
            DrawTableCell(12, player.PvPDamageTaken?.ToString("N0") ?? "-");
            DrawTableCell(13, player.PvPHealingDone?.ToString("N0") ?? "-");
        }
        EndStyledTable();
    }
}
