
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
            var connection = new ConnectionString
            {
                Filename = path,
                ReadOnly = true
            };

            using var db = new LiteDatabase(connection);

            foreach (var collectionName in new[] { "ccmatch", "flmatch" })
            {
                var collection =
                    db.GetCollection<BsonDocument>(collectionName);

                var match = collection.FindById(new ObjectId(matchId));

                if (match == null)
                    continue;

                if (!match.ContainsKey("IsCompleted") ||
                    !match["IsCompleted"].IsBoolean ||
                    !match["IsCompleted"].AsBoolean ||
                    !match.ContainsKey("DutyStartTime") ||
                    !match["DutyStartTime"].IsDateTime ||
                    !match.ContainsKey("PostMatch") ||
                    !match["PostMatch"].IsDocument)
                    return null;

                var postMatch = match["PostMatch"].AsDocument;

                if (!postMatch.ContainsKey("Teams") ||
                    !postMatch["Teams"].IsDocument)
                {
                    log.Warning(
                        $"{collectionName}: unexpected PostMatch structure. " +
                        $"Fields: {string.Join(", ", postMatch.Keys)}");
                    return null;
                }

                var result = new PvpMatchResult
                {
                    MatchId = matchId,
                    DutyStartTime = match["DutyStartTime"].AsDateTime
                };

                foreach (var team in postMatch["Teams"].AsDocument)
                {
                    if (!team.Value.IsDocument)
                        continue;

                    var teamData = team.Value.AsDocument;

                    if (!teamData.ContainsKey("PlayerStats") ||
                        !teamData["PlayerStats"].IsArray)
                        continue;

                    foreach (var player in teamData["PlayerStats"].AsArray)
                    {
                        if (!player.IsDocument)
                            continue;

                        var playerData = player.AsDocument;

                        if (!playerData.ContainsKey("Player") ||
                            !playerData["Player"].IsDocument ||
                            !playerData.ContainsKey("Deaths"))
                            continue;

                        var alias = playerData["Player"].AsDocument;

                        if (!alias.ContainsKey("Name") ||
                            !alias["Name"].IsString ||
                            !alias.ContainsKey("HomeWorld") ||
                            !alias["HomeWorld"].IsString ||
                            !playerData["Deaths"].IsInt32)
                            continue;

                        var name = alias["Name"].AsString;
                        var world = alias["HomeWorld"].AsString;
                        var deaths = playerData["Deaths"].AsInt32;

                        result.PlayerDeaths[$"{name}@{world}"] = deaths;
                    }
                }

                if (result.PlayerDeaths.Count > 0)
                {
                    log.Information(
                        $"{collectionName} scoreboard loaded: " +
                        $"{result.PlayerDeaths.Count} players.");

                    return result;
                }

                log.Warning(
                    $"{collectionName}: match found, but no player deaths " +
                    "could be extracted.");

                return null;
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

}
