using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin.Services;
using SupplyMissionHelper.Models;

namespace SupplyMissionHelper;

public sealed class GcMissionScanner
{
    private readonly ITimerManager timerManager;
    private readonly IDataManager dataManager;

    // Likely property names returned by the timer entries (be lenient)
    private static readonly string[] ItemIdNames = { "ItemId", "ItemID", "Item", "ItemRowId" };
    private static readonly string[] QtyNames    = { "Quantity", "Count", "Amount" };
    private static readonly string[] JobIdNames  = { "JobId", "ClassJobId", "ClassId" };
    private static readonly string[] HqNames     = { "IsHQ", "IsHighQuality", "HQ" };

    public GcMissionScanner(ITimerManager timerManager, IDataManager dataManager)
    {
        this.timerManager = timerManager;
        this.dataManager = dataManager;
    }

    public IReadOnlyList<MissionItem> GetTodayMissions()
    {
        var missionsProp = timerManager.GetType()
            .GetProperty("GrandCompanySupplyMissions", BindingFlags.Public | BindingFlags.Instance);
        var missionsObj = missionsProp?.GetValue(timerManager);

        if (missionsObj is not IEnumerable enumerable)
            return Array.Empty<MissionItem>();

        var results = new List<MissionItem>();

        foreach (var mission in enumerable)
        {
            uint itemId = ReadUInt(mission, ItemIdNames);
            uint qty    = ReadUInt(mission, QtyNames, 1);
            uint jobId  = ReadUInt(mission, JobIdNames);
            bool isHq   = ReadBool(mission, HqNames);

            string itemName = ResolveItemName(itemId) ?? $"Item #{itemId}";
            var type = IsCraftingJob(jobId) ? MissionType.Supply : MissionType.Provisioning;

            results.Add(new MissionItem
            {
                ItemId   = itemId,
                ItemName = itemName,
                Quantity = qty,
                JobId    = jobId,
                IsHq     = isHq,
                Type     = type
            });
        }

        return results
            .OrderBy(r => r.Type == MissionType.Provisioning) // Supply first
            .ThenBy(r => r.JobId)
            .ThenBy(r => r.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string? ResolveItemName(uint itemId)
    {
        // Try to use generated sheet type via reflection only if it exists in runtime
        var itemType = Type.GetType("Lumina.Excel.GeneratedSheets.Item, Lumina.Excel.GeneratedSheets");
        if (itemType == null)
            return null;

        // IDataManager.GetExcelSheet<T>()
        var getSheetGeneric = typeof(IDataManager).GetMethods()
            .FirstOrDefault(m => m.Name == "GetExcelSheet" && m.IsGenericMethod && m.GetParameters().Length == 0);
        if (getSheetGeneric == null)
            return null;

        var getRow = itemType.Assembly
            .GetType("Lumina.Excel.ExcelSheet`1")?
            .MakeGenericType(itemType)
            .GetMethod("GetRow", new[] { typeof(uint) });

        if (getRow == null)
            return null;

        var sheet = getSheetGeneric.MakeGenericMethod(itemType).Invoke(dataManager, null);
        if (sheet == null)
            return null;

        var row = getRow.Invoke(sheet, new object[] { itemId });
        if (row == null)
            return null;

        // Item.Name is usually SeString; ToString() yields the text
        var nameProp = row.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
        var nameVal = nameProp?.GetValue(row);
        return nameVal?.ToString();
    }

    private static uint ReadUInt(object obj, string[] names, uint defaultValue = 0)
    {
        foreach (var n in names)
        {
            var p = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) continue;
            var v = p.GetValue(obj);
            if (v == null) continue;
            try { return Convert.ToUInt32(v); } catch { }
        }
        return defaultValue;
    }

    private static bool ReadBool(object obj, string[] names, bool defaultValue = false)
    {
        foreach (var n in names)
        {
            var p = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) continue;
            var v = p.GetValue(obj);
            if (v == null) continue;
            try { return Convert.ToBoolean(v); } catch { }
        }
        return defaultValue;
    }

    private static bool IsCraftingJob(uint jobId) => jobId is >= 8 and <= 15;   // DoH
    // private static bool IsGatheringJob(uint jobId) => jobId is >= 16 and <= 18; // DoL
}
