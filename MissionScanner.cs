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

        // Primary: actual GC supply/provisioning list
        private static readonly string[] PrimaryGcAddons = { "GrandCompanySupplyList" };

        // Things we detect but don't parse (dashboard etc.)
        private static readonly string[] KnownDashboards = { "ContentsInfoDetail", "ContentsInfo" };

        // Static labels we should ignore
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

        public bool IsSupplyWindowOpen() => GetAddonPtr(PrimaryGcAddons) != nint.Zero;

        public List<MissionItem> TryReadMissions(out string? status)
        {
            status = null;
            var results = new List<MissionItem>();

            var gcPtr = GetAddonPtr(PrimaryGcAddons);
            if (gcPtr == nint.Zero)
            {
                // Friendly hint if the dashboard is open instead
                if (GetAddonPtr(KnownDashboards) != nint.Zero)
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

                // Collect ALL visible text from the window once, top-to-bottom.
                var allTexts = CollectWindowText(unit);
                if (allTexts.Count == 0)
                {
                    status = "GC list detected, but no text was readable.";
                    return results;
                }

                // Capped state short-circuit
                if (allTexts.Any(x => x.Contains("No more deliveries are being accepted today", StringComparison.OrdinalIgnoreCase)))
                {
                    status = "No missions available today.";
                    return results;
                }

                // Filter obvious headers/static labels
                var filtered = allTexts.Where(t => !IsHeaderish(t)).ToList();

                // Parse rows heuristically:
                //  - a "name-like" line (longer, non-numeric, not "N/M")
                //  - followed by a small positive integer (Requested)
                // We ignore the "Qty." column ("0/0" style fractions) for now.
                string? currentName = null;
                foreach (var t in filtered)
                {
                    if (LooksLikeFraction(t))             // "0/20" etc. (Qty. column) – skip for now
                        continue;

                    if (TryParseInt(t, out var asInt))    // a number-only line
                    {
                        if (asInt > 0 && !string.IsNullOrEmpty(currentName))
                        {
                            results.Add(new MissionItem
                            {
                                Name = currentName,
                                Quantity = asInt,
                                ItemId = 0 // later: resolve via Lumina Item sheet by name
                            });
                            currentName = null;            // reset for next row
                        }
                        continue;
                    }

                    // Otherwise treat as a candidate name
                    if (IsLikelyName(t))
                    {
                        // If we had a previous name that never picked up a number,
                        // just keep the longer of the two (handles wrapped names)
                        if (string.IsNullOrEmpty(currentName) || t.Length > currentName.Length)
                            currentName = t;
                    }
                }

                if (results.Count == 0)
                {
                    status = "GC list parsed, but no rows matched (maybe capped or UI variant).";
                    return results;
                }

                // Merge any duplicates (shouldn't really happen, but safe)
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

        // ---------- helpers (no pointer-type generic args; compiles cleanly) ----------

        private nint GetAddonPtr(IEnumerable<string> names)
        {
            foreach (var n in names)
            {
                try
                {
                    var ptr = _gameGui.GetAddonByName(n, 1);
                    if (ptr != nint.Zero) return ptr;
                }
                catch { /* ignore */ }
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
            // very small label-like tokens to exclude
            if (s.Equals("A", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("B", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("C", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        private unsafe static List<string> CollectWindowText(AtkUnitBase* unit)
        {
            var list = new List<string>();

            // Walk the NodeList (which is already a flattened, z-ordered list).
            for (var i = 0; i < unit->UldManager.NodeListCount; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null) continue;

                // Do a cheap visibility check using the node flags (no extension methods needed).
                // Bit 0x20 (32) is the "Visible" flag in AtkResNode flags.
                if ((node->Flags & 0x20) == 0) continue;

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
    }

    public sealed class MissionItem
    {
        public uint   ItemId   { get; set; }
        public string? Name    { get; set; }
        public int    Quantity { get; set; } // requested amount
    }
}
