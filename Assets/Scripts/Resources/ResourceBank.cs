using System;
using System.Collections.Generic;
using UnityEngine;

public static class ResourceBank
{
    static List<int> L => SaveManager.Data.resourceCounts;

    // ─────────────────────────────────────────────────────────────
    // Booster cap settings (PER-RESOURCE, not shared)
    // ─────────────────────────────────────────────────────────────
    // Each booster resource (PPEPermit, TrainingVoucher, WellnessVoucher, EfficiencyVoucher)
    // caps independently at this value.
    public const int BoosterCapPerType = 50;

    private static readonly ResourceType[] BoosterTypes = new[]
    {
        ResourceType.PPEPermit,
        ResourceType.TrainingVoucher,
        ResourceType.WellnessVoucher,
        ResourceType.EfficiencyVoucher
    };

    // ─────────────────────────────────────────────────────────────
    // Batching system (from your original)
    // ─────────────────────────────────────────────────────────────
    static int _batchDepth = 0;
    static bool _dirty = false;

    public static void BeginBatch()
    {
        _batchDepth++;
    }

    public static void EndBatch()
    {
        _batchDepth = Mathf.Max(0, _batchDepth - 1);
        if (_batchDepth == 0 && _dirty)
        {
            _dirty = false;
            SaveManager.Save();
            GameEvents.OnResourcesChanged?.Invoke();
        }
    }

    static void EmitChanged()
    {
        if (_batchDepth > 0)
        {
            _dirty = true;
            return;
        }

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────
    // Core methods
    // ─────────────────────────────────────────────────────────────
    public static void EnsureSize()
    {
        SaveManager.LoadOrCreate();
        if (SaveManager.Data.resourceCounts == null)
            SaveManager.Data.resourceCounts = new List<int>();

        // IMPORTANT: size by max enum value + 1 (NOT Enum.GetValues().Length)
        int need = 0;
        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
            need = Mathf.Max(need, (int)t + 1);

        while (SaveManager.Data.resourceCounts.Count < need)
            SaveManager.Data.resourceCounts.Add(0);
    }

    static int Index(ResourceType t) => (int)t;

    public static int Get(ResourceType t)
    {
        EnsureSize();
        int i = Index(t);
        if (i < 0 || i >= L.Count) return 0;
        return Mathf.Max(0, L[i]);
    }

    public static void Set(ResourceType t, int value)
    {
        EnsureSize();
        int i = Index(t);
        if (i < 0 || i >= L.Count) return;

        int v = Mathf.Max(0, value);

        // Enforce per-type cap for boosters on Set as well (prevents UI confusion / bad saves)
        if (IsCappedType(t))
            v = Mathf.Min(v, BoosterCapPerType);

        if (L[i] == v) return;
        L[i] = v;
        EmitChanged();
    }

    // ─────────────────────────────────────────────────────────────
    // Add logic (with booster cap enforcement)
    // ─────────────────────────────────────────────────────────────
    public static void Add(ResourceType t, int delta)
    {
        if (delta == 0) return;
        EnsureSize();
        int i = Index(t);
        if (i < 0 || i >= L.Count) return;

        // Handle boosters with per-type cap
        if (IsCappedType(t))
        {
            AddCappedResource(t, delta);
            return;
        }

        long next = (long)L[i] + delta;
        if (next < 0) next = 0;
        if (next > int.MaxValue) next = int.MaxValue;

        int newVal = (int)next;
        if (newVal == L[i]) return;
        L[i] = newVal;

        EmitChanged();
    }

    public static bool TrySpend(ResourceType t, int amount)
    {
        if (amount <= 0) return true;
        EnsureSize();
        int i = Index(t);
        if (i < 0 || i >= L.Count) return false;

        if (L[i] < amount) return false;
        L[i] -= amount;

        EmitChanged();
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Booster helpers
    // ─────────────────────────────────────────────────────────────
    static bool IsCappedType(ResourceType t)
    {
        for (int i = 0; i < BoosterTypes.Length; i++)
            if (BoosterTypes[i] == t)
                return true;
        return false;
    }

    static void AddCappedResource(ResourceType t, int delta)
    {
        EnsureSize();
        int i = Index(t);
        if (i < 0 || i >= L.Count) return;

        int cur = Mathf.Max(0, L[i]);

        // Per-resource clamp
        long next = (long)cur + delta;
        if (next < 0) next = 0;
        if (next > BoosterCapPerType) next = BoosterCapPerType;

        int newVal = (int)next;
        if (newVal == cur) return;

        L[i] = newVal;
        EmitChanged();
    }

    /// <summary>
    /// Returns remaining capacity for the given booster type.
    /// For non-capped resources, returns int.MaxValue.
    /// </summary>
    public static int GetBoosterRoom(ResourceType t)
    {
        if (!IsCappedType(t)) return int.MaxValue;
        return Mathf.Max(0, BoosterCapPerType - Get(t));
    }

    /// <summary>
    /// Optional: total count across all booster types (no longer used for caps).
    /// </summary>
    public static int GetTotalBoosters()
    {
        int total = 0;
        for (int i = 0; i < BoosterTypes.Length; i++)
            total += Get(BoosterTypes[i]);
        return total;
    }
}
