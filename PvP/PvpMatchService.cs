using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using MentorRouletteTracker.DutyTracking;

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
        private readonly HashSet<string> restoredPvpMatches = new();
        private Task<List<PvpMatchReference>?>? restoreTask;
        private DateTime[]? restoreTimes;
        private bool restoreNeeded = true;
        private DateTime nextRestoreAttempt = DateTime.MinValue;
        private DateTime nextPvpCheck = DateTime.MinValue;
        private static readonly TimeSpan PvpRetryInterval =
            TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PvpRetryLimit =
            TimeSpan.FromMinutes(30);

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
        private DutyRecord? FindMatchingRecord(DateTime startTime, bool includePending = false)
        {
            IEnumerable<DutyRecord> records = config.History.Records;
            if (includePending && config.History.PendingDuty is { } pending)
                records = records.Append(pending);
            return records.Where(record => record.DutyType == "PvP")
                .Select(record => (Record: record, Difference: Math.Abs(
                    (record.Timestamp.ToUniversalTime() - startTime.ToUniversalTime()).TotalSeconds)))
                .Where(candidate => candidate.Difference < 30)
                .OrderBy(candidate => candidate.Difference)
                .Select(candidate => candidate.Record)
                .FirstOrDefault();
        }

        private void RestorePendingPvpMatches(DateTime now)
        {
            if (!restoreNeeded || now < nextRestoreAttempt)
                return;

            if (restoreTask == null)
            {
                IEnumerable<DutyRecord> records = config.History.Records;
                if (config.History.PendingDuty is { } pending)
                    records = records.Append(pending);
                restoreTimes ??= records.Where(record => record.DutyType == "PvP" &&
                        record.Party.Any(player => !player.PvPScoreboardDeaths.HasValue))
                    .Select(record => record.Timestamp).ToArray();
                var times = restoreTimes;
                // Only the timestamp copy goes to the worker. History stays on the game thread.
                restoreTask = Task.Run(() => PvpTrackerDatabase.FindMatches(times, log));
                return;
            }
            if (!restoreTask.IsCompleted)
                return;

            var matches = restoreTask.GetAwaiter().GetResult();
            restoreTask = null;
            if (matches == null)
            {
                nextRestoreAttempt = now.Add(PvpRetryInterval);
                return;
            }
            foreach (var match in matches)
            {
                if (processedPvpMatches.Contains(match.MatchId) || pendingPvpMatches.ContainsKey(match.MatchId))
                    continue;
                var record = FindMatchingRecord(match.DutyStartTime, includePending: true);
                if (record == null || !record.Party.Any(player => !player.PvPScoreboardDeaths.HasValue))
                    continue;

                var queuedAt = ReferenceEquals(record, config.History.PendingDuty)
                    ? now : record.Timestamp.ToUniversalTime() + record.TimeInDuty;
                if (queuedAt > now)
                    queuedAt = now;
                var firstCheck = queuedAt.AddMinutes(match.IsFrontline ? 5 : 1);
                pvpQueuedAt[match.MatchId] = queuedAt;
                pendingPvpMatches[match.MatchId] = firstCheck > now ? firstCheck : now;
                // Older duties get one lookup for results that are already available.
                restoredPvpMatches.Add(match.MatchId);
                log.Information($"Resumed PvP scoreboard lookup for {record.DutyName}.");
            }

            // PvPStats may still be writing the match when the plugin reloads.
            restoreTimes = (restoreTimes ?? Array.Empty<DateTime>()).Where(time =>
            {
                if (matches.Any(match => Math.Abs(
                    (match.DutyStartTime.ToUniversalTime() - time.ToUniversalTime()).TotalSeconds) < 30))
                    return false;
                var record = FindMatchingRecord(time, includePending: true);
                return record != null && record.Party.Any(player => !player.PvPScoreboardDeaths.HasValue) &&
                    (ReferenceEquals(record, config.History.PendingDuty) ||
                     now - (record.Timestamp.ToUniversalTime() + record.TimeInDuty) < PvpRetryLimit);
            }).ToArray();
            restoreNeeded = restoreTimes.Length > 0;
            nextRestoreAttempt = now.Add(PvpRetryInterval);
        }

        private void OnPvpMatchSaved(string matchId)
        {
            if (!config.StorageAvailable)
                return;

            if (string.IsNullOrWhiteSpace(matchId) ||
                processedPvpMatches.Contains(matchId) ||
                pendingPvpMatches.ContainsKey(matchId))
                return;

            var now = DateTime.UtcNow;

            var isFrontline = PvpTrackerDatabase.IsFrontlineMatch(matchId, log);

            var delay = isFrontline == true ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(1);

            pvpQueuedAt[matchId] = now;
            pendingPvpMatches[matchId] = now.Add(delay);

            if (isFrontline == null)
                unknownPvpModes.Add(matchId);

            log.Information(
                $"PvP Tracker match queued: {matchId} | " +
                $"Mode: {(isFrontline == true ? "Frontline" : isFrontline == false ? "CC" : "Unknown")} | " +
                $"Checking in {delay.TotalMinutes:0} min.");
        }

        private bool ApplyPvpMatch(string matchId)
        {
            if (!config.StorageAvailable)
                return false;

            var result =
                PvpTrackerDatabase.GetMatch(matchId, log);

            if (result == null)
            {
                log.Warning(
                    $"PvP scoreboard {matchId}: " +
                    "match results not available.");

                return false;
            }

            return ApplyPvpMatchResult(result);
        }

        private bool ApplyPvpMatchResult(PvpMatchResult result)
        {
            var matchId = result.MatchId;
            var matchingRecord = FindMatchingRecord(result.DutyStartTime);

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

            if (result.IsFrontline)
            {
                var unmatchedPlayers = matchingRecord.Party
                    .Where(player =>
                        !result.PlayerDeaths.ContainsKey(
                            $"{player.Name}@{player.World}"))
                    .ToList();

                // A partial scoreboard may be missing players who were actually there.
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

            config.History.Save();

            log.Information(
                $"PvP scoreboard applied: " +
                $"{matchingRecord.DutyName} | " +
                $"Matched {updatedPlayers}/" +
                $"{matchingRecord.Party.Count} players.");

            return true;
        }
        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!config.StorageAvailable)
                return;

            var now = DateTime.UtcNow;

            RestorePendingPvpMatches(now);

            if (now < nextPvpCheck || pendingPvpMatches.Count == 0)
                return;

            nextPvpCheck = now.AddSeconds(1);

            foreach (var matchId in pendingPvpMatches.Keys.ToList())
            {
                if (now - pvpQueuedAt[matchId] >= PvpRetryLimit && !restoredPvpMatches.Contains(matchId))
                {
                    pendingPvpMatches.Remove(matchId);
                    pvpQueuedAt.Remove(matchId);
                    unknownPvpModes.Remove(matchId);
                    processedPvpMatches.Add(matchId);
                    log.Warning(
                        $"PvP scoreboard {matchId}: stopped retrying after 30 minutes without applying results.");
                    continue;
                }

                if (now < pendingPvpMatches[matchId])
                    continue;

                restoredPvpMatches.Remove(matchId);

                try
                {
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
