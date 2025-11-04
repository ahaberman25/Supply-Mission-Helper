using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace SupplyMissionHelper
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        // Add your configuration properties here
        public bool ShowRawMaterialsOnly { get; set; } = true;
        public bool IncludeGatheringLocations { get; set; } = false;

        // the below exist just to make saving less cumbersome
        [NonSerialized]
        private IDalamudPluginInterface? PluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.PluginInterface!.SavePluginConfig(this);
        }
    }
}