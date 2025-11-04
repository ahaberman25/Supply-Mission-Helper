using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    public sealed unsafe class MissionScanner
    {
        private readonly IGameGui _gameGui;
        private readonly IPluginLog _log;

        public MissionScanner(IGameGui gameGui, IPluginLog log)
        {
            _gameGui = gameGui;
            _log = log;
        }

        private static readonly string[] TargetAddons = { "ContentsInfoDetail" };

        public bool IsSupplyWindowOpen()
        {
            foreach (var n in TargetAddons)
            {
                var ptr = _gameGui.GetAddonByName(n, 1);
                if (ptr != nint.Zero) return true;
                ptr = _gameGui.GetAddonByName(n, 0);
                if (ptr != nint.Zero) return true;
            }
            return false;
        }

        public List<MissionItem> TryReadMissions(out string status)
        {
            status = "";
            var list = new List<MissionItem>();

            nint ptr = nint.Zero;
            foreach (var n in TargetAddons)
            {
                ptr = _gameGui.GetAddonByName(n, 1);
                if (ptr == nint.Zero) ptr = _gameGui.GetAddonByName(n, 0);
                if (ptr != nint.Zero) break;
            }
            if (ptr == nint.Zero)
            {
                status = "GC mission window not found.";
                return list;
            }

            var unit = (AtkUnitBase*)ptr;
            if (unit == null || unit->UldManager.NodeListCount == 0)
            {
                status = "Addon found, but node list was empty.";
                return list;
            }

            // Supply nodes 6–13, Provisioning 17–19
            ReadMissionGroup(unit, 6, 13, list, "Supply");
            ReadMissionGroup(unit, 17, 19, list, "Provisioning");

            if (list.Count == 0)
            {
                status = "No missions detected (likely daily cap reached).";
            }
            else
            {
                status = $"Parsed {list.Count} mission items.";
            }

            return list;
        }

        private static void ReadMissionGroup(AtkUnitBase* unit, int first, int last, List<MissionItem> list, string section)
        {
            for (int i = first; i <= last; i++)
            {
                if (i >= unit->UldManager.NodeListCount) break;
                var node = unit->UldManager.NodeList[i];
                if (node == null) continue;

                var comp = node->GetAsAtkComponentNode();
                if (comp == null) continue;
                if (comp->Component == null) continue;

                var baseComp = comp->Component;
                var child = baseComp->UldManager.NodeListCount;
                if (child == 0) continue;

                string? name = null;
                int qty = 0;

                // loop text nodes inside the component
                for (var j = 0; j < baseComp->UldManager.NodeListCount; j++)
                {
                    var n = baseComp->UldManager.NodeList[j];
                    if (n == null) continue;
                    if (n->Type == NodeType.Text)
                    {
                        var t = n->GetAsAtkTextNode();
                        if (t == null || t->NodeText.StringPtr == null) continue;
                        var text = t->NodeText.ToString().Trim();

                        if (string.IsNullOrWhiteSpace(text)) continue;
                        if (int.TryParse(text, out var val))
                            qty = val;
                        else if (!text.Contains("/"))
                            name = text;
                    }
                }

                if (!string.IsNullOrEmpty(name))
                {
                    list.Add(new MissionItem
                    {
                        Name = name,
                        Quantity = qty,
                        ItemId = 0,
                        Section = section
                    });
                }
            }
        }
    }

    public sealed class MissionItem
    {
        public string? Name { get; set; }
        public int Quantity { get; set; }
        public uint ItemId { get; set; }
        public string? Section { get; set; }
    }
}
