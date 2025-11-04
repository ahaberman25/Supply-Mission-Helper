using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System;

namespace SupplyMissionHelper
{
    public class SupplyMissionHelper : IDalamudPlugin
    {
        public string Name => "Supply Mission Helper";
        private const string CommandName = "/supplymission";

        private IDalamudPluginInterface PluginInterface { get; init; }
        private ICommandManager CommandManager { get; init; }
        private IDataManager DataManager { get; init; }
        private IGameGui GameGui { get; init; }
        private IClientState ClientState { get; init; }
        private IChatGui ChatGui { get; init; }
        
        public Configuration Configuration { get; init; }
        public WindowSystem WindowSystem = new("SupplyMissionHelper");
        
        private MainWindow MainWindow { get; init; }
        public SupplyMissionScanner Scanner { get; init; }
        public AddonInspector Inspector { get; init; }

        public SupplyMissionHelper(
            IDalamudPluginInterface pluginInterface,
            ICommandManager commandManager,
            IDataManager dataManager,
            IGameGui gameGui,
            IClientState clientState,
            IChatGui chatGui)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;
            this.DataManager = dataManager;
            this.GameGui = gameGui;
            this.ClientState = clientState;
            this.ChatGui = chatGui;

            // Load configuration
            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            // Initialize services
            this.Scanner = new SupplyMissionScanner(this.GameGui, this.DataManager);
            this.Inspector = new AddonInspector(this.GameGui, this.ChatGui);

            // Initialize main window
            MainWindow = new MainWindow(this);
            WindowSystem.AddWindow(MainWindow);

            // Register command
            this.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the Supply Mission Helper window"
            });

            // Register UI draw
            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
        }

        public void Dispose()
        {
            this.WindowSystem.RemoveAllWindows();
            MainWindow.Dispose();
            this.CommandManager.RemoveHandler(CommandName);
        }

        private void OnCommand(string command, string args)
        {
            MainWindow.IsOpen = true;
        }

        private void DrawUI()
        {
            this.WindowSystem.Draw();
        }

        private void DrawConfigUI()
        {
            MainWindow.IsOpen = true;
        }
    }
}
