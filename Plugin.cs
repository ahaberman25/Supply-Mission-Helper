using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace SupplyMissionHelper;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/supplymission";

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("SupplyMissionHelper");

    private IDalamudPluginInterface PluginInterface { get; init; }
    private ICommandManager CommandManager { get; init; }
    private MainWindow MainWindow { get; init; }

    private readonly IDataManager dataManager;
    private readonly ITimerManager timerManager;
    private readonly GcMissionScanner scanner;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IDataManager dataManager,
        IGameGui gameGui,
        ITimerManager timerManager // ⬅️ inject Timers service (API 13)
    )
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        this.dataManager = dataManager;
        this.timerManager = timerManager;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        scanner = new GcMissionScanner(this.timerManager, this.dataManager);

        MainWindow = new MainWindow(this, scanner);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Supply Mission Helper window"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) => ToggleConfigUI();

    private void DrawUI() => WindowSystem.Draw();

    private void ToggleConfigUI() => MainWindow.Toggle();
}
