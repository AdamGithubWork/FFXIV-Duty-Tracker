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
        private readonly Func<string, PvpMode> readMode;
        private readonly Func<string, PvpMatchResult?> readScoreboard;
        private readonly Func<DateTime[], List<PvpMatchReference>?> findMatches;
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
        private int observedRecordCount;
        private DateTime nextRestoreAttempt = DateTime.MinValue;
        private DateTime nextPvpCheck = DateTime.MinValue;
        private static readonly TimeSpan PvpRetryInterval =
            TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PvpRetryLimit =
            TimeSpan.FromMinutes(30);

        private sealed class PendingMatch
        {
            public DateTime QueuedAt { get; }
            public DateTime NextCheck { get; set; }
            public PvpMode Mode { get; set; }
            public bool AllowCatchUp { get; set; }
            public Task<PvpMode>? ModeRead { get; set; }
            public Task<PvpMatchResult?>? ScoreboardRead { get; set; }
            public bool ReadInProgress => ModeRead != null || ScoreboardRead != null;
            public DateTime FirstCheck => QueuedAt.AddMinutes(Mode is PvpMode.Frontline or PvpMode.RivalWings ? 5 : 1);

            public PendingMatch(DateTime queuedAt, PvpMode mode = PvpMode.Unknown)
            {
                QueuedAt = queuedAt;
                Mode = mode;
                NextCheck = FirstCheck;
            }
        }

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
            readMode = matchId => PvpTrackerDatabase.GetMatchMode(matchId, log);
            observedRecordCount = config.StorageAvailable ? config.History.Records.Count : 0;
            readScoreboard = matchId => PvpTrackerDatabase.GetMatch(matchId, log);
            findMatches = times => PvpTrackerDatabase.FindMatches(times, log);

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
                restoreTimes ??= records.Where(NeedsPvpLookup)
                    .Select(record => record.Timestamp).ToArray();
                var times = restoreTimes;
                // Only the timestamp copy goes to the worker. History stays on the game thread.
                restoreTask = Task.Run(() => findMatches(times));
                return;
            }
            if (!restoreTask.IsCompleted)
                return;

            var matches = restoreTask.GetAwaiter().GetResult();
            restoreTask = null;
            if (matches == null)
            {
                restoreTimes = (restoreTimes ?? Array.Empty<DateTime>()).Where(time => CanKeepLooking(time, now)).ToArray();
                restoreNeeded = restoreTimes.Length > 0;
                nextRestoreAttempt = now.Add(PvpRetryInterval);
                return;
            }
            foreach (var match in matches)
            {
                if (processedPvpMatches.Contains(match.MatchId) || pendingPvpMatches.ContainsKey(match.MatchId))
                    continue;
                var record = FindMatchingRecord(match.DutyStartTime, includePending: true);
                if (record == null || !NeedsPvpLookup(record))
                    continue;

                var queuedAt = ReferenceEquals(record, config.History.PendingDuty)
                    ? now : record.Timestamp.ToUniversalTime() + record.TimeInDuty;
                if (queuedAt > now)
                    queuedAt = now;
                var pending = new PendingMatch(queuedAt, match.Mode);
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
                return CanKeepLooking(time, now);
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
                        pending.Mode = modeRead.GetAwaiter().GetResult();
                        if (pending.Mode != PvpMode.Unknown)
                        {
                            pending.NextCheck = pending.FirstCheck > now ? pending.FirstCheck : now;
                            log.Information($"PvP scoreboard {matchId}: mode identified as {pending.Mode}.");
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

        private static bool NeedsPvpLookup(DutyRecord record) => record.DutyType == "PvP" &&
            (record.PvPRosterComplete == true
                ? !record.PvPCombatStatsComplete || !record.PvPResultsComplete
                : record.PvPRosterComplete == false || record.Party.Any(player => !player.PvPScoreboardDeaths.HasValue));

        private bool CanKeepLooking(DateTime time, DateTime now)
        {
            var record = FindMatchingRecord(time, includePending: true);
            return record != null && NeedsPvpLookup(record) &&
                (ReferenceEquals(record, config.History.PendingDuty) ||
                 now - (record.Timestamp.ToUniversalTime() + record.TimeInDuty) < PvpRetryLimit);
        }

        private void DiscoverNewDuties()
        {
            var count = config.History.Records.Count;
            if (count == observedRecordCount)
                return;
            var start = count > observedRecordCount ? observedRecordCount : 0;
            observedRecordCount = count;
            var times = config.History.Records.Skip(start).Where(NeedsPvpLookup).Select(record => record.Timestamp).ToArray();
            if (times.Length == 0)
                return;
            restoreTimes = (restoreTimes ?? Array.Empty<DateTime>()).Concat(times).Distinct().ToArray();
            restoreNeeded = true;
            nextRestoreAttempt = DateTime.MinValue;
        }

        private bool ApplyPvpMatchResult(PvpMatchResult result)
        {
            if (disposed || !config.StorageAvailable || result.Mode == PvpMode.Unknown)
                return false;
            var record = FindMatchingRecord(result.DutyStartTime);
            if (record == null)
            {
                log.Warning($"PvP scoreboard {result.MatchId}: no matching duty record.");
                return false;
            }
            if (!string.IsNullOrWhiteSpace(result.RecorderName) && !string.IsNullOrWhiteSpace(record.RecorderName) &&
                !string.IsNullOrWhiteSpace(record.RecorderWorld) &&
                (result.RecorderName != record.RecorderName || result.RecorderWorld != record.RecorderWorld))
            {
                log.Warning($"PvP scoreboard {result.MatchId}: recorder does not match the duty.");
                return false;
            }

            var recorderKey = $"{record.RecorderName}@{record.RecorderWorld}";
            if (!result.Players.ContainsKey(recorderKey) &&
                !record.Party.Any(player => result.Players.ContainsKey($"{player.Name}@{player.World}")))
            {
                log.Warning($"PvP scoreboard {result.MatchId}: no recorded players matched the roster.");
                return false;
            }
            if (record.PvPRosterComplete == true && !NeedsPvpLookup(record))
                return true;

            var changed = false;
            if (record.PvPMode != result.Mode)
            {
                record.PvPMode = result.Mode;
                changed = true;
            }
            var byIdentity = record.Party.GroupBy(player => $"{player.Name}@{player.World}")
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            var localTeam = result.Players.GetValueOrDefault(recorderKey)?.Team;
            foreach (var (key, player) in result.Players)
            {
                bool? enemy = key == recorderKey ? false : !string.IsNullOrEmpty(localTeam) && player.Team.Length > 0
                    ? player.Team != localTeam : null;
                // Without a known team, importing an unfamiliar player could bypass the enemy setting.
                if (!record.TrackPvpEnemies && (enemy == true || enemy == null &&
                    (!byIdentity.TryGetValue(key, out var known) || known.All(snapshot => snapshot.PvPIsEnemy != false))))
                    continue;
                if (!byIdentity.TryGetValue(key, out var existing))
                {
                    var added = new PartyMemberSnapshot
                    {
                        Name = player.Name, World = player.World, Job = player.Job,
                        Race = "Unknown", Gender = "Unknown", Status = "Unknown",
                        PvPScoreboardDeaths = player.Deaths
                    };
                    record.Party.Add(added);
                    byIdentity.Add(key, existing = new List<PartyMemberSnapshot> { added });
                    changed = true;
                }
                foreach (var snapshot in existing)
                {
                    var before = (snapshot.PvPTeam, snapshot.PvPIsEnemy, snapshot.PvPDamageDealt, snapshot.PvPKills, snapshot.PvPAssists);
                    if (player.Placement.HasValue && snapshot.PvPPlacement != player.Placement)
                    {
                        snapshot.PvPPlacement = player.Placement;
                        changed = true;
                    }
                    if (player.Team.Length > 0)
                        snapshot.PvPTeam = player.Team;
                    snapshot.PvPIsEnemy = enemy ?? snapshot.PvPIsEnemy;
                    snapshot.PvPDamageDealt = player.DamageDealt ?? snapshot.PvPDamageDealt;
                    snapshot.PvPKills = player.Kills ?? snapshot.PvPKills;
                    snapshot.PvPAssists = player.Assists ?? snapshot.PvPAssists;
                    changed |= before != (snapshot.PvPTeam, snapshot.PvPIsEnemy, snapshot.PvPDamageDealt, snapshot.PvPKills, snapshot.PvPAssists);
                    if (player.Deaths.HasValue && snapshot.PvPScoreboardDeaths != player.Deaths)
                    {
                        snapshot.PvPScoreboardDeaths = player.Deaths;
                        changed = true;
                    }
                    if ((string.IsNullOrWhiteSpace(snapshot.Job) || snapshot.Job == "Unknown") && player.Job != "Unknown")
                    {
                        snapshot.Job = player.Job;
                        changed = true;
                    }
                }
            }

            if (result.Placement.HasValue && record.PvPPlacement != result.Placement)
            {
                record.PvPPlacement = result.Placement;
                changed = true;
            }
            var complete = result.RosterComplete && !string.IsNullOrEmpty(localTeam) &&
                result.Players.Values.All(player => player.Deaths.HasValue && player.DamageDealt.HasValue &&
                    player.Kills.HasValue && player.Assists.HasValue && player.Team.Length > 0);
            if (record.PvPCombatStatsComplete != complete)
            {
                record.PvPCombatStatsComplete = complete;
                changed = true;
            }
            var resultsComplete = result.RosterComplete && result.Players.Values.All(player => player.Placement.HasValue);
            if (record.PvPResultsComplete != resultsComplete)
            {
                record.PvPResultsComplete = resultsComplete;
                changed = true;
            }
            if (record.PvPRosterComplete != complete)
            {
                record.PvPRosterComplete = complete;
                changed = true;
            }
            if (changed)
            {
                config.History.Save();
                log.Information($"PvP scoreboard applied: {record.DutyName} | {result.Players.Count} scoreboard players, {record.Party.Count} recorded players.");
            }
            return complete && resultsComplete;
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

            DiscoverNewDuties();
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
