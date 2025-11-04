using System;
using System.Numerics;
using Dalamud.Interface.Windowing;

namespace SupplyMissionHelper;

public class MainWindow : Window, IDisposable
{
    private Plugin Plugin { get; init; }

    public MainWindow(Plugin plugin)
        : base("Supply Mission Helper")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Placeholder - UI will be added once we confirm ImGui availability
    }
}
