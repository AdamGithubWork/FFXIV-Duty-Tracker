
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

            var match = collection.FindAll()
                .FirstOrDefault(doc =>
                    doc.ContainsKey("PostMatch") &&
                    doc["PostMatch"].IsDocument);

            if (match == null)
            {
                log.Warning("No CC matches with post-match data found.");
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
}
