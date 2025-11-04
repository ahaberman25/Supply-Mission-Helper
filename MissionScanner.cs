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
        private readonly IDataManager _data;
        private readonly IPluginLog _log;

        private static readonly string[] TargetAddons = { "ContentsInfoDetail" };

        private const int CHILD_INDEX_NAME      = 4;
        private const int CHILD_INDEX_REQUESTED = 7;
        private const int CHILD_INDEX_ONHAND    = 8;

        public MissionScanner(IGameGui gameGui, IDataManager data, IPluginLog log)
        {
            _gameGui = gameGui;
            _data = data;
            _log = log;
        }

        public bool IsReady => _gameGui != null && _data != null;

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

            var hiIdx = Math.Max(supplyHeaderIdx, provisioningHeaderIdx);
            var loIdx = Math.Min(supplyHeaderIdx, provisioningHeaderIdx);
            _log.Info($"[SMH] Headers: SupplyHeader={supplyHeaderIdx}, ProvisioningHeader={provisioningHeaderIdx}");

            var supplyRows       = new List<MissionItem>();
            var provisioningRows = new List<MissionItem>();

            // Scan EVERYTHING between headers, recursively
            for (int i = hiIdx - 1; i > loIdx; i--)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null) continue;
                RecurseNode(node, supplyRows, "Supply");
            }

            // Then scan everything below the lower header for provisioning
            for (int i = loIdx - 1; i >= 0; i--)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null) continue;
                RecurseNode(node, provisioningRows, "Provisioning");
            }

            _log.Info($"[SMH] Supply rows found={supplyRows.Count}, Provisioning rows found={provisioningRows.Count}");

            results.AddRange(supplyRows);
            results.AddRange(provisioningRows);

            if (results.Count == 0)
            {
                if (WindowContains(unit, "No more deliveries are being accepted today"))
                    status = "No missions available today.";
                else
                    status = "No mission rows parsed in the expected bands.";
                return results;
            }

            // Merge dupes by Section+Name, keep first ItemId (currently 0)
            results = results
                .GroupBy(r => $"{r.Section}|{r.Name}")
                .Select(g => new MissionItem
                {
                    Section  = g.First().Section,
                    Name     = g.First().Name,
                    Quantity = g.Sum(x => x.Quantity),
                    OnHand   = g.Sum(x => x.OnHand),
                    ItemId   = g.First().ItemId
                })
                .ToList();

            status = $"Parsed {results.Count} missions successfully.";
            return results;
        }

        // Recursively searches every child component for a row
        private void RecurseNode(AtkResNode* node, List<MissionItem> sink, string section)
        {
            if (node == null) return;

            if (node->Type == NodeType.Component)
            {
                var comp = (AtkComponentNode*)node;
                if (comp->Component != null)
                {
                    if (TryParseRowFromUld(comp->Component->UldManager, out var item))
                    {
                        item.Section = section;
                        sink.Add(item);
                        _log.Info($"[SMH] {section.ToUpper()} row: '{item.Name}' req={item.Quantity} have={item.OnHand}");
                    }

                    var uld = comp->Component->UldManager;
                    if (uld.NodeList != null)
                    {
                        for (int i = 0; i < uld.NodeListCount; i++)
                            RecurseNode(uld.NodeList[i], sink, section);
                    }
                }
            }
            else if (node->Type == NodeType.Res)
            {
                var res = (AtkResNode*)node;
                if (res->ChildNode != null)
                    RecurseNode(res->ChildNode, sink, section);
            }
        }

        private bool TryParseRowFromUld(AtkUldManager uld, out MissionItem item)
        {
            item = new MissionItem();
            if (uld.NodeList == null || uld.NodeListCount < 5) return false;

            if (TryReadTextAt(uld, CHILD_INDEX_NAME, out var name) && !string.IsNullOrWhiteSpace(name) &&
                TryReadIntAt(uld, CHILD_INDEX_REQUESTED, out var requested))
            {
                TryReadIntAt(uld, CHILD_INDEX_ONHAND, out var onHand);
                item.Name     = name.Trim();
                item.Quantity = requested;
                item.OnHand   = onHand;
                item.ItemId   = 0; // TODO: Lumina lookup mapping from name -> ItemId
                return true;
            }

            // Fallback: heuristic
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

                if (IsNumeric(s))
                {
                    if (req == 0) req = int.Parse(s, CultureInfo.InvariantCulture);
                    else have = int.Parse(s, CultureInfo.InvariantCulture);
                }
                else if (!s.Contains("Mission", StringComparison.OrdinalIgnoreCase))
                {
                    bestName = s;
                }
            }

            if (!string.IsNullOrEmpty(bestName) && req > 0)
            {
                item.Name     = bestName;
                item.Quantity = req;
                item.OnHand   = have;
                item.ItemId   = 0;
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
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool IsNumeric(string s)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

        private static bool WindowContains(AtkUnitBase* unit, string needle)
        {
            for (int i = 0; i < unit->UldManager.NodeListCount; i++)
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

        private static bool TryFindHeaderIndicesByText(AtkUnitBase* unit, out int supplyHeaderIdx, out int provisioningHeaderIdx)
        {
            supplyHeaderIdx = -1;
            provisioningHeaderIdx = -1;

            for (var i = 0; i < unit->UldManager.NodeListCount; i++)
            {
                var node = unit->UldManager.NodeList[i];
                if (node == null || node->Type != NodeType.Text) continue;

                var t = (AtkTextNode*)node;
                if (t->NodeText.StringPtr == null) continue;

                var s = t->NodeText.ToString().Trim();

                if (supplyHeaderIdx < 0 && s.Contains("Supply Mission", StringComparison.OrdinalIgnoreCase))
                    supplyHeaderIdx = i;

                if (provisioningHeaderIdx < 0 && s.Contains("Provisioning Mission", StringComparison.OrdinalIgnoreCase))
                    provisioningHeaderIdx = i;
            }

            return supplyHeaderIdx >= 0 && provisioningHeaderIdx >= 0;
        }
    }

    public sealed class MissionItem
    {
        public string? Name { get; set; }
        public int Quantity { get; set; }
        public int OnHand { get; set; }
        public string? Section { get; set; }
        public uint ItemId { get; set; } // default 0; filled later via Lumina
    }
}
