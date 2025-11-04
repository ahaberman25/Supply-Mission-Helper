using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace SupplyMissionHelper
{
    public sealed class MissionScanner
    {
        private readonly IGameGui _gameGui;
        private readonly IDataManager _data;
        private readonly IPluginLog _log;

        private static readonly string[] CandidateAddonNames =
        {
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
                    // v13: use GetAddonByName(name, index) and check for non-zero pointer
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
            var results = new List<MissionItem>();

            if (!IsSupplyWindowOpen())
            {
                status = "Supply Mission window not detected.";
                return results;
            }

            try
            {
                // placeholder rows for end-to-end UI validation
                results.Add(new MissionItem { ItemId = 1252, Name = "Ash Lumber", Quantity = 3 });
                results.Add(new MissionItem { ItemId = 5333, Name = "Iron Ingot", Quantity = 2 });

                status = "Scan complete (placeholder data).";
                return results;
            }
            catch (Exception ex)
            {
                status = $"Scan failed: {ex.Message}";
                _log.Error(ex, "TryReadMissions failed");
                return new List<MissionItem>();
            }
        }
    }

    public sealed class MissionItem
    {
        public uint ItemId { get; set; }
        public string? Name { get; set; }
        public int Quantity { get; set; }
    }
}
