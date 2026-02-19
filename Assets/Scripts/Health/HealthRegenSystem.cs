using System;
using System.Collections.Generic;
using UnityEngine;


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

        // --- Regen core ---
        // IMPORTANT:
        // 1) Do NOT overwrite lastHPUnix every tick when no HP is gained.
        //    If we do, UI countdowns never progress and monsters can appear "tied together".
        // 2) When HP is gained, preserve fractional remainder time by advancing lastHPUnix
        //    by only the time actually consumed.

        bool TryApplyToEntry(OwnedMonsterData e, MonsterDataSO def, out OwnedMonsterData updated)
        {
            updated = e;
            if (e == null || def == null) return false;

            int lvl = Mathf.Max(1, e.level);
            int maxHP = HealingService.CalcMaxHP(def, lvl);

            // Normalize legacy/uninitialized HP (never negative).
            // If it was negative, treat as full HP.
            if (updated.currentHP < 0)
            {
                OwnedMonsterHP.Normalize(ref updated, nowUnix, OwnedMonsterHP.Reason.OfflineRegen);
                return true;
            }

            // Nothing to do if already full.
            if (e.currentHP >= maxHP)
            {
                // Keep lastHPUnix stable; don't churn timers/UI.
                return false;
            }

            long last = e.lastHPUnix > 0 ? e.lastHPUnix : SaveManager.Data.lastSavedUnix;
            long delta = deltaSecondsOverride ?? Math.Max(0, nowUnix - Math.Max(0, last));
            if (delta <= 0) return false;

            float perHour = def.hpRegenPerHour > 0f ? def.hpRegenPerHour : defaultRegenPerHour;
            if (perHour <= 0.0001f) return false;

            // Convert regen to "seconds per 1 HP" and award whole HP based on elapsed.
            float secPerHpF = 3600f / perHour;
            int gained = (int)Math.Floor(delta / secPerHpF);
            if (gained <= 0)
            {
                // Do NOT update lastHPUnix here; we want elapsed time to accumulate.
                return false;
            }

            int before = Mathf.Clamp(e.currentHP, 0, maxHP);
            int after = Mathf.Clamp(before + gained, 0, maxHP);
            int actualGained = after - before;
            if (actualGained <= 0) return false;

            // Preserve remainder time:
            // move lastHPUnix forward by the time consumed for the HP we actually awarded.
            long consumedSeconds = (long)Math.Round(actualGained * secPerHpF);
            long newLast = Math.Min(nowUnix, last + Math.Max(0, consumedSeconds));
            if (newLast < 0) newLast = nowUnix;

            updated.currentHP = after;
            updated.lastHPUnix = newLast;

            // Final invariant safety.
            if (updated.currentHP < 0) updated.currentHP = 0;
            return true;
        }

        // Owned collection (source of truth)
        var owned = SaveManager.Data.owned ?? new List<OwnedMonsterData>();
        for (int i = 0; i < owned.Count; i++)
        {
            var e = owned[i];
            if (e == null || string.IsNullOrEmpty(e.monsterId)) continue;

            var def = MonsterLibraryLocator.GetById(e.monsterId);
            if (def == null) continue;

            if (TryApplyToEntry(e, def, out var updated))
            {
                owned[i] = updated;
                changed = true;
            }
        }

        // Team mirror:
        // Prefer mirroring from owned via ownedUID so a monster can't have two timers.
        var team = SaveManager.Data.team ?? new List<OwnedMonsterData>();
        for (int i = 0; i < team.Count; i++)
        {
            var t = team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            if (!string.IsNullOrEmpty(t.ownedUID))
            {
                var ownedMatch = SaveManager.GetOwnedByUid(t.ownedUID);
                if (ownedMatch != null && !string.IsNullOrEmpty(ownedMatch.monsterId))
                {
                    if (t.currentHP != ownedMatch.currentHP || t.lastHPUnix != ownedMatch.lastHPUnix)
                    {
                        t.currentHP = ownedMatch.currentHP;
                        t.lastHPUnix = ownedMatch.lastHPUnix;
                        team[i] = t;
                        changed = true;
                    }
                    continue;
                }
            }

            var def2 = MonsterLibraryLocator.GetById(t.monsterId);
            if (def2 == null) continue;

            if (TryApplyToEntry(t, def2, out var updatedT))
            {
                team[i] = updatedT;
                changed = true;
            }
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
