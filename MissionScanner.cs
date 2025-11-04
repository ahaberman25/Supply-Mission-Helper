using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    // partial needed because we use [GeneratedRegex]
    public sealed partial class MissionScanner
    {
        private readonly IGameGui _gameGui;
        private readonly IDataManager _data;
        private readonly IPluginLog _log;

        // On your client, the GC list is rendered inside ContentsInfoDetail.
        private static readonly string[] TargetAddons = { "ContentsInfoDetail" };

        // Static labels we should ignore as headers
        private static readonly string[] HeaderKeywords =
        {
            "Supply Missions", "Provisioning Missions", "Requested", "Qty.", "Close"
        };

        public bool IsReady => _gameGui is not null && _data is not null;

        public MissionScanner(IGameGui gameGui, IDataManager dataManager, IPluginLog log)
        {
            _gameGui = gameGui;
            _data = dataManager;
            _log = log;
        }

        public bool IsSupplyWindowOpen()
        {
            var ptr = GetAddonPtr(TargetAddons);
            if (ptr == nint.Zero) return false;

            unsafe
            {
                var unit = (AtkUnitBase*)ptr;
                var texts = CollectWindowText(unit);
                return LooksLikeGcSupplyProvisioning(texts);
            }
        }

        public List<MissionItem> TryReadMissions(out string? status)
        {
            status = null;
            var results = new List<MissionItem>();

            var ptr = GetAddonPtr(TargetAddons);
            if (ptr == nint.Zero)
            {
                status = "Supply Mission list not detected. Open GC “Supply & Provisioning Missions”.";
                return results;
            }

            unsafe
            {
                var unit = (AtkUnitBase*)ptr;
                if (unit == null || unit->UldManager.NodeListCount == 0)
                {
                    status = "UI detected, but no nodes were found.";
                    return results;
                }

                var allTexts = CollectWindowText(unit);
                if (allTexts.Count == 0)
                {
                    status = "UI detected, but no text was readable.";
                    return results;
                }

                if (!LooksLikeGcSupplyProvisioning(allTexts))
                {
                    status = "Contents window is open, but it isn’t the GC Supply & Provisioning view.";
                    return results;
                }

                if (allTexts.Any(x => x.Contains("No more deliveries are being accepted today", StringComparison.OrdinalIgnoreCase)))
                {
                    status = "No missions available today.";
                    return results;
                }

                // Filter obvious headers/static labels
                var filtered = allTexts.Where(t => !IsHeaderish(t)).ToList();

                // State machine: we’re either in Supply, Provisioning, or None
                var section = Section.None;
                string? currentName = null;

                foreach (var t in allTexts)
                {
                    // Track section by raw stream (not filtered) so we don’t miss boundaries.
                    if (t.Equals("Supply Missions", StringComparison.OrdinalIgnoreCase)) { section = Section.Supply; currentName = null; continue; }
                    if (t.Equals("Provisioning Missions", StringComparison.OrdinalIgnoreCase)) { section = Section.Provisioning; currentName = null; continue; }
                }

                // Now parse from filtered stream so we skip the headers/labels.
                section = Section.None;
                currentName = null;

                foreach (var t in filtered)
                {
                    // Section changes still possible if the labels survived filtering (defensive)
                    if (t.Equals("Supply Missions", StringComparison.OrdinalIgnoreCase))        { section = Section.Supply; currentName = null; continue; }
                    if (t.Equals("Provisioning Missions", StringComparison.OrdinalIgnoreCase)) { section = Section.Provisioning; currentName = null; continue; }

                    // Skip Qty column (e.g., "0/20")
                    if (LooksLikeFraction(t)) continue;

                    // If it's an integer, treat it as the Requested qty for the active row
                    if (TryParseInt(t, out var asInt))
                    {
                        if (asInt > 0 && !string.IsNullOrEmpty(currentName) && section != Section.None)
                        {
                            results.Add(new MissionItem
                            {
                                Name     = currentName,
                                Quantity = asInt,
                                ItemId   = 0, // later: resolve via Lumina Item sheet by name
                            });
                            currentName = null;
                        }
                        continue;
                    }

                    // Otherwise, treat as a name fragment
                    if (IsLikelyName(t))
                    {
                        // Names can wrap: keep the longer string as the name for this row
                        if (string.IsNullOrEmpty(currentName) || t.Length > currentName.Length)
                            currentName = t;
                    }
                }

                if (results.Count == 0)
                {
                    status = "Parsed the GC view, but found no (name, requested) rows (possibly capped or UI variant).";
                    return results;
                }

                // Merge duplicates by name just in case
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
                    // Try both indices; some variants bind at 0
                    var p1 = _gameGui.GetAddonByName(n, 1);
                    if (p1 != nint.Zero) return p1;
                    var p0 = _gameGui.GetAddonByName(n, 0);
                    if (p0 != nint.Zero) return p0;
                }
                catch { /* ignore */ }
            }
            return nint.Zero;
        }

        private static bool LooksLikeGcSupplyProvisioning(IReadOnlyList<string> texts)
        {
            var hasSupply = texts.Any(t => t.Equals("Supply Missions", StringComparison.OrdinalIgnoreCase));
            var hasProv   = texts.Any(t => t.Equals("Provisioning Missions", StringComparison.OrdinalIgnoreCase));
            return hasSupply && hasProv;
        }

        private static bool IsHeaderish(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            foreach (var kw in HeaderKeywords)
                if (s.Equals(kw, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool TryParseInt(string s, out int val)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out val);

        private static bool LooksLikeFraction(string s) => FractionRegex().IsMatch(s);

        [GeneratedRegex(@"^\s*\d+\s*\/\s*\d+\s*$")]
        private static partial Regex FractionRegex();

        private static bool IsLikelyName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s.Length <= 1) return false;
            if (LooksLikeFraction(s)) return false;
            if (TryParseInt(s, out _)) return false;
            // exclude tiny label-like tokens
            if (s.Equals("A", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("B", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("C", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        private unsafe static List<string> CollectWindowText(AtkUnitBase* unit)
        {
            var list = new List<string>();

            // Walk flattened NodeList and take all Text nodes (no visibility checks needed)
            for (var i = 0; i < unit->UldManager.NodeListCount; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null) continue;

                if (node->Type == NodeType.Text)
                {
                    var t = (AtkTextNode*)node;
                    if (t->NodeText.StringPtr == null) continue;

                    var s = t->NodeText.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(s.Trim());
                }
            }

            return list;
        }

        private enum Section { None, Supply, Provisioning }
    }

    public sealed class MissionItem
    {
        public uint   ItemId   { get; set; }
        public string? Name    { get; set; }
        public int    Quantity { get; set; } // requested amount
    }
}
