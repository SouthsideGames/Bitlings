using System;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────
// BattleManager.Backgrounds
// Background assignment, transitions, and rift-scene visuals.
// ─────────────────────────────────────────────────────────────

public partial class BattleManager : MonoBehaviour
{
    [Serializable]
    private sealed class TypeBackgroundSet
    {
        public MonsterType type = MonsterType.None;
        [Header("Day/Night Pools")]
        [Tooltip("Daytime sprite pool for this type.")]
        public Sprite[] daySprites;

        [Tooltip("Nighttime sprite pool for this type.")]
        public Sprite[] nightSprites;
    }

    private enum TimeOfDayOverride
    {
        System,
        ForceDay,
        ForceNight
    }

    [Header("Battle Backgrounds")]
    [Tooltip("Background Image used on the player's side. This will be set to the SAME sprite as the wild background.")]
    [SerializeField] private Image playerBackground;

    [Tooltip("Background Image used on the wild's side. This will be set to the SAME sprite as the player background.")]
    [SerializeField] private Image wildBackground;

    [Tooltip("Fallback background used when the wild type has no configured sprites.")]
    [SerializeField] private Sprite defaultBackground;

    [Tooltip("Sprite pools per MonsterType. The wild monster's type selects the pool.")]
    [SerializeField] private TypeBackgroundSet[] backgroundsByType;

    [Header("Time of Day")]
    [Tooltip("How the background system determines Day vs Night.")]
    [SerializeField] private TimeOfDayOverride timeOfDay = TimeOfDayOverride.System;

    [Tooltip("Local time hour (0-23) when Night starts. Example: 18 = 6pm.")]
    [SerializeField, Range(0, 23)] private int nightStartHour = 18;

    [Tooltip("Local time hour (0-23) when Night ends. Example: 6 = 6am.")]
    [SerializeField, Range(0, 23)] private int nightEndHour = 6;

    [Tooltip("If true, picks a random sprite from the selected pool. If false, uses the first sprite.")]
    [SerializeField] private bool randomizeWithinType = true;

    [Tooltip("If true, random selection uses the deterministic battle RNG (seeded).")]
    [SerializeField] private bool useDeterministicBattleRng = true;

    [Tooltip("If true, disables background Images when no sprite can be resolved.")]
    [SerializeField] private bool disableImagesWhenMissing = true;

    private bool _backgroundDefaultsCaptured;
    private Image _defaultPlayerBackground;
    private Image _defaultWildBackground;

    /// <summary>
    /// Applies the battle background based on the current wild monster type.
    /// Safe to call even if backgrounds are not wired.
    /// </summary>
    private void TryApplyBattleBackgroundFromWild()
    {
        var t = (wildDef != null) ? wildDef.type : MonsterType.None;
        ApplyBattleBackground(t);
    }

    /// <summary>
    /// Public helper for UI systems that need to re-apply backgrounds after wiring/rebuilds.
    /// </summary>
    public void ForceRefreshBattleBackground()
    {
        TryApplyBattleBackgroundFromWild();
    }

    public void SetBattleBackgroundOverride(Image overridePlayerBackground, Image overrideWildBackground)
    {
        if (!_backgroundDefaultsCaptured)
        {
            _backgroundDefaultsCaptured = true;
            _defaultPlayerBackground = playerBackground;
            _defaultWildBackground = wildBackground;
        }

        if (overridePlayerBackground) playerBackground = overridePlayerBackground;
        if (overrideWildBackground) wildBackground = overrideWildBackground;

        TryApplyBattleBackgroundFromWild();
    }

    public void ClearBattleBackgroundOverride()
    {
        if (!_backgroundDefaultsCaptured) return;

        playerBackground = _defaultPlayerBackground;
        wildBackground = _defaultWildBackground;
        TryApplyBattleBackgroundFromWild();
    }

    private void ApplyBattleBackground(MonsterType wildType)
    {
        if (!playerBackground && !wildBackground) return;

        Sprite s = ResolveBackgroundSprite(wildType);

        if (playerBackground)
        {
            playerBackground.sprite = s;
            if (disableImagesWhenMissing)
                playerBackground.enabled = (s != null);
        }

        if (wildBackground)
        {
            wildBackground.sprite = s;
            if (disableImagesWhenMissing)
                wildBackground.enabled = (s != null);
        }
    }

    private Sprite ResolveBackgroundSprite(MonsterType type)
    {
        bool isNight = IsNightRightNow();
        Sprite[] pool = null;

        if (backgroundsByType != null)
        {
            for (int i = 0; i < backgroundsByType.Length; i++)
            {
                var entry = backgroundsByType[i];
                if (entry == null) continue;
                if (entry.type != type) continue;

                pool = ResolvePoolForEntry(entry, isNight);
                break;
            }
        }

        if (pool == null || pool.Length == 0)
            return defaultBackground;

        if (!randomizeWithinType)
            return pool[0];

        if (useDeterministicBattleRng)
            EnsureBattleRngInitialized();

        float r = useDeterministicBattleRng ? Rng01() : UnityEngine.Random.value;
        int idx = Mathf.Clamp((int)(r * pool.Length), 0, pool.Length - 1);
        return pool[idx];
    }

    private Sprite[] ResolvePoolForEntry(TypeBackgroundSet entry, bool isNight)
    {
        if (isNight)
        {
            if (entry.nightSprites != null && entry.nightSprites.Length > 0)
                return entry.nightSprites;

            if (entry.daySprites != null && entry.daySprites.Length > 0)
                return entry.daySprites;

            return null;
        }

        if (entry.daySprites != null && entry.daySprites.Length > 0)
            return entry.daySprites;

        if (entry.nightSprites != null && entry.nightSprites.Length > 0)
            return entry.nightSprites;

        return null;
    }

    private bool IsNightRightNow()
    {
        switch (timeOfDay)
        {
            case TimeOfDayOverride.ForceDay:
                return false;
            case TimeOfDayOverride.ForceNight:
                return true;
        }

        DateTime now = DateTime.Now;
        int hour = now.Hour;

        if (nightStartHour == nightEndHour)
            return false;

        if (nightStartHour < nightEndHour)
        {
            return (hour >= nightStartHour && hour < nightEndHour);
        }

        return (hour >= nightStartHour || hour < nightEndHour);
    }
}
