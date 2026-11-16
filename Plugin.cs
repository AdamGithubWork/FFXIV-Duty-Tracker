using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MentorRouletteTracker.DutyTracking;
using MentorRouletteTracker.PvP;

namespace MentorRouletteTracker
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Duty Tracker";
        private const string CommandName = "/dt";
        private readonly IDalamudPluginInterface pluginInterface;
        private readonly ICommandManager commandManager;
        private readonly IPluginLog log;
        private readonly Configuration config;
        private readonly WindowSystem windowSystem = new("MentorRouletteTracker");
        private readonly DutyTrackerService tracker;
        private readonly MainWindow mainWindow;
        private readonly DutyTimerWindow dutyTimerWindow;
        private readonly PvpMatchService pvpMatchService;

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
            IPluginLog log)
        {
            this.pluginInterface = pluginInterface;
            this.commandManager = commandManager;
            this.log = log;

            var savedConfig = pluginInterface.GetPluginConfig() as Configuration;
            config = savedConfig ?? new Configuration();

            config.Initialize(pluginInterface, ex => log.Error(ex, "Duty Tracker history storage error."), createDatabase: savedConfig == null);
            var trackedTypes = config.TrackedContentTypeIds.Distinct().ToList();
            var timerTypes = config.TimerVisibleContentTypeIds.Distinct().ToList();
            var columnSettingsChanged = config.UpgradePvpColumnSettings();
            var settingsChanged = savedConfig == null || columnSettingsChanged ||
                trackedTypes.Count != config.TrackedContentTypeIds.Count ||
                timerTypes.Count != config.TimerVisibleContentTypeIds.Count;
            config.TrackedContentTypeIds = trackedTypes;
            config.TimerVisibleContentTypeIds = timerTypes;
            if (config.StorageAvailable && settingsChanged)
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
                config,
                () => PvpAvailability.IsEnabled(pluginInterface));

            pvpMatchService = new PvpMatchService(
                pluginInterface,
                framework,
                log,
                config);

            var exportDir = pluginInterface.GetPluginConfigDirectory();
            Directory.CreateDirectory(exportDir);

            mainWindow = new MainWindow(
                config,
                tracker,
                exportDir,
                objectTable);
            windowSystem.AddWindow(mainWindow);
            if (!config.StorageAvailable)
                mainWindow.IsOpen = true;

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
                        "Open Duty Tracker. Use '/dt arm' to record duties or '/dt disarm' to stop recording.",
                });

            pluginInterface.UiBuilder.Draw += DrawUi;
            pluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;
        }

        private void DrawUi()
        {
            windowSystem.Draw();
            mainWindow.DrawIndependentWindows();
        }

        private void OnOpenMainUi()
        {
            mainWindow.IsOpen = true;
        }

        private void OnCommand(string command, string args)
        {
            args = args.Trim().ToLowerInvariant();

            switch (args)
            {
                case "arm":
                    if (!config.StorageAvailable)
                    {
                        mainWindow.IsOpen = true;
                        log.Warning("Duty recording is paused because history storage is unavailable.");
                        break;
                    }
                    tracker.Armed = true;
                    log.Information("Duty Tracker: recording enabled.");
                    break;

                case "disarm":
                    tracker.Armed = false;
                    log.Information("Duty Tracker: recording disabled.");
                    break;

                default:
                    mainWindow.IsOpen = !mainWindow.IsOpen;
                    break;
            }
        }

        public void Dispose()
        {
            pluginInterface.UiBuilder.Draw -= DrawUi;
            pluginInterface.UiBuilder.OpenMainUi -= OnOpenMainUi;
            commandManager.RemoveHandler(CommandName);
            windowSystem.RemoveAllWindows();
            pvpMatchService.Dispose();
            mainWindow.Dispose();
            tracker.Dispose();
            try { config.DisposeHistory(); }
            catch (Exception ex) { log.Error(ex, "Could not finish saving Duty Tracker history. Check the recovery file before restarting."); }
        }
    }
}
