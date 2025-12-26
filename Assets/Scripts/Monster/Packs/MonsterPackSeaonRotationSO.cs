using UnityEngine;
using System;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Data/Monster Packs/Season Rotation", fileName = "MonsterPackSeasons")]
public class MonsterPackSeasonRotationSO : ScriptableObject
{
    [Serializable]
    public class SeasonEntry
    {
        [Tooltip("Season name shown in UI (e.g., 'Verdant Uprising').")]
        public string seasonName = "Season";

        [Tooltip("Pack IDs available during this season (e.g., 3 packs).")]
        public List<string> packIds = new List<string>();
    }

    [Header("Rotation")]
    [Tooltip("UTC unix timestamp (seconds) when rotation begins. If 0, code will treat as 'now'.")]
    public long rotationStartUnix = 0;

    [Tooltip("Default 30 (monthly-ish). Change to 7 for weekly seasons, etc.")]
    [Min(1)] public int seasonLengthDays = 30;

    [Tooltip("If true and seasonIndex exceeds entries count, wraps around.")]
    public bool loop = true;

    [Header("Seasons (in order)")]
    public List<SeasonEntry> seasons = new List<SeasonEntry>();

    public long SeasonLengthSeconds => (long)seasonLengthDays * 86400L;

    public int GetSeasonIndex(long nowUnix)
    {
        if (seasons == null || seasons.Count == 0) return -1;

        long start = rotationStartUnix;
        if (start <= 0) start = nowUnix; // first-run fallback

        long len = Math.Max(1, SeasonLengthSeconds);
        long elapsed = Math.Max(0, nowUnix - start);

        long rawIndex = elapsed / len;

        if (!loop)
            return (rawIndex >= seasons.Count) ? (seasons.Count - 1) : (int)rawIndex;

        return (int)(rawIndex % seasons.Count);
    }

    public int GetNextSeasonIndex(long nowUnix)
    {
        int idx = GetSeasonIndex(nowUnix);
        if (idx < 0) return -1;

        if (!loop) return Mathf.Min(idx + 1, seasons.Count - 1);
        return (idx + 1) % seasons.Count;
    }

    public long GetSeasonStartUnix(long nowUnix)
    {
        if (seasons == null || seasons.Count == 0) return nowUnix;

        long start = rotationStartUnix;
        if (start <= 0) start = nowUnix;

        long len = Math.Max(1, SeasonLengthSeconds);
        long elapsed = Math.Max(0, nowUnix - start);
        long rawIndex = elapsed / len;

        return start + rawIndex * len;
    }

    public long GetSeasonEndUnix(long nowUnix)
    {
        long start = GetSeasonStartUnix(nowUnix);
        return start + Math.Max(1, SeasonLengthSeconds);
    }

    public SeasonEntry GetActiveSeason(long nowUnix)
    {
        int idx = GetSeasonIndex(nowUnix);
        if (idx < 0 || idx >= seasons.Count) return null;
        return seasons[idx];
    }

    public SeasonEntry GetNextSeason(long nowUnix)
    {
        int idx = GetNextSeasonIndex(nowUnix);
        if (idx < 0 || idx >= seasons.Count) return null;
        return seasons[idx];
    }

    public IReadOnlyList<string> GetActivePackIds(long nowUnix) => GetActiveSeason(nowUnix)?.packIds;
    public IReadOnlyList<string> GetNextPackIds(long nowUnix) => GetNextSeason(nowUnix)?.packIds;
}
