using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Dalamud.Game.Gui;
using Dalamud.Logging; // v13 removed; we won't use PluginLog static, just keep the using harmless
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper;

public sealed class MissionScanner
{
    private readonly IGameGui _gameGui;
    private readonly IDataManager _data;
    private readonly IPluginLog _log;

    // Stable, user-confirmed row “NodeId” values under ContentsInfoDetail.
    // These are the indices in the top-level NodeList (NOT AtkTextNode.NodeID field),
    // as they appear in Dalamud’s Addon Inspector.
    private static readonly ushort[] SupplyRowNodeIds = { 112, 111, 110, 109, 108, 107, 106, 105 };
    private static readonly ushort[] ProvRowNodeIds   = { 101, 100,  99 };

    // Within each row’s Base Component, child text-node slots we care about:
    private const ushort ChildIdx_Name      = 4;  // item name
    private const ushort ChildIdx_Requested = 7;  // requested qty
    private const ushort ChildIdx_Have      = 8;  // have/on-hand qty

    // Found rows are cached from the last scan to make UI faster to render after “Calculate”
    public IReadOnlyList<MissionItem> LastSupply { get; private set; } = Array.Empty<MissionItem>();
    public IReadOnlyList<MissionItem> LastProvisioning { get; private set; } = Array.Empty<MissionItem>();

    public bool LastHeadersFound { get; private set; }
    public int  LastTopNodeCount { get; private set; }

    public MissionScanner(IGameGui gameGui, IDataManager data, IPluginLog log)
    {
        _gameGui = gameGui;
        _data = data;
        _log = log;
    }

    public bool IsReady() => true;

    public bool IsWindowOpen()
    {
        unsafe
        {
            if (!_gameGui.TryGetAddonByName("ContentsInfoDetail", 1, out var addonPtr) || addonPtr == nint.Zero)
                return false;
            var unit = (AtkUnitBase*)addonPtr;
            return unit->IsVisible;
        }
    }

    /// <summary>
    /// Scan the currently opened ContentsInfoDetail and return Supply/Provisioning rows discovered.
    /// </summary>
    public (List<MissionItem> supply, List<MissionItem> provisioning) Scan()
    {
        var supply = new List<MissionItem>();
        var prov   = new List<MissionItem>();

        unsafe
        {
            if (!_gameGui.TryGetAddonByName("ContentsInfoDetail", 1, out var addonPtr) || addonPtr == nint.Zero)
            {
                _log.Info("[SMH] ContentsInfoDetail not found/open.");
                LastHeadersFound = false;
                LastSupply = supply;
                LastProvisioning = prov;
                return (supply, prov);
            }

            var unit = (AtkUnitBase*)addonPtr;
            if (!unit->IsVisible)
            {
                _log.Info("[SMH] ContentsInfoDetail exists but is not visible.");
                LastHeadersFound = false;
                LastSupply = supply;
                LastProvisioning = prov;
                return (supply, prov);
            }

            var nodeList = unit->RootNode is null ? default(AtkUldManager.NodeList) : unit->UldManager.NodeList;
            var nodes = nodeList.NodeList;
            var count = nodeList.Count;
            LastTopNodeCount = count;

            if (nodes == null || count == 0)
            {
                _log.Info("[SMH] NodeList empty.");
                LastHeadersFound = false;
                LastSupply = supply;
                LastProvisioning = prov;
                return (supply, prov);
            }

            // Confirm headers by text so we know we’re in the right place.
            var supplyHeaderIdx = FindTextIndex(nodes, count, "Supply Missions");
            var provHeaderIdx   = FindTextIndex(nodes, count, "Provisioning Missions");
            LastHeadersFound = (supplyHeaderIdx >= 0 && provHeaderIdx >= 0);

            _log.Info($"[SMH] Top NodeListCount={count}");
            _log.Info($"[SMH] Headers: SupplyHeader={(supplyHeaderIdx >= 0 ? supplyHeaderIdx : -1)}, ProvisioningHeader={(provHeaderIdx >= 0 ? provHeaderIdx : -1)}");

            // Pull rows by the user-confirmed NodeId map.
            supply.AddRange(CollectBand(unit, nodes, count, SupplyRowNodeIds, "Supply"));
            prov.AddRange(CollectBand(unit, nodes, count, ProvRowNodeIds, "Provisioning"));
        }

        _log.Info($"[SMH] Found {supply.Count} supply rows and {prov.Count} provisioning rows.");
        LastSupply = supply;
        LastProvisioning = prov;
        return (supply, prov);
    }

    // ------------ Helpers ------------

