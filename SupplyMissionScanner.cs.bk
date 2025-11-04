using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    public unsafe class SupplyMissionScanner
    {
        private readonly IGameGui gameGui;
        private readonly IDataManager dataManager;

        public SupplyMissionScanner(IGameGui gameGui, IDataManager dataManager)
        {
            this.gameGui = gameGui;
            this.dataManager = dataManager;
        }

        public List<SupplyMissionItem> ScanSupplyMissions()
        {
            var missions = new List<SupplyMissionItem>();

            try
            {
                // The Supply Mission window is called "GrandCompanySupplyList"
                var addonPtr = gameGui.GetAddonByName("GrandCompanySupplyList", 1);
                
                if (addonPtr == IntPtr.Zero)
                {
                    // Window is not open
                    return missions;
                }

                var addon = (AtkUnitBase*)addonPtr;
                
                if (addon == null || !addon->IsVisible)
                {
                    return missions;
                }

                // Parse the addon to extract supply mission items
                missions = ParseSupplyMissionAddon(addon);
            }
            catch (Exception ex)
            {
                // Log error - we'll add proper logging later
                Console.WriteLine($"Error scanning supply missions: {ex.Message}");
            }

            return missions;
        }

        private List<SupplyMissionItem> ParseSupplyMissionAddon(AtkUnitBase* addon)
        {
            var missions = new List<SupplyMissionItem>();

            try
            {
                // The Supply Mission list contains multiple components
                // We need to iterate through the list items
                
                // The addon structure has a component list we need to traverse
                if (addon->UldManager.NodeListCount < 1)
                {
                    return missions;
                }

                // The list typically starts at a specific node
                // We need to find the list component that contains the items
                var rootNode = addon->RootNode;
                if (rootNode == null) return missions;

                // Look for list items - these are typically in a component list
                // The exact structure may vary, so we'll need to inspect it
                // For now, let's try to find nodes that contain item information
                
                missions = ParseSupplyMissionNodes(addon);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing supply mission addon: {ex.Message}");
            }

            return missions;
        }

        private List<SupplyMissionItem> ParseSupplyMissionNodes(AtkUnitBase* addon)
        {
            var missions = new List<SupplyMissionItem>();

            try
            {
                // The GrandCompanySupplyList addon has a specific structure
                // We need to iterate through the visible list items
                
                // Typically the list has multiple entries, and we need to read:
                // - Item ID
                // - Item Name
                // - Quantity Requested
                // - Quantity Completed (current progress)
                // - Whether it's HQ required
                
                // Let's try to access the list data through the addon's components
                // The addon usually has numbered nodes that represent each list entry
                
                for (int i = 0; i < 20; i++) // Max 20 possible entries (supply + provisioning)
                {
                    var item = TryParseListItem(addon, i);
                    if (item != null)
                    {
                        missions.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing nodes: {ex.Message}");
            }

            return missions;
        }

        private SupplyMissionItem? TryParseListItem(AtkUnitBase* addon, int index)
        {
            try
            {
                // This is where we need to read the actual data from the addon
                // The structure will depend on the game's UI implementation
                
                // For now, this is a placeholder that we'll need to refine
                // based on actual testing with the game
                
                // We need to find the correct node indices for:
                // - Item icon/ID
                // - Item name text
                // - Quantity values
                
                // This will require some reverse engineering or use of existing
                // documentation about the addon structure
                
                return null; // TODO: Implement actual parsing
            }
            catch
            {
                return null;
            }
        }

        public bool IsSupplyMissionWindowOpen()
        {
            try
            {
                var addonPtr = gameGui.GetAddonByName("GrandCompanySupplyList", 1);
                if (addonPtr == IntPtr.Zero) return false;

                var addon = (AtkUnitBase*)addonPtr;
                return addon != null && addon->IsVisible;
            }
            catch
            {
                return false;
            }
        }
    }

    public class SupplyMissionItem
    {
        public uint ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int QuantityRequested { get; set; }
        public int QuantityCompleted { get; set; }
        public bool IsHighQuality { get; set; }
        public SupplyMissionType MissionType { get; set; }
        
        public int QuantityNeeded => Math.Max(0, QuantityRequested - QuantityCompleted);
    }

    public enum SupplyMissionType
    {
        Supply,      // Crafted items
        Provisioning // Gathered items
    }
}
