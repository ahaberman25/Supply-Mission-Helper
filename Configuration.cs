using Dalamud.Configuration;   // IPluginConfiguration
using Dalamud.Plugin;          // IDalamudPluginInterface

namespace SupplyMissionHelper
{
    public sealed class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        public bool ShowRawMaterialsOnly { get; set; }
        public bool IncludeGatheringLocations { get; set; }

        private IDalamudPluginInterface? _pi;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            _pi = pluginInterface;
        }

        public void Save() => _pi?.SavePluginConfig(this);
    }
}
