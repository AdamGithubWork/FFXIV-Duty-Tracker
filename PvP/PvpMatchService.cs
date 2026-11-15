using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using MentorRouletteTracker.DutyTracking;
using System.Collections.Concurrent;

namespace MentorRouletteTracker.PvP
{
    public sealed class PvpMatchService : IDisposable
    {
        private readonly IPluginLog log;
        private readonly IFramework framework;
        private readonly ICallGateSubscriber<string, bool> pvpMatchSaved;
        private readonly Configuration config;
        private readonly Func<string, bool?> readMode;
        private readonly Func<string, PvpMatchResult?> readScoreboard;
        private readonly Dictionary<string, PendingMatch> pendingPvpMatches = new();
        private readonly HashSet<string> processedPvpMatches = new();
        private readonly Queue<string> processedMatchOrder = new();
        private const int ProcessedMatchLimit = 1024;
        private readonly ConcurrentQueue<(string MatchId, DateTime QueuedAt)> savedMatches = new();
        private readonly object lifecycleGate = new();
        private volatile bool disposed;
        private Task<List<PvpMatchReference>?>? restoreTask;
        private DateTime[]? restoreTimes;
        private bool restoreNeeded = true;
        private DateTime nextRestoreAttempt = DateTime.MinValue;
        private DateTime nextPvpCheck = DateTime.MinValue;
        private static readonly TimeSpan PvpRetryInterval =
            TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PvpRetryLimit =
            TimeSpan.FromMinutes(30);

        private enum PvpMode
        {
            Unknown,
            CrystallineConflict,
            Frontline
        }

        private sealed class PendingMatch
        {
            public DateTime QueuedAt { get; }
            public DateTime NextCheck { get; set; }
            public PvpMode Mode { get; set; }
            public bool AllowCatchUp { get; set; }
            public Task<bool?>? ModeRead { get; set; }
            public Task<PvpMatchResult?>? ScoreboardRead { get; set; }
            public bool ReadInProgress => ModeRead != null || ScoreboardRead != null;
            public DateTime FirstCheck => QueuedAt.AddMinutes(Mode == PvpMode.Frontline ? 5 : 1);

            public PendingMatch(DateTime queuedAt, PvpMode mode = PvpMode.Unknown)
            {
                QueuedAt = queuedAt;
                Mode = mode;
                NextCheck = FirstCheck;
            }
        }

        private static PvpMode GetMode(bool? isFrontline) => isFrontline switch
        {
            true => PvpMode.Frontline,
            false => PvpMode.CrystallineConflict,
            _ => PvpMode.Unknown
        };

        private void RememberProcessedMatch(string matchId)
        {
            if (!processedPvpMatches.Add(matchId))
                return;
            processedMatchOrder.Enqueue(matchId);
            if (processedMatchOrder.Count > ProcessedMatchLimit)
                processedPvpMatches.Remove(processedMatchOrder.Dequeue());
        }

        public PvpMatchService(
            IDalamudPluginInterface pluginInterface,
            IFramework framework,
            IPluginLog log,
            Configuration config)
        {
            this.framework = framework;
            this.log = log;
            this.config = config;
            readMode = matchId => PvpTrackerDatabase.IsFrontlineMatch(matchId, log);
            readScoreboard = matchId => PvpTrackerDatabase.GetMatch(matchId, log);

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
                var pending = new PendingMatch(queuedAt, GetMode(match.IsFrontline));
                pending.NextCheck = pending.FirstCheck > now ? pending.FirstCheck : now;
                // Older duties get one lookup for results that are already available.
                pending.AllowCatchUp = true;
                pendingPvpMatches.Add(match.MatchId, pending);
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
            if (disposed || !config.StorageAvailable || string.IsNullOrWhiteSpace(matchId))
                return;
            savedMatches.Enqueue((matchId, DateTime.UtcNow));
        }