    private unsafe static int FindTextIndex(AtkResNode** nodes, int count, string target)
    {
        for (var i = 0; i < count; i++)
        {
            var n = nodes[i];
            if (n == null || n->Type != NodeType.Text) continue;
            var text = GetText((AtkTextNode*)n);
            if (!string.IsNullOrEmpty(text) && string.Equals(text.Trim(), target, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private unsafe static string GetText(AtkTextNode* t)
    {
        if (t == null) return string.Empty;
        // Prefer RawText if present (Payload 0). Fallback to the “NodeText.StringPtr”.
        if (t->Text != null && t->Text->StringPtr != null)
        {
            try { return Marshal.PtrToStringUTF8((nint)t->Text->StringPtr) ?? string.Empty; }
            catch { /* ignore */ }
        }
        return string.Empty;
    }

    private unsafe List<MissionItem> CollectBand(AtkUnitBase* unit, AtkResNode** topNodes, int topCount, ushort[] rowNodeIds, string bandName)
    {
        var result = new List<MissionItem>();

        int hits = 0;
        foreach (var rowNodeId in rowNodeIds)
        {
            var idx = (int)rowNodeId;
            if (idx < 0 || idx >= topCount) continue;

            var res = topNodes[idx];
            if (res == null) continue;

            // Each “row” in your screenshots is a Base Component Node hanging under this res node.
            // We walk the subtree rooted at 'res' and grab the FIRST BaseComponent we find,
            // then read its text children #4, #7, #8.
            var baseComponent = FindFirstBaseComponent(res);
            if (baseComponent == null) continue;

            var name      = GetChildText(baseComponent, ChildIdx_Name);
            var requested = ParseIntSafe(GetChildText(baseComponent, ChildIdx_Requested));
            var have      = ParseIntSafe(GetChildText(baseComponent, ChildIdx_Have));

            // Ignore empty rows (grey ones): no name => no mission on that slot.
            if (string.IsNullOrWhiteSpace(name))
                continue;

            hits++;

            var mi = new MissionItem
            {
                Name = name.Trim(),
                Requested = requested,
                Have = have,
                // We don’t have itemId here yet—name is sufficient to drive the next step (recipe lookup).
                // ItemId can be resolved later via Lumina’s Item sheet by name.
            };

            result.Add(mi);
        }

        _log.Info($"[SMH] {bandName} band scanned {rowNodeIds.Length} mapped nodes, rows={hits}");
        return result;
    }

    private unsafe static AtkComponentNode* FindFirstBaseComponent(AtkResNode* root)
    {
        if (root == null) return null;

        // Breadth-first walk capped to a reasonable breadth/depth to be safe.
        Span<AtkResNode*> queue = stackalloc AtkResNode*[128];
        int qh = 0, qt = 0;

        queue[qt++] = root;

        while (qh < qt)
        {
            var n = queue[qh++];
            if (n == null) continue;

            if (n->Type == NodeType.Component)
                return (AtkComponentNode*)n;

            // children
            for (var child = n->ChildNode; child != null; child = child->PrevSiblingNode)
            {
                if (qt < queue.Length) queue[qt++] = child;
            }
        }

        return null;
    }

    private unsafe static string GetChildText(AtkComponentNode* compNode, ushort childIndex)
    {
        if (compNode == null) return string.Empty;

        // Component -> UldManager.NodeList (child nodes of the component)
        var childList = compNode->Component->UldManager.NodeList;
        var childNodes = childList.NodeList;
        var childCount = childList.Count;

        if (childNodes == null || childCount <= 0) return string.Empty;

        var idx = (int)childIndex;
        if (idx < 0 || idx >= childCount) return string.Empty;

        var child = childNodes[idx];
        if (child == null) return string.Empty;

        if (child->Type == NodeType.Text)
            return GetText((AtkTextNode*)child);

        // Sometimes the text you want can be under a nested component; try a quick descend:
        if (child->Type == NodeType.Component)
        {
            var nested = (AtkComponentNode*)child;
            // Look for the *first* text node in that nested component:
            var nestedList = nested->Component->UldManager.NodeList;
            var nestedNodes = nestedList.NodeList;
            var nestedCount = nestedList.Count;
            for (var i = 0; i < nestedCount; i++)
            {
                var maybeText = nestedNodes[i];
                if (maybeText != null && maybeText->Type == NodeType.Text)
                    return GetText((AtkTextNode*)maybeText);
            }
        }

        return string.Empty;
    }

    private static int ParseIntSafe(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;

        // Strip any non-digits (icons, glyphs)
        var span = s.AsSpan();
        int val = 0, sign = 1;
        bool any = false;

        for (int i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (ch == '-' && !any) { sign = -1; continue; }
            if (ch >= '0' && ch <= '9')
            {
                any = true;
                val = val * 10 + (ch - '0');
            }
        }

        return any ? sign * val : 0;
    }
}

// What the UI needs out of a scan.
public sealed class MissionItem
{
    public string Name { get; set; } = string.Empty;
    public int Requested { get; set; }
    public int Have { get; set; }

    // Optional later: resolved via Lumina by name
    public uint ItemId { get; set; }
}
