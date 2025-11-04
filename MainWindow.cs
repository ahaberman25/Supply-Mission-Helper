using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace SupplyMissionHelper;

public class MainWindow : Window, IDisposable
{
    private Plugin Plugin { get; init; }

    public MainWindow(Plugin plugin)
        : base("Supply Mission Helper", ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Supply Mission Helper");
        ImGui.Spacing();

        ImGui.TextWrapped("Welcome to Supply Mission Helper!");
        ImGui.Spacing();

        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Instructions:");
        ImGui.BulletText("Open your Grand Company Supply Mission window");
        ImGui.BulletText("Click 'Scan Supply Missions' button (coming soon)");
        ImGui.BulletText("View the materials needed to complete your missions");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Configuration:");
        
        var showRaw = Plugin.Configuration.ShowRawMaterialsOnly;
        if (ImGui.Checkbox("Show Raw Materials Only", ref showRaw))
        {
            Plugin.Configuration.ShowRawMaterialsOnly = showRaw;
            Plugin.Configuration.Save();
        }

        var includeLocations = Plugin.Configuration.IncludeGatheringLocations;
        if (ImGui.Checkbox("Include Gathering Locations", ref includeLocations))
        {
            Plugin.Configuration.IncludeGatheringLocations = includeLocations;
            Plugin.Configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), "Status: Plugin loaded successfully!");
        ImGui.TextWrapped("Scanning functionality will be added in a future update.");
    }
}