        private void QueueSavedMatch(string matchId, DateTime queuedAt)
        {
            if (processedPvpMatches.Contains(matchId) ||
                pendingPvpMatches.ContainsKey(matchId))
                return;

            pendingPvpMatches.Add(matchId, new PendingMatch(queuedAt)
            {
                ModeRead = Task.Run(() => readMode(matchId))
            });
            log.Information($"PvP Tracker match queued: {matchId}. Identifying mode.");
        }

        private void CollectReadResults(DateTime now)
        {
            foreach (var (matchId, pending) in pendingPvpMatches.ToArray())
            {
                if (pending.ModeRead is { IsCompleted: true } modeRead)
                {
                    pending.ModeRead = null;
                    try
                    {
                        pending.Mode = GetMode(modeRead.GetAwaiter().GetResult());
                        if (pending.Mode != PvpMode.Unknown)
                        {
                            pending.NextCheck = pending.FirstCheck > now ? pending.FirstCheck : now;
                            log.Information($"PvP scoreboard {matchId}: mode identified as {(pending.Mode == PvpMode.Frontline ? "Frontline" : "CC")}.");
                        }
                        else
                        {
                            var retryAt = now.Add(PvpRetryInterval);
                            if (pending.NextCheck < retryAt)
                                pending.NextCheck = retryAt;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex, $"Failed to identify PvP match {matchId}.");
                        pending.NextCheck = now.Add(PvpRetryInterval);
                    }
                }

                if (pending.ScoreboardRead is not { IsCompleted: true } scoreboardRead)
                    continue;
                pending.ScoreboardRead = null;
                try
                {
                    var result = scoreboardRead.GetAwaiter().GetResult();
                    if (result != null && ApplyPvpMatchResult(result))
                    {
                        RememberProcessedMatch(matchId);
                        pendingPvpMatches.Remove(matchId);
                    }
                    else
                    {
                        if (result == null)
                            log.Warning($"PvP scoreboard {matchId}: match results not available.");
                        pending.NextCheck = now.Add(PvpRetryInterval);
                    }
                }
                catch (Exception ex)
                {
                    log.Error(ex, $"Failed to process PvP match {matchId}.");
                    pending.NextCheck = now.Add(PvpRetryInterval);
                }
            }
        }

        private bool ApplyPvpMatchResult(PvpMatchResult result)
        {
            if (disposed || !config.StorageAvailable)
                return false;
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
            lock (lifecycleGate)
                UpdatePendingMatches();
        }

        private void UpdatePendingMatches()
        {
            if (disposed || !config.StorageAvailable)
                return;

            var now = DateTime.UtcNow;

            while (savedMatches.TryDequeue(out var saved))
                QueueSavedMatch(saved.MatchId, saved.QueuedAt);
            RestorePendingPvpMatches(now);
            CollectReadResults(now);

            if (now < nextPvpCheck || pendingPvpMatches.Count == 0)
                return;

            nextPvpCheck = now.AddSeconds(1);

            foreach (var (matchId, pending) in pendingPvpMatches.ToArray())
            {
                if (pending.ReadInProgress)
                    continue;
                if (now - pending.QueuedAt >= PvpRetryLimit && !pending.AllowCatchUp)
                {
                    pendingPvpMatches.Remove(matchId);
                    RememberProcessedMatch(matchId);
                    log.Warning($"PvP scoreboard {matchId}: stopped retrying after 30 minutes without applying results.");
                    continue;
                }

                if (now < pending.NextCheck)
                    continue;

                pending.AllowCatchUp = false;
                if (pending.Mode == PvpMode.Unknown)
                    pending.ModeRead = Task.Run(() => readMode(matchId));
                else
                    pending.ScoreboardRead = Task.Run(() => readScoreboard(matchId));
            }
        }

        public void Dispose()
        {
            lock (lifecycleGate)
            {
                if (disposed)
                    return;
                disposed = true;
                framework.Update -= OnFrameworkUpdate;
                pvpMatchSaved.Unsubscribe(OnPvpMatchSaved);
            }
        }
    }

}
