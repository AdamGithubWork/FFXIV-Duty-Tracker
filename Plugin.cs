
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

        private readonly Configuration config;
        private readonly WindowSystem windowSystem = new("MentorRouletteTracker");
        private readonly DutyTrackerService tracker;
        private readonly MainWindow mainWindow;

        private readonly ICallGateSubscriber<string, bool> pvpMatchSaved;

        private readonly Dictionary<string, DateTime> pendingPvpMatches = new();
        private readonly HashSet<string> processedPvpMatches = new();

        private DateTime nextPvpCheck = DateTime.MinValue;

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

            // PvP Tracker IPC
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

            commandManager.AddHandler(
                CommandName,
                new Dalamud.Game.Command.CommandInfo(OnCommand)
                {
                    HelpMessage =
                        "Open the Mentor Roulette Tracker window. Use '/mrt arm' or '/mrt disarm' to toggle logging.",
                });

            pluginInterface.UiBuilder.Draw += windowSystem.Draw;

            pluginInterface.UiBuilder.OpenMainUi += () =>
                mainWindow.IsOpen = true;
        }

        private void OnPvpMatchSaved(string matchId)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                return;

            if (processedPvpMatches.Contains(matchId))
                return;

            pendingPvpMatches[matchId] = DateTime.UtcNow;

            log.Information($"PvP Tracker match queued: {matchId}");
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (DateTime.UtcNow < nextPvpCheck)
                return;

            nextPvpCheck = DateTime.UtcNow.AddSeconds(5);

            foreach (var matchId in pendingPvpMatches.Keys.ToList())
            {
                try
                {
                    var result = PvpTrackerDatabaseTest.GetMatch(matchId, log);

                    // Scoreboard not ready yet.
                    if (result == null)
                        continue;

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

                    // Duty record might not have been finalized yet.
                    if (matchingRecord == null)
                        continue;

                    int updatedPlayers = 0;

                    foreach (var player in matchingRecord.Party)
                    {
                        var playerKey = $"{player.Name}@{player.World}";

                        if (result.PlayerDeaths.TryGetValue(
                                playerKey, out int deaths))
                        {
                            player.PvPScoreboardDeaths = deaths;
                            updatedPlayers++;
                        }
                    }

                    if (updatedPlayers == 0)
                    {
                        log.Warning(
                            $"PvP scoreboard {matchId}: no players matched.");
                        continue;
                    }

                    config.Save();

                    log.Information(
                        $"PvP scoreboard applied: {matchingRecord.DutyName} | " +
                        $"Matched {updatedPlayers}/{matchingRecord.Party.Count} players.");

                    processedPvpMatches.Add(matchId);
                    pendingPvpMatches.Remove(matchId);
                }
                catch (Exception ex)
                {
                    log.Error(
                        $"Failed to process PvP match {matchId}: {ex}");
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
                    log.Info("Mentor Roulette Tracker: armed.");
                    break;

                case "disarm":
                    tracker.Armed = false;
                    log.Info("Mentor Roulette Tracker: disarmed.");
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

            pluginInterface.UiBuilder.Draw -= windowSystem.Draw;

            commandManager.RemoveHandler(CommandName);

            windowSystem.RemoveAllWindows();

            mainWindow.Dispose();
            tracker.Dispose();
        }
    }
}
