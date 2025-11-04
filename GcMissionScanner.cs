using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin.Services;
using Lumina.Excel.GeneratedSheets;

namespace SupplyMissionHelper;

public enum MissionType
{
    Supply,        // DoH
    Provisioning   // DoL
}

public record GcMissionEntry(
    uint ItemId,
    string ItemName,
    uint Quantity,
    uint JobId,
    bool IsHq,
    MissionType Type
);

public sealed class GcMissionScanner
{
    private readonly ITimerManager timerManager;
    private readonly IDataManager dataManager;

    // Known property name variants we might encounter on the Timer entry objects
    private static readonly string[] ItemIdNames   = { "ItemId", "ItemID", "Item", "ItemRowId" };
    private static readonly string[] QtyNames      = { "Quantity", "Count", "Amount" };
    private static readonly string[] JobIdNames    = { "JobId", "ClassJobId", "ClassId" };
    private static readonly string[] HqNames       = { "IsHQ", "IsHighQuality", "HQ" };

    public GcMissionScanner(ITimerManager timerManager, IDataManager dataManager)
    {
        this.timerManager = timerManager;
        this.dataManager = dataManager;
    }

    public IReadOnlyList<GcMissionEntry> GetTodayMissions()
    {
        // Try to access ITimerManager.GrandCompanySupplyMissions (API 13)
        var missionsObj = timerManager
            .GetType()
            .GetProperty("GrandCompanySupplyMissions", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(timerManager);

        if (missionsObj is not IEnumerable enumerable)
            return Array.Empty<GcMissionEntry>();

        var entries = new List<GcMissionEntry>();
        var itemSheet = dataManager.GetExcelSheet<Item>();

        foreach (var mission in enumerable)
        {
            // Reflect properties
            uint itemId   = ReadUInt(mission, ItemIdNames);
            uint qty      = ReadUInt(mission, QtyNames, defaultValue: 1);
            uint jobId    = ReadUInt(mission, JobIdNames);
            bool isHq     = ReadBool(mission, HqNames);

            // Look up item name
            var itemRow = itemSheet?.GetRow(itemId);
            var itemName = itemRow?.Name?.ToString() ?? $"Item #{itemId}";

            // Classify Supply (DoH) vs Provisioning (DoL)
            var type = IsCraftingJob(jobId) ? MissionType.Supply : MissionType.Provisioning;

            entries.Add(new GcMissionEntry(itemId, itemName, qty, jobId, isHq, type));
        }

        // Stable ordering: Supply first (crafting jobs ASC), then Provisioning (gathering jobs ASC)
        return entries
            .OrderBy(e => e.Type == MissionType.Provisioning) // Supply before Provisioning
            .ThenBy(e => e.JobId)
            .ThenBy(e => e.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static uint ReadUInt(object obj, string[] names, uint defaultValue = 0)
    {
        foreach (var n in names)
        {
            var prop = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) continue;

            var val = prop.GetValue(obj);
            if (val == null) continue;

            try
            {
                return Convert.ToUInt32(val);
            }
            catch { /* ignore */ }
        }
        return defaultValue;
    }

    private static bool ReadBool(object obj, string[] names, bool defaultValue = false)
    {
        foreach (var n in names)
        {
            var prop = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) continue;

            var val = prop.GetValue(obj);
            if (val == null) continue;

            try
            {
                return Convert.ToBoolean(val);
            }
            catch { /* ignore */ }
        }
        return defaultValue;
    }

    // DoH (8–15) / DoL (16–18) — consistent with FFXIV ClassJob rows
    private static bool IsCraftingJob(uint jobId)
        => jobId is >= 8 and <= 15;

    private static bool IsGatheringJob(uint jobId)
        => jobId is >= 16 and <= 18;
}
