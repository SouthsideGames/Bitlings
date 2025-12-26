using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies passive HP regeneration to owned + team monsters.
/// - Uses MonsterDataSO.hpRegenPerHour if set; otherwise a default.
/// - Works on resume (offline catch-up) and light online ticks.
/// - KO stays KO until regen lifts HP above 0.
/// </summary>
[DefaultExecutionOrder(-275)]
public class HealthRegenSystem : MonoBehaviour
{
    public static HealthRegenSystem I { get; private set; }

    [Header("Defaults")]
    [Tooltip("HP regenerated per real-time hour if MonsterDataSO does not override.")]
    [SerializeField, Min(0f)] private float defaultRegenPerHour = 6f;

    [Tooltip("How often to tick regen while the app is open (seconds).")]
    [SerializeField, Min(5f)] private float tickSeconds = 30f;

    float _nextTickAt;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    void OnEnable()
    {
        TryApplyOfflineRegen();
        _nextTickAt = Time.unscaledTime + tickSeconds;
    }

    void Update()
    {
        if (Time.unscaledTime >= _nextTickAt)
        {
            _nextTickAt = Time.unscaledTime + tickSeconds;
            TickOnlineRegen();
        }
    }

    public void TryApplyOfflineRegen()
    {
        if (SaveManager.Data == null) return;
        ApplyRegen(SaveManager.NowUnix(), null);
    }

    public void TickOnlineRegen()
    {
        if (SaveManager.Data == null) return;
        ApplyRegen(SaveManager.NowUnix(), (long)tickSeconds);
    }

    void ApplyRegen(long nowUnix, long? deltaSecondsOverride)
    {
        var lib = MonsterLibraryLocator.Lib;
        if (lib == null) return;

        bool changed = false;

        // Owned collection
        var owned = SaveManager.Data.owned ?? new List<OwnedMonsterData>();
        for (int i = 0; i < owned.Count; i++)
        {
            var e = owned[i];
            if (e == null || string.IsNullOrEmpty(e.monsterId)) continue;

            var def = MonsterLibraryLocator.GetById(e.monsterId);
            if (def == null) continue;

            int maxHP = HealingService.CalcMaxHP(def, Mathf.Max(1, e.level));
            if (e.currentHP < 0) { e.currentHP = maxHP; e.lastHPUnix = nowUnix; owned[i] = e; changed = true; continue; }

            long last = e.lastHPUnix > 0 ? e.lastHPUnix : SaveManager.Data.lastSavedUnix;
            long delta = deltaSecondsOverride ?? Math.Max(0, nowUnix - Math.Max(0, last));
            if (delta <= 0) continue;

            float perHour = def.hpRegenPerHour > 0f ? def.hpRegenPerHour : defaultRegenPerHour;
            int gain = Mathf.FloorToInt(perHour * (delta / 3600f));
            if (gain <= 0) { e.lastHPUnix = nowUnix; owned[i] = e; continue; }

            int before = e.currentHP;
            e.currentHP = Mathf.Clamp(before + gain, 0, maxHP);
            e.lastHPUnix = nowUnix;

            if (e.currentHP != before) { owned[i] = e; changed = true; }
        }

        // Team mirror
        var team = SaveManager.Data.team ?? new List<OwnedMonsterData>();
        for (int i = 0; i < team.Count; i++)
        {
            var t = team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            var def = MonsterLibraryLocator.GetById(t.monsterId);
            if (def == null) continue;

            int maxHP = HealingService.CalcMaxHP(def, Mathf.Max(1, t.level));
            if (t.currentHP < 0) { t.currentHP = maxHP; t.lastHPUnix = nowUnix; team[i] = t; changed = true; continue; }

            long last = t.lastHPUnix > 0 ? t.lastHPUnix : SaveManager.Data.lastSavedUnix;
            long delta = deltaSecondsOverride ?? Math.Max(0, nowUnix - Math.Max(0, last));
            if (delta <= 0) continue;

            float perHour = def.hpRegenPerHour > 0f ? def.hpRegenPerHour : defaultRegenPerHour;
            int gain = Mathf.FloorToInt(perHour * (delta / 3600f));
            if (gain <= 0) { t.lastHPUnix = nowUnix; team[i] = t; continue; }

            int before = t.currentHP;
            t.currentHP = Mathf.Clamp(before + gain, 0, maxHP);
            t.lastHPUnix = nowUnix;

            if (t.currentHP != before) { team[i] = t; changed = true; }
        }

        if (changed)
        {
            SaveManager.Data.owned = owned;
            SaveManager.Data.team = team;
            SaveManager.Save();
            GameEvents.OnTeamChanged?.Invoke();
        }
    }
    
    public static float GetDefaultRegenPerHour()
    {
        return I ? I.defaultRegenPerHour : 6f; // fall back to 6 if system not present yet
    }

    public static void ForceApplyNow() => I?.TryApplyOfflineRegen();
}
