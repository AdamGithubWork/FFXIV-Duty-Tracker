using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker;

public partial class MainWindow
{
    private static readonly Vector4 RecordWarningColor = new(1f, 0.75f, 0.3f, 1f);

    private static bool IsPartialPvpScoreboard(DutyRecord record) => record.DutyType == "PvP" &&
        record.PvPLookupFinished && (record.PvPRosterComplete != true || !record.PvPCombatStatsComplete ||
            !record.PvPResultsComplete || !record.PvPExtendedStatsComplete) &&
        record.Party.Any(player => player.PvPScoreboardDeaths.HasValue || player.PvPDamageDealt.HasValue ||
            player.PvPHealingDone.HasValue);

    private static string[] GetRecordDataWarnings(DutyRecord record)
    {
        List<string>? warnings = null;
        void Add(string message) => (warnings ??= new()).Add(message);

        if (record.Party.Count == 0)
            Add("No player details were recorded.");
        if (string.IsNullOrWhiteSpace(record.RecorderName) || string.IsNullOrWhiteSpace(record.RecorderWorld))
            Add("The recording character is unknown.");

        if (record.DutyType == "PvP" && !IsPartialPvpScoreboard(record))
        {
            if (record.PvPRosterComplete == false)
                Add(record.PvPLookupFinished ? "No PvP scoreboard was available." : "The full PvP scoreboard hasn't been imported.");
            else if (record.PvPRosterComplete == true)
            {
                if (!record.PvPCombatStatsComplete)
                    Add("Some PvP combat scores or team details are missing.");
                if (!record.PvPResultsComplete)
                    Add("Some PvP placements are missing.");
                if (!record.PvPExtendedStatsComplete)
                    Add("Some PvP damage taken or healing scores are missing.");
            }
            else if (record.Party.Any(player => !player.PvPScoreboardDeaths.HasValue))
                Add("Some PvP death counts are missing.");
        }

        return warnings?.ToArray() ?? Array.Empty<string>();
    }

    private static void DrawRecordDataWarnings(DutyRecord record)
    {
        var warnings = GetRecordDataWarnings(record);
        var partial = IsPartialPvpScoreboard(record);
        if (warnings.Length == 0 && !partial)
            return;

        ImGui.Spacing();
        if (warnings.Length > 0)
            ImGui.TextColored(RecordWarningColor, "Some recording data is missing");
        else
            ImGui.TextDisabled("Partial PvP scoreboard");
        ImGui.PushTextWrapPos();
        try
        {
            foreach (var warning in warnings)
                ImGui.TextUnformatted("- " + warning);
            if (partial)
                ImGui.TextUnformatted("Available stats are saved. Automatic lookups have finished; missing entries stay unknown.");
            else if (record.DutyType == "PvP" && !record.PvPLookupFinished)
                ImGui.TextDisabled("PvP results can take a few minutes to appear.");
        }
        finally
        {
            ImGui.PopTextWrapPos();
        }
        ImGui.Spacing();
        ImGui.Separator();
    }
}
