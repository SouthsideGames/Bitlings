using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-20)]
public class BossDebuffSystem : MonoBehaviour
{
    public static BossDebuffSystem I { get; private set; }

    // Fair-play rails
    private const int OFFLINE_GRACE_SECONDS = 30 * 60;      // 30m immune
    private const int OFFLINE_MAX_APPLY_SECONDS = 4 * 3600; // at most 4h while offline

    // In-memory index for quick lookups
    private readonly Dictionary<JobType, JobGlobalMod> _byType = new();

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    void OnEnable()
    {
        RestoreFromSave();
        GameEvents.BossSpawned   += OnBossSpawned;
        GameEvents.BossDefeated  += OnBossDefeated;
    }

    void OnDisable()
    {
        GameEvents.BossSpawned   -= OnBossSpawned;
        GameEvents.BossDefeated  -= OnBossDefeated;
    }

    // ——— External: JobManager calls this each tick to fetch the multiplier ———
    public static float GetMultiplier(JobType site, long nowUnix)
    {
        if (I == null || SaveManager.Data == null) return 1f;
        I.PruneExpired(nowUnix);
        if (I._byType.TryGetValue(site, out var mod) && nowUnix < mod.expiresUnix)
            return Mathf.Clamp(mod.multiplier, 0.1f, 1f);
        return 1f;
    }

    // ——— Events ———
    private void OnBossSpawned(string bossId, MonsterDataSO bossDef)
    {
        if (bossDef == null || bossDef.bossJobDebuffs == null || bossDef.bossJobDebuffs.Count == 0) return;
        long now = SaveManager.NowUnix();
        bool playerIsOnline = Application.isFocused; // swap if you track this differently

        foreach (var debuff in bossDef.bossJobDebuffs)
        {
            if (!playerIsOnline && !debuff.appliesWhileIdle) continue;
            ApplyDebuffWithFairPlay(bossId, debuff, now, playerIsOnline);
        }
    }

    private void OnBossDefeated(string bossId)
    {
        ClearBossDebuffs(bossId);
    }

    // ——— Core apply/clear ———
    private void ApplyDebuffWithFairPlay(string bossId, JobDebuff debuff, long now, bool playerIsOnline)
    {
        long lastSeen = SaveManager.Data?.lastSavedUnix ?? now; // already tracked in your save
        long offlineSec = Math.Max(0, now - lastSeen);

        if (!playerIsOnline)
        {
            if (offlineSec <= OFFLINE_GRACE_SECONDS) return;

            int durSec = debuff.durationHours * 3600;
            long effectiveElapsed = Math.Min(offlineSec, OFFLINE_MAX_APPLY_SECONDS);
            int remainingSec = Mathf.Max(1, durSec - (int)effectiveElapsed);
            var trimmed = debuff;
            trimmed.durationHours = Mathf.CeilToInt(remainingSec / 3600f);

            ApplyOne(bossId, trimmed, now);
            return;
        }

        ApplyOne(bossId, debuff, now);
    }

    private void ApplyOne(string bossId, JobDebuff debuff, long now)
    {
        long expires = now + debuff.durationHours * 3600L;

        if (_byType.TryGetValue(debuff.jobType, out var existing))
        {
            bool stronger = debuff.rateMultiplier < existing.multiplier;
            if (stronger)
            {
                existing.multiplier   = debuff.rateMultiplier;
                existing.expiresUnix  = Math.Max(existing.expiresUnix, expires);
                existing.sourceBossId = bossId;
            }
        }
        else
        {
            var m = new JobGlobalMod {
                jobType      = debuff.jobType,
                multiplier   = debuff.rateMultiplier,
                expiresUnix  = expires,
                sourceBossId = bossId
            };
            SaveManager.Data.activeJobMods ??= new List<JobGlobalMod>();
            SaveManager.Data.activeJobMods.Add(m);
            _byType[debuff.jobType] = m;
        }

        SaveManager.Save();
        GameEvents.JobGlobalModsChanged?.Invoke();
    }

    private void ClearBossDebuffs(string bossId)
    {
        var list = SaveManager.Data?.activeJobMods;
        if (list == null || list.Count == 0) return;

        bool changed = false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] != null && list[i].sourceBossId == bossId)
            {
                _byType.Remove(list[i].jobType);
                list.RemoveAt(i);
                changed = true;
            }
        }

        if (changed)
        {
            SaveManager.Save();
            GameEvents.JobGlobalModsChanged?.Invoke();
        }
    }

    private void PruneExpired(long now)
    {
        var list = SaveManager.Data?.activeJobMods;
        if (list == null || list.Count == 0) return;

        bool changed = false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null || now >= list[i].expiresUnix)
            {
                if (list[i] != null) _byType.Remove(list[i].jobType);
                list.RemoveAt(i);
                changed = true;
            }
        }

        if (changed)
        {
            SaveManager.Save();
            GameEvents.JobGlobalModsChanged?.Invoke();
        }
    }

    private void RestoreFromSave()
    {
        _byType.Clear();
        var list = SaveManager.Data?.activeJobMods;
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m == null) continue;
            _byType[m.jobType] = m;
        }
    }
}
