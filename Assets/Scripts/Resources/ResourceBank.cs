using System;
using System.Collections.Generic;
using UnityEngine;

public static class ResourceBank
{
    static List<int> L => SaveManager.Data.resourceCounts;

    // ─────────────────────────────────────────────────────────────
    // Booster/Sigil cap settings
    // ─────────────────────────────────────────────────────────────
    public const int BoosterCapTotal = 50;          // Combined cap
    public const int BoosterOverflowcreditValue = 1;  // Overflow → credits

    private static readonly ResourceType[] BoosterTypes = new[]
    {
        ResourceType.PPEPermit,
        ResourceType.TrainingVoucher_ATK,
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

        int need = Enum.GetValues(typeof(ResourceType)).Length;
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
        if (L[i] == v) return;
        L[i] = v;
        EmitChanged();
    }

    // ─────────────────────────────────────────────────────────────
    // Add logic (with booster/sigil cap enforcement)
    // ─────────────────────────────────────────────────────────────
    public static void Add(ResourceType t, int delta)
    {
        if (delta == 0) return;
        EnsureSize();
        int i = Index(t);
        if (i < 0 || i >= L.Count) return;

        // Handle boosters/sigils with cap
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
    // Booster/Sigil helpers
    // ─────────────────────────────────────────────────────────────
    static bool IsCappedType(ResourceType t)
    {
        for (int i = 0; i < BoosterTypes.Length; i++)
            if (BoosterTypes[i] == t)
                return true;
        return false;
    }

    static int GetCappedTotal()
    {
        int total = 0;
        for (int i = 0; i < BoosterTypes.Length; i++)
            total += Get(BoosterTypes[i]);
        return total;
    }

    static void AddCappedResource(ResourceType t, int delta)
    {
        if (delta <= 0) return;

        int currentTotal = GetCappedTotal();
        int room = BoosterCapTotal - currentTotal;
        if (room < 0) room = 0;

        int toAdd = Mathf.Min(delta, room);
        int overflow = Mathf.Max(0, delta - toAdd);

        int i = Index(t);
        if (i < 0 || i >= L.Count) return;

        // Add what fits
        if (toAdd > 0)
            L[i] = Mathf.Clamp(L[i] + toAdd, 0, int.MaxValue);

        // Overflow becomes credits
        if (overflow > 0 && BoosterOverflowcreditValue > 0)
            Add(ResourceType.Credits, overflow * BoosterOverflowcreditValue);

        EmitChanged();
    }
}
