// Assets/Scripts/Arena/ArenaTeamValidator.cs
// BRN Arena v1 — Validates the arena battle team for tournament entry eligibility.
// Pure validation logic, no side effects.

using System;
using System.Collections.Generic;

/// <summary>
/// Validates the current arena battle team.
/// All methods are static and read-only — they never modify save data.
/// </summary>
public static class ArenaTeamValidator
{
    /// <summary>Returns true if the battle team has all 3 slots filled with valid references.</summary>
    public static bool IsBattleTeamComplete()
    {
        return ArenaSaveHelper.IsBattleTeamComplete();
    }

    /// <summary>Returns true if the battle team is locked to an active tournament.</summary>
    public static bool IsBattleTeamLocked()
    {
        return ArenaSaveHelper.IsBattleTeamLocked();
    }

    /// <summary>
    /// Full validation check: team is complete, all references resolve, no duplicate instances,
    /// and a visibility mode is selected. Returns true only if every check passes.
    /// </summary>
    public static bool IsBattleTeamValid()
    {
        return GetBattleTeamValidationErrors().Count == 0;
    }

    /// <summary>
    /// Returns a list of human-readable validation error strings.
    /// An empty list means the team is valid and ready for tournament entry.
    /// </summary>
    public static List<string> GetBattleTeamValidationErrors()
    {
        var errors = new List<string>();
        var arena = SaveManager.GetArenaSaveData();

        if (arena == null)
        {
            errors.Add("Arena data not initialized.");
            return errors;
        }

        var team = arena.battleTeamData;
        if (team == null)
        {
            errors.Add("Battle team data missing.");
            return errors;
        }

        var data = SaveManager.Data;
        if (data == null)
        {
            errors.Add("Player save data unavailable.");
            return errors;
        }

        // Collect slot UIDs.
        var slotUids = new string[]
        {
            team.slot1OwnedBitlingId ?? "",
            team.slot2OwnedBitlingId ?? "",
            team.slot3OwnedBitlingId ?? ""
        };

        // ─── Check: exactly 3 Bitlings required ───
        int filledCount = 0;
        for (int i = 0; i < slotUids.Length; i++)
        {
            if (!string.IsNullOrEmpty(slotUids[i]))
                filledCount++;
        }

        if (filledCount < ArenaConstants.BattleTeamSize)
            errors.Add($"Battle team requires {ArenaConstants.BattleTeamSize} Bitlings ({filledCount} assigned).");

        // ─── Check: valid references only ───
        var seenUids = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < slotUids.Length; i++)
        {
            string uid = slotUids[i];
            if (string.IsNullOrEmpty(uid)) continue;

            // Resolve from owned collection.
            var owned = FindOwnedByUid(data, uid);
            if (owned == null || string.IsNullOrEmpty(owned.monsterId))
            {
                errors.Add($"Slot {i + 1} references an unknown Bitling (uid: {uid}).");
                continue;
            }

            // ─── Check: no duplicate owned instance ───
            if (!seenUids.Add(uid))
                errors.Add($"Slot {i + 1} contains a duplicate instance (same Bitling assigned twice).");
        }

        // ─── Check: visibility mode must be selected ───
        // Both FullReveal (0) and LimitedReveal (1) are valid; we just verify the value
        // is within the enum range. Since the enum only has 0 and 1 right now, any other
        // value would be invalid.
        int modeVal = (int)team.visibilityMode;
        if (modeVal < 0 || modeVal > 1)
            errors.Add("Visibility mode is invalid. Select FullReveal or LimitedReveal.");

        return errors;
    }

    // ─────────────────────────────────────────────

    private static OwnedMonsterData FindOwnedByUid(PlayerManager data, string uid)
    {
        if (data == null || string.IsNullOrEmpty(uid)) return null;

        if (data.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var o = data.owned[i];
                if (o != null && string.Equals(o.ownedUID, uid, StringComparison.Ordinal))
                    return o;
            }
        }

        if (data.team != null)
        {
            for (int i = 0; i < data.team.Count; i++)
            {
                var t = data.team[i];
                if (t != null && string.Equals(t.ownedUID, uid, StringComparison.Ordinal))
                    return t;
            }
        }

        return null;
    }
}
