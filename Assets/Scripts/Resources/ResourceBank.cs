using System;
using System.Collections.Generic;
using UnityEngine;

public static class ResourceBank
{
    static List<int> L => SaveManager.Data.resourceCounts;

    // Energy may overcap rift max, but never this absolute hard cap.
    public const int EnergyHardCap = 5000;

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

    // Fired when a resource hits its cap during an Add() call. UI can subscribe to show a toast.
    public static event Action<ResourceType> OnResourceCapped;

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
        if (_batchDepth <= 0)
        {
            if (_dirty)
            {
                _dirty = false;
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
            }
            _batchDepth = 0;  // Defensive clamp prevents anomalies
        }
    }

    /// <summary>
    /// Discard a batch without persisting. Resets depth and dirty flag
    /// so that partial in-memory changes from a failed batch are not
    /// flushed to disk by subsequent EndBatch or EmitChanged calls.
    /// The caller is responsible for reloading authoritative state if needed.
    /// </summary>
    public static void CancelBatch()
    {
        _batchDepth = 0;
        _dirty = false;
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
    // Computed once: Enum.GetValues allocates, and EnsureSize runs on every Get/Add.
    private static int _neededSize = -1;

    public static void EnsureSize()
    {
        SaveManager.LoadOrCreate();
        if (SaveManager.Data.resourceCounts == null)
            SaveManager.Data.resourceCounts = new List<int>();

        // IMPORTANT: size by max enum value + 1 (NOT Enum.GetValues().Length)
        if (_neededSize < 0)
        {
            int max = 0;
            foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
                max = Mathf.Max(max, (int)t + 1);
            _neededSize = max;
        }
        int need = _neededSize;

        while (SaveManager.Data.resourceCounts.Count < need)
            SaveManager.Data.resourceCounts.Add(0);

        // Safety migration: if any legacy save has energy above hard cap, convert overflow to credits once.
        NormalizeEnergyIfNeeded();
    }

    static int Index(ResourceType t) => (int)t;

    public static int Get(ResourceType t)
    {
        // Arena tickets live in ArenaSaveData, not resourceCounts.
        if (t == ResourceType.ArenaTicket)
            return ArenaSaveHelper.GetArenaTicketCount();

        EnsureSize();
        int i = Index(t);
        if (i < 0 || i >= L.Count) return 0;
        return Mathf.Max(0, L[i]);
    }

    // Arena tickets live in ArenaSaveData (see Get above). Without this routing,
    // Add/Set/TrySpend wrote to resourceCounts[(int)ArenaTicket] — a slot Get never
    // reads — so any designer-configured reward granting ArenaTicket was silently lost.
    static void SetArenaTicketsClamped(long value)
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return;

        long v = value;
        if (v < 0) v = 0;
        if (v > ArenaConstants.MaxTickets) v = ArenaConstants.MaxTickets;

        if (arena.arenaTickets == (int)v) return;
        arena.arenaTickets = (int)v;
        EmitChanged();
    }

    public static void Set(ResourceType t, int value)
    {
        if (t == ResourceType.ArenaTicket)
        {
            SetArenaTicketsClamped(value);
            return;
        }

        EnsureSize();
        int i = Index(t);
        if (i < 0 || i >= L.Count) return;

        int v = Mathf.Max(0, value);
        bool changedCredits = false;

        if (t == ResourceType.Energy && v > EnergyHardCap)
        {
            int overflow = v - EnergyHardCap;
            v = EnergyHardCap;
            changedCredits = TryAddCreditsRaw(overflow);
        }

        // Enforce per-type cap for boosters on Set as well (prevents UI confusion / bad saves)
        if (IsCappedType(t))
            v = Mathf.Min(v, BoosterCapPerType);

        if (L[i] == v)
        {
            if (changedCredits) EmitChanged();
            return;
        }
        L[i] = v;
        EmitChanged();
    }

    // ─────────────────────────────────────────────────────────────
    // Add logic (with booster cap enforcement)
    // ─────────────────────────────────────────────────────────────
    public static void Add(ResourceType t, int delta)
    {
        if (delta == 0) return;

        // Arena tickets: route to their real store (and skip event multipliers —
        // tickets are a capped competitive currency, not a farmable resource).
        if (t == ResourceType.ArenaTicket)
        {
            SetArenaTicketsClamped((long)ArenaSaveHelper.GetArenaTicketCount() + delta);
            return;
        }

        // World Events: optional resource gain multipliers (e.g., Voucher Drives).
        // Apply ONLY to positive gains.
        if (delta > 0 && WorldEventSystem.I != null)
        {
            float mul = 1f;
            try { mul = WorldEventSystem.I.GetResourceGainMultiplier(t); } catch { mul = 1f; }
            if (!Mathf.Approximately(mul, 1f))
                delta = Mathf.CeilToInt(delta * Mathf.Max(0f, mul));
        }
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

        bool changedCredits = false;
        bool hitEnergyCap = false;
        if (t == ResourceType.Energy && next > EnergyHardCap)
        {
            int overflow = (int)(next - EnergyHardCap);
            next = EnergyHardCap;
            changedCredits = TryAddCreditsRaw(overflow);
            hitEnergyCap = (L[i] < EnergyHardCap); // only notify if we just reached the cap
        }

        int newVal = (int)next;
        if (newVal == L[i])
        {
            if (changedCredits) EmitChanged();
            return;
        }
        L[i] = newVal;

        if (hitEnergyCap)
        {
            OnResourceCapped?.Invoke(ResourceType.Energy);
            GameEvents.RaiseToast("Energy is full! Extras convert to Credits.");
        }
        EmitChanged();
    }

    public static bool TrySpend(ResourceType t, int amount)
    {
        if (amount <= 0) return true;

        if (t == ResourceType.ArenaTicket)
        {
            var arena = SaveManager.GetArenaSaveData();
            if (arena == null || arena.arenaTickets < amount) return false;
            arena.arenaTickets -= amount;
            EmitChanged();
            return true;
        }

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
        bool hitCap = next > BoosterCapPerType && cur < BoosterCapPerType;
        if (next > BoosterCapPerType) next = BoosterCapPerType;

        int newVal = (int)next;
        if (newVal == cur) return;

        L[i] = newVal;
        if (hitCap)
        {
            OnResourceCapped?.Invoke(t);
            GameEvents.RaiseToast($"{ResourceLabel(t)} is full! You've reached the limit.");
        }
        EmitChanged();
    }

    private static string ResourceLabel(ResourceType t) => t switch
    {
        ResourceType.Energy          => "Energy",
        ResourceType.PPEPermit       => "PPE Permits",
        ResourceType.TrainingVoucher => "Training Vouchers",
        ResourceType.WellnessVoucher => "Wellness Vouchers",
        ResourceType.EfficiencyVoucher => "Efficiency Vouchers",
        _                            => t.ToString()
    };

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

    static bool NormalizeEnergyIfNeeded()
    {
        int energyIdx = Index(ResourceType.Energy);
        if (energyIdx < 0 || energyIdx >= L.Count) return false;

        int cur = Mathf.Max(0, L[energyIdx]);
        if (cur <= EnergyHardCap)
        {
            if (L[energyIdx] < 0)
            {
                L[energyIdx] = 0;
                EmitChanged();
                return true;
            }
            return false;
        }

        int overflow = cur - EnergyHardCap;
        L[energyIdx] = EnergyHardCap;
        bool changedCredits = TryAddCreditsRaw(overflow);

        // Energy always changed in this path.
        if (overflow > 0 || changedCredits)
            EmitChanged();

        return true;
    }

    static bool TryAddCreditsRaw(int amount)
    {
        if (amount <= 0) return false;

        int creditsIdx = Index(ResourceType.Credits);
        if (creditsIdx < 0 || creditsIdx >= L.Count) return false;

        long cur = Mathf.Max(0, L[creditsIdx]);
        long next = cur + amount;
        if (next > int.MaxValue) next = int.MaxValue;

        int newVal = (int)next;
        if (newVal == L[creditsIdx]) return false;

        L[creditsIdx] = newVal;
        return true;
    }
}
