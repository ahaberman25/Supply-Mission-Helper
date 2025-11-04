using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;

namespace SupplyMissionHelper;

/// <summary>
/// Expands mission items into materials. Currently returns the mission items as-is,
/// but provides a single place to later look up recipes via Lumina (IDataManager).
/// </summary>
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
        // Phase 3 (initial): pass-through model so UI and plumbing can be validated.
        // Next iteration:
        //  - Use _data.GetExcelSheet<Item>() / Recipe / RecipeLevelTable
        //  - Build a tree, aggregate quantities, respect cfg.ShowRawMaterialsOnly
        var flat = missionItems
            .Select(mi => new MaterialRequirement
            {
                ItemId = mi.ItemId,
                Name = mi.Name,
                Quantity = mi.Quantity,
                LocationHint = cfg.IncludeGatheringLocations ? SimpleHint(mi.ItemId) : null
            })
            .ToList();

        return flat;
    }

    // Temporary hint stub; replace with real gathering/leve location lookups later.
    private static string? SimpleHint(uint itemId)
    {
        return itemId switch
        {
            1252 => "Likely crafted (Carpenter). Check Marketboard / Craft.",
            5333 => "Likely crafted (Blacksmith/Armorer). Check Marketboard / Craft.",
            _ => null
        };
    }
}

public sealed class MaterialRequirement
{
    public uint ItemId { get; set; }
    public string? Name { get; set; }
    public int Quantity { get; set; }
    public string? LocationHint { get; set; }
}
