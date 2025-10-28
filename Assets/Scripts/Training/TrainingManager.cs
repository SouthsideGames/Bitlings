using System;
using UnityEngine;

public class TrainingManager : MonoBehaviour
{
    public static TrainingManager I;

    [Header("Tick")]
    [SerializeField, Min(0.1f)] private float tickSeconds = 1f;

    private float _acc;

    void Awake()
    {
        if (I != this && I != null) { Destroy(gameObject); return; }
        I = this;
    }

    void OnEnable() { GrantOfflineAll(); }

    void Update()
    {
        _acc += Time.unscaledDeltaTime;
        if (_acc >= tickSeconds) { _acc = 0f; TickLive(); }
    }

    void TickLive()
    {
        var d = SaveManager.Data;
        if (d?.owned == null) return;

        long now = SaveManager.NowUnix();
        foreach (var om in d.owned)
        {
            if (!om.isTraining) continue;
            GrantForElapsed(om, now);
        }
        SaveManager.Save();
    }

    void GrantOfflineAll()
    {
        var d = SaveManager.Data;
        if (d?.owned == null) return;

        long now = SaveManager.NowUnix();
        foreach (var om in d.owned)
        {
            if (!om.isTraining) continue;
            GrantForElapsed(om, now);
        }
        SaveManager.Save();
    }

    public void ProcessOfflineTrainingAll()
    {
        GrantOfflineAll();
        GameEvents.OnResourcesChanged?.Invoke();
    }

    void FinalizeGrant(OwnedMonsterData om)
    {
        long now = SaveManager.NowUnix();
        GrantForElapsed(om, now);
    }

    void GrantForElapsed(OwnedMonsterData om, long nowUnix)
    {
        if (om.trainingLastUnix <= 0) { om.trainingLastUnix = nowUnix; return; }
        long elapsed = Math.Max(0, nowUnix - om.trainingLastUnix);
        if (elapsed <= 0) { om.trainingLastUnix = nowUnix; return; }

        int perHour = CurrentPerHour(om);
        float perSec = perHour / 3600f;
        int grant = Mathf.FloorToInt(perSec * elapsed);

        if (grant > 0) { AddXPIntoPending(om, grant); }
        om.trainingLastUnix = nowUnix;
    }

    void AddXPIntoPending(OwnedMonsterData om, int add)
    {
        if (add <= 0) return;
        if (om.level >= LevelRules.MaxLevel) return;

        om.currentXP += add;

        while (om.level + om.pendingLevels < LevelRules.MaxLevel)
        {
            int effectiveLevel = om.level + om.pendingLevels;
            int need = LevelRules.XPToNext(effectiveLevel);
            if (om.currentXP < need) break;
            om.currentXP -= need;
            om.pendingLevels++;
        }

        if (om.level + om.pendingLevels >= LevelRules.MaxLevel)
            om.currentXP = 0;
    }

    int CountGymWorkers() => (JobManager.I != null) ? Mathf.Clamp(JobManager.I.GymWorkerCount, 0, 3) : 0;

    MonsterDataSO Resolve(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var libs = Resources.FindObjectsOfTypeAll<MonsterLibrarySO>();
        foreach (var lib in libs)
        {
            var def = lib != null ? lib.GetById(id) : null;
            if (def != null) return def;
        }
        return null;
    }

    OwnedMonsterData FindOwned(string id)
    {
        var d = SaveManager.Data;
        if (d?.owned == null) return null;
        foreach (var m in d.owned) if (m != null && m.monsterId == id) return m;
        return null;
    }

    // ---------- Public API ----------
    public void StartTraining(string monsterId)
    {
        var d = SaveManager.Data;
        if (d?.owned == null || string.IsNullOrEmpty(monsterId)) return;

        foreach (var m in d.owned) m.isTraining = false;

        var om = FindOwned(monsterId);
        if (om == null) return;

        om.isTraining = true;
        om.trainingLastUnix = SaveManager.NowUnix();

        d.trainingMonsterId = monsterId;
        d.trainingMonsterLevel = om.level;

        SaveManager.Save();
    }

    public void StopTraining(string monsterId)
    {
        var d = SaveManager.Data;
        if (d == null) return;

        var om = FindOwned(monsterId);
        if (om == null) return;

        FinalizeGrant(om);
        om.isTraining = false;

        if (d.trainingMonsterId == monsterId)
        {
            d.trainingMonsterId = null;
            d.trainingMonsterLevel = 0;
        }

        SaveManager.Save();
    }

    public int CurrentPerHour(OwnedMonsterData om)
    {
        var def = Resolve(om?.monsterId);
        if (def == null) return 0;
        int workers = Mathf.Clamp(CountGymWorkers(), 0, 3);
        float mult = 1f + 0.25f * workers;
        return Mathf.CeilToInt(def.baseTrainingXPPerHour * mult);
    }

    public (int perHour, int workers, float multiplier) CurrentRateTuple(OwnedMonsterData om)
    {
        var def = Resolve(om?.monsterId);
        if (def == null) return (0, 0, 1f);
        int workers = Mathf.Clamp(CountGymWorkers(), 0, 3);
        float mult = 1f + 0.25f * workers;
        int perHour = Mathf.CeilToInt(def.baseTrainingXPPerHour * mult);
        return (perHour, workers, mult);
    }

    public bool CanClaimLevel(OwnedMonsterData om)
    {
        if (om == null) return false;
        if (om.level >= LevelRules.MaxLevel) return false;
        if (om.pendingLevels <= 0) return false;
        int today = SaveManager.TodayDayIndexUTC(); 
        return om.lastLevelClaimDay != today;
    }

    public bool ClaimOneLevel(OwnedMonsterData om)
    {
        if (!CanClaimLevel(om)) return false;

        om.pendingLevels = Math.Max(0, om.pendingLevels - 1);
        om.level = Mathf.Min(LevelRules.MaxLevel, om.level + 1);
        om.lastLevelClaimDay = SaveManager.TodayDayIndexUTC();

        if (SaveManager.Data != null && SaveManager.Data.trainingMonsterId == om.monsterId)
            SaveManager.Data.trainingMonsterLevel = om.level;

        GameEvents.MonsterLeveled?.Invoke(om.monsterId, om.level);

        var def = Resolve(om.monsterId);
        if (def != null && def.evolutionLevel > 0 && om.level >= def.evolutionLevel)
            GameEvents.EvolutionOffered?.Invoke(om.monsterId);

        SaveManager.Save();
        return true;
    }

    public int GrantInstantTrainingXP(OwnedMonsterData target, int amount)
    {
        if (target == null || amount <= 0) return 0;

        int capacity = ComputeRemainingXPCapacity(target);
        if (capacity <= 0) return 0;

        int applied = Mathf.Min(amount, capacity);

        AddXPIntoPending(target, applied);

        SaveManager.Save();
        return applied;
    }


    int ComputeRemainingXPCapacity(OwnedMonsterData om)
    {
        if (om == null) return 0;
        if (om.level >= LevelRules.MaxLevel) return 0;

        int effectiveLevel = om.level + om.pendingLevels;
        int needToNext = LevelRules.XPToNext(effectiveLevel);
        int remainingThisLevel = Mathf.Max(0, needToNext - om.currentXP);

        int levelsLeft = Mathf.Max(0, LevelRules.MaxLevel - (effectiveLevel + 1)); // after next level
        int futureLevelsCost = 0;

        for (int lvl = effectiveLevel + 1; lvl < LevelRules.MaxLevel; lvl++)
            futureLevelsCost += LevelRules.XPToNext(lvl);

        return remainingThisLevel + futureLevelsCost;
    }
}
