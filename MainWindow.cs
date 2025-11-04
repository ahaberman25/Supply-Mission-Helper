using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace SupplyMissionHelper;

public class MainWindow : Window, IDisposable
{
    private Plugin Plugin { get; init; }
    private readonly GcMissionScanner scanner;

    public MainWindow(Plugin plugin, GcMissionScanner scanner)
        : base("Supply Mission Helper")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
        this.scanner = scanner;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.Button("Refresh"))
        {
            // Just re-draw; scanning happens on demand below
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Source: Timers → Grand Company Supply/Provisioning (API 13)");

        ImGui.Separator();

        var missions = scanner.GetTodayMissions();

        if (missions.Count == 0)
        {
            ImGui.TextWrapped("No missions found yet. Open the in-game Timers window once this session if needed, or try Refresh.");
            return;
        }

        // Table header
        if (ImGui.BeginTable("gc_table", 6, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Type", 0, 0.15f);
            ImGui.TableSetupColumn("Job", 0, 0.10f);
            ImGui.TableSetupColumn("Qty", 0, 0.08f);
            ImGui.TableSetupColumn("HQ", 0, 0.06f);
            ImGui.TableSetupColumn("Item ID", 0, 0.14f);
            ImGui.TableSetupColumn("Item Name", 0, 0.47f);
            ImGui.TableHeadersRow();

            foreach (var m in missions)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(m.Type == MissionType.Supply ? "Supply" : "Provisioning");

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(m.JobId.ToString());

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(m.Quantity.ToString());

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(m.IsHq ? "✔" : "—");

                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted(m.ItemId.ToString());

                ImGui.TableSetColumnIndex(5);
                ImGui.TextUnformatted(m.ItemName);
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Tip: Supply = Disciples of the Hand (jobs 8–15), Provisioning = Disciples of the Land (jobs 16–18).");
    }
}
