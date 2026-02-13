using System;
using System.Collections.Generic;
using UnityEngine;

public static class ShinySystems
{
    // ---------- Tuning ----------
    public const float JobAuraSlot1Min = 0.03f;   
    public const float JobAuraSlot1Max = 0.05f;   
    public const float JobAuraCap      = 0.10f;  

    public const float TrainingSpark   = 0.05f;  

    public const float SanctumPerShiny = 0.03f;   
    public const float SanctumCap      = 0.10f;  

    public const int   SynergyEveryN   = 5;     
    public const float SynergyStep     = 0.01f;   
    public const float SynergyCap      = 0.05f;   

    public const float LeadCaptureBonus = 0.01f;  

    public static OwnedMonsterData ResolveOwned(WorkerRef w)
    {
        if (w == null) return null;
        var data = SaveManager.Data;
        if (data == null) return null;

        // 1) Prefer explicit ownedUID
        string uid = w.ownedUID;

        string token = !string.IsNullOrEmpty(uid) ? uid : w.monsterId;
        if (string.IsNullOrEmpty(token))
            token = w.monsterId;
        if (string.IsNullOrEmpty(token)) return null;

        var list = data.owned;
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m != null && !string.IsNullOrEmpty(m.ownedUID) && m.ownedUID == token)
                    return m;
            }
        }

        OwnedMonsterData found = null;
        int count = 0;
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m != null && m.monsterId == token)
                {
                    count++;
                    if (count == 1) found = m;
                }
            }
        }
        if (count == 1) return found;

        return null;
    }

    public static bool IsWorkerShiny(WorkerRef w)
    {
        var owned = ResolveOwned(w);
        return owned != null && owned.isShiny;
    }

    // ---------- (1) Job Aura ----------
    public static float SiteShinyAuraMult(IReadOnlyList<WorkerRef> workers)
    {
        if (workers == null || workers.Count == 0) return 1f;

        float bonus = 0f;
        var slot0 = workers[0];
        if (IsWorkerShiny(slot0))
        {
            float roll = UnityEngine.Random.Range(JobAuraSlot1Min, JobAuraSlot1Max);
            bonus += roll;
        }
        bonus = Mathf.Clamp(bonus, 0f, JobAuraCap);
        return 1f + bonus;
    }

    // ---------- (2) Training Spark ----------
    public static float TrainingXpMult(OwnedMonsterData owned)
        => (owned != null && owned.isShiny) ? (1f + TrainingSpark) : 1f;

    // ---------- (3) Sanctum Favor ----------
    public static float SanctumDurationMult(IEnumerable<OwnedMonsterData> present)
    {
        int n = CountShinies(present);
        float reduction = Mathf.Min(n * SanctumPerShiny, SanctumCap);
        return 1f - reduction;
    }

    // ---------- (4) Global Collection Synergy ----------
    public static float GlobalPickupRangeBonus(IEnumerable<OwnedMonsterData> allOwned)
    {
        int shinies = CountShinies(allOwned);
        int steps = shinies / SynergyEveryN;
        return Mathf.Min(steps * SynergyStep, SynergyCap);
    }

    // ---------- (5) Charm of Fortune ----------
    public static float LeadCaptureMult(OwnedMonsterData lead)
        => (lead != null && lead.isShiny) ? (1f + LeadCaptureBonus) : 1f;

    public static int CountShinies(IEnumerable<OwnedMonsterData> list)
    {
        if (list == null) return 0;
        int n = 0;
        foreach (var m in list) if (m != null && m.isShiny) n++;
        return n;
    }
}
