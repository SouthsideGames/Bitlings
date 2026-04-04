using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class IronTitleRoller
{
    [Header("Policy")]
    [Range(0f, 1f)] public float chanceHasTitlePlayer = 1f;
    [Range(0f, 1f)] public float chanceHasTitleWild = 1f;

    [Header("Iron Policy")]
    [Tooltip("If ironTitles exist, chance to use that curated pool before falling back to the normal title track.")]
    [Range(0f, 1f)] public float chanceUseIronCuratedPool = 0.85f;

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

    public TitleSO RollIronTitle(MonsterDataSO def, int level, IronRngStream rng, bool isWild)
    {
        if (def == null) return null;

        float chance = isWild ? chanceHasTitleWild : chanceHasTitlePlayer;
        if (rng != null && !rng.Chance(chance))
            return null;

        if (!isWild && def.ironTitles != null && def.ironTitles.Length > 0)
        {
            bool useCurated = rng != null ? rng.Chance(chanceUseIronCuratedPool) : UnityEngine.Random.value <= chanceUseIronCuratedPool;
            if (useCurated)
            {
                var curated = PickFromArray(def.ironTitles, rng);
                if (curated != null)
                    return curated;
            }
        }

        if (def.titleTrack == null) return null;

        var candidates = CollectCandidates(def.titleTrack, Mathf.Max(1, level));
        if (candidates == null || candidates.Count == 0)
            return null;

        int idx = (rng != null) ? rng.NextInt(0, candidates.Count) : UnityEngine.Random.Range(0, candidates.Count);
        idx = Mathf.Clamp(idx, 0, candidates.Count - 1);
        return candidates[idx];
    }

    private static TitleSO PickFromArray(TitleSO[] arr, IronRngStream rng)
    {
        if (arr == null || arr.Length == 0) return null;

        int attempts = arr.Length;
        for (int i = 0; i < attempts; i++)
        {
            int idx = (rng != null) ? rng.NextInt(0, arr.Length) : UnityEngine.Random.Range(0, arr.Length);
            var t = arr[idx];
            if (t != null) return t;
        }

        for (int i = 0; i < arr.Length; i++)
            if (arr[i] != null)
                return arr[i];

        return null;
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
