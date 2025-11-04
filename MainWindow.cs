using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using static Dalamud.Interface.Utility.ImGuiHelpers;

namespace SupplyMissionHelper
{
    public class MainWindow : Window, IDisposable
    {
        private SupplyMissionHelper Plugin;
        private List<SupplyMissionItem> scannedMissions = new();
        private string statusMessage = "Ready";

        public MainWindow(SupplyMissionHelper plugin) : base(
            "Supply Mission Helper")
        {
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(500, 400),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            this.Plugin = plugin;
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        public override void Draw()
        {
           ImGuiNET.ImGui.Text("Supply Mission Helper");
            ImGuiNET.ImGui.Spacing();

            // Button row
            if (ImGuiNET.ImGui.Button("Scan Supply Missions"))
            {
                ScanSupplyMissions();
            }
            
            ImGuiNET.ImGui.SameLine();
            
            if (ImGuiNET.ImGui.Button("Inspect Addon (Debug)"))
            {
                Plugin.Inspector.InspectSupplyMissionAddon();
            }

            ImGuiNET.ImGui.Spacing();
            
            // Status message
            if (!string.IsNullOrEmpty(statusMessage))
            {
                ImGuiNET.ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1.0f), statusMessage);
            }

            ImGuiNET.ImGui.Spacing();
            ImGuiNET.ImGui.Separator();
            ImGuiNET.ImGui.Spacing();

            // Display scanned missions
            if (scannedMissions.Count > 0)
            {
                ImGuiNET.ImGui.Text($"Found {scannedMissions.Count} supply missions:");
                ImGuiNET.ImGui.Spacing();

                if (ImGuiNET.ImGui.BeginTable("MissionsTable", 4, ImGuiNET.ImGuiTableFlags.Borders | ImGuiNET.ImGuiTableFlags.RowBg))
                {
                    ImGuiNET.ImGui.TableSetupColumn("Item Name");
                    ImGuiNET.ImGui.TableSetupColumn("Type");
                    ImGuiNET.ImGui.TableSetupColumn("Quantity");
                    ImGuiNET.ImGui.TableSetupColumn("HQ");
                    ImGuiNET.ImGui.TableHeadersRow();

                    foreach (var mission in scannedMissions)
                    {
                        ImGuiNET.ImGui.TableNextRow();
                        
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.Text(mission.ItemName);
                        
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.Text(mission.MissionType.ToString());
                        
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.Text($"{mission.QuantityNeeded}/{mission.QuantityRequested}");
                        
                        ImGuiNET.ImGui.TableNextColumn();
                        ImGuiNET.ImGui.Text(mission.IsHighQuality ? "Yes" : "No");
                    }

                    ImGuiNET.ImGui.EndTable();
                }
            }
            else
            {
                ImGuiNET.ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "No missions scanned yet. Open the Grand Company Supply window and click 'Scan Supply Missions'.");
            }

            ImGuiNET.ImGui.Spacing();
            ImGuiNET.ImGui.Separator();
            ImGuiNET.ImGui.Spacing();

            // Configuration options
            ImGuiNET.ImGui.Text("Options:");
            if (ImGuiNET.ImGui.Checkbox("Show Raw Materials Only", ref Plugin.Configuration.ShowRawMaterialsOnly))
            {
                Plugin.Configuration.Save();
            }

            if (ImGuiNET.ImGui.Checkbox("Include Gathering Locations", ref Plugin.Configuration.IncludeGatheringLocations))
            {
                Plugin.Configuration.Save();
            }
        }

        private void ScanSupplyMissions()
        {
            // Check if window is open
            if (!Plugin.Scanner.IsSupplyMissionWindowOpen())
            {
                statusMessage = "❌ Please open the Grand Company Supply Mission window first!";
                scannedMissions.Clear();
                return;
            }

            // Scan the missions
            scannedMissions = Plugin.Scanner.ScanSupplyMissions();

            if (scannedMissions.Count > 0)
            {
                statusMessage = $"✓ Successfully scanned {scannedMissions.Count} missions!";
            }
            else
            {
                statusMessage = "⚠ No missions found. The scanning logic needs to be implemented.";
            }
        }
    }
}