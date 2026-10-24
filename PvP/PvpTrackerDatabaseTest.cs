
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
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher",
            "pluginConfigs",
            "PvpStats",
            "data.db"
        );

        if (!File.Exists(path))
            return null;

        try
        {
            var connection = new ConnectionString
            {
                Filename = path,
                ReadOnly = true
            };

            using var db = new LiteDatabase(connection);
            var collection = db.GetCollection<BsonDocument>("ccmatch");

            if (matchId.Length != 24 ||
                !matchId.All(Uri.IsHexDigit))
                return null;

            var match = collection.FindById(new ObjectId(matchId));

            if (match == null ||
                !match.ContainsKey("IsCompleted") ||
                !match["IsCompleted"].AsBoolean ||
                !match.ContainsKey("PostMatch") ||
                !match["PostMatch"].IsDocument)
            {
                return null;
            }

            var postMatch = match["PostMatch"].AsDocument;

            if (!postMatch.ContainsKey("Teams") ||
                !postMatch["Teams"].IsDocument)
                return null;

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
                        !alias.ContainsKey("HomeWorld"))
                        continue;

                    var name = alias["Name"].AsString;
                    var world = alias["HomeWorld"].AsString;
                    var deaths = playerData["Deaths"].AsInt32;

                    result.PlayerDeaths[$"{name}@{world}"] = deaths;
                }
            }

            return result.PlayerDeaths.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            log.Error(ex, "Could not retrieve PvP match results.");
            return null;
        }
    }

}
