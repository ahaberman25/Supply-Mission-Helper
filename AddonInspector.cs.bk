using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    public unsafe class AddonInspector
    {
        private readonly IGameGui gameGui;
        private readonly IChatGui chatGui;

        public AddonInspector(IGameGui gameGui, IChatGui chatGui)
        {
            this.gameGui = gameGui;
            this.chatGui = chatGui;
        }

        public void InspectSupplyMissionAddon()
        {
            try
            {
                var addonPtr = gameGui.GetAddonByName("GrandCompanySupplyList");
                
                if (addonPtr == IntPtr.Zero)
                {
                    chatGui.Print("Supply Mission window is not open!");
                    return;
                }

                var addon = (AtkUnitBase*)addonPtr;
                
                if (addon == null || !addon->IsVisible)
                {
                    chatGui.Print("Supply Mission window is not visible!");
                    return;
                }

                chatGui.Print($"=== GrandCompanySupplyList Inspection ===");
                chatGui.Print($"NodeListCount: {addon->UldManager.NodeListCount}");
                chatGui.Print($"RootNode: {(IntPtr)addon->RootNode:X}");
                
                // Inspect the node tree
                InspectNodeTree(addon->RootNode, 0);
                
                chatGui.Print("=== Inspection Complete ===");
            }
            catch (Exception ex)
            {
                chatGui.Print($"Error inspecting addon: {ex.Message}");
            }
        }

        private void InspectNodeTree(AtkResNode* node, int depth)
        {
            if (node == null || depth > 10) return;

            var indent = new string(' ', depth * 2);
            var nodeType = node->Type;
            
            chatGui.Print($"{indent}Node: Type={nodeType}, Visible={node->NodeFlags.HasFlag(NodeFlags.Visible)}, ID={node->NodeId}");

            // Check if it's a component node (has children)
            if (nodeType == NodeType.Component)
            {
                var componentNode = (AtkComponentNode*)node;
                var component = componentNode->Component;
                
                if (component != null)
                {
                    chatGui.Print($"{indent}  Component: Type={component->UldManager.NodeListCount} nodes");
                    
                    // Inspect component's nodes
                    for (int i = 0; i < component->UldManager.NodeListCount; i++)
                    {
                        var childNode = component->UldManager.NodeList[i];
                        InspectNodeTree(childNode, depth + 1);
                    }
                }
            }
            // Check for text nodes
            else if (nodeType == NodeType.Text)
            {
                var textNode = (AtkTextNode*)node;
                var text = textNode->NodeText.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    chatGui.Print($"{indent}  Text: \"{text}\"");
                }
            }
            // Check for image nodes (icons)
            else if (nodeType == NodeType.Image)
            {
                var imageNode = (AtkImageNode*)node;
                chatGui.Print($"{indent}  Image: PartId={imageNode->PartId}");
            }

            // Traverse siblings
            var sibling = node->PrevSiblingNode;
            if (sibling != null)
            {
                InspectNodeTree(sibling, depth);
            }
        }

        public string DumpAddonStructure()
        {
            var sb = new StringBuilder();
            
            try
            {
                var addonPtr = gameGui.GetAddonByName("GrandCompanySupplyList");
                
                if (addonPtr == IntPtr.Zero)
                {
                    return "Supply Mission window is not open!";
                }

                var addon = (AtkUnitBase*)addonPtr;
                
                sb.AppendLine("=== GrandCompanySupplyList Structure ===");
                sb.AppendLine($"NodeListCount: {addon->UldManager.NodeListCount}");
                sb.AppendLine($"Visible: {addon->IsVisible}");
                sb.AppendLine();

                // Dump all nodes
                for (int i = 0; i < addon->UldManager.NodeListCount; i++)
                {
                    var node = addon->UldManager.NodeList[i];
                    if (node != null)
                    {
                        sb.AppendLine($"Node[{i}]: Type={node->Type}, ID={node->NodeId}, Visible={node->NodeFlags.HasFlag(NodeFlags.Visible)}");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error: {ex.Message}");
            }

            return sb.ToString();
        }
    }
}
