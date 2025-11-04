using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    /// <summary>
    /// Reads GC "Supply & Provisioning Missions" from ContentsInfoDetail.
    /// Strategy:
    ///   1) Find the two section headers by text ("Supply Missions", "Provisioning Missions").
    ///   2) Treat the *index bands* as authoritative:
    ///        - Supply rows:     strictly between the two headers (descending indices).
    ///        - Provisioning rows: below the lower header (descending indices).
    ///   3) For each top-level node in those bands, recursively search *all nested components*
    ///      for a "row-like" component that fits the child pattern:
    ///         #4  = item name (text)
    ///         #7  = requested (int)
    ///         #8  = on-hand  (int, optional)
    ///   4) Log discoveries so we can lock fast paths later if desired.
    /// </summary>
    public sealed unsafe class MissionScanner
    {
        private readonly IGameGui _gameGui;
        private readonly IDataManager _data; // reserved for Lumina lookups later
        private readonly IPluginLog _log;

        private static readonly string[] TargetAddons = { "ContentsInfoDetail" };

        // Child indices inside a mission row component
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

            if (!TryFindHeaderIndicesByText(unit, out var supplyHeaderIdx, out var provisioningHeaderIdx))
            {
                status = "Headers not found (localization/variant).";
                return results;
            }

            // We don’t rely on which header is “higher” by meaning — we use index bands only.
            var hiIdx = Math.Max(supplyHeaderIdx, provisioningHeaderIdx);
            var loIdx = Math.Min(supplyHeaderIdx, provisioningHeaderIdx);

            _log.Info($"[SMH] Headers: SupplyHeader={supplyHeaderIdx}, ProvisioningHeader={provisioningHeaderIdx}");

            // -------- Deep scan bands --------
            var supplyFound       = new List<MissionItem>();
            var provisioningFound = new List<MissionItem>();

            // Supply band: strictly between headers
            int supplyScanComponents = 0;
            for (int i = hiIdx - 1; i > loIdx; i--)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || node->Type != NodeType.Component) continue;

                supplyScanComponents++;
                var comp = (AtkComponentNode*)node;
                DeepCollectRows(comp, supplyFound, "Supply");
            }
            _log.Info($"[SMH] Supply band scanned {supplyScanComponents} top-level components, rows={supplyFound.Count}");

            // Provisioning band: below the lower header
            int provScanComponents = 0;
            for (int i = loIdx - 1; i >= 0; i--)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || node->Type != NodeType.Component) continue;

                provScanComponents++;
                var comp = (AtkComponentNode*)node;
                DeepCollectRows(comp, provisioningFound, "Provisioning");
            }
            _log.Info($"[SMH] Provisioning band scanned {provScanComponents} top-level components, rows={provisioningFound.Count}");

            // Collate
            results.AddRange(supplyFound);
            results.AddRange(provisioningFound);

            if (results.Count == 0)
            {
                if (WindowContains(unit, "No more deliveries are being accepted today"))
                    status = "No missions available today.";
                else
                    status = "No mission rows parsed in the expected index bands.";
                return results;
            }

            // Merge duplicates defensively (by Section+Name)
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

        // ---------------- internals ----------------

        private static bool TryFindHeaderIndicesByText(AtkUnitBase* unit, out int supplyHeaderIdx, out int provisioningHeaderIdx)
        {
            supplyHeaderIdx = -1;
            provisioningHeaderIdx = -1;

            // Pass 1: exact English labels
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

        /// <summary>
        /// Recursively walk a component and its descendants to collect any row-like components.
        /// </summary>
        private void DeepCollectRows(AtkComponentNode* root, List<MissionItem> sink, string section)
        {
            if (root == null || root->Component == null) return;
            var uld = root->Component->UldManager;

            // Check THIS component for row shape
            if (TryParseRowFromUld(uld, out var item))
            {
                item.Section = section;
                sink.Add(item);
                _log.Info($"[SMH] {section.ToUpperInvariant()} row: '{item.Name}' req={item.Quantity} have={item.OnHand}");
                // Don’t return — there could be multiple rows under same top-level container (lists)
            }

            // Recurse into children (depth-first)
            if (uld.NodeList != null && uld.NodeListCount > 0)
            {
                for (int i = 0; i < uld.NodeListCount; i++)
                {
                    var n = uld.NodeList[i];
                    if (n == null || n->Type != NodeType.Component) continue;

                    var child = (AtkComponentNode*)n;
                    if (child->Component == null) continue;

                    DeepCollectRows(child, sink, section);
                }
            }
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
                    if (req == 0) req = iv; else have = iv;
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
