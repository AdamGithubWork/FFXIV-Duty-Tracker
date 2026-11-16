using MentorRouletteTracker.PvP;

namespace MentorRouletteTracker.DutyTracking;

internal static class DutyClassification
{
    private static readonly HashSet<string> FrontlineMaps = new(StringComparer.OrdinalIgnoreCase)
    {
        "The Borderland Ruins (Secure)", "Seal Rock (Seize)", "The Fields of Glory (Shatter)",
        "Onsal Hakair (Danshig Naadam)", "Worqor Chirteh (Triumph)"
    };
    private static readonly HashSet<string> RivalWingsMaps = new(StringComparer.OrdinalIgnoreCase)
    {
        "Astragalos", "Hidden Gorge"
    };
    internal static string DutyType(DutyRecord record) =>
        record.DutyName.Contains("(Ultimate)", StringComparison.OrdinalIgnoreCase) ? "Ultimate Raids" : record.DutyType;
    internal static string DisplayType(DutyRecord record) => record.DutyType == "PvP" ? Mode(record) switch
    {
        PvpMode.Frontline => "PvP (Frontlines)",
        PvpMode.CrystallineConflict => "PvP (Crystalline Conflict)",
        PvpMode.RivalWings => "PvP (Rival Wings)",
        _ => "PvP"
    } : DutyType(record);
    internal static PvpMode Mode(DutyRecord record)
    {
        if (record.DutyType != "PvP")
            return PvpMode.Unknown;
        var name = record.DutyName.Trim();
        if (FrontlineMaps.Contains(name))
            return PvpMode.Frontline;
        if (RivalWingsMaps.Contains(name))
            return PvpMode.RivalWings;
        if (record.PvPMode != PvpMode.Unknown)
            return record.PvPMode;
        return name.Length > 0 ? PvpMode.CrystallineConflict : PvpMode.Unknown;
    }
}
