using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;

namespace MentorRouletteTracker;

public partial class MainWindow
{
    private static readonly string[] EncounterRaces =
        ["", "Hyur", "Elezen", "Lalafell", "Miqo'te", "Roegadyn", "Au Ra", "Hrothgar", "Viera"];
    private static readonly string[] EncounterGenders = ["", "Male", "Female"];

    private string EncounterTargetRace => Array.IndexOf(EncounterRaces, config.EncounterTargetRace) >= 0
        ? config.EncounterTargetRace : "Lalafell";
    private string EncounterTargetGender => Array.IndexOf(EncounterGenders, config.EncounterTargetGender) >= 0
        ? config.EncounterTargetGender : string.Empty;
    private string EncounterTargetLabel => string.Join(" ", new[]
        { EncounterTargetGender, EncounterTargetRace.Length == 0 ? "players" : EncounterTargetRace }
        .Where(value => value.Length > 0));

    private bool MatchesEncounterTarget(string race, string gender) =>
        (EncounterTargetRace.Length == 0 || race == EncounterTargetRace) &&
        (EncounterTargetGender.Length == 0 || gender == EncounterTargetGender);

    private bool MatchesEncounterTarget(PartyMemberSnapshot player) => MatchesEncounterTarget(player.Race, player.Gender);

    private string EncounterMilestoneLabel(string label) => label switch
    {
        "Longest streak without a Lalafell" => $"Longest streak without {EncounterTargetLabel}",
        "Longest streak with a Lalafell" => $"Longest streak with {EncounterTargetLabel}",
        _ => label.Replace("Lalafell", EncounterTargetLabel, StringComparison.Ordinal)
    };

    private string ColumnLabel(TableColumnOption column) => column.Id == "lalafell"
        ? column.Label.Replace("Lalafell", EncounterTargetLabel, StringComparison.Ordinal)
        : column.Label;

    private void DrawEncounterTargetSettings()
    {
        ImGui.TextWrapped("Choose a race and gender for the count columns, streaks, and droughts. This also changes the count beside Unique players.");
        var raceIndex = Array.IndexOf(EncounterRaces, EncounterTargetRace);
        var genderIndex = Array.IndexOf(EncounterGenders, EncounterTargetGender);
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 14);
        if (ImGui.Combo("Race##EncounterTarget", ref raceIndex,
                ["All races", ..EncounterRaces.Skip(1)], EncounterRaces.Length))
        {
            config.EncounterTargetRace = EncounterRaces[raceIndex];
            config.Save();
        }
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 14);
        if (ImGui.Combo("Gender##EncounterTarget", ref genderIndex, ["Either", "Male", "Female"], EncounterGenders.Length))
        {
            config.EncounterTargetGender = EncounterGenders[genderIndex];
            config.Save();
        }
        ImGui.TextWrapped("Player filters and Include yourself in statistics still apply. Unknown race or gender won't match a specific choice.");
    }
}
