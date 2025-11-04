using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    public sealed unsafe class MissionScanner
    {
        private readonly IGameGui _gameGui;
        private readonly IDataManager _data;   // reserved for Item/Recipe lookup later
        private readonly IPluginLog _log;

        // Your client renders the GC list inside ContentsInfoDetail
        private static readonly string[] TargetAddons = { "ContentsInfoDetail" };

        public bool IsReady => _gameGui is not null && _data is not null;

        public MissionScanner(IGameGui gameGui, IDataManager dataManager, IPluginLog log)
        {
            _gameGui = gameGui;
            _data    = dataManager;
            _log     = log;
        }

        public bool IsSupplyWindowOpen()
        {
            foreach (var n in TargetAddons)
            {
                var p = _gameGui.GetAddonByName(n, 1);
                if (p != nint.Zero) return true;
                p = _gameGui.GetAddonByName(n, 0);
                if (p != nint.Zero) return true;
            }
            return false;
        }

        public List<MissionItem> TryReadMissions(out string status)
        {
            status = string.Empty;
            var results = new List<MissionItem>();

            // Bind to ContentsInfoDetail
            nint ptr = nint.Zero;
            foreach (var n in TargetAddons)
            {
                ptr = _gameGui.GetAddonByName(n, 1);
                if (ptr == nint.Zero) ptr = _gameGui.GetAddonByName(n, 0);
                if (ptr != nint.Zero) break;
            }
            if (ptr == nint.Zero)
            {
                status = "Supply Mission list not detected. Open GC “Supply & Provisioning Missions”.";
                return results;
            }

            var unit = (AtkUnitBase*)ptr;
            if (unit == null || unit->UldManager.NodeListCount == 0)
            {
                status = "UI detected, but node list was empty.";
                return results;
            }

            // Read rows from the node ranges you identified
            ReadMissionGroup(unit, firstIndex: 6,  lastIndex: 13, results, "Supply");
            ReadMissionGroup(unit, firstIndex: 17, lastIndex: 19, results, "Provisioning");

            if (results.Count == 0)
            {
                status = "No missions detected (likely daily cap reached).";
            }
            else
            {
                // Merge duplicates by name just in case
                results = results
                    .GroupBy(r => r.Name ?? string.Empty)
                    .Select(g => new MissionItem
                    {
                        Name     = g.Key,
                        Quantity = g.Sum(x => x.Quantity),
                        ItemId   = 0,
                        Section  = g.First().Section
                    })
                    .ToList();

                status = $"Parsed {results.Count} mission item(s).";
            }

            return results;
        }

        private static void ReadMissionGroup(AtkUnitBase* unit, int firstIndex, int lastIndex, List<MissionItem> sink, string section)
        {
            // Clamp to available nodes
            var max = unit->UldManager.NodeListCount - 1;
            firstIndex = Math.Max(0, Math.Min(firstIndex, max));
            lastIndex  = Math.Max(0, Math.Min(lastIndex,  max));

            for (int i = firstIndex; i <= lastIndex; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null) continue;

                // We expect a Component node here
                if (node->Type != NodeType.Component) continue;
                var compNode = (AtkComponentNode*)node;
                if (compNode->Component == null) continue;

                var comp = compNode->Component;
                var uld  = comp->UldManager;
                if (uld.NodeListCount == 0 || uld.NodeList == null) continue;

                string? name = null;
                int requested = 0;

                // Scan child nodes inside this row component
                for (var j = 0; j < uld.NodeListCount; j++)
                {
                    var c = uld.NodeList[j];
                    if (c == null) continue;

                    if (c->Type == NodeType.Text)
                    {
                        var t = (AtkTextNode*)c;
                        if (t->NodeText.StringPtr == null) continue;

                        var s = t->NodeText.ToString().Trim();
                        if (string.IsNullOrWhiteSpace(s)) continue;

                        // Ignore fraction "0/20" style (Qty column)
                        if (LooksLikeFraction(s)) continue;

                        // If it's a standalone integer, it's usually the "Requested" column
                        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nval))
                        {
                            requested = nval;
                            continue;
                        }

                        // Otherwise treat as the best candidate for the item name
                        // Names can wrap; keep the longest non-numeric text we see
                        if (!IsNumeric(s))
                        {
                            if (name == null || s.Length > name.Length)
                                name = s;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(name) && requested > 0)
                {
                    sink.Add(new MissionItem
                    {
                        Name     = name,
                        Quantity = requested,
                        ItemId   = 0,
                        Section  = section
                    });
                }
            }
        }

        private static bool LooksLikeFraction(string s)
        {
            // very cheap check to avoid Regex/GeneratedRegex
            // formats like "0/20", " 1 / 3 "
            int slash = s.IndexOf('/');
            if (slash <= 0 || slash >= s.Length - 1) return false;

            var left  = s.Substring(0, slash).Trim();
            var right = s[(slash + 1)..].Trim();

            return IsNumeric(left) && IsNumeric(right);
        }

        private static bool IsNumeric(string s)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    public sealed class MissionItem
    {
        public string? Name { get; set; }
        public int     Quantity { get; set; }   // requested amount
        public uint    ItemId { get; set; }     // future: resolve via Lumina
        public string? Section { get; set; }    // "Supply" or "Provisioning"
    }
}
