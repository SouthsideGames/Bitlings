using UnityEngine;
using System;

public partial class EncounterManager
{
    [Header("Energy (Regen)")]
    [Tooltip("If SaveManager.Data has encounterMax/Cost, those win; otherwise these are used.")]
    [SerializeField, Min(1)] private int fallbackEncounterMax = 10;

    [SerializeField, Min(1)] private int fallbackEncounterCost = 1;

    [Tooltip("Seconds required to regenerate 1 energy point.")]
    [SerializeField, Min(1f)] private float energySecondsPerPoint = 3600f;

    float _tickAccum;

    // ─────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────

    public int GetEnergyPoints() => GetBankEnergy();

    public int GetEncounterMax() =>
        (SaveManager.Data != null && SaveManager.Data.encounterMax > 0)
            ? SaveManager.Data.encounterMax
            : fallbackEncounterMax;

    public int GetEncounterCost() =>
        (SaveManager.Data != null && SaveManager.Data.encounterCost > 0)
            ? SaveManager.Data.encounterCost
            : fallbackEncounterCost;

    public bool HasEnergy() => GetEnergyPoints() >= GetEncounterCost();

    public int GetSecondsUntilFull()
    {
        int max = GetEncounterMax();
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

        int max    = GetEncounterMax();
        int before = GetBankEnergy();

        int next = before + amount;
        if (!allowOvercap)
            next = Mathf.Min(next, max);

        next = Mathf.Max(0, next);

        SetBankEnergy(next);
        ClampEnergyBank();

        int after  = GetBankEnergy();
        int gained = Mathf.Max(0, after - before);

        // Reset regen baseline on gain so time math remains sane
        SetEnergyLastUnix(NowUnix());

        // Keep remainder within valid range
        SetEnergyRemainderSecs(ClampRemainder(GetEnergyRemainderSecs()));

        SaveEnergyStateToJson();
        GameEvents.EnergyChanged?.Invoke();
        OnStateChanged?.Invoke();

        if (gained > 0)
            OnEnergyGained?.Invoke(gained, after);
    }

    public bool SpendEnergy()
    {
        int cost = GetEncounterCost();
        if (cost <= 0) return true;

        // Single source of truth: bank
        if (!ResourceBank.TrySpend(ResourceType.Energy, cost))
            return false;

        ClampEnergyBank();

        // Reset timing baseline on spend
        SetEnergyLastUnix(NowUnix());
        SetEnergyRemainderSecs(0f);

        SaveEnergyStateToJson();
        GameEvents.EnergyChanged?.Invoke();
        OnStateChanged?.Invoke();
        return true;
    }

    public bool SpendEnergyIfPossible() => SpendEnergy();

    // ─────────────────────────────────────────────────────────────
    // Lifecycle hooks expected by EncounterManager
    // (Call from EncounterManager.Awake/Start/Update)
    // ─────────────────────────────────────────────────────────────

    void LoadEnergy()
    {
        SaveManager.LoadOrCreate();
        ResourceBank.EnsureSize();

        int max = GetEncounterMax();

        // If this is a new account and energy hasn't been initialized, default to FULL.
        // NOTE: If you want new accounts to start at 10 instead of full,
        // initialize ResourceBank.Energy in your new-account initializer instead.
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
        int max = GetEncounterMax();
        int cur = GetBankEnergy();

        if (cur >= max)
        {
            SetEnergyRemainderSecs(0f);
            SaveEnergyStateToJson();
            return;
        }

        long now  = NowUnix();
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
        int max = GetEncounterMax();
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

    // ─────────────────────────────────────────────────────────────
    // Internal helpers (Bank-only + JSON-only)
    // ─────────────────────────────────────────────────────────────

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
        int max = GetEncounterMax();
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

    // ─────────────────────────────────────────────────────────────
    // JSON state accessors (SaveManager.Data)
    // You MUST add these fields to SaveData:
    //   public long energyLastUnix;
    //   public float energyRemainderSecs;
    // ─────────────────────────────────────────────────────────────

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
}
