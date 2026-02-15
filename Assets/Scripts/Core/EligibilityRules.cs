using UnityEngine;


public static class EligibilityRules
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Encounter / Battle
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// True if the Encounter action (start battle) should be available.
    /// </summary>
    public static bool CanStartEncounter(int minRequiredAliveTeamMembers, out string reason)
    {
        reason = null;

        bool inBattle = (EncounterManager.I != null) && EncounterManager.I.IsInBattle;
        if (inBattle)
        {
            reason = "Already in battle.";
            return false;
        }

        if (!HasMinimumAliveTeam(minRequiredAliveTeamMembers))
        {
            reason = "No healthy team.";
            return false;
        }

        if (WorldEventSystem.I != null && WorldEventSystem.I.AreEncountersDisabled())
        {
            reason = "Encounters suspended.";
            return false;
        }

        if (!HasRequiredEnergyOrFree(out int needed, out int current))
        {
            reason = $"Need {needed} energy (have {current}).";
            return false;
        }

        return true;
    }

    /// <summary>
    /// True if a specific team entry can participate in battle.
    /// </summary>
    public static bool CanBattle(OwnedMonsterData teamEntry, out string reason)
    {
        reason = null;
        if (teamEntry == null)
        {
            reason = "Missing monster.";
            return false;
        }

        if (teamEntry.currentHP <= 0)
        {
            reason = "0 HP monsters can't battle.";
            return false;
        }

        return true;
    }

    public static bool HasMinimumAliveTeam(int minMembers)
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null) return false;

        int count = 0;
        for (int i = 0; i < data.team.Count; i++)
        {
            var entry = data.team[i];
            if (entry == null) continue;
            if (string.IsNullOrEmpty(entry.monsterId)) continue;

            // Alive means HP > 0. (HP can briefly go negative during battle calc.)
            if (entry.currentHP > 0)
            {
                count++;
                if (count >= minMembers) return true;
            }
        }

        return false;
    }

    public static bool HasRequiredEnergyOrFree(out int needed, out int current)
    {
        // Free encounter bypasses the energy check.
        if (EncounterManager.I != null && EncounterManager.I.NextEncounterIsFree)
        {
            needed = 0;
            current = (EncounterManager.I != null) ? Mathf.Max(0, EncounterManager.I.GetEnergyPoints()) : Mathf.Max(0, ResourceBank.Get(ResourceType.Energy));
            return true;
        }

        needed = 1;
        current = 0;

        if (EncounterManager.I != null)
        {
            needed = Mathf.Max(1, EncounterManager.I.GetEncounterCost());
            current = Mathf.Max(0, EncounterManager.I.GetEnergyPoints());
        }
        else
        {
            needed = 1;
            current = Mathf.Max(0, ResourceBank.Get(ResourceType.Energy));
        }

        return current >= needed;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Jobs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Jobs are NOT HP-gated by design.
    /// Slot gating is controlled by slot cooldown + exhausted fatigue.
    /// </summary>
    public static bool CanUseJobSlot(JobType job, int slotIndex, out string reason, out long remainingSeconds)
    {
        reason = null;
        remainingSeconds = 0;

        if (WorldEventSystem.I != null && WorldEventSystem.I.IsJobSiteDisabled(job))
        {
            reason = "Site offline.";
            remainingSeconds = 0;
            return false;
        }

        var jm = JobManager.I;
        if (jm == null)
        {
            // When JobManager isn't present, treat as unavailable (prevents UI drift and null behavior).
            reason = "Jobs not available.";
            return false;
        }

        if (!jm.TryGetSlotCooldownRemainingSeconds(job, slotIndex, out remainingSeconds, out bool exhausted))
        {
            reason = "Invalid slot.";
            remainingSeconds = 0;
            return false;
        }

        if (remainingSeconds > 0)
        {
            reason = $"Resting: {FormatHm(remainingSeconds)}";
            return false;
        }

        if (exhausted)
        {
            reason = "Slot is exhausted.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validate a candidate worker for a job slot (type + slot readiness).
    /// (HP is intentionally ignored.)
    /// </summary>
    public static bool CanAssignWorkerToJobSlot(JobType job, int slotIndex, MonsterDataSO def, string ownedUid, out string reason)
    {
        reason = null;

        if (!def)
        {
            reason = "Missing monster.";
            return false;
        }

        var jm = JobManager.I;
        if (jm == null)
        {
            reason = "Jobs not available.";
            return false;
        }

        if (!jm.IsTypeEligibleFor(job, def.type))
        {
            reason = "Type not eligible.";
            return false;
        }

        if (!CanUseJobSlot(job, slotIndex, out string slotReason, out _))
        {
            reason = slotReason;
            return false;
        }

        return true;
    }

    private static string FormatHm(long seconds)
    {
        if (seconds <= 0) return "0m";
        long h = seconds / 3600;
        long m = (seconds % 3600) / 60;
        if (h > 0) return $"{h}h {m}m";
        return $"{Mathf.Max(1, (int)m)}m";
    }
}
