using System;
using System.Numerics;
using Dalamud.Interface.Windowing;

namespace SupplyMissionHelper
{
    public class MainWindow : Window, IDisposable
    {
        private SupplyMissionHelper Plugin;

        public MainWindow(SupplyMissionHelper plugin) : base("Supply Mission Helper")
        {
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(400, 300),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            this.Plugin = plugin;
        }

        public void Dispose()
        {
        }

        public override void Draw()
        {
            // Placeholder UI - will be implemented once the plugin builds successfully
            // The complex ImGui code will be added after we confirm the basic structure works
        }
    }
}