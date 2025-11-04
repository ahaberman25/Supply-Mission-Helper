using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SupplyMissionHelper
{
    public sealed class MissionScanner
    {
        private readonly IGameGui _gameGui;
        private readonly IDataManager _data;
        private readonly IPluginLog _log;

        // v13: confirmed name from your screenshot
        private static readonly string[] CandidateAddonNames =
        {
            "ContentsInfoDetail",   // ← GC Supply/Provisioning mission sheet
            // Fallback guesses, kept for safety:
            "GrandCompanySupplyList",
            "GcSupplyList",
            "SupplyList",
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
            foreach (var name in CandidateAddonNames)
            {
                try
                {
                    var ptr = _gameGui.GetAddonByName(name, 1);
                    if (ptr != nint.Zero)
                        return true;
                }
                catch (Exception ex)
                {
                    _log.Warning($"Addon check failed for {name}: {ex.Message}");
                }
            }
            return false;
        }

        public List<MissionItem> TryReadMissions(out string? status)
        {
            status = null;
            var results = new List<MissionItem>();

            var addonPtr = GetFirstAvailableAddonPtr();
            if (addonPtr == nint.Zero)
            {
                status = "Supply Mission window not detected.";
                return results;
            }

            try
            {
                unsafe
                {
                    var addon = (AtkUnitBase*)addonPtr;
                    if (addon == null || addon->UldManager.NodeListCount == 0)
                    {
                        status = "UI detected, but no nodes found.";
                        return results;
                    }

                    // Collect all visible text strings in the addon for debugging & parsing
                    var texts = new List<string>();
                    for (var i = 0; i < addon->UldManager.NodeListCount; i++)
                    {
                        var node = addon->UldManager.NodeList[i];
                        if (node == null || !node->IsVisible())
                            continue;

                        if (node->Type == NodeType.Text)
                        {
                            var t = (AtkTextNode*)node;
                            if (t->NodeText.StringPtr != null)
                            {
                                var s = t->NodeText.ToString();
                                if (!string.IsNullOrWhiteSpace(s))
                                    texts.Add(s.Trim());
                            }
                        }
                    }

                    // Log what we see (first 40 lines max, to avoid spam)
                    if (texts.Count > 0)
                    {
                        _log.Info($"[SMH] ContentsInfoDetail text dump ({Math.Min(texts.Count, 40)} shown / {texts.Count} total):");
                        foreach (var line in texts.Take(40))
                            _log.Info($"[SMH] · {line}");
                    }
                    else
                    {
                        _log.Info("[SMH] ContentsInfoDetail: no text nodes contained content.");
                    }

                    // Common state: no missions available today
                    if (texts.Any(x => x.Contains("No more deliveries are being accepted today", StringComparison.OrdinalIgnoreCase)))
                    {
                        status = "No missions available today.";
                        return results; // empty list is expected here
                    }

                    // Very simple heuristic parsing:
                    // Look for lines with quantities (integers) and pair them with the closest preceding non-empty line (item name).
                    // This is just to get you going; once we capture a dump on a day with missions, we’ll parse by exact nodes.
                    var parsed = new List<MissionItem>();
                    for (int i = 1; i < texts.Count; i++)
                    {
                        if (TryParseQty(texts[i], out var qty))
                        {
                            // find a plausible name one or two lines above
                            var name = FindNearestName(texts, i - 1);
                            if (!string.IsNullOrEmpty(name))
                            {
                                parsed.Add(new MissionItem
                                {
                                    Name = name,
                                    Quantity = qty,
                                    // ItemId unknown at this point; we can resolve via Lumina by name later
                                    ItemId = 0
                                });
                            }
                        }
                    }

                    if (parsed.Count > 0)
                    {
                        // Merge dupes by name to be tidy
                        results = parsed
                            .GroupBy(p => p.Name ?? string.Empty)
                            .Select(g => new MissionItem
                            {
                                Name = g.Key,
                                Quantity = g.Sum(x => x.Quantity),
                                ItemId = 0
                            })
                            .ToList();

                        status = $"Parsed {results.Count} mission item(s) (heuristic).";
                        return results;
                    }

                    status = "UI detected, but no mission rows matched the heuristic parser.";
                    return results;
                }
            }
            catch (Exception ex)
            {
                status = $"Scan failed: {ex.Message}";
                _log.Error(ex, "TryReadMissions failed");
                return new List<MissionItem>();
            }
        }

        private nint GetFirstAvailableAddonPtr()
        {
            foreach (var name in CandidateAddonNames)
            {
                try
                {
                    var ptr = _gameGui.GetAddonByName(name, 1);
                    if (ptr != nint.Zero)
                        return ptr;
                }
                catch { /* ignore; logged elsewhere */ }
            }
            return nint.Zero;
        }

        private static bool TryParseQty(string s, out int qty)
        {
            // Try to read a plain integer (e.g., "3", "12"), or "x 3"
            s = s.Trim();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out qty))
                return true;

            // common pattern "x 3" / "x3"
            var idx = s.IndexOf('x');
            if (idx >= 0)
            {
                var rest = s[(idx + 1)..].Trim();
                if (int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out qty))
                    return true;
            }

            qty = 0;
            return false;
        }

        private static string? FindNearestName(List<string> texts, int startIndex)
        {
            // Look back 1-3 lines to find a non-numeric name-ish string
            for (int k = 0; k < 3 && startIndex - k >= 0; k++)
            {
                var candidate = texts[startIndex - k].Trim();
                if (!string.IsNullOrEmpty(candidate) && !int.TryParse(candidate, out _))
                    return candidate;
            }
            return null;
        }
    }

    public sealed class MissionItem
    {
        public uint ItemId { get; set; }
        public string? Name { get; set; }
        public int Quantity { get; set; }
    }
}
