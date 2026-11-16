using Dalamud.Plugin.Services;
using LiteDB;

namespace MentorRouletteTracker.PvP;

public static class PvpTrackerDatabase
{
    private static readonly (string Collection, PvpMode Mode)[] MatchCollections =
    [
        ("ccmatch", PvpMode.CrystallineConflict),
        ("flmatch", PvpMode.Frontline),
        ("rwmatch", PvpMode.RivalWings)
    ];

    // PvPStats uses these enum ordinals when jobs are stored as numbers.
    private static readonly string[] Jobs =
        ["PLD", "WAR", "DRK", "GNB", "WHM", "SCH", "AST", "SGE", "MNK", "DRG", "NIN", "SAM", "RPR", "VPR", "BRD", "MCH", "DNC", "BLM", "SMN", "RDM", "PCT"];

    private static string DatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XIVLauncher", "pluginConfigs", "PvpStats", "data.db");

    public static List<PvpMatchReference>? FindMatches(IEnumerable<DateTime> dutyStartTimes, IPluginLog log) =>
        FindMatches(DatabasePath, dutyStartTimes, log);

    internal static List<PvpMatchReference>? FindMatches(
        string databasePath, IEnumerable<DateTime> dutyStartTimes, IPluginLog log)
    {
        var times = dutyStartTimes.Select(time => time.ToUniversalTime()).Distinct().ToArray();
        if (times.Length == 0)
            return new();
        if (!File.Exists(databasePath))
            return null;
        try
        {
            using var db = Open(databasePath);
            var matches = new List<PvpMatchReference>();
            var ranges = times.Select(time => Query.Between("DutyStartTime", time.AddSeconds(-30), time.AddSeconds(30))).ToArray();
            var query = ranges.Length == 1 ? ranges[0] : Query.Or(ranges);
            foreach (var (collection, mode) in MatchCollections)
            {
                foreach (var match in db.GetCollection<BsonDocument>(collection).Find(query))
                {
                    if (match["_id"].IsObjectId && match["DutyStartTime"].IsDateTime && !IsDeleted(match))
                        matches.Add(new(match["_id"].AsObjectId.ToString(), match["DutyStartTime"].AsDateTime, mode));
                }
            }
            return matches;
        }
        catch (Exception ex)
        {
            log.Warning($"Could not find unfinished PvP scoreboards: {ex.Message}");
            return null;
        }
    }

    public static PvpMode GetMatchMode(string matchId, IPluginLog log) => GetMatchMode(DatabasePath, matchId, log);

    internal static PvpMode GetMatchMode(string databasePath, string matchId, IPluginLog log)
    {
        if (!ValidId(matchId) || !File.Exists(databasePath))
            return PvpMode.Unknown;
        try
        {
            using var db = Open(databasePath);
            var id = new ObjectId(matchId);
            foreach (var (collection, mode) in MatchCollections)
            {
                var match = db.GetCollection<BsonDocument>(collection).FindById(id);
                if (match != null && !IsDeleted(match))
                    return mode;
            }
        }
        catch (Exception ex)
        {
            log.Warning($"Could not identify PvP match {matchId}: {ex.Message}");
        }
        return PvpMode.Unknown;
    }

    public static PvpMatchResult? GetMatch(string matchId, IPluginLog log) => GetMatch(DatabasePath, matchId, log);

