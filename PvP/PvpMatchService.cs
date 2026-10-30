using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Dalamud.Plugin;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MentorRouletteTracker.PvP
{
    public sealed class PvpMatchService : IDisposable
    {
        private readonly IPluginLog log;
        private readonly IFramework framework;
        private readonly ICallGateSubscriber<string, bool> pvpMatchSaved;
        private readonly Configuration config;
        private readonly Dictionary<string, DateTime> pendingPvpMatches = new();
        private readonly HashSet<string> processedPvpMatches = new();
        private readonly Dictionary<string, DateTime> pvpQueuedAt = new();
        private readonly HashSet<string> unknownPvpModes = new();
        private DateTime nextPvpCheck = DateTime.MinValue;
        private static readonly TimeSpan PvpRetryInterval =
            TimeSpan.FromSeconds(30);

        public PvpMatchService(
            IDalamudPluginInterface pluginInterface,
            IFramework framework,
            IPluginLog log,
            Configuration config)
        {
            this.framework = framework;
            this.log = log;
            this.config = config;

            pvpMatchSaved =
                pluginInterface.GetIpcSubscriber<string, bool>(
                    "PvpStats.OnMatchSaved");

            pvpMatchSaved.Subscribe(OnPvpMatchSaved);
            framework.Update += OnFrameworkUpdate;
        }
        private void OnPvpMatchSaved(string matchId)
        {
            if (string.IsNullOrWhiteSpace(matchId) ||
                processedPvpMatches.Contains(matchId) ||
                pendingPvpMatches.ContainsKey(matchId))
                return;

            var now = DateTime.UtcNow;

            var isFrontline = PvpTrackerDatabase.IsFrontlineMatch(matchId, log);

            // If match is frontlines, delay the first check for 5 minutes, otherwise delay it for 1 minute.
            var delay = isFrontline == true ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(1);

            pvpQueuedAt[matchId] = now;
            pendingPvpMatches[matchId] = now.Add(delay);

            if (isFrontline == null)
                unknownPvpModes.Add(matchId);

            log.Information(
                $"PvP Tracker match queued: {matchId} | " +
                $"Mode: {(isFrontline == true ? "Frontline" : isFrontline == false ? "CC" : "Unknown")} | " +
                $"First check in {delay.TotalMinutes:0} minute(s).");
        }

        private bool ApplyPvpMatch(string matchId)
        {
            var result =
                PvpTrackerDatabase.GetMatch(matchId, log);

            if (result == null)
            {
                log.Warning(
                    $"PvP scoreboard {matchId}: " +
                    "match results not available.");

                return false;
            }

            var matchingRecord = config.Records
                .Where(r =>
                    Math.Abs(
                        (r.Timestamp.ToUniversalTime() -
                         result.DutyStartTime.ToUniversalTime())
                        .TotalSeconds) < 30)
                .OrderBy(r =>
                    Math.Abs(
                        (r.Timestamp.ToUniversalTime() -
                         result.DutyStartTime.ToUniversalTime())
                        .TotalSeconds))
                .FirstOrDefault();

            if (matchingRecord == null)
            {
                log.Warning(
                    $"PvP scoreboard {matchId}: " +
                    "no matching duty record. " +
                    $"PvP start: " +
                    $"{result.DutyStartTime:yyyy-MM-dd HH:mm:ss}");

                return false;
            }

            int updatedPlayers = 0;

            foreach (var player in matchingRecord.Party)
            {
                var playerKey =
                    $"{player.Name}@{player.World}";

                if (result.PlayerDeaths.TryGetValue(
                    playerKey,
                    out int deaths))
                {
                    player.PvPScoreboardDeaths = deaths;
                    updatedPlayers++;
                }
            }

            if (updatedPlayers == 0)
            {
                log.Warning(
                    $"PvP scoreboard {matchId}: no players matched " +
                    $"in {matchingRecord.DutyName}.");

                return false;
            }

            // Remove players absent from the final Frontline scoreboard.
            // Never perform this cleanup for CC or PvE.
            if (result.IsFrontline)
            {
                var unmatchedPlayers = matchingRecord.Party
                    .Where(player =>
                        !result.PlayerDeaths.ContainsKey(
                            $"{player.Name}@{player.World}"))
                    .ToList();

                // Only clean up when nearly all tracked players matched.
                // This protects against an incomplete or incompatible scoreboard.
                if (unmatchedPlayers.Count > 0 &&
                    updatedPlayers >= matchingRecord.Party.Count * 0.8)
                {
                    foreach (var player in unmatchedPlayers)
                    {
                        log.Information(
                            $"Frontline cleanup: removing {player.Name}@{player.World}");
                    }

                    matchingRecord.Party.RemoveAll(player =>
                        unmatchedPlayers.Contains(player));

                    log.Information(
                        $"Frontline cleanup: removed {unmatchedPlayers.Count} " +
                        $"players absent from the final scoreboard.");
                }
                else if (unmatchedPlayers.Count > 0)
                {
                    log.Warning(
                        $"Frontline cleanup skipped: only {updatedPlayers}/" +
                        $"{matchingRecord.Party.Count} players matched.");
                }
            }

            config.Save();

            log.Information(
                $"PvP scoreboard applied: " +
                $"{matchingRecord.DutyName} | " +
                $"Matched {updatedPlayers}/" +
                $"{matchingRecord.Party.Count} players.");

            return true;
        }
        private void OnFrameworkUpdate(IFramework framework)
        {
            var now = DateTime.UtcNow;

            if (now < nextPvpCheck || pendingPvpMatches.Count == 0)
                return;

            // Check due times once per second, without opening the database.
            nextPvpCheck = now.AddSeconds(1);

            foreach (var matchId in pendingPvpMatches.Keys.ToList())
            {
                if (now < pendingPvpMatches[matchId])
                    continue;

                try
                {
                    // If the mode wasn't available when the IPC arrived,
                    // resolve it now so Frontline still gets its 5-minute delay.
                    if (unknownPvpModes.Contains(matchId))
                    {
                        var isFrontline =
                            PvpTrackerDatabase.IsFrontlineMatch(matchId, log);

                        if (isFrontline == null)
                        {
                            pendingPvpMatches[matchId] =
                                now.Add(PvpRetryInterval);

                            continue;
                        }

                        unknownPvpModes.Remove(matchId);

                        if (isFrontline.Value)
                        {
                            var firstCheck =
                                pvpQueuedAt[matchId].AddMinutes(5);

                            if (now < firstCheck)
                            {
                                pendingPvpMatches[matchId] = firstCheck;
                                continue;
                            }
                        }
                    }

                    if (ApplyPvpMatch(matchId))
                    {
                        processedPvpMatches.Add(matchId);
                        pendingPvpMatches.Remove(matchId);
                        pvpQueuedAt.Remove(matchId);
                        unknownPvpModes.Remove(matchId);
                    }
                    else
                    {
                        pendingPvpMatches[matchId] =
                            now.Add(PvpRetryInterval);
                    }
                }
                catch (Exception ex)
                {
                    log.Error(
                        $"Failed to process PvP match {matchId}: {ex}");

                    pendingPvpMatches[matchId] =
                        now.Add(PvpRetryInterval);
                }
            }
        }
        public void Dispose()
        {
            framework.Update -= OnFrameworkUpdate;
            pvpMatchSaved.Unsubscribe(OnPvpMatchSaved);
        }
    }

}