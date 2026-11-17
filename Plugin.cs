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
        private bool commandRegistered;
        private bool drawSubscribed;
        private bool mainUiSubscribed;
        private bool disposed;

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

            try
            {
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

                commandRegistered = commandManager.AddHandler(
                    CommandName,
                    new Dalamud.Game.Command.CommandInfo(OnCommand)
                    {
                        HelpMessage =
                            "Open Duty Tracker. Use '/dt arm' to record duties or '/dt disarm' to stop recording.",
                    });

                pluginInterface.UiBuilder.Draw += DrawUi;
                drawSubscribed = true;
                pluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;
                mainUiSubscribed = true;
            }
            catch
            {
                Dispose();
                throw;
            }
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
            if (disposed)
                return;
            disposed = true;

            if (drawSubscribed)
                Cleanup(() => pluginInterface.UiBuilder.Draw -= DrawUi, "drawing callback");
            if (mainUiSubscribed)
                Cleanup(() => pluginInterface.UiBuilder.OpenMainUi -= OnOpenMainUi, "main window callback");
            if (commandRegistered)
                Cleanup(() => commandManager.RemoveHandler(CommandName), "command handler");
            Cleanup(windowSystem.RemoveAllWindows, "windows");
            Cleanup(() => pvpMatchService?.Dispose(), "PvP tracking");
            Cleanup(() => mainWindow?.Dispose(), "main window");
            Cleanup(() => tracker?.Dispose(), "duty tracking");
            Cleanup(config.DisposeHistory, "history storage; check the recovery file if saving failed");
        }

        private void Cleanup(Action action, string resource)
        {
            try { action(); }
            catch (Exception ex)
            {
                try { log.Error(ex, $"Could not clean up {resource}."); }
                catch { }
            }
        }
    }
}