    internal static PvpMatchResult? GetMatch(string databasePath, string matchId, IPluginLog log)
    {
        if (!ValidId(matchId) || !File.Exists(databasePath))
            return null;
        try
        {
            using var db = Open(databasePath);
            var id = new ObjectId(matchId);
            foreach (var (collection, mode) in MatchCollections)
            {
                var match = db.GetCollection<BsonDocument>(collection).FindById(id);
                if (match == null)
                    continue;
                if (IsDeleted(match) || !match["IsCompleted"].IsBoolean || !match["IsCompleted"].AsBoolean ||
                    !match["DutyStartTime"].IsDateTime)
                    return null;

                var result = new PvpMatchResult
                {
                    MatchId = matchId,
                    DutyStartTime = match["DutyStartTime"].AsDateTime,
                    Mode = mode,
                    ExpectedPlayerCount = mode == PvpMode.CrystallineConflict ? 10 :
                        PositiveCount(match["PlayerCount"]) ?? (mode == PvpMode.Frontline ? 72 : 48)
                };
                if (TryAlias(match["LocalPlayer"], out var recorder, out var recorderWorld))
                {
                    result.RecorderName = recorder;
                    result.RecorderWorld = recorderWorld;
                }
                if (mode == PvpMode.CrystallineConflict)
                {
                    if (!match["PostMatch"].IsDocument || !match["PostMatch"]["Teams"].IsDocument)
                        return null;
                    foreach (var team in match["PostMatch"]["Teams"].AsDocument)
                    {
                        if (!team.Value.IsDocument || !team.Value["PlayerStats"].IsArray)
                            continue;
                        foreach (var value in team.Value["PlayerStats"].AsArray)
                        {
                            if (!value.IsDocument || !TryAlias(value["Player"], out var name, out var world))
                                continue;
                            AddPlayer(result, name, world, ReadJob(value.AsDocument), ReadDeaths(value["Deaths"]),
                                ReadTeam(new BsonValue(team.Key), mode), value.AsDocument);
                        }
                    }
                }
                else
                {
                    if (!match["PlayerScoreboards"].IsDocument)
                        return null;
                    if (match["Players"].IsArray)
                    {
                        foreach (var value in match["Players"].AsArray)
                        {
                            if (value.IsDocument && TryAlias(value["Name"], out var name, out var world))
                                AddPlayer(result, name, world, ReadJob(value.AsDocument), null, ReadTeam(value["Team"], mode));
                        }
                    }
                    foreach (var entry in match["PlayerScoreboards"].AsDocument)
                    {
                        var separator = entry.Key.LastIndexOf(' ');
                        if (!entry.Value.IsDocument || separator <= 0 || separator == entry.Key.Length - 1)
                            continue;
                        AddPlayer(result, entry.Key[..separator], entry.Key[(separator + 1)..], "Unknown",
                            ReadDeaths(entry.Value["Deaths"]), scoreboard: entry.Value.AsDocument);
                    }
                }
                if (result.Players.Count == 0 || !result.Players.Values.Any(player => player.Deaths.HasValue))
                    return null;
                var placements = new Dictionary<string, int>(StringComparer.Ordinal);
                if (mode == PvpMode.Frontline && match["Teams"].IsDocument)
                {
                    foreach (var team in match["Teams"].AsDocument)
                    {
                        var name = ReadTeam(new BsonValue(team.Key), mode);
                        if (name.Length > 0 && team.Value.IsDocument &&
                            ReadDeaths(team.Value["Placement"]) is >= 0 and <= 2 and var place)
                            placements[name] = place + 1;
                    }
                }
                var winner = ReadTeam(match["MatchWinner"], mode);
                if (winner.Length == 0 && mode == PvpMode.CrystallineConflict && match["PostMatch"].IsDocument)
                    winner = ReadTeam(match["PostMatch"]["MatchWinner"], mode);
                foreach (var player in result.Players.Values)
                    player.Placement = mode == PvpMode.Frontline
                        ? placements.TryGetValue(player.Team, out var place) ? place : null
                        : winner.Length > 0 && player.Team.Length > 0 ? player.Team == winner ? 1 : 2 : null;
                result.Placement = result.Players.GetValueOrDefault($"{result.RecorderName}@{result.RecorderWorld}")?.Placement;
                log.Information($"{collection} scoreboard loaded: {result.Players.Count} players.");
                return result;
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Could not read PvP match results.");
        }
        return null;
    }

    private static LiteDatabase Open(string path) => new(new ConnectionString { Filename = path, ReadOnly = true });
    private static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length == 24 && id.All(Uri.IsHexDigit);
    private static bool IsDeleted(BsonDocument match) => match["IsDeleted"].IsBoolean && match["IsDeleted"].AsBoolean;
    private static int? PositiveCount(BsonValue value)
    {
        var count = ReadDeaths(value);
        return count > 0 ? count : null;
    }

    private static int? ReadDeaths(BsonValue value)
    {
        if (!value.IsInt32 && !value.IsInt64)
            return null;
        var count = value.AsInt64;
        return count >= 0 && count <= int.MaxValue ? (int)count : null;
    }

    private static string ReadJob(BsonDocument player)
    {
        var value = player["Job"];
        if (value.IsString)
            return Jobs.FirstOrDefault(job => string.Equals(job, value.AsString, StringComparison.OrdinalIgnoreCase)) ?? "Unknown";
        if (value.IsInt32 || value.IsInt64)
        {
            var index = value.AsInt64;
            if (index >= 0 && index < Jobs.Length)
                return Jobs[(int)index];
        }
        return "Unknown";
    }

    private static bool TryAlias(BsonValue value, out string name, out string world)
    {
        name = string.Empty;
        world = string.Empty;
        if (!value.IsDocument || !value["Name"].IsString || !value["HomeWorld"].IsString)
            return false;
        name = value["Name"].AsString.Trim();
        world = value["HomeWorld"].AsString.Trim();
        return ValidPlayer(name, world);
    }

    private static bool ValidPlayer(string name, string world) => !string.IsNullOrWhiteSpace(name) &&
        !string.IsNullOrWhiteSpace(world) && name != "_UNKNOWN_" && world != "_UNKNOWN_";

    private static string ReadTeam(BsonValue value, PvpMode mode)
    {
        string[] teams = mode switch
        {
            PvpMode.CrystallineConflict => ["", "Astra", "Umbra"],
            PvpMode.Frontline => ["Maelstrom", "Adders", "Flames"],
            PvpMode.RivalWings => ["Falcons", "Ravens"],
            _ => []
        };
        if (value.IsString)
        {
            var text = value.AsString;
            if (!int.TryParse(text, out var index))
                return teams.FirstOrDefault(team => team.Length > 0 && string.Equals(team, text, StringComparison.OrdinalIgnoreCase)) ?? "";
            return index >= 0 && index < teams.Length ? teams[index] : "";
        }
        if (value.IsInt32 || value.IsInt64)
        {
            var index = value.AsInt64;
            return index >= 0 && index < teams.Length ? teams[(int)index] : "";
        }
        return "";
    }

    private static long? ReadDamage(BsonValue value) =>
        (value.IsInt32 || value.IsInt64) && value.AsInt64 >= 0 ? value.AsInt64 : null;

    private static void AddPlayer(PvpMatchResult result, string name, string world, string job, int? deaths,
        string team = "", BsonDocument? scoreboard = null)
    {
        name = name.Trim();
        world = world.Trim();
        if (!ValidPlayer(name, world))
            return;
        var key = $"{name}@{world}";
        if (!result.Players.TryGetValue(key, out var player))
            result.Players.Add(key, player = new PvpPlayerResult { Name = name, World = world });
        if (job != "Unknown")
            player.Job = job;
        if (deaths.HasValue)
            player.Deaths = deaths;
        if (team.Length > 0)
            player.Team = team;
        if (scoreboard != null)
        {
            player.Kills ??= ReadDeaths(scoreboard["Kills"]);
            player.Assists ??= ReadDeaths(scoreboard["Assists"]);
            player.DamageDealt ??= ReadDamage(scoreboard["DamageDealt"]);
            player.DamageTaken ??= ReadDamage(scoreboard["DamageTaken"]);
            player.HealingDone ??= ReadDamage(scoreboard["HPRestored"]);
        }
    }
}
