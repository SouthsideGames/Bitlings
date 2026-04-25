using UnityEngine;
using System;

// ─────────────────────────────────────────────────────────────
// RiftManager.Energy
// Energy economy: costs, spend/add operations, online/offline regeneration.
// ─────────────────────────────────────────────────────────────

public partial class RiftManager
{
    [Header("Energy (Regen)")]
    [Tooltip("If SaveManager.Data has riftMax/Cost, those win; otherwise these are used.")]
    [SerializeField, Min(1)] private int fallbackRiftMax = 10;

    [SerializeField, Min(1)] private int fallbackRiftCost = 1;

    [Tooltip("Seconds required to regenerate 1 energy point.")]
    // NOTE: Keep this in sync with RiftPanelUI's ETA default.
    // 1200s = 20 minutes per energy.
    [SerializeField, Min(1f)] private float energySecondsPerPoint = 1200f;

    float _tickAccum;

    // ─────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────

    public int GetEnergyPoints() => GetBankEnergy();

    public int GetRiftMax() =>
        (SaveManager.Data != null && SaveManager.Data.riftMax > 0)
            ? SaveManager.Data.riftMax
            : fallbackRiftMax;

    public int GetRiftCost() =>
        GetRiftCost_Internal();

    private int GetRiftCost_Internal()
    {
        int baseCost = (SaveManager.Data != null && SaveManager.Data.riftCost > 0)
            ? SaveManager.Data.riftCost
            : fallbackRiftCost;

        float mul = (WorldEventSystem.I != null) ? WorldEventSystem.I.GetRiftEnergyCostMultiplier() : 1f;
        if (mul <= 0f) return 0;

        int next = Mathf.RoundToInt(baseCost * mul);

        // Preserve the “at least 1” semantics if baseCost is positive.
        if (baseCost > 0) next = Mathf.Max(1, next);
        return next;
    }

    public bool HasEnergy() => GetEnergyPoints() >= GetRiftCost();

    public int GetSecondsUntilFull()
    {
        int max = GetRiftMax();
        int cur = GetEnergyPoints();
        if (cur >= max) return 0;

        float rem = GetEnergyRemainderSecs();
        int missing = max - cur;
        double total = (missing * energySecondsPerPoint) - rem;
        return Mathf.Max(0, (int)Math.Ceiling(total));
    }

    public void AddEnergy(int amount, bool allowOvercap = true)
    {
        if (amount == 0) return;

        int max = GetRiftMax();
        int before = GetBankEnergy();

        int next = before + amount;
        if (!allowOvercap)
            next = Mathf.Min(next, max);

        next = Mathf.Max(0, next);

        SetBankEnergy(next);
        ClampEnergyBank();

        int after = GetBankEnergy();
        int gained = Mathf.Max(0, after - before);

        SetEnergyLastUnix(NowUnix());

        SetEnergyRemainderSecs(ClampRemainder(GetEnergyRemainderSecs()));

        SaveEnergyStateToJson();
        GameEvents.EnergyChanged?.Invoke();
        OnStateChanged?.Invoke();

        if (gained > 0)
            OnEnergyGained?.Invoke(gained, after);
    }

    public bool SpendEnergy()
    {
        int cost = GetRiftCost();
        if (cost <= 0) return true;

        if (!ResourceBank.TrySpend(ResourceType.Energy, cost))
            return false;

        ClampEnergyBank();

        SetEnergyLastUnix(NowUnix());
        SetEnergyRemainderSecs(0f);

        SaveEnergyStateToJson();
        GameEvents.EnergyChanged?.Invoke();
        OnStateChanged?.Invoke();
        return true;
    }

    public bool SpendEnergyIfPossible() => SpendEnergy();

    // ─────────────────────────────────────────────────────────────
    // Lifecycle hooks expected by RiftManager
    // (Call from RiftManager.Awake/Start/Update)
    // ─────────────────────────────────────────────────────────────

    void LoadEnergy()
    {
        SaveManager.LoadOrCreate();
        ResourceBank.EnsureSize();

        int max = GetRiftMax();

        int cur = GetBankEnergy();
        if (cur <= 0 && max > 0)
            SetBankEnergy(max);

        // Ensure JSON timer fields exist and are sane
        long last = GetEnergyLastUnix();
        if (last <= 0)
            SetEnergyLastUnix(NowUnix());

        float rem = GetEnergyRemainderSecs();
        SetEnergyRemainderSecs(ClampRemainder(rem));

        ClampEnergyBank();
        SaveEnergyStateToJson();
    }

