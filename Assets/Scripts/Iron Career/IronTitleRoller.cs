using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class IronTitleRoller
{
    [Header("Policy")]
    [Range(0f, 1f)] public float chanceHasTitlePlayer = 1f;
    [Range(0f, 1f)] public float chanceHasTitleWild = 1f;

    public TitleSO RollLockedTitle(MonsterDataSO def, int level, IronRngStream rng, bool isWild)
    {
        if (def == null) return null;
        if (def.titleTrack == null) return null;

        float chance = isWild ? chanceHasTitleWild : chanceHasTitlePlayer;
        if (rng != null && !rng.Chance(chance))
            return null;

        var candidates = CollectCandidates(def.titleTrack, Mathf.Max(1, level));
        if (candidates == null || candidates.Count == 0)
            return null;

        int idx = (rng != null) ? rng.NextInt(0, candidates.Count) : UnityEngine.Random.Range(0, candidates.Count);
        idx = Mathf.Clamp(idx, 0, candidates.Count - 1);
        return candidates[idx];
    }

    private static List<TitleSO> CollectCandidates(TitleTrackSO track, int level)
    {
        if (track == null || track.tiers == null) return null;

        var list = new List<TitleSO>(8);
        for (int i = 0; i < track.tiers.Count; i++)
        {
            var tier = track.tiers[i];
            if (tier == null) continue;
            if (level < tier.levelRequired) continue;
            var arr = tier.unlockChoices;
            if (arr == null) continue;
            for (int j = 0; j < arr.Count; j++)
            {
                var t = arr[j];
                if (t == null) continue;
                // Ignore any gating on TitleSO; Iron uses the track directly.
                list.Add(t);
            }
        }
        return list;
    }
}
