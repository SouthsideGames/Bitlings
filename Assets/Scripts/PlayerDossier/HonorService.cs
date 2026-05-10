using System;
using System.Collections.Generic;
using UnityEngine;

public static class HonorService
{
    public static bool CanHonor(string mentorUID) // FIXED: 48-hour per-mentor cooldown instead of one-per-week global lock
    {
        if (string.IsNullOrEmpty(mentorUID)) return false;
        SaveManager.LoadOrCreate();
        if (!SaveManager.TryGetMentorRecord(mentorUID, out _)) return false;

        const long COOLDOWN_SECONDS = 48L * 3600L;
        var arena = SaveManager.GetArenaSaveData();
        if (arena?.mentorHonorCooldowns == null) return true;

        var entry = arena.mentorHonorCooldowns.Find(c => c.mentorUID == mentorUID);
        if (entry == null) return true;

        return (SaveManager.NowUnix() - entry.lastHonoredUnix) >= COOLDOWN_SECONDS;
    }

    public static bool CanHonorAny() // FIXED: checks each mentor individually against 48h cooldown
    {
        SaveManager.LoadOrCreate();
        var mentors = SaveManager.GetMentorHallSnapshot();
        if (mentors == null || mentors.Count == 0) return false;
        foreach (var m in mentors)
        {
            if (!string.IsNullOrEmpty(m.ownedUID) && CanHonor(m.ownedUID)) return true;
        }
        return false;
    }

    public static string HonorLegend(string mentorUID)
    {
        if (!CanHonor(mentorUID))
            return "Honor is unavailable right now.";

        if (!SaveManager.TryGetMentorRecord(mentorUID, out var mentor) || mentor == null)
            return "Retired monster not found.";

        var bonus = BuildBonus(mentor);
        long now = SaveManager.NowUnix();
        bonus.expiresAtUnix = now + 86400L;

        SaveManager.SetActiveHonorBonus(bonus, null);

        // FIXED: record per-mentor timestamp instead of single weekly flag
        var arenaForHonor = SaveManager.GetArenaSaveData();
        if (arenaForHonor != null)
        {
            arenaForHonor.mentorHonorCooldowns ??= new List<MentorHonorCooldown>();
            var existing = arenaForHonor.mentorHonorCooldowns.Find(c => c.mentorUID == mentorUID);
            if (existing != null)
                existing.lastHonoredUnix = SaveManager.NowUnix();
            else
                arenaForHonor.mentorHonorCooldowns.Add(new MentorHonorCooldown { mentorUID = mentorUID, lastHonoredUnix = SaveManager.NowUnix() });
        }

        SaveManager.Save();

        GameEvents.OnJobsChanged?.Invoke();
        GameEvents.BattleStatsChanged?.Invoke();
        GameEvents.HonorApplied?.Invoke(mentorUID);

        string name = string.IsNullOrWhiteSpace(mentor.displayName) ? "A retired monster" : mentor.displayName;
        GameEvents.RaiseToast($"{name} honored. {mentor.monsterType} monsters are inspired for 24 hours.");

        return null;
    }

    public static HonorBonusState GetActiveBonus()
    {
        SaveManager.LoadOrCreate();

        var bonus = SaveManager.GetActiveHonorBonusRaw();
        if (bonus == null)
            return null;

        long now = SaveManager.NowUnix();
        if (bonus.expiresAtUnix > now)
            return bonus;

        SaveManager.ClearActiveHonorBonus();
        SaveManager.Save();
        return null;
    }

    public static bool CanApplyCombatBonuses()
    {
        if (ExecutiveTrialRuntime.IsActive)
            return false;

        if (ArenaBattleSimulationScope.IsActive)
            return false;

        return true;
    }

    public static float GetHonorAttackMultiplier(MonsterType type)
    {
        if (!CanApplyCombatBonuses()) return 1f;
        var bonus = GetActiveBonus();
        if (bonus == null || bonus.honoredType != type || Mathf.Approximately(bonus.atkPct, 0f))
            return 1f;
        return 1f + bonus.atkPct;
    }

    public static float GetHonorDefenseMultiplier(MonsterType type)
    {
        if (!CanApplyCombatBonuses()) return 1f;
        var bonus = GetActiveBonus();
        if (bonus == null || bonus.honoredType != type || Mathf.Approximately(bonus.defPct, 0f))
            return 1f;
        return 1f + bonus.defPct;
    }

    public static float GetHonorJobMultiplier(MonsterType type)
    {
        var bonus = GetActiveBonus();
        if (bonus == null || bonus.honoredType != type || Mathf.Approximately(bonus.jobMul, 0f))
            return 1f;
        return bonus.jobMul;
    }

    public static float GetHonorXpMultiplier(MonsterType type)
    {
        var bonus = GetActiveBonus();
        if (bonus == null || bonus.honoredType != type || Mathf.Approximately(bonus.xpMul, 0f))
            return 1f;
        return bonus.xpMul;
    }

    public static void CheckWeekReset()
    {
        SaveManager.LoadOrCreate();

        long now = SaveManager.NowUnix();
        long nextReset = SaveManager.GetHonorWeekResetUnix();

        if (nextReset <= 0)
        {
            SaveManager.SetHonorWeekResetUnix(ComputeNextMondayLocalUnix(now));
            SaveManager.Save();
            return;
        }

        if (now < nextReset)
            return;

        SaveManager.SetCurrentWeekHonoredUID(null);
        SaveManager.SetHonorWeekResetUnix(ComputeNextMondayLocalUnix(now));
        SaveManager.Save();
    }

    private static HonorBonusState BuildBonus(MentorRecord mentor)
    {
        var state = new HonorBonusState
        {
            honoredUID = mentor.mentorUID,
            honoredType = mentor.monsterType,
            atkPct = 0f,
            defPct = 0f,
            xpMul = 0f,
            jobMul = 0f
        };

        switch (mentor.quality)
        {
            case MentorQuality.Bronze:
                state.xpMul = 1.08f;
                break;
            case MentorQuality.Silver:
                state.jobMul = 1.10f;
                break;
            case MentorQuality.Gold:
                state.atkPct = 0.05f;
                state.defPct = 0.05f;
                break;
            case MentorQuality.Legend:
                state.atkPct = 0.05f;
                state.defPct = 0.05f;
                state.xpMul = 1.08f;
                state.jobMul = 1.10f;
                break;
        }

        return state;
    }

    private static long ComputeNextMondayLocalUnix(long nowUnix)
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(nowUnix).ToLocalTime();
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0)
            daysUntilMonday = 7;

        DateTime nextMondayLocal = now.Date.AddDays(daysUntilMonday);
        return new DateTimeOffset(nextMondayLocal).ToUnixTimeSeconds();
    }
}
