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
        private readonly IDataManager _data; // reserved for future Item/Recipe lookups
        private readonly IPluginLog _log;

        // Your client renders the GC list inside this addon.
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
            if (unit == null || unit->UldManager.NodeListCount == 0 || unit->UldManager.NodeList == null)
            {
                status = "UI detected, but node list was empty.";
                return results;
            }

            // Locate section headers by text (stable across clients)
            if (!TryFindHeaderIndices(unit, out var supplyHeaderIdx, out var provisioningHeaderIdx))
            {
                status = "GC window found, but headers not found (UI variant?).";
                return results;
            }

            // Collect row components between the headers
            var supplyRows       = CollectRowComponentsBetween(unit, supplyHeaderIdx, provisioningHeaderIdx);
            var provisioningRows = CollectRowComponentsBetween(unit, provisioningHeaderIdx, unit->UldManager.NodeListCount);


            // Parse with precise child indices (name=#4, requested=#7, onHand=#8)
            ParseRowComponents(supplyRows, "Supply", results);
            ParseRowComponents(provisioningRows, "Provisioning", results);

            if (results.Count == 0)
            {
                if (WindowContains(unit, "No more deliveries are being accepted today"))
                    status = "No missions available today.";
                else
                    status = "GC view detected, but no rows were parsed (layout changed?).";
                return results;
            }

            // Merge dupes defensively
            results = results
                .GroupBy(r => r.Name ?? string.Empty)
                .Select(g => new MissionItem
                {
                    Name     = g.Key,
                    Quantity = g.Sum(x => x.Quantity),
                    OnHand   = g.Sum(x => x.OnHand),
                    ItemId   = 0,
                    Section  = g.First().Section
                })
                .ToList();

            status = $"Parsed {results.Count} mission item(s).";
            return results;
        }

        // ---------- internals ----------

        // At top of the class (near fields), you can keep these constants:
        private const ushort SUPPLY_HEADER_NODEID       = 3;   // "Supply Missions"
        private const ushort PROVISIONING_HEADER_NODEID = 16;  // "Provisioning Missions"

        private static unsafe bool TryFindHeaderIndices(AtkUnitBase* unit,
            out int supplyHeaderIdx, out int provisioningHeaderIdx)
        {
            supplyHeaderIdx = -1;
            provisioningHeaderIdx = -1;

            // 1) Prefer exact NodeId match (stable across clients)
            for (var i = 0; i < unit->UldManager.NodeListCount; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || node->Type != NodeType.Text) continue;

                // NOTE: field name is NodeId
                ushort id = node->NodeId;

                if (id == SUPPLY_HEADER_NODEID)
                    supplyHeaderIdx = i;
                else if (id == PROVISIONING_HEADER_NODEID)
                    provisioningHeaderIdx = i;
            }

            // 2) Fallback to text matching if NodeIds weren’t found
            if (supplyHeaderIdx < 0 || provisioningHeaderIdx < 0)
            {
                for (var i = 0; i < unit->UldManager.NodeListCount; i++)
                {
                    var node = unit->UldManager.NodeList[i];
                    if (node == null || node->Type != NodeType.Text) continue;

                    var t = (AtkTextNode*)node;
                    if (t->NodeText.StringPtr == null) continue;

                    var s = t->NodeText.ToString().Trim();
                    if (supplyHeaderIdx < 0 &&
                        s.Equals("Supply Missions", StringComparison.OrdinalIgnoreCase))
                        supplyHeaderIdx = i;

                    if (provisioningHeaderIdx < 0 &&
                        s.Equals("Provisioning Missions", StringComparison.OrdinalIgnoreCase))
                        provisioningHeaderIdx = i;
                }
            }

            if (supplyHeaderIdx >= 0 && provisioningHeaderIdx >= 0 &&
                supplyHeaderIdx > provisioningHeaderIdx)
                (supplyHeaderIdx, provisioningHeaderIdx) = (provisioningHeaderIdx, supplyHeaderIdx);

            return supplyHeaderIdx >= 0 && provisioningHeaderIdx >= 0;
        }



        private static List<nint> CollectRowComponentsBetween(AtkUnitBase* unit, int startExclusive, int endExclusive)
        {
            var rows = new List<nint>();
            var max  = unit->UldManager.NodeListCount;

            startExclusive = Math.Clamp(startExclusive, -1, max);
            endExclusive   = Math.Clamp(endExclusive, 0,  max);

            for (var i = startExclusive + 1; i < endExclusive; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null) continue;

                // stop if another header text is encountered
                if (node->Type == NodeType.Text)
                {
                    var t = (AtkTextNode*)node;
                    if (t->NodeText.StringPtr != null)
                    {
                        var s = t->NodeText.ToString().Trim();
                        if (s.Equals("Supply Missions", StringComparison.OrdinalIgnoreCase) ||
                            s.Equals("Provisioning Missions", StringComparison.OrdinalIgnoreCase))
                            break;
                    }
                }

                if (node->Type != NodeType.Component) continue;
                var compNode = (AtkComponentNode*)node;
                if (compNode->Component == null) continue;

                rows.Add((nint)compNode); // store the pointer as nint
            }

            return rows;
        }

        private static void ParseRowComponents(IEnumerable<nint> rows, string section, List<MissionItem> sink)
        {
            foreach (var rowPtr in rows)
            {
                var compNode = (AtkComponentNode*)rowPtr;   // cast back to pointer
                var comp = compNode->Component;
                var uld  = comp->UldManager;
                if (uld.NodeList == null || uld.NodeListCount == 0) continue;

                string? name = null;
                int requested = 0;
                int onHand = 0;

                ReadTextAt(uld, 4, out var nameCandidate);
                if (!string.IsNullOrWhiteSpace(nameCandidate))
                    name = nameCandidate!.Trim();

                if (TryReadIntAt(uld, 7, out var req))  requested = req;
                if (TryReadIntAt(uld, 8, out var have)) onHand   = have;

                // Fallback scan if needed
                if (name is null || requested == 0)
                {
                    for (var j = 0; j < uld.NodeListCount; j++)
                    {
                        var n = uld.NodeList[j];
                        if (n == null || n->Type != NodeType.Text) continue;

                        var t = (AtkTextNode*)n;
                        if (t->NodeText.StringPtr == null) continue;

                        var s = t->NodeText.ToString().Trim();
                        if (string.IsNullOrWhiteSpace(s)) continue;
                        if (LooksLikeFraction(s)) continue;

                        if (requested == 0 && IsNumeric(s) &&
                            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                        {
                            requested = iv; continue;
                        }

                        if (name is null || s.Length > name.Length) name = s;
                    }
                }

                if (onHand == 0)
                {
                    for (var j = 0; j < uld.NodeListCount; j++)
                    {
                        var n = uld.NodeList[j];
                        if (n == null || n->Type != NodeType.Text) continue;

                        var t = (AtkTextNode*)n;
                        if (t->NodeText.StringPtr == null) continue;

                        var s = t->NodeText.ToString().Trim();
                        if (string.IsNullOrWhiteSpace(s) || LooksLikeFraction(s)) continue;

                        if (IsNumeric(s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                        {
                            if (iv != requested) { onHand = iv; break; }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(name) && requested > 0)
                {
                    sink.Add(new MissionItem
                    {
                        Name     = name,
                        Quantity = requested,
                        OnHand   = onHand,
                        ItemId   = 0,
                        Section  = section
                    });
                }
            }
        }

        private static bool WindowContains(AtkUnitBase* unit, string needle)
        {
            for (var i = 0; i < unit->UldManager.NodeListCount; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || node->Type != NodeType.Text) continue;

                var t = (AtkTextNode*)node;
                if (t->NodeText.StringPtr == null) continue;

                var s = t->NodeText.ToString();
                if (!string.IsNullOrEmpty(s) &&
                    s.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static void ReadTextAt(AtkUldManager uld, int index, out string? text)
        {
            text = null;
            if (index < 0 || index >= uld.NodeListCount) return;
            var n = uld.NodeList[index];
            if (n == null || n->Type != NodeType.Text) return;
            var t = (AtkTextNode*)n;
            if (t->NodeText.StringPtr == null) return;
            text = t->NodeText.ToString();
        }

        private static bool TryReadIntAt(AtkUldManager uld, int index, out int value)
        {
            value = 0;
            if (index < 0 || index >= uld.NodeListCount) return false;
            var n = uld.NodeList[index];
            if (n == null || n->Type != NodeType.Text) return false;
            var t = (AtkTextNode*)n;
            if (t->NodeText.StringPtr == null) return false;
            var s = t->NodeText.ToString().Trim();
            if (string.IsNullOrWhiteSpace(s) || !IsNumeric(s)) return false;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool LooksLikeFraction(string s)
        {
            var slash = s.IndexOf('/');
            if (slash <= 0 || slash >= s.Length - 1) return false;
            var left  = s.AsSpan(0, slash).Trim();
            var right = s.AsSpan(slash + 1).Trim();
            return int.TryParse(left,  NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                   int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        private static bool IsNumeric(string s)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    public sealed class MissionItem
    {
        public string? Name { get; set; }
        public int     Quantity { get; set; } // requested
        public int     OnHand   { get; set; } // how many you currently have
        public uint    ItemId   { get; set; } // future: Lumina lookup
        public string? Section  { get; set; } // "Supply" or "Provisioning"
    }
}
