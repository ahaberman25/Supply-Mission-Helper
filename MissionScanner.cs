using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    public sealed partial class MissionScanner
    {
        private readonly IGameGui _gameGui;
        private readonly IDataManager _data;
        private readonly IPluginLog _log;

        // Primary target: the actual Supply & Provisioning list
        private static readonly string[] PrimaryGcAddons = { "GrandCompanySupplyList" };

        // Secondary / dashboard UIs we **don’t** parse – we’ll nudge the user instead
        private static readonly string[] KnownDashboards = { "ContentsInfoDetail", "ContentsInfo" };

        // Filters to ignore header/label text from parsing
        private static readonly string[] HeaderKeywords =
        {
            "Supply Missions", "Provisioning Missions", "Requested", "Qty.", "Close"
        };

        public bool IsReady => _gameGui is not null && _data is not null;

        public MissionScanner(IGameGui gameGui, IDataManager dataManager, IPluginLog log)
        {
            _gameGui   = gameGui;
            _data      = dataManager;
            _log       = log;
        }

        public bool IsSupplyWindowOpen() => GetAddonPtr(PrimaryGcAddons) != nint.Zero;

        public List<MissionItem> TryReadMissions(out string? status)
        {
            status = null;
            var results = new List<MissionItem>();

            var gcPtr = GetAddonPtr(PrimaryGcAddons);
            if (gcPtr == nint.Zero)
            {
                // If the dashboard is up, guide the user
                var dashPtr = GetAddonPtr(KnownDashboards);
                if (dashPtr != nint.Zero)
                {
                    status = "Detected the Contents Info dashboard. Open GC Personnel Officer → “Supply & Provisioning Missions”.";
                    return results;
                }

                status = "Supply Mission list not detected. Open GC “Supply & Provisioning Missions”.";
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

                // Short-circuit: common “capped” text
                var allWinText = CollectText(unit);
                if (allWinText.Any(x => x.Contains("No more deliveries are being accepted today", StringComparison.OrdinalIgnoreCase)))
                {
                    status = "No missions available today.";
                    return results;
                }

                // 1) Find the list (table) component
                var list = FindFirstListComponent(unit);
                if (list == null)
                {
                    status = "GC list detected, but rows component wasn’t found (UI variant?).";
                    return results;
                }

                // 2) Walk each visible row, scrape child text nodes
                var rowCount = list->ListLength; // can include padding rows
                for (int i = 0; i < rowCount; i++)
                {
                    var renderer = list->GetItemRenderer(i);
                    if (renderer == null) continue;

                    var rowRoot = &renderer->AtkResNode;
                    if (!rowRoot->IsVisible()) continue;

                    var texts = CollectTextFromNode(rowRoot);
                    if (texts.Count == 0) continue;

                    // Filter obvious headers/labels/instructions
                    texts = texts.Where(t => !IsHeaderish(t)).ToList();
                    if (texts.Count == 0) continue;

                    // From the row texts, pick:
                    //  - Name: longest non-numeric string
                    //  - Requested Qty: first integer text, or right-hand number in an "N/M" pattern
                    var name = PickName(texts);
                    var qty  = PickRequestedQty(texts);

                    if (!string.IsNullOrWhiteSpace(name) && qty > 0)
                    {
                        results.Add(new MissionItem
                        {
                            Name     = name,
                            Quantity = qty,
                            ItemId   = 0 // later: resolve via Lumina Item sheet
                        });
                    }
                }

                if (results.Count == 0)
                {
                    status = "GC list parsed, but no (name, qty) rows found (possibly capped today).";
                    return results;
                }

                // Merge duplicates by name (just in case)
                results = results
                    .GroupBy(r => r.Name ?? string.Empty)
                    .Select(g => new MissionItem
                    {
                        Name     = g.Key,
                        Quantity = g.Sum(x => x.Quantity),
                        ItemId   = 0
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
                catch { /* ignored */ }
            }
            return nint.Zero;
        }

        private static bool IsHeaderish(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            foreach (var kw in HeaderKeywords)
                if (s.Equals(kw, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string? PickName(List<string> texts)
        {
            // pick the longest non-numeric, non “N/M” text as the name
            string? best = null;
            foreach (var t in texts)
            {
                if (LooksLikeNumber(t) || LooksLikeFraction(t)) continue;
                if (best == null || t.Length > best.Length) best = t;
            }
            return best;
        }

        private static int PickRequestedQty(List<string> texts)
        {
            // if a fraction "N/M" exists, prefer the right-hand side as the requested/target amount
            foreach (var t in texts)
            {
                if (TryParseFraction(t, out var left, out var right))
                    return Math.Max(left, right); // some UIs show "0/20" – target is 20
            }
            // else, take the first standalone integer found (often under "Requested")
            foreach (var t in texts)
            {
                if (int.TryParse(t, out var n) && n > 0) return n;
            }
            return 0;
        }

        private static bool LooksLikeNumber(string s) => int.TryParse(s, out _);

        private static bool LooksLikeFraction(string s) => FractionRegex().IsMatch(s);

        private static bool TryParseFraction(string s, out int left, out int right)
        {
            var m = FractionRegex().Match(s);
            if (m.Success &&
                int.TryParse(m.Groups["a"].Value, out left) &&
                int.TryParse(m.Groups["b"].Value, out right))
            {
                return true;
            }
            left = right = 0;
            return false;
        }

        [GeneratedRegex(@"^\s*(?<a>\d+)\s*\/\s*(?<b>\d+)\s*$")]
        private static partial Regex FractionRegex();

        private unsafe static AtkComponentList* FindFirstListComponent(AtkUnitBase* unit)
        {
            for (var i = 0; i < unit->UldManager.NodeListCount; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || !node->IsVisible()) continue;
                if (node->Type != NodeType.Component) continue;

                var compNode = (AtkComponentNode*)node;
                if (compNode->Component == null) continue;

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
            TraverseNode(root, texts);
            return texts;
        }

        private unsafe static void TraverseNode(AtkResNode* node, List<string> acc)
        {
            if (node == null) return;

            if (node->Type == NodeType.Text)
            {
                var t = (AtkTextNode*)node;
                if (t->NodeText.StringPtr != null)
                {
                    var s = t->NodeText.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        acc.Add(s.Trim());
                }
            }

            for (var child = node->ChildNode; child != null; child = child->NextSiblingNode)
                TraverseNode(child, acc);
        }
    }

    public sealed class MissionItem
    {
        public uint   ItemId   { get; set; }
        public string? Name    { get; set; }
        public int    Quantity { get; set; } // requested amount
    }
}
