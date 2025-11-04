using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace SupplyMissionHelper;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Supply Mission Helper";

    private readonly IDalamudPluginInterface _pi;
    private readonly ICommandManager _commands;
    private readonly IPluginLog _log;
    private readonly IGameGui _gameGui;
    private readonly IDataManager _data;

    private readonly WindowSystem _windows = new("SupplyMissionHelper");
    private readonly Configuration _config;
    private readonly MainWindow _mainWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog log,
        IGameGui gameGui,
        IDataManager dataManager)
    {
        _pi = pluginInterface;
        _commands = commandManager;
        _log = log;
        _gameGui = gameGui;
        _data = dataManager;

        _config = _pi.GetPluginConfig() as Configuration ?? new Configuration();
        _config.Initialize(_pi);

        // NEW: services
        var scanner = new MissionScanner(_gameGui, _data, _log);
        var calculator = new RecipeCalculator(_data, _log);

        // Window
        _mainWindow = new MainWindow(_config, scanner, calculator);
        _windows.AddWindow(_mainWindow);

        // Slash command
        _commands.AddHandler("/supplymission", new Dalamud.Game.Command.CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Supply Mission Helper window"
        });

        // Hooks
        _pi.UiBuilder.Draw += DrawUI;
        _pi.UiBuilder.OpenMainUi += ToggleMainWindow;
        _pi.UiBuilder.OpenConfigUi += ToggleMainWindow;

        _log.Info("Supply Mission Helper loaded.");
    }

    private void OnCommand(string cmd, string args) => ToggleMainWindow();

    private void ToggleMainWindow()
    {
        _mainWindow.IsOpen = !_mainWindow.IsOpen;
    }

    private void DrawUI() => _windows.Draw();

    public void Dispose()
    {
        _windows.RemoveAllWindows();
        _commands.RemoveHandler("/supplymission");
        _pi.UiBuilder.Draw -= DrawUI;
        _pi.UiBuilder.OpenMainUi -= ToggleMainWindow;
        _pi.UiBuilder.OpenConfigUi -= ToggleMainWindow;
        _log.Info("Supply Mission Helper disposed.");
    }
}