    void ApplyOfflineRegen()
    {
        int max = GetRiftMax();
        int cur = GetBankEnergy();

        if (cur >= max)
        {
            SetEnergyRemainderSecs(0f);
            SaveEnergyStateToJson();
            return;
        }

        long now = NowUnix();
        long last = GetEnergyLastUnix();
        if (last <= 0) last = now;

        long elapsed = now - last;
        if (elapsed <= 0) return;

        double total = GetEnergyRemainderSecs() + elapsed;
        int gained = (int)Math.Floor(total / energySecondsPerPoint);
        float newRem = (float)(total - (gained * energySecondsPerPoint));

        if (gained > 0)
        {
            int next = Mathf.Min(max, cur + gained);
            SetBankEnergy(next);

            // If we hit full, remainder should be 0
            if (next >= max) newRem = 0f;

            GameEvents.EnergyChanged?.Invoke();
            OnEnergyGained?.Invoke(gained, next);
        }

        SetEnergyLastUnix(now);
        SetEnergyRemainderSecs(ClampRemainder(newRem));
        SaveEnergyStateToJson();
    }

    void TickEnergyRuntime()
    {
        int max = GetRiftMax();
        int cur = GetBankEnergy();
        if (cur >= max) return;

        _tickAccum += Time.unscaledDeltaTime;
        if (_tickAccum < 1f) return;
        _tickAccum = 0f;

        float rem = GetEnergyRemainderSecs() + 1f;

        if (rem >= energySecondsPerPoint)
        {
            rem -= energySecondsPerPoint;

            int before = cur;
            int next = Mathf.Min(max, before + 1);
            SetBankEnergy(next);

            if (next >= max) rem = 0f;

            GameEvents.EnergyChanged?.Invoke();
            OnStateChanged?.Invoke();

            int gained = Mathf.Max(0, next - before);
            if (gained > 0)
                OnEnergyGained?.Invoke(gained, next);
        }

        SetEnergyRemainderSecs(ClampRemainder(rem));
        SetEnergyLastUnix(NowUnix());
        SaveEnergyStateToJson();
    }


    int GetBankEnergy()
    {
        ResourceBank.EnsureSize();
        return ResourceBank.Get(ResourceType.Energy);
    }

    void SetBankEnergy(int value)
    {
        ResourceBank.EnsureSize();
        ResourceBank.Set(ResourceType.Energy, Mathf.Max(0, value));
    }

    void ClampEnergyBank()
    {
        int max = GetRiftMax();
        int cur = GetBankEnergy();
        int clamped = Mathf.Clamp(cur, 0, Mathf.Max(1, max));
        if (clamped != cur)
            SetBankEnergy(clamped);
    }

    float ClampRemainder(float rem)
    {
        float cap = Mathf.Max(0f, energySecondsPerPoint - 0.001f);
        return Mathf.Clamp(rem, 0f, cap);
    }


    long GetEnergyLastUnix()
    {
        SaveManager.LoadOrCreate();
        return (SaveManager.Data != null) ? SaveManager.Data.energyLastUnix : 0;
    }

    void SetEnergyLastUnix(long unix)
    {
        SaveManager.LoadOrCreate();
        if (SaveManager.Data == null) return;
        SaveManager.Data.energyLastUnix = unix;
    }

    float GetEnergyRemainderSecs()
    {
        SaveManager.LoadOrCreate();
        return (SaveManager.Data != null) ? SaveManager.Data.energyRemainderSecs : 0f;
    }

    void SetEnergyRemainderSecs(float secs)
    {
        SaveManager.LoadOrCreate();
        if (SaveManager.Data == null) return;
        SaveManager.Data.energyRemainderSecs = secs;
    }

    void SaveEnergyStateToJson()
    {
        SaveManager.Save();
    }

    static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    
    public void Cheat_ApplyOfflineEnergyRegen()
    {
        ApplyOfflineRegen();
        OnStateChanged?.Invoke();
    }

}
