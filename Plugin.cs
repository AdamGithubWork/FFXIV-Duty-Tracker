using System.IO;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Linq;
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

        private readonly Configuration config;
        private readonly WindowSystem windowSystem = new("MentorRouletteTracker");
        private readonly DutyTrackerService tracker;
        private readonly FrontlineScoreboardService frontlineScoreboard;
        private readonly MainWindow mainWindow;

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
            this.log = log;

            var savedConfig = pluginInterface.GetPluginConfig() as Configuration;

            config = savedConfig ?? new Configuration();

            if (savedConfig == null)
            {
                config.TrackedContentTypeIds.AddRange(
                    new uint[] { 2, 3, 4, 5, 6, 21, 28, 30, 37 });
            }
            config.Initialize(pluginInterface);
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

            frontlineScoreboard = new FrontlineScoreboardService(
                log,
                sigScanner,
                gameInteropProvider);

            var exportDir = pluginInterface.GetPluginConfigDirectory();
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
            pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
            commandManager.RemoveHandler(CommandName);
            windowSystem.RemoveAllWindows();
            mainWindow.Dispose();
            frontlineScoreboard.Dispose();
            tracker.Dispose();
        }
    }
}