using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MentorRouletteTracker.DutyTracking;
using MentorRouletteTracker.PvP;

namespace MentorRouletteTracker
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Noumenon";
        private static readonly string[] CommandNames = ["/noumenon", "/nom", "/lastduty"];
        private readonly IDalamudPluginInterface pluginInterface;
        private readonly ICommandManager commandManager;
        private readonly IPluginLog log;
        private readonly Configuration config;
        private readonly WindowSystem windowSystem = new("MentorRouletteTracker");
        private readonly DutyTrackerService tracker;
        private readonly MainWindow mainWindow;
        private readonly DutyTimerWindow dutyTimerWindow;
        private readonly PvpMatchService pvpMatchService;
        private readonly List<string> registeredCommands = new();
        private bool drawSubscribed;
        private bool mainUiSubscribed;
        private bool configUiSubscribed;
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
                config.Initialize(pluginInterface, ex => log.Error(ex, "Noumenon history storage error."), createDatabase: savedConfig == null);
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

                foreach (var command in CommandNames)
                {
                    if (commandManager.AddHandler(command, new Dalamud.Game.Command.CommandInfo(OnCommand)
                        {
                            HelpMessage = command == "/lastduty"
                                ? "Open your latest saved duty in Noumenon."
                                : $"Open Noumenon. Recording is on by default. Use '{command} disarm' to turn it off or '{command} arm' to turn it back on. Your choice is saved.",
                        }))
                        registeredCommands.Add(command);
                    else
                        log.Warning($"Could not register {command}; another plugin is using it.");
                }

                pluginInterface.UiBuilder.Draw += DrawUi;
                drawSubscribed = true;
                pluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;
                mainUiSubscribed = true;
                pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
                configUiSubscribed = true;
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

        private void OnOpenConfigUi() => mainWindow.OpenSettings();

        private void OnCommand(string command, string args)
        {
            if (command == "/lastduty")
            {
                mainWindow.OpenLastDuty();
                return;
            }

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
                    log.Information("Noumenon: recording enabled.");
                    break;

                case "disarm":
                    tracker.Armed = false;
                    log.Information("Noumenon: recording disabled.");
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
            if (configUiSubscribed)
                Cleanup(() => pluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi, "settings callback");
            foreach (var command in registeredCommands)
                Cleanup(() => commandManager.RemoveHandler(command), $"{command} command handler");
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
