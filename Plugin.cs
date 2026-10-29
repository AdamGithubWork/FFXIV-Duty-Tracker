
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using MentorRouletteTracker.PvP;

namespace MentorRouletteTracker
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Mentor Roulette Tracker";

        private const string CommandName = "/mrt";

        private readonly IDalamudPluginInterface pluginInterface;
        private readonly ICommandManager commandManager;
        private readonly IPluginLog log;
        private readonly IFramework framework;
        private readonly IClientState clientState;

        private readonly Configuration config;

        private readonly WindowSystem windowSystem =
            new("MentorRouletteTracker");

        private readonly DutyTrackerService tracker;
        private readonly MainWindow mainWindow;
        private readonly DutyTimerWindow dutyTimerWindow;

        private readonly ICallGateSubscriber<string, bool> pvpMatchSaved;

        private readonly Dictionary<string, DateTime>
            pendingPvpMatches = new();

        private readonly HashSet<string>
            processedPvpMatches = new();

        private DateTime nextPvpCheck = DateTime.MinValue;

        private readonly Dictionary<string, DateTime> pvpQueuedAt = new();
        private readonly HashSet<string> unknownPvpModes = new();

        private static readonly TimeSpan PvpRetryInterval =
            TimeSpan.FromSeconds(30);

        public Plugin(
            IDalamudPluginInterface pluginInterface,
            ICommandManager commandManager,
            IClientState clientState,
            IDutyState dutyState,
            ICondition condition,
            IPartyList partyList,
            IObjectTable objectTable,
            IDataManager dataManager,
            IFramework framework,
            ISigScanner sigScanner,
            IGameInteropProvider gameInteropProvider,
            IPluginLog log)
        {
            this.pluginInterface = pluginInterface;
            this.commandManager = commandManager;
            this.framework = framework;
            this.log = log;
            this.clientState = clientState;

            var savedConfig =
                pluginInterface.GetPluginConfig() as Configuration;

            config = savedConfig ?? new Configuration();

            if (savedConfig == null)
            {
                config.TrackedContentTypeIds.AddRange(
                    new uint[] { 2, 3, 4, 5, 6, 21, 28, 30, 37 });
            }

            config.Initialize(pluginInterface);

            config.TrackedContentTypeIds =
                config.TrackedContentTypeIds.Distinct().ToList();

            config.Save();

            tracker = new DutyTrackerService(
                clientState,
                dutyState,
                condition,
                partyList,
                objectTable,
                dataManager,
                framework,
                log,
                config);

            // PvP Tracker IPC.
            pvpMatchSaved =
                pluginInterface.GetIpcSubscriber<string, bool>(
                    "PvpStats.OnMatchSaved");

            pvpMatchSaved.Subscribe(OnPvpMatchSaved);

            // Check pending PvP scoreboards every 5 seconds.
            framework.Update += OnFrameworkUpdate;

            var exportDir =
                pluginInterface.GetPluginConfigDirectory();

            Directory.CreateDirectory(exportDir);

            mainWindow = new MainWindow(
                config,
                tracker,
                exportDir,
                objectTable);

            windowSystem.AddWindow(mainWindow);

            dutyTimerWindow = new DutyTimerWindow(
                tracker,
                mainWindow,
                config);

            windowSystem.AddWindow(dutyTimerWindow);

            commandManager.AddHandler(
                CommandName,
                new Dalamud.Game.Command.CommandInfo(OnCommand)
                {
                    HelpMessage =
                        "Open the Mentor Roulette Tracker window. " +
                        "Use '/mrt arm' or '/mrt disarm' " +
                        "to toggle logging.",
                });

            pluginInterface.UiBuilder.Draw += windowSystem.Draw;

            pluginInterface.UiBuilder.OpenMainUi += () =>
                mainWindow.IsOpen = true;
        }

        // Returns true for Frontline, false for CC, or null if the match
        // has not yet appeared in PvP Tracker's database.
        private void OnPvpMatchSaved(string matchId)
        {
            if (string.IsNullOrWhiteSpace(matchId) ||
                processedPvpMatches.Contains(matchId) ||
                pendingPvpMatches.ContainsKey(matchId))
                return;

            var now = DateTime.UtcNow;

            var isFrontline =
                PvpTrackerDatabase.IsFrontlineMatch(matchId, log);

            var delay = isFrontline == true
                ? TimeSpan.FromMinutes(5)
                : TimeSpan.FromMinutes(1);

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
            // Only show the floating window during non-PvP duties.
            // Setting IsOpen to false prevents the empty black box.
            dutyTimerWindow.IsOpen =
                tracker.ActiveDuty != null &&
                !clientState.IsPvP;

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

        private void OnCommand(string command, string args)
        {
            args = args.Trim().ToLowerInvariant();

            switch (args)
            {
                case "arm":
                    tracker.Armed = true;

                    log.Info(
                        "Mentor Roulette Tracker: armed.");

                    break;

                case "disarm":
                    tracker.Armed = false;

                    log.Info(
                        "Mentor Roulette Tracker: disarmed.");

                    break;

                default:
                    mainWindow.IsOpen = !mainWindow.IsOpen;
                    break;
            }
        }

        public void Dispose()
        {
            framework.Update -= OnFrameworkUpdate;

            pvpMatchSaved.Unsubscribe(OnPvpMatchSaved);

            pluginInterface.UiBuilder.Draw -=
                windowSystem.Draw;

            commandManager.RemoveHandler(CommandName);

            windowSystem.RemoveAllWindows();

            mainWindow.Dispose();
            tracker.Dispose();
        }
    }
}
