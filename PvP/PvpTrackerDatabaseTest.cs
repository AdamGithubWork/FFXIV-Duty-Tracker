
using System;
using System.IO;
using LiteDB;
using Dalamud.Plugin.Services;
using System.Linq;
namespace MentorRouletteTracker.PvP;

public static class PvpTrackerDatabaseTest
{
    public static void Run(IPluginLog log)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "XIVLauncher",
            "pluginConfigs",
            "PvpStats",
            "data.db"
        );

        if (!File.Exists(path))
        {
            log.Warning($"PvP Tracker database not found: {path}");
            return;
        }

        try
        {
            var connection = new ConnectionString
            {
                Filename = path,
                ReadOnly = true
            };

            using var db = new LiteDatabase(connection);

            var collection = db.GetCollection<BsonDocument>("ccmatch");
            var count = collection.Count();

            log.Information($"PvP Tracker database opened. CC matches: {count}");
            var firstMatch = collection.FindOne(Query.All());

            if (firstMatch != null)
            {
                log.Information(
                    $"Sample CC ID: {firstMatch["_id"]}");

                log.Information(
                    $"Sample CC ID type: {firstMatch["_id"].Type}");
            }
            var matchId = "6adbf8f17cda100a4c4918e6";

            var match = collection.FindById(
                new ObjectId(matchId));
            if (match == null)
            {
                log.Warning($"CC match not found: {matchId}");
                return;
            }
            log.Information($"Matched CC record: {match["_id"]}");
            log.Information(
    $"PvP DutyStartTime: {match["DutyStartTime"]}");
            var pvpDutyStart = match["DutyStartTime"].AsDateTime;

            log.Information(
                $"PvP duty start (local): {pvpDutyStart.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            log.Information(
                $"PvP MatchStartTime: {match["MatchStartTime"]}");

            log.Information(
                $"PvP MatchEndTime: {match["MatchEndTime"]}");

            log.Information(
                $"PvP IsCompleted: {match["IsCompleted"]}");
            log.Information(
                $"Match fields: {string.Join(", ", match.Keys)}");

            if (!match.ContainsKey("PostMatch") ||
                !match["PostMatch"].IsDocument)
            {
                log.Warning("Match found, but post-match results are missing.");
                return;
            }
            var postMatch = match["PostMatch"].AsDocument;

            log.Information(
                $"CC match found: {match["_id"]}");


            var teams = postMatch["Teams"].AsDocument;

            foreach (var team in teams)
            {
                log.Information($"CC Team: {team.Key}");

                var teamData = team.Value.AsDocument;

                log.Information(
                    $"Team fields: {string.Join(", ", teamData.Keys)}");

                if (!teamData.ContainsKey("PlayerStats"))
                    continue;

                var players = teamData["PlayerStats"].AsArray;

                log.Information($"Players on team: {players.Count}");

                foreach (var player in players)
                {
                    var playerData = player.AsDocument;

                    if (!playerData.ContainsKey("Player") ||
                        !playerData["Player"].IsDocument ||
                        !playerData.ContainsKey("Deaths"))
                    {
                        continue;
                    }

                    var playerAlias = playerData["Player"].AsDocument;

                    var name = playerAlias["Name"].AsString;
                    var homeWorld = playerAlias["HomeWorld"].AsString;
                    var deaths = playerData["Deaths"].AsInt32;

                    log.Information(
                        $"CC Scoreboard: {name}@{homeWorld} - {deaths} deaths");
                }
            }

        }
        catch (Exception ex)
        {
            log.Error(ex, "Could not read PvP Tracker database.");
        }
    }



    public static PvpMatchResult? GetMatch(string matchId, IPluginLog log)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "XIVLauncher",
            "pluginConfigs",
            "PvpStats",
            "data.db");

        if (!File.Exists(path) ||
            string.IsNullOrWhiteSpace(matchId) ||
            matchId.Length != 24 ||
            !matchId.All(Uri.IsHexDigit))
            return null;

        try
        {
            using var db = new LiteDatabase(new ConnectionString
            {
                Filename = path,
                ReadOnly = true
            });

            var id = new ObjectId(matchId);

            foreach (var collectionName in new[] { "ccmatch", "flmatch" })
            {
                var collection = db.GetCollection<BsonDocument>(collectionName);
                var match = collection.FindById(id);

                if (match == null)
                    continue;

                if (!match.ContainsKey("IsCompleted") ||
                    !match["IsCompleted"].IsBoolean ||
                    !match["IsCompleted"].AsBoolean ||
                    !match.ContainsKey("DutyStartTime") ||
                    !match["DutyStartTime"].IsDateTime)
                    return null;

                var result = new PvpMatchResult
                {
                    MatchId = matchId,
                    DutyStartTime = match["DutyStartTime"].AsDateTime
                };

                if (collectionName == "flmatch")
                {
                    if (!match.ContainsKey("PlayerScoreboards") ||
                        !match["PlayerScoreboards"].IsDocument)
                        return null;

                    var scoreboards = match["PlayerScoreboards"].AsDocument;

                    foreach (var entry in scoreboards)
                    {
                        if (!entry.Value.IsDocument)
                            continue;

                        var player = entry.Value.AsDocument;

                        if (!player.ContainsKey("Deaths"))
                            continue;

                        var deathValue = player["Deaths"];

                        if (!deathValue.IsInt32 && !deathValue.IsInt64)
                            continue;

                        var deaths = checked((int)deathValue.AsInt64);

                        // Frontline keys use "First Last World".
                        // Convert to the "First Last@World" format
                        // expected by our existing integration.
                        var key = entry.Key;
                        var lastSpace = key.LastIndexOf(' ');

                        if (lastSpace <= 0 || lastSpace >= key.Length - 1)
                            continue;

                        var name = key.Substring(0, lastSpace);
                        var world = key.Substring(lastSpace + 1);

                        result.PlayerDeaths[$"{name}@{world}"] = deaths;
                    }
                }
                else
                {
                    // Existing Crystalline Conflict handling.
                    if (!match.ContainsKey("PostMatch") ||
                        !match["PostMatch"].IsDocument)
                        return null;

                    var postMatch = match["PostMatch"].AsDocument;

                    if (!postMatch.ContainsKey("Teams") ||
                        !postMatch["Teams"].IsDocument)
                        return null;

                    foreach (var team in postMatch["Teams"].AsDocument)
                    {
                        if (!team.Value.IsDocument)
                            continue;

                        var teamData = team.Value.AsDocument;

                        if (!teamData.ContainsKey("PlayerStats") ||
                            !teamData["PlayerStats"].IsArray)
                            continue;

                        foreach (var playerValue in teamData["PlayerStats"].AsArray)
                        {
                            if (!playerValue.IsDocument)
                                continue;

                            var player = playerValue.AsDocument;

                            if (!player.ContainsKey("Player") ||
                                !player["Player"].IsDocument ||
                                !player.ContainsKey("Deaths"))
                                continue;

                            var alias = player["Player"].AsDocument;

                            if (!alias.ContainsKey("Name") ||
                                !alias["Name"].IsString ||
                                !alias.ContainsKey("HomeWorld") ||
                                !alias["HomeWorld"].IsString)
                                continue;

                            var deathValue = player["Deaths"];

                            if (!deathValue.IsInt32 && !deathValue.IsInt64)
                                continue;

                            var name = alias["Name"].AsString;
                            var world = alias["HomeWorld"].AsString;
                            var deaths = checked((int)deathValue.AsInt64);

                            result.PlayerDeaths[$"{name}@{world}"] = deaths;
                        }
                    }
                }

                if (result.PlayerDeaths.Count == 0)
                {
                    log.Warning(
                        $"{collectionName}: no player deaths extracted.");
                    return null;
                }

                log.Information(
                    $"{collectionName} scoreboard loaded: " +
                    $"{result.PlayerDeaths.Count} players.");

                return result;
            }

            return null;
        }
        catch (Exception ex)
        {
            log.Error(ex, "Could not retrieve PvP match results.");
            return null;
        }
    }



    public static void InspectFrontline(IPluginLog log)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "XIVLauncher", "pluginConfigs", "PvpStats", "data.db");

        using var db = new LiteDatabase(
            $"Filename={dbPath};Connection=shared;ReadOnly=true");

        foreach (var name in db.GetCollectionNames())
        {
            log.Information($"PvP collection: {name}");
        }
    }
    public static void InspectFrontlineMatch(IPluginLog log)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher", "pluginConfigs", "PvpStats", "data.db");

        using var db = new LiteDatabase(new ConnectionString
        {
            Filename = path,
            ReadOnly = true
        });

        var match = db.GetCollection<BsonDocument>("flmatch")
            .FindById(new ObjectId("6add2a2443aeaf0f5001dfc1"));

        if (match == null)
        {
            log.Warning("Frontline match not found in database.");
            return;
        }

        log.Information(
            $"FL match fields: {string.Join(", ", match.Keys)}");

        log.Information(
            $"FL completed: {(match.ContainsKey("IsCompleted") ? match["IsCompleted"].ToString() : "Missing")}");

        if (match.ContainsKey("PostMatch") &&
            match["PostMatch"].IsDocument)
        {
            var postMatch = match["PostMatch"].AsDocument;

            log.Information(
                $"FL PostMatch fields: {string.Join(", ", postMatch.Keys)}");
        }
        else
        {
            log.Warning("FL PostMatch is missing.");
        }
        var scoreboards = match["PlayerScoreboards"].AsDocument;

        foreach (var entry in scoreboards)
        {
            log.Information($"FL player key: {entry.Key}");
            log.Information($"FL player value type: {entry.Value.Type}");

            if (entry.Value.IsDocument)
            {
                var playerData = entry.Value.AsDocument;

                log.Information(
                    $"FL player scoreboard fields: {string.Join(", ", playerData.Keys)}");

                if (playerData.ContainsKey("Deaths"))
                {
                    log.Information(
                        $"FL example deaths: {playerData["Deaths"]}");
                }
            }

            break; // Only inspect one player
        }
    }

}
