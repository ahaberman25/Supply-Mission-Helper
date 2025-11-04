using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    /// <summary>
    /// Scans the GC "Supply & Provisioning Missions" (ContentsInfoDetail) window.
    /// Uses stable NodeIds for headers/rows and fixed child indices per row:
    ///   - child #4  = item name
    ///   - child #7  = requested amount
    ///   - child #8  = on-hand amount
    /// </summary>
    public sealed unsafe class MissionScanner
    {
        private readonly IGameGui _gameGui;
        private readonly IDataManager _data;   // reserved for future Lumina lookups
        private readonly IPluginLog _log;

        // The GC mission list lives inside this addon.
        private static readonly string[] TargetAddons = { "ContentsInfoDetail" };

        // Header TextNode NodeIds (stable per your inspection)
        private const uint SUPPLY_HEADER_NODEID       = 3u;   // "Supply Missions"
        private const uint PROVISIONING_HEADER_NODEID = 16u;  // "Provisioning Missions"

        // Row Base Component NodeIds (stable per your inspection)
        private static readonly uint[] SUPPLY_ROW_NODEIDS       = { 6u, 7u, 8u, 9u, 10u, 11u, 12u, 13u };
        private static readonly uint[] PROVISIONING_ROW_NODEIDS = { 17u, 18u, 19u };

        // Child text indices inside a row component
        private const int CHILD_INDEX_NAME      = 4;
        private const int CHILD_INDEX_REQUESTED = 7;
        private const int CHILD_INDEX_ONHAND    = 8;

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
            if (unit == null || unit->UldManager.NodeList == null || unit->UldManager.NodeListCount == 0)
            {
                status = "UI detected, but node list was empty.";
                return results;
            }

            // Try to locate headers (for better status/diagnostics). Not strictly required for parsing.
            var haveHeaders = TryFindHeaderIndices(unit, out var supplyHeaderIdx, out var provisioningHeaderIdx);

            // Gather rows by NodeId (robust, avoids layout guessing)
            var supplyRows       = CollectRowComponentsByNodeId(unit, SUPPLY_ROW_NODEIDS);
            var provisioningRows = CollectRowComponentsByNodeId(unit, PROVISIONING_ROW_NODEIDS);

            _log.Info($"[SMH] Found {supplyRows.Count} supply-row nodes and {provisioningRows.Count} provisioning-row nodes by NodeId. HeadersFound={haveHeaders}");

            // Parse each row into Name / Requested / OnHand
            ParseRowComponents(supplyRows, "Supply", results);
            ParseRowComponents(provisioningRows, "Provisioning", results);

            if (results.Count == 0)
            {
                // Prefer an explicit capped message if present
                if (WindowContains(unit, "No more deliveries are being accepted today"))
                    status = "No missions available today.";
                else if (!haveHeaders)
                    status = "GC view detected, but section headers not found (UI variant?).";
                else
                    status = "No active mission rows detected (likely daily cap or empty).";
                return results;
            }

            // Merge duplicates defensively
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

        // ----------------- internals -----------------

        private static bool TryFindHeaderIndices(AtkUnitBase* unit, out int supplyHeaderIdx, out int provisioningHeaderIdx)
        {
            supplyHeaderIdx = -1;
            provisioningHeaderIdx = -1;

            // Prefer exact NodeId matches (most stable)
            for (var i = 0; i < unit->UldManager.NodeListCount; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || node->Type != NodeType.Text) continue;

                uint id = node->NodeId;
                if (id == SUPPLY_HEADER_NODEID)
                    supplyHeaderIdx = i;
                else if (id == PROVISIONING_HEADER_NODEID)
                    provisioningHeaderIdx = i;
            }

            // Fallback to text matching (future-proof, different locales may need localization)
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

            // Ensure order supply < provisioning
            if (supplyHeaderIdx >= 0 && provisioningHeaderIdx >= 0 &&
                supplyHeaderIdx > provisioningHeaderIdx)
            {
                (supplyHeaderIdx, provisioningHeaderIdx) = (provisioningHeaderIdx, supplyHeaderIdx);
            }

            return supplyHeaderIdx >= 0 && provisioningHeaderIdx >= 0;
        }

        private static List<nint> CollectRowComponentsByNodeId(AtkUnitBase* unit, IReadOnlyCollection<uint> wantedNodeIds)
        {
            var rows = new List<nint>();
            if (unit->UldManager.NodeList == null || unit->UldManager.NodeListCount == 0) return rows;

            for (var i = 0; i < unit->UldManager.NodeListCount; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || node->Type != NodeType.Component) continue;

                if (!wantedNodeIds.Contains(node->NodeId)) continue;

                var compNode = (AtkComponentNode*)node;
                if (compNode->Component == null) continue;

                rows.Add((nint)compNode);
            }

            return rows;
        }

        private static void ParseRowComponents(IEnumerable<nint> rows, string section, List<MissionItem> sink)
        {
            foreach (var rowPtr in rows)
            {
                var compNode = (AtkComponentNode*)rowPtr;   // cast back to pointer
                if (compNode == null || compNode->Component == null) continue;

                var uld = compNode->Component->UldManager;
                if (uld.NodeList == null || uld.NodeListCount == 0) continue;

                string? name = null;
                int requested = 0;
                int onHand = 0;

                // Read fixed child indices (fast path)
                ReadTextAt(uld, CHILD_INDEX_NAME, out var nameCandidate);
                if (!string.IsNullOrWhiteSpace(nameCandidate))
                    name = nameCandidate!.Trim();

                if (TryReadIntAt(uld, CHILD_INDEX_REQUESTED, out var req)) requested = req;
                if (TryReadIntAt(uld, CHILD_INDEX_ONHAND, out var have))   onHand   = have;

                // Fallback: scan all text nodes if needed (handles slight layout shifts)
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

                        // Ignore the fraction column like "0/20"
                        if (LooksLikeFraction(s)) continue;

                        if (requested == 0 && IsNumeric(s) &&
                            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                        {
                            requested = iv;
                            continue;
                        }

                        if (name is null || s.Length > name.Length)
                            name = s;
                    }
                }

                // If on-hand still unknown, try to infer another clean int different from requested
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
        public string? Name   { get; set; }
        public int     Quantity { get; set; } // requested
        public int     OnHand   { get; set; } // how many you currently have
        public uint    ItemId   { get; set; } // future: Lumina lookup
        public string? Section  { get; set; } // "Supply" or "Provisioning"
    }
}
