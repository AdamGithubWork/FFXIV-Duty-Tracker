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
            this.framework = framework;
            this.log = log;
            this.clientState = clientState;

            var savedConfig = pluginInterface.GetPluginConfig() as Configuration;

            config = savedConfig ?? new Configuration();

            // Sets all content types as tracked by default when no saved configuration exists.
            if (savedConfig == null)
            {
                config.TrackedContentTypeIds.AddRange([2, 3, 4, 5, 6, 21, 28, 30, 37]);
            }

            config.Initialize(pluginInterface);

            // Remove duplicate content type IDs, if any. 
            config.TrackedContentTypeIds = config.TrackedContentTypeIds.Distinct().ToList();

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

            pvpMatchService = new PvpMatchService(
                pluginInterface,
                framework,
                log,
                config);

            framework.Update += OnFrameworkUpdate;

            var exportDir = pluginInterface.GetPluginConfigDirectory();

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

        private void OnFrameworkUpdate(IFramework framework)
        {
            dutyTimerWindow.IsOpen =
                tracker.ActiveDuty != null &&
                !clientState.IsPvP;
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

            pluginInterface.UiBuilder.Draw -=
                windowSystem.Draw;

            commandManager.RemoveHandler(CommandName);

            windowSystem.RemoveAllWindows();
            pvpMatchService.Dispose();
            mainWindow.Dispose();
            tracker.Dispose();
        }
    }
}
