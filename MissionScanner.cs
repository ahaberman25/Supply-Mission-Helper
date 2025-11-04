using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    /// <summary>
    /// Scanner that reads GC "Supply & Provisioning Missions" from ContentsInfoDetail.
    /// Uses top-level NodeList *indices* (not NodeIds) to split sections, based on
    /// observed layout:
    ///   - One header (e.g., Provisioning) near the top (higher index)
    ///   - Then Supply rows (indices strictly between the two headers, descending)
    ///   - Then Supply header (lower index)
    ///   - Then Provisioning rows (below the Supply header, further descending)
    /// Child mapping inside each row component:
    ///   - #4  = item name (text)
    ///   - #7  = requested (int)
    ///   - #8  = on-hand  (int, optional)
    /// </summary>
    public sealed unsafe class MissionScanner
    {
        private readonly IGameGui _gameGui;
        private readonly IDataManager _data;   // reserved for Lumina lookups later
        private readonly IPluginLog _log;

        private static readonly string[] TargetAddons = { "ContentsInfoDetail" };

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
            if (unit == null || unit->UldManager.NodeList == null)
            {
                status = "UI detected, but node list was empty.";
                return results;
            }

            var nodeCount = unit->UldManager.NodeListCount;
            _log.Info($"[SMH] Top NodeListCount={nodeCount}");

            // Find headers by TEXT (index-based splitting is all we need)
            if (!TryFindHeaderIndicesByText(unit, out var supplyHeaderIdx, out var provisioningHeaderIdx))
            {
                status = "Headers not found (localization/variant).";
                return results;
            }

            // Ensure header ordering (we expect provisioning header above supply header by index)
            var hiIdx = Math.Max(supplyHeaderIdx, provisioningHeaderIdx);
            var loIdx = Math.Min(supplyHeaderIdx, provisioningHeaderIdx);
            var hiHeaderName = hiIdx == provisioningHeaderIdx ? "ProvisioningHeader" : "SupplyHeader";
            var loHeaderName = loIdx == supplyHeaderIdx ? "SupplyHeader" : "ProvisioningHeader";
            _log.Info($"[SMH] Headers: {hiHeaderName}={hiIdx}, {loHeaderName}={loIdx}");

            // Based on your observation:
            // - Supply rows are strictly between hiIdx and loIdx (descending)
            // - Provisioning rows are below the supply header (i < loIdx), descending
            var supplyRows       = new List<MissionItem>();
            var provisioningRows = new List<MissionItem>();

            // 1) Supply band: (hiIdx - 1) down to (loIdx + 1)
            for (int i = hiIdx - 1; i > loIdx; i--)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || node->Type != NodeType.Component) continue;

                if (TryParseRowFromComponent((AtkComponentNode*)node, out var item))
                {
                    item.Section = "Supply";
                    supplyRows.Add(item);
                    _log.Info($"[SMH] SUPPLY row at idx={i}: '{item.Name}' req={item.Quantity} have={item.OnHand}");
                }
            }

            // 2) Provisioning band: walk below the lower header (loIdx - 1 .. 0) until rows stop parsing
            //    (we don't know exact count, so just try all; non-rows will be skipped)
            for (int i = loIdx - 1; i >= 0; i--)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || node->Type != NodeType.Component) continue;

                if (TryParseRowFromComponent((AtkComponentNode*)node, out var item))
                {
                    item.Section = "Provisioning";
                    provisioningRows.Add(item);
                    _log.Info($"[SMH] PROVISIONING row at idx={i}: '{item.Name}' req={item.Quantity} have={item.OnHand}");
                }
            }

            // Collate
            results.AddRange(supplyRows);
            results.AddRange(provisioningRows);

            if (results.Count == 0)
            {
                if (WindowContains(unit, "No more deliveries are being accepted today"))
                    status = "No missions available today.";
                else
                    status = "No mission rows parsed in the expected index bands.";
                return results;
            }

            // Merge duplicates defensively
            results = results
                .GroupBy(r => (r.Section ?? "") + "||" + (r.Name ?? ""))
                .Select(g => new MissionItem
                {
                    Section  = g.First().Section,
                    Name     = g.First().Name,
                    Quantity = g.Sum(x => x.Quantity),
                    OnHand   = g.Sum(x => x.OnHand),
                    ItemId   = 0
                })
                .ToList();

            status = $"Parsed {results.Count} mission item(s).";
            return results;
        }

        // -------- internals --------

        private static bool TryFindHeaderIndicesByText(AtkUnitBase* unit, out int supplyHeaderIdx, out int provisioningHeaderIdx)
        {
            supplyHeaderIdx = -1;
            provisioningHeaderIdx = -1;

            // Pass 1: exact English labels (adjust here if you run non-English client)
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

            if (supplyHeaderIdx >= 0 && provisioningHeaderIdx >= 0)
                return true;

            // Pass 2: looser contains() fallback
            for (var i = 0; i < unit->UldManager.NodeListCount; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || node->Type != NodeType.Text) continue;

                var t = (AtkTextNode*)node;
                if (t->NodeText.StringPtr == null) continue;
                var s = t->NodeText.ToString().Trim();

                if (supplyHeaderIdx < 0 &&
                    s.Contains("Supply", StringComparison.OrdinalIgnoreCase) &&
                    s.Contains("Mission", StringComparison.OrdinalIgnoreCase))
                    supplyHeaderIdx = i;

                if (provisioningHeaderIdx < 0 &&
                    s.Contains("Provision", StringComparison.OrdinalIgnoreCase) &&
                    s.Contains("Mission", StringComparison.OrdinalIgnoreCase))
                    provisioningHeaderIdx = i;
            }

            return supplyHeaderIdx >= 0 && provisioningHeaderIdx >= 0;
        }

        private bool TryParseRowFromComponent(AtkComponentNode* compNode, out MissionItem item)
        {
            item = new MissionItem();
            if (compNode == null || compNode->Component == null) return false;

            // Try this component's own children
            if (TryParseRowFromUld(compNode->Component->UldManager, out item))
                return true;

            // Otherwise: one nested level (some skins wrap a component inside another)
            var uld = compNode->Component->UldManager;
            if (uld.NodeList != null && uld.NodeListCount > 0)
            {
                for (int j = 0; j < uld.NodeListCount; j++)
                {
                    var n = uld.NodeList[j];
                    if (n == null || n->Type != NodeType.Component) continue;
                    var nested = (AtkComponentNode*)n;
                    if (nested->Component == null) continue;

                    if (TryParseRowFromUld(nested->Component->UldManager, out item))
                        return true;
                }
            }

            return false;
        }

        private bool TryParseRowFromUld(AtkUldManager uld, out MissionItem item)
        {
            item = new MissionItem();
            if (uld.NodeList == null || uld.NodeListCount < 5) return false;

            // Fast path: fixed child indices
            if (TryReadTextAt(uld, CHILD_INDEX_NAME, out var name) && !string.IsNullOrWhiteSpace(name) &&
                TryReadIntAt(uld, CHILD_INDEX_REQUESTED, out var requested))
            {
                TryReadIntAt(uld, CHILD_INDEX_ONHAND, out var onHand);
                item.Name     = name.Trim();
                item.Quantity = requested;
                item.OnHand   = onHand;
                return true;
            }

            // Fallback: scan child texts to recover weird layouts
            string? bestName = null;
            int req = 0, have = 0;

            for (int i = 0; i < uld.NodeListCount; i++)
            {
                var n = uld.NodeList[i];
                if (n == null || n->Type != NodeType.Text) continue;
                var t = (AtkTextNode*)n;
                if (t->NodeText.StringPtr == null) continue;
                var s = t->NodeText.ToString().Trim();
                if (string.IsNullOrEmpty(s)) continue;
                if (LooksLikeFraction(s)) continue; // skip "0/20" style texts

                if (IsNumeric(s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                {
                    if (req == 0) req = iv;         // first number we see = requested
                    else have = iv;                 // second number (if any) = on-hand
                }
                else
                {
                    if (bestName is null || s.Length > bestName.Length)
                        bestName = s;
                }
            }

            if (!string.IsNullOrWhiteSpace(bestName) && req > 0)
            {
                item.Name     = bestName;
                item.Quantity = req;
                item.OnHand   = have;
                return true;
            }

            return false;
        }

        private static bool TryReadTextAt(AtkUldManager uld, int index, out string? text)
        {
            text = null;
            if (index < 0 || index >= uld.NodeListCount) return false;
            var n = uld.NodeList[index];
            if (n == null || n->Type != NodeType.Text) return false;
            var t = (AtkTextNode*)n;
            if (t->NodeText.StringPtr == null) return false;
            text = t->NodeText.ToString();
            return true;
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
        public int     OnHand   { get; set; } // on-hand
        public uint    ItemId   { get; set; } // future: Lumina lookup
        public string? Section  { get; set; } // "Supply" or "Provisioning"
    }
}
