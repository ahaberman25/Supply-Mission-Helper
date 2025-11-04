using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace SupplyMissionHelper;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool ShowRawMaterialsOnly { get; set; } = true;
    public bool IncludeGatheringLocations { get; set; } = false;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        this.pluginInterface!.SavePluginConfig(this);
    }
}
