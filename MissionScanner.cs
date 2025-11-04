using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    public sealed class MissionScanner
    {
        private readonly IGameGui _gameGui;
        private readonly IDataManager _data;
        private readonly IPluginLog _log;

        private static readonly string[] PrimaryGcAddons = { "GrandCompanySupplyList" };
        private static readonly string[] KnownDashboards = { "ContentsInfoDetail", "ContentsInfo" };

        public bool IsReady => _gameGui is not null && _data is not null;

        public MissionScanner(IGameGui gameGui, IDataManager dataManager, IPluginLog log)
        {
            _gameGui = gameGui;
            _data = dataManager;
            _log = log;
        }

        public bool IsSupplyWindowOpen() => GetAddonPtr(PrimaryGcAddons) != nint.Zero;

        public List<MissionItem> TryReadMissions(out string? status)
        {
            status = null;
            var results = new List<MissionItem>();

            var gcPtr = GetAddonPtr(PrimaryGcAddons);
            if (gcPtr == nint.Zero)
            {
                var dashPtr = GetAddonPtr(KnownDashboards);
                if (dashPtr != nint.Zero)
                {
                    status = "Detected the Contents Info dashboard. Open the GC Personnel Officer → “Supply & Provisioning Missions” list window.";
                    return results;
                }

                status = "Supply Mission list not detected. Open the GC “Supply & Provisioning Missions” window.";
                return results;
            }

            unsafe
            {
                var unit = (AtkUnitBase*)gcPtr;
                if (unit == null || unit->UldManager.NodeListCount == 0)
                {
                    status = "GC list detected, but no nodes were found.";
                    return results;
                }

                // 1) find an AtkComponentList (the rows/table)
                var list = FindFirstListComponent(unit);
                if (list == null)
                {
                    // No list found: dump a few texts to help mapping and return a friendly status.
                    var all = CollectText(unit);
                    if (all.Any(x => x.Contains("No more deliveries are being accepted today", StringComparison.OrdinalIgnoreCase)))
                    {
                        status = "No missions available today.";
                        return results;
                    }

                    status = "GC list detected, but rows component wasn’t found (UI variant?).";
                    return results;
                }

                // 2) iterate visible list items and scrape text from each row
                var rowCount = list->ListLength; // total rows (includes empty/headers)
                for (int i = 0; i < rowCount; i++)
                {
                    var item = list->GetItemRenderer(i);
                    if (item == null || !item->AtkResNode.IsVisible())
                        continue;

                    // Typical row: multiple children; one text is the item name, one is qty.
                    // We gather all texts in the row and then pick name + qty heuristically.
                    var texts = CollectTextFromNode(&item->AtkResNode);
                    if (texts.Count == 0) continue;

                    var qty = TryPickQty(texts, out var val) ? val : 0;
                    var name = TryPickName(texts);

                    if (!string.IsNullOrEmpty(name) && qty > 0)
                    {
                        results.Add(new MissionItem
                        {
                            Name = name,
                            Quantity = qty,
                            ItemId = 0 // we’ll resolve to itemId later via Lumina (by Name → Item sheet)
                        });
                    }
                }

                if (results.Count == 0)
                {
                    status = "GC list parsed, but no mission rows with (name, qty) found (maybe capped today).";
                    return results;
                }

                // Merge dupes by name (if the UI shows the same item in supply/provisioning)
                results = results
                    .GroupBy(r => r.Name ?? string.Empty)
                    .Select(g => new MissionItem
                    {
                        Name = g.Key,
                        Quantity = g.Sum(x => x.Quantity),
                        ItemId = 0
                    })
                    .ToList();

                status = $"Parsed {results.Count} mission item(s).";
                return results;
            }
        }

        // ---------------- helpers ----------------

        private nint GetAddonPtr(IEnumerable<string> names)
        {
            foreach (var n in names)
            {
                try
                {
                    var ptr = _gameGui.GetAddonByName(n, 1);
                    if (ptr != nint.Zero) return ptr;
                }
                catch { }
            }
            return nint.Zero;
        }

        private unsafe static AtkComponentList* FindFirstListComponent(AtkUnitBase* unit)
        {
            for (var i = 0; i < unit->UldManager.NodeListCount; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || !node->IsVisible()) continue;
                if (node->Type != NodeType.Component) continue;

                var compNode = (AtkComponentNode*)node;
                if (compNode->Component == null) continue;

                // ComponentType == List → AtkComponentList
                if (compNode->Component->Type == ComponentType.List)
                    return (AtkComponentList*)compNode->Component;
            }
            return null;
        }

        private unsafe static List<string> CollectText(AtkUnitBase* unit)
        {
            var list = new List<string>();
            for (var i = 0; i < unit->UldManager.NodeListCount; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || !node->IsVisible()) continue;
                if (node->Type != NodeType.Text) continue;

                var t = (AtkTextNode*)node;
                if (t->NodeText.StringPtr == null) continue;
                var s = t->NodeText.ToString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
            }
            return list;
        }

        private unsafe static List<string> CollectTextFromNode(AtkResNode* root)
        {
            var texts = new List<string>();
            Walk(root, n =>
            {
                if (n->Type == NodeType.Text)
                {
                    var t = (AtkTextNode*)n;
                    if (t->NodeText.StringPtr != null)
                    {
                        var s = t->NodeText.ToString();
                        if (!string.IsNullOrWhiteSpace(s))
                            texts.Add(s.Trim());
                    }
                }
            });
            return texts;
        }

        private unsafe static void Walk(AtkResNode* node, Action<AtkResNode*> visitor)
        {
            if (node == null) return;
            visitor(node);
            for (var c = node->ChildNode; c != null; c = c->NextSiblingNode)
                Walk(c, visitor);
        }

        private static bool TryPickQty(List<string> texts, out int qty)
        {
            // Look for an integer or "x N" in row texts; prefer the smallest positive number.
            foreach (var s in texts)
            {
                if (int.TryParse(s, out qty) && qty > 0) return true;
                var idx = s.IndexOf('x');
                if (idx >= 0 && int.TryParse(s[(idx + 1)..].Trim(), out qty) && qty > 0) return true;
            }
            qty = 0; return false;
        }

        private static string? TryPickName(List<string> texts)
        {
            // Heuristic: longest non-numeric string in the row is usually the item name.
            var nameish = texts
                .Where(t => !int.TryParse(t, out _))
                .OrderByDescending(t => t.Length)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(nameish) ? null : nameish;
        }
    }

    public sealed class MissionItem
    {
        public uint ItemId { get; set; }
        public string? Name { get; set; }
        public int Quantity { get; set; }
    }
}
