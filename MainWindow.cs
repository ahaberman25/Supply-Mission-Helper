using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace SupplyMissionHelper;

public sealed class MainWindow : Window
{
    private readonly Configuration _config;
    private readonly MissionScanner _scanner;
    private readonly RecipeCalculator _calculator;

    private string _status = "Idle.";
    private List<MissionItem> _lastScan = new();
    private List<MaterialRequirement> _lastMaterials = new();

    public MainWindow(Configuration config, MissionScanner scanner, RecipeCalculator calculator)
        : base("Supply Mission Helper")
    {
        _config = config;
        _scanner = scanner;
        _calculator = calculator;

        Size = new Vector2(520, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        RespectCloseHotkey = true;
    }

    public override void Draw()
    {
        // Phase 1: Welcome & Instructions
        ImGui.TextWrapped("Welcome to Supply Mission Helper!");
        ImGui.Separator();
        ImGui.TextWrapped("Use this tool to scan your Grand Company Supply Missions, compute required materials/recipes, and produce a shopping list.");

        if (ImGui.CollapsingHeader("How to use"))
        {
            ImGui.BulletText("Open your Grand Company Supply Mission window in-game.");
            ImGui.BulletText("Click 'Scan Missions' below to read current mission items and quantities.");
            ImGui.BulletText("Click 'Calculate Materials' to expand those items into required components.");
            ImGui.BulletText("Toggle options below to customize output.");
        }

        ImGui.Spacing();

        // Config toggles (persisted)
        bool showRaw = _config.ShowRawMaterialsOnly;
        if (ImGui.Checkbox("Show raw materials only", ref showRaw))
        {
            _config.ShowRawMaterialsOnly = showRaw;
            _config.Save();
        }

        bool includeLoc = _config.IncludeGatheringLocations;
        if (ImGui.Checkbox("Include gathering location hints", ref includeLoc))
        {
            _config.IncludeGatheringLocations = includeLoc;
            _config.Save();
        }

        ImGui.Spacing();

        // Phase 2: Scan controls
        ImGui.BeginDisabled(!_scanner.IsReady);
        if (ImGui.Button("Scan Missions", new Vector2(160, 0)))
        {
            _status = "Scanning…";
            try
            {
                if (!_scanner.IsSupplyWindowOpen())
                {
                    _status = "Supply Mission window not detected. Open the GC Supply Mission UI and try again.";
                    _lastScan.Clear();
                }
                else
                {
                    _lastScan = _scanner.TryReadMissions(out var scanMsg);
                    _status = scanMsg ?? $"Scanned {_lastScan.Count} mission item(s).";
                }
            }
            catch (Exception ex)
            {
                _status = $"Scan error: {ex.Message}";
                _lastScan.Clear();
            }
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Clear", new Vector2(100, 0)))
        {
            _lastScan.Clear();
            _lastMaterials.Clear();
            _status = "Cleared.";
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Status: ");
        ImGui.SameLine();
        ImGui.TextWrapped(_status);

        ImGui.Separator();

        // Phase 2: Scan results panel
        if (ImGui.CollapsingHeader("Scanned Missions", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (_lastScan.Count == 0)
            {
                ImGui.TextDisabled("No mission data yet.");
            }
            else
            {
                if (ImGui.BeginTable("missions", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("Item");
                    ImGui.TableSetupColumn("Item ID", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableHeadersRow();

                    foreach (var mi in _lastScan)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(mi.Name ?? "(Unknown)");
                        ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(mi.ItemId.ToString());
                        ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(mi.Quantity.ToString());
                    }

                    ImGui.EndTable();
                }
            }
        }

        ImGui.Spacing();

        // Phase 3: Calculate Materials (stubbed calculator)
        ImGui.BeginDisabled(_lastScan.Count == 0);
        if (ImGui.Button("Calculate Materials", new Vector2(200, 0)))
        {
            try
            {
                _lastMaterials = _calculator.Calculate(_lastScan, _config);
                _status = $"Calculated {_lastMaterials.Count} material row(s).";
            }
            catch (Exception ex)
            {
                _status = $"Calculation error: {ex.Message}";
                _lastMaterials.Clear();
            }
        }
        ImGui.EndDisabled();

        if (ImGui.CollapsingHeader("Shopping List (calculated)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (_lastMaterials.Count == 0)
            {
                ImGui.TextDisabled("No materials calculated yet.");
            }
            else
            {
                if (ImGui.BeginTable("materials", _config.IncludeGatheringLocations ? 4 : 3,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("Material");
                    ImGui.TableSetupColumn("Item ID", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("Total Qty", ImGuiTableColumnFlags.WidthFixed, 80);
                    if (_config.IncludeGatheringLocations)
                        ImGui.TableSetupColumn("Location Hint");
                    ImGui.TableHeadersRow();

                    foreach (var m in _lastMaterials)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(m.Name ?? "(Unknown)");
                        ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(m.ItemId.ToString());
                        ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(m.Quantity.ToString());
                        if (_config.IncludeGatheringLocations)
                        {
                            ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(m.LocationHint ?? "");
                        }
                    }

                    ImGui.EndTable();
                }
            }
        }

        // Footer
        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Reset to Defaults"))
        {
            _config.ShowRawMaterialsOnly = false;
            _config.IncludeGatheringLocations = false;
            _config.Save();
            _status = "Settings reset.";
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Tip: Open the GC Supply Mission UI before scanning.");
    }
}
