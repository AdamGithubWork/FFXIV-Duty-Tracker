using MentorRouletteTracker.DutyTracking;
using MentorRouletteTracker.PvP;

namespace MentorRouletteTracker;

public partial class MainWindow
{
    private sealed class PvpStatistics
    {
        public long? Damage { get; init; }
        public long? Kills { get; init; }
        public long? Assists { get; init; }
        public double? AverageDamage { get; init; }
        public double? AverageKills { get; init; }
        public double? AverageAssists { get; init; }
        public int KnownResults { get; init; }
        public int Wins { get; init; }
        public int Losses { get; init; }
        public int FrontlineResults { get; init; }
        public int First { get; init; }
        public int Second { get; init; }
        public int Third { get; init; }
        public double? WinRate => Percentage(Wins, KnownResults);
        public double? FirstRate => Percentage(First, FrontlineResults);
        public double? SecondRate => Percentage(Second, FrontlineResults);
        public double? ThirdRate => Percentage(Third, FrontlineResults);
        private static double? Percentage(int count, int total) => total == 0 ? null : 100d * count / total;
    }

    private static bool HasPvpResult(PlayerEncounter encounter) => encounter.Record.DutyType == "PvP" &&
        (encounter.Record.PvPMode switch
        {
            PvpMode.Frontline => encounter.Player.PvPPlacement is >= 1 and <= 3,
            PvpMode.CrystallineConflict or PvpMode.RivalWings => encounter.Player.PvPPlacement is 1 or 2,
            _ => false
        });

    private static PvpStatistics BuildPvpStatistics(IEnumerable<PlayerEncounter> encounters)
    {
        var pvp = encounters.Where(encounter => encounter.Record.DutyType == "PvP").ToList();
        var results = pvp.Where(HasPvpResult).ToList();
        var frontline = results.Where(encounter => encounter.Record.PvPMode == PvpMode.Frontline).ToList();
        return new PvpStatistics
        {
            Damage = SumPvpScores(pvp.Select(encounter => encounter.Player.PvPDamageDealt)),
            Kills = SumPvpScores(pvp.Select(encounter => (long?)encounter.Player.PvPKills)),
            Assists = SumPvpScores(pvp.Select(encounter => (long?)encounter.Player.PvPAssists)),
            AverageDamage = pvp.Average(encounter => (double?)encounter.Player.PvPDamageDealt),
            AverageKills = pvp.Average(encounter => (double?)encounter.Player.PvPKills),
            AverageAssists = pvp.Average(encounter => (double?)encounter.Player.PvPAssists),
            KnownResults = results.Count,
            Wins = results.Count(encounter => encounter.Player.PvPPlacement == 1),
            Losses = results.Count(encounter => encounter.Player.PvPPlacement > 1),
            FrontlineResults = frontline.Count,
            First = frontline.Count(encounter => encounter.Player.PvPPlacement == 1),
            Second = frontline.Count(encounter => encounter.Player.PvPPlacement == 2),
            Third = frontline.Count(encounter => encounter.Player.PvPPlacement == 3)
        };
    }

    private static TableColumnOption[] PvpResultColumns() =>
    [
        new("pvpWins", "Wins (PvP)", false), new("pvpLosses", "Losses (PvP)", false),
        new("pvpWinRate", "Win rate (PvP)", false),
        new("pvpFirst", "1st (Frontline)", false), new("pvpFirstRate", "1st % (Frontline)", false),
        new("pvpSecond", "2nd (Frontline)", false), new("pvpSecondRate", "2nd % (Frontline)", false),
        new("pvpThird", "3rd (Frontline)", false), new("pvpThirdRate", "3rd % (Frontline)", false)
    ];

    private static TableColumnOption[] PvpStatisticColumns(bool averages = false) =>
    [
        new("pvpDamage", averages ? "Average damage (PvP)" : "Damage dealt (PvP)", false),
        new("pvpKills", averages ? "Average kills (PvP)" : "Kills (PvP)", false),
        new("pvpAssists", averages ? "Average assists (PvP)" : "Assists (PvP)", false), ..PvpResultColumns()
    ];

    private static IComparable? PvpStatisticValue(PvpStatistics stats, int column, bool averages = false)
    {
        if (averages && column is >= 0 and <= 2)
            return column switch { 0 => stats.AverageDamage, 1 => stats.AverageKills, _ => stats.AverageAssists };
        return column switch
        {
            0 => stats.Damage, 1 => stats.Kills, 2 => stats.Assists,
            3 => stats.KnownResults == 0 ? null : stats.Wins,
            4 => stats.KnownResults == 0 ? null : stats.Losses,
            5 => stats.WinRate,
            6 => stats.FrontlineResults == 0 ? null : stats.First,
            7 => stats.FirstRate,
            8 => stats.FrontlineResults == 0 ? null : stats.Second,
            9 => stats.SecondRate,
            10 => stats.FrontlineResults == 0 ? null : stats.Third,
            11 => stats.ThirdRate,
            _ => null
        };
    }

    private static string PvpStatisticText(PvpStatistics stats, int column, bool averages = false) => PvpStatisticValue(stats, column, averages) switch
    {
        double rate when column is 5 or 7 or 9 or 11 => $"{rate:F1}%",
        double average => average.ToString(column == 0 ? "N0" : "N2"),
        long total => total.ToString("N0"),
        int count => count.ToString(),
        _ => "-"
    };

    private static void DrawPvpStatisticCells(int start, PvpStatistics stats, bool includeScores = true, bool averages = false)
    {
        var first = includeScores ? 0 : 3;
        for (var index = first; index < 12; index++)
            DrawTableCell(start + index - first, PvpStatisticText(stats, index, averages));
    }

    private static string PvpPlayerResultLabel(DutyRecord record, PartyMemberSnapshot player) =>
        record.PvPMode == PvpMode.Frontline ? PvpPlacementLabel(player.PvPPlacement) : player.PvPPlacement switch
        {
            1 => "Win", 2 => "Loss", _ => "Unknown"
        };
}
