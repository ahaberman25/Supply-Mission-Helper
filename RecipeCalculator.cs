using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;   // IDataManager, IPluginLog

namespace SupplyMissionHelper
{
    public sealed class RecipeCalculator
    {
        private readonly IDataManager _data;
        private readonly IPluginLog _log;

        public RecipeCalculator(IDataManager data, IPluginLog log)
        {
            _data = data;
            _log = log;
        }

        public List<MaterialRequirement> Calculate(List<MissionItem> missionItems, Configuration cfg)
        {
            // Stub pass-through; replace with real recipe expansion
            return missionItems
                .Select(mi => new MaterialRequirement
                {
                    ItemId = mi.ItemId,
                    Name = mi.Name,
                    Quantity = mi.Quantity,
                    LocationHint = cfg.IncludeGatheringLocations ? SimpleHint(mi.ItemId) : null
                })
                .ToList();
        }

        private static string? SimpleHint(uint itemId) => itemId switch
        {
            1252 => "Craft (Carpenter) or Marketboard",
            5333 => "Craft (BSM/ARM) or Marketboard",
            _ => null
        };
    }

    public sealed class MaterialRequirement
    {
        public uint ItemId { get; set; }
        public string? Name { get; set; }
        public int Quantity { get; set; }
        public string? LocationHint { get; set; }
    }
}
