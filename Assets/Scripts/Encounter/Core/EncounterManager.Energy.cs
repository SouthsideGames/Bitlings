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

    const string PP_ENERGY_POINTS = "ENERGY_points";
    const string PP_ENERGY_LAST   = "ENERGY_lastUnix";
    const string PP_ENERGY_REM    = "ENERGY_remainder";

    int   _energyPoints;
    long  _energyLastUnix;
    float _energyRemainderSecs;
    float _tickAccum;

    public int  GetEnergyPoints()  => _energyPoints;

    public int  GetEncounterMax()  =>
        (SaveManager.Data != null && SaveManager.Data.encounterMax > 0)
            ? SaveManager.Data.encounterMax
            : fallbackEncounterMax;

    public int  GetEncounterCost() =>
        (SaveManager.Data != null && SaveManager.Data.encounterCost > 0)
            ? SaveManager.Data.encounterCost
            : fallbackEncounterCost;

    public bool HasEnergy()        => _energyPoints >= GetEncounterCost();

    public int GetSecondsUntilFull()
    {
        int max = GetEncounterMax();
        if (_energyPoints >= max) return 0;
        int missing = max - _energyPoints;
        double total = (missing * energySecondsPerPoint) - _energyRemainderSecs;
        return Mathf.Max(0, (int)Math.Ceiling(total));
    }

    public void AddEnergy(int amount, bool allowOvercap = true)
    {
        int max = GetEncounterMax();
        int before = _energyPoints;

        _energyPoints += amount;
        if (!allowOvercap) _energyPoints = Mathf.Min(_energyPoints, max);
        ClampEnergy();

        int gained = Mathf.Max(0, _energyPoints - before);

        SaveEnergy();
        MirrorEnergyIntoSaveData();
        GameEvents.EnergyChanged?.Invoke();
        OnStateChanged?.Invoke();

        if (gained > 0)
            OnEnergyGained?.Invoke(gained, _energyPoints);
    }

    public bool SpendEnergy()
    {
        int cost = GetEncounterCost();
        if (_energyPoints < cost) return false;

        _energyPoints -= cost;
        ClampEnergy();

        _energyLastUnix = NowUnix();
        _energyRemainderSecs = 0f;

        SaveEnergy();
        MirrorEnergyIntoSaveData();
        GameEvents.EnergyChanged?.Invoke();
        OnStateChanged?.Invoke();
        return true;
    }

    public bool SpendEnergyIfPossible() => SpendEnergy();

    void LoadEnergy()
    {
        int max = GetEncounterMax();

        int def = Mathf.Clamp(max, 1, 9999);
        _energyPoints        = PlayerPrefs.GetInt(PP_ENERGY_POINTS, def);
        string lastStr       = PlayerPrefs.GetString(PP_ENERGY_LAST, NowUnix().ToString());
        _energyLastUnix      = long.Parse(lastStr);
        _energyRemainderSecs = PlayerPrefs.GetFloat(PP_ENERGY_REM, 0f);

        ClampEnergy();
        _energyRemainderSecs = Mathf.Clamp(
            _energyRemainderSecs,
            0f,
            Mathf.Max(0f, energySecondsPerPoint - 0.001f)
        );
    }

    void SaveEnergy()
    {
        PlayerPrefs.SetInt(PP_ENERGY_POINTS, _energyPoints);
        PlayerPrefs.SetString(PP_ENERGY_LAST, _energyLastUnix.ToString());
        PlayerPrefs.SetFloat(PP_ENERGY_REM, _energyRemainderSecs);
        PlayerPrefs.Save();
    }

    void ClampEnergy()
    {
        int max = GetEncounterMax();
        _energyPoints = Mathf.Clamp(_energyPoints, 0, Mathf.Max(1, max));
    }

    void ApplyOfflineRegen()
    {
        int max = GetEncounterMax();
        if (_energyPoints >= max) { _energyRemainderSecs = 0f; return; }

        long elapsed = NowUnix() - _energyLastUnix;
        if (elapsed <= 0) return;

        double total = _energyRemainderSecs + elapsed;
        int gained = (int)Math.Floor(total / energySecondsPerPoint);
        _energyRemainderSecs = (float)(total - (gained * energySecondsPerPoint));

        if (gained > 0)
        {
            _energyPoints = Mathf.Min(max, _energyPoints + gained);
            if (_energyPoints >= max) _energyRemainderSecs = 0f;

            MirrorEnergyIntoSaveData();
            SaveEnergy();
            GameEvents.EnergyChanged?.Invoke();

            OnEnergyGained?.Invoke(gained, _energyPoints);
        }

        _energyLastUnix = NowUnix();
        SaveEnergy();
    }

    void TickEnergyRuntime()
    {
        int max = GetEncounterMax();
        if (_energyPoints >= max) return;

        _tickAccum += Time.unscaledDeltaTime;
        if (_tickAccum < 1f) return;
        _tickAccum = 0f;

        _energyRemainderSecs += 1f;
        if (_energyRemainderSecs >= energySecondsPerPoint)
        {
            _energyRemainderSecs -= energySecondsPerPoint;
            int before = _energyPoints;
            _energyPoints = Mathf.Min(max, _energyPoints + 1);
            if (_energyPoints >= max) _energyRemainderSecs = 0f;

            MirrorEnergyIntoSaveData();
            SaveEnergy();
            GameEvents.EnergyChanged?.Invoke();
            OnStateChanged?.Invoke();

            int gained = Mathf.Max(0, _energyPoints - before);
            if (gained > 0)
                OnEnergyGained?.Invoke(gained, _energyPoints);
        }
    }

    void MirrorEnergyIntoSaveData()
    {
        if (SaveManager.Data == null) return;

        SaveManager.Data.encounterPoints = _energyPoints;
        SaveManager.Save();
    }

    static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
