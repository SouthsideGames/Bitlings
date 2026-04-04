using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Dev-only invariant harness: detects persistent "bleed" into the main save while Iron Career is active.
/// 
/// IMPORTANT:
/// - We DO want background systems (jobs, timers) to keep running.
/// - Therefore we must ignore volatile fields that are expected to change (timestamps, tick markers).
/// - We still flag meaningful persistence changes (team/owned composition, rank/xp, etc.).
/// - Iron run rewards (Credits/Growth Cores) are intentionally persisted and are normalized out.
/// </summary>
public static class IronNoBleedHarness
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool BreakOnViolation { get; set; } = true;

    private static string _baselineHash;
    private static string _baselineJson;
    private static string _lastReportedHash;
    private static bool _armed;

    private const int JsonPreviewChars = 1200;

    [Serializable]
    private sealed class Snapshot
    {
        // Core no-bleed requirements
        public System.Collections.Generic.List<OwnedMonsterData> team;
        public System.Collections.Generic.List<OwnedMonsterData> owned;
        public System.Collections.Generic.List<int> resourceCounts;
        public int credits;

        // Secondary "should not move" signals
        public int promotionRank;
        public int promotionXP;
        public int winStreak;
        public int encounterPoints;
        public int encounterMax;
        public int encounterCost;
        public long energyLastUnix;
        public float energyRemainderSecs;
    }

    /// <summary>Arm the harness for a run and capture baseline persistent state.</summary>
    public static void Arm(string context)
    {
        _armed = true;
        CaptureBaseline(context);
    }

    /// <summary>Disarm the harness (no further checks). Call when leaving Iron.</summary>
    public static void Disarm()
    {
        _armed = false;
        _baselineHash = null;
        _baselineJson = null;
        _lastReportedHash = null;
    }

    public static void CaptureBaseline(string context)
    {
        if (!_armed) return;

        var snap = BuildSnapshotSanitized();
        _baselineJson = JsonUtility.ToJson(snap);
        _baselineHash = Hash(_baselineJson);
        _lastReportedHash = null;
    }

    public static void AssertUnchanged(string context)
    {
        if (!_armed) return;

        if (string.IsNullOrEmpty(_baselineHash))
        {
            CaptureBaseline("auto");
            return;
        }

        var snap = BuildSnapshotSanitized();
        string json = JsonUtility.ToJson(snap);
        string h = Hash(json);

        if (h == _baselineHash)
        {
            _lastReportedHash = null;
            return;
        }

        if (_lastReportedHash == h)
            return;

        _lastReportedHash = h;

        Debug.LogError(
            "[IronNoBleedHarness] SAVE BLEED DETECTED during Iron Career. " +
            $"context='{context}' baselineHash={_baselineHash} currentHash={h}\n" +
            $"BaselinePreview: {Preview(_baselineJson)}\n" +
            $"CurrentPreview: {Preview(json)}");

#if UNITY_EDITOR
    if (BreakOnViolation)
        Debug.Break();
#endif
    }

    /// <summary>
    /// Builds a snapshot but normalizes known volatile fields that legitimately change while Iron is active
    /// (jobs/offline, regen timestamps, training timers, etc.).
    /// </summary>
    private static Snapshot BuildSnapshotSanitized()
    {
        var p = SaveManager.Data;
        if (p == null)
        {
            return new Snapshot
            {
                team = null,
                owned = null,
                resourceCounts = null,
                credits = 0,
                promotionRank = 0,
                promotionXP = 0,
                winStreak = 0,
                encounterPoints = 0,
                encounterMax = 0,
                encounterCost = 0,
                energyLastUnix = 0,
                energyRemainderSecs = 0f
            };
        }

        // Clone lists so we can normalize without touching live data.
        var teamCopy = CloneListOwnedMonsters(p.team);
        var ownedCopy = CloneListOwnedMonsters(p.owned);

        // Normalize volatile fields inside monsters (these are expected to move from background systems).
        NormalizeMonsters(teamCopy);
        NormalizeMonsters(ownedCopy);

        // Resource rewards are now an intentional Iron persistence path.
        // Keep guarding resources, but ignore Credit/GrowthCore buckets specifically.
        var resourcesCopy = CloneIntList(p.resourceCounts);
        NormalizeAllowedIronResourceDeltas(resourcesCopy);

        int creditsCopy = 0;

        // promotionRank/XP also must not change during Iron; DO NOT normalize.
        // encounter points and related fields should not change during Iron; DO NOT normalize.

        // Energy can legitimately tick on resume; choose whether to normalize.
        // If energy ticking during Iron is acceptable (common on mobile), normalize it.
        // If you want energy to be strictly frozen during Iron, comment these two lines out.
        long energyLastUnix = 0;
        float energyRemainderSecs = 0f;

        return new Snapshot
        {
            team = teamCopy,
            owned = ownedCopy,
            resourceCounts = resourcesCopy,
            credits = creditsCopy,

            promotionRank = p.promotionRank,
            promotionXP = p.promotionXP,
            winStreak = p.winStreak,
            encounterPoints = p.encounterPoints,
            encounterMax = p.encounterMax,
            encounterCost = p.encounterCost,

            energyLastUnix = energyLastUnix,
            energyRemainderSecs = energyRemainderSecs,
        };
    }

    // ─────────────────────────────────────────────────────────────
    // Normalization helpers
    // ─────────────────────────────────────────────────────────────

    private static System.Collections.Generic.List<OwnedMonsterData> CloneListOwnedMonsters(System.Collections.Generic.List<OwnedMonsterData> src)
    {
        if (src == null) return null;

        var copy = new System.Collections.Generic.List<OwnedMonsterData>(src.Count);
        for (int i = 0; i < src.Count; i++)
        {
            // OwnedMonsterData appears to be a class in your project; if it's a struct, this still works.
            // We shallow-copy by JSON to avoid missing fields while also not mutating the live object.
            var m = src[i];
            if (m == null) { copy.Add(null); continue; }

            // Safe deep-ish copy via JsonUtility (works with [Serializable] classes/structs).
            string j = JsonUtility.ToJson(m);
            var clone = JsonUtility.FromJson<OwnedMonsterData>(j);
            copy.Add(clone);
        }
        return copy;
    }

    private static void NormalizeMonsters(System.Collections.Generic.List<OwnedMonsterData> list)
    {
        if (list == null) return;

        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m == null) continue;

            // These fields showed up in your violation log and are typical volatile/ticking markers.
            // Set them to a constant so your no-bleed hash remains stable.
            //
            // NOTE: Do NOT normalize currentHP/level/xp here because those are core gameplay state.
            // If your background systems can change those, that's real bleed and should be flagged.
            m.lastHPUnix = 0;
            m.trainingLastUnix = 0;
            m.lastLevelClaimDay = 0;

            // If you later see other drifting fields in logs, normalize them here.
            // Examples you might have:
            // m.lastOnlineUnix = 0;
            // m.lastSaveUnix = 0;
        }
    }

    private static System.Collections.Generic.List<int> CloneIntList(System.Collections.Generic.List<int> src)
    {
        if (src == null) return null;
        return new System.Collections.Generic.List<int>(src);
    }

    private static void NormalizeAllowedIronResourceDeltas(System.Collections.Generic.List<int> resourceCounts)
    {
        if (resourceCounts == null) return;

        int creditsIdx = (int)ResourceType.Credits;
        if (creditsIdx >= 0 && creditsIdx < resourceCounts.Count)
            resourceCounts[creditsIdx] = 0;

        int growthCoreIdx = (int)ResourceType.GrowthCore;
        if (growthCoreIdx >= 0 && growthCoreIdx < resourceCounts.Count)
            resourceCounts[growthCoreIdx] = 0;
    }

    private static string Hash(string s)
    {
        if (s == null) return "null";
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(s);
        var hash = sha.ComputeHash(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
            sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }

    private static string Preview(string s)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        if (s.Length <= JsonPreviewChars) return s;
        return s.Substring(0, JsonPreviewChars) + "…";
    }
#else
    public static void Arm(string context) { }
    public static void Disarm() { }
    public static void CaptureBaseline(string context) { }
    public static void AssertUnchanged(string context) { }
#endif
}