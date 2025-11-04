namespace SupplyMissionHelper.Models;

public enum MissionType
{
    Supply,        // DoH
    Provisioning   // DoL
}

public sealed class MissionItem
{
    public uint ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public uint Quantity { get; init; }
    public uint JobId { get; init; }
    public bool IsHq { get; init; }
    public MissionType Type { get; init; }
}
