using System;
using System.IO;
using System.Linq;
using LiteDB;
using Dalamud.Plugin.Services;

namespace MentorRouletteTracker.PvP;

public static class PvpTrackerDatabase
{
    private static string DatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XIVLauncher", "pluginConfigs", "PvpStats", "data.db");

    public static PvpMatchResult? GetMatch(string matchId, IPluginLog log)
    {
        if (!File.Exists(DatabasePath) ||
            string.IsNullOrWhiteSpace(matchId) ||
            matchId.Length != 24 ||
            !matchId.All(Uri.IsHexDigit))
            return null;

        try
        {
            using var db = new LiteDatabase(new ConnectionString
            {
                Filename = DatabasePath,
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
                    DutyStartTime = match["DutyStartTime"].AsDateTime,
                    IsFrontline = collectionName == "flmatch"
                };

                if (result.IsFrontline)
                {
                    if (!match.ContainsKey("PlayerScoreboards") ||
                        !match["PlayerScoreboards"].IsDocument)
                        return null;

                    ParseFrontlineDeaths(match, result);
                }
                else
                {
                    if (!match.ContainsKey("PostMatch") ||
                        !match["PostMatch"].IsDocument)
                        return null;

                    var postMatch = match["PostMatch"].AsDocument;
                    if (!postMatch.ContainsKey("Teams") ||
                        !postMatch["Teams"].IsDocument)
                        return null;

                    ParseCrystallineConflictDeaths(postMatch, result);
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

    public static bool? IsFrontlineMatch(string matchId, IPluginLog log)
    {
        if (string.IsNullOrWhiteSpace(matchId) ||
            matchId.Length != 24 || !matchId.All(Uri.IsHexDigit))
            return null;

        if (!File.Exists(DatabasePath))
            return null;

        try
        {
            using var db = new LiteDatabase(new ConnectionString
            {
                Filename = DatabasePath,
                ReadOnly = true
            });

            var id = new ObjectId(matchId);

            if (db.GetCollection<BsonDocument>("flmatch").FindById(id) != null)
                return true;

            if (db.GetCollection<BsonDocument>("ccmatch").FindById(id) != null)
                return false;
        }
        catch (Exception ex)
        {
            log.Warning($"Could not identify PvP match {matchId}: {ex.Message}");
        }

        return null;
    }

    private static void ParseFrontlineDeaths(BsonDocument match, PvpMatchResult result)
    {
        foreach (var entry in match["PlayerScoreboards"].AsDocument)
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
            var key = entry.Key;
            var lastSpace = key.LastIndexOf(' ');
            if (lastSpace <= 0 || lastSpace >= key.Length - 1)
                continue;

            var name = key.Substring(0, lastSpace);
            var world = key.Substring(lastSpace + 1);
            result.PlayerDeaths[$"{name}@{world}"] = deaths;
        }
    }

    private static void ParseCrystallineConflictDeaths(BsonDocument postMatch, PvpMatchResult result)
    {
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
}
