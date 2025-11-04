using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    /// <summary>
    /// Robust scanner for GC "Supply & Provisioning Missions" (ContentsInfoDetail).
    /// 1) Detects headers by stable NodeId (Supply=3, Provisioning=16).
    /// 2) Recursively searches all component nodes for "row-like" components:
    ///      - child #4: text (name)
    ///      - child #7: int  (requested)
    ///      - child #8: int  (on-hand, optional)
    /// 3) Logs discoveries so we can later hardcode fast NodeId paths.
    /// </summary>
    public sealed unsafe class MissionScanner
    {
        private readonly IGameGui _gameGui;
        private readonly IDataManager _data;   // reserved for future Lumina lookups
        private readonly IPluginLog _log;

        private static readonly string[] TargetAddons = { "ContentsInfoDetail" };

        // Header TextNode NodeIds (stable per your inspection)
        private const uint SUPPLY_HEADER_NODEID       = 3u;   // "Supply Missions"
        private const uint PROVISIONING_HEADER_NODEID = 16u;  // "Provisioning Missions"

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

            // Locate headers (useful for bounds/status; not strictly required to parse rows)
            var haveHeaders = TryFindHeaderIndices(unit, out var supplyHeaderIdx, out var provisioningHeaderIdx);
            _log.Info($"[SMH] NodeListCount={unit->UldManager.NodeListCount}, HeadersFound={haveHeaders} (SupplyIdx={supplyHeaderIdx}, ProvIdx={provisioningHeaderIdx})");

            // Recursively discover row-like components anywhere under the addon
            var allRowCandidates = FindRowLikeComponentsRecursive(unit);
            _log.Info($"[SMH] Discovered {allRowCandidates.Count} row-like component(s).");

            // If we have headers, try to split rows into sections by comparing index proximity:
            // a row belongs to the nearest header index (supply or provisioning).
            var rowsSupply       = new List<RowCandidate>();
            var rowsProvisioning = new List<RowCandidate>();

            if (haveHeaders && allRowCandidates.Count > 0)
            {
                foreach (var rc in allRowCandidates)
                {
                    var distToSupply = Math.Abs(rc.TopIndex - supplyHeaderIdx);
                    var distToProv   = Math.Abs(rc.TopIndex - provisioningHeaderIdx);
                    if (distToSupply <= distToProv) rowsSupply.Add(rc); else rowsProvisioning.Add(rc);
                }
            }
            else
            {
                // If no headers, just dump everything into "Supply" so we at least show something
                rowsSupply.AddRange(allRowCandidates);
            }

            // Parse each candidate component into Name / Requested / OnHand
            ParseRowCandidates(rowsSupply, "Supply", results);
            ParseRowCandidates(rowsProvisioning, "Provisioning", results);

            if (results.Count == 0)
            {
                if (WindowContains(unit, "No more deliveries are being accepted today"))
                    status = "No missions available today.";
                else if (!haveHeaders)
                    status = "GC view detected, but section headers not found (UI variant?).";
                else
                    status = "No active mission rows detected (UI layout variant or daily cap).";
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

            // Prefer NodeId (stable)
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

            // Fallback: text matching (localization may differ)
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

        // A compact struct to hold a candidate row component and a bit of context
        private readonly struct RowCandidate
        {
            public readonly nint CompPtr;     // AtkComponentNode* (stored as nint)
            public readonly int  TopIndex;    // index of this component in the top-level NodeList (closest ancestor we iterated)
            public readonly uint NodeId;      // the component's NodeId

            public RowCandidate(nint compPtr, int topIndex, uint nodeId)
            {
                CompPtr = compPtr;
                TopIndex = topIndex;
                NodeId = nodeId;
            }
        }

        /// <summary>
        /// Recursively search for components that look like mission rows anywhere under the addon.
        /// </summary>
        private List<RowCandidate> FindRowLikeComponentsRecursive(AtkUnitBase* unit)
        {
            var found = new List<RowCandidate>();
            var list = unit->UldManager.NodeList;
            var count = unit->UldManager.NodeListCount;
            if (list == null || count == 0) return found;

            for (int i = 0; i < count; i++)
            {
                var node = list[i];
                if (node == null) continue;

                if (node->Type == NodeType.Component)
                {
                    var compNode = (AtkComponentNode*)node;
                    if (compNode->Component != null)
                    {
                        // 1) Check this component itself
                        if (LooksLikeRow(&(compNode->Component->UldManager)))
                        {
                            found.Add(new RowCandidate((nint)compNode, i, node->NodeId));
                            _log.Info($"[SMH] Row-like component at topIndex={i}, NodeId={node->NodeId}");
                            continue;
                        }

                        // 2) Recurse into its children (nested components)
                        var uld = compNode->Component->UldManager;
                        if (uld.NodeList != null && uld.NodeListCount > 0)
                        {
                            FindRowLikeComponentsRecursiveInto(uld, i, found);
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Recursively descend into a component's children, looking for row-like components.
        /// </summary>
        private void FindRowLikeComponentsRecursiveInto(AtkUldManager parentUld, int topIndexContext, List<RowCandidate> sink)
        {
            for (int j = 0; j < parentUld.NodeListCount; j++)
            {
                var n = parentUld.NodeList[j];
                if (n == null) continue;
                if (n->Type != NodeType.Component) continue;

                var cnode = (AtkComponentNode*)n;
                if (cnode->Component == null) continue;

                var uld = cnode->Component->UldManager;

                if (LooksLikeRow(&uld))
                {
                    sink.Add(new RowCandidate((nint)cnode, topIndexContext, n->NodeId));
                    _log.Info($"[SMH] Row-like nested component under topIndex={topIndexContext}, NodeId={n->NodeId}");
                }
                else if (uld.NodeList != null && uld.NodeListCount > 0)
                {
                    // Keep going down
                    FindRowLikeComponentsRecursiveInto(uld, topIndexContext, sink);
                }
            }
        }

        /// <summary>
        /// Determines if a component's own UldManager fits the expected row shape.
        /// </summary>
        private bool LooksLikeRow(AtkUldManager* uld)
        {
            if (uld == null || uld->NodeList == null) return false;
            // Need indices up to 8 at least (0..8) to read name/#7/#8 reliably
            if (uld->NodeListCount < 9) return false;

            // child #4 must be non-empty text
            if (!TryReadTextAt(*uld, CHILD_INDEX_NAME, out var name) || string.IsNullOrWhiteSpace(name))
                return false;

            // child #7 must parse to int
            if (!TryReadIntAt(*uld, CHILD_INDEX_REQUESTED, out var req))
                return false;

            // #8 is optional (some layouts might omit, but if present we’ll use it)
            return true;
        }

        private static void ParseRowCandidates(IEnumerable<RowCandidate> rows, string section, List<MissionItem> sink)
        {
            foreach (var rc in rows)
            {
                var compNode = (AtkComponentNode*)rc.CompPtr;
                if (compNode == null || compNode->Component == null) continue;

                var uld = compNode->Component->UldManager;
                if (uld.NodeList == null || uld.NodeListCount == 0) continue;

                string? name = null;
                int requested = 0;
                int onHand = 0;

                // Fast path: fixed indices
                if (TryReadTextAt(uld, CHILD_INDEX_NAME, out var nameCandidate) && !string.IsNullOrWhiteSpace(nameCandidate))
                    name = nameCandidate.Trim();

                TryReadIntAt(uld, CHILD_INDEX_REQUESTED, out requested);
                TryReadIntAt(uld, CHILD_INDEX_ONHAND, out onHand);

                // Fallback: scan texts to recover (layout drift)
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
                        if (LooksLikeFraction(s)) continue; // skip "0/20"

                        if (requested == 0 && IsNumeric(s) &&
                            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                        {
                            requested = iv; continue;
                        }

                        if (name is null || s.Length > name.Length) name = s;
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

                    // log one-liner so we can see what we captured (and its NodeId for future fast path)
                    // rc.TopIndex is where this subtree is anchored in the top-level list (near its header).
                    // rc.NodeId is the component's NodeId — likely a good candidate to hardcode later.
                    // (Use Info not Debug so it shows up in default logs.)
                    Dalamud.Logging.PluginLog.Information($"[SMH] Captured {section} row (TopIdx={rc.TopIndex}, NodeId={rc.NodeId}): '{name}' req={requested} have={onHand}");
                }
            }
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
