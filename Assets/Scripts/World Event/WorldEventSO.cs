using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/World Events/World Event", fileName = "WorldEvent_")]
public class WorldEventSO : ScriptableObject
{
    [Header("Identity")]
    public string id;
    public string displayName;

    [Header("Category")]
    public WorldEventCategory category = WorldEventCategory.Flavor;

    [Tooltip("If true, this is considered a holiday-style ticker item. (May be scheduled or rotate.)")]
    public bool isHoliday = false;

    [Header("Ticker")]
    [TextArea(2, 4)] public string tickerMessage;

    [Header("Scheduling (optional)")]
    [Tooltip("If true, this event only activates within the scheduled window.")]
    public bool scheduledOnly = false;

    [Tooltip("Unix seconds; 0 = ignore.")]
    public long startUnix;

    [Tooltip("Unix seconds; 0 = ignore.")]
    public long endUnix;

    [Header("Rotation (if not scheduledOnly)")]
    public bool canRotate = true;
    [Min(0)] public int weight = 1;

    [Tooltip("If > 0, this event should not be re-rolled until at least this many days have passed.")]
    [Min(0f)] public float minDaysBetween = 0f;

    [Header("Placeholder Effects")]
    public List<WorldEventEffect> effects = new();

    public bool IsActiveNow(long nowUnix)
    {
        if (startUnix <= 0 && endUnix <= 0) return !scheduledOnly;

        if (startUnix > 0 && nowUnix < startUnix) return false;
        if (endUnix > 0 && nowUnix > endUnix) return false;
        return true;
    }
}

public enum WorldEventCategory
{
    Job = 0,
    Encounter = 1,
    Meta = 2,
    Flavor = 3
}

[Serializable]
public struct WorldEventEffect
{
    public WorldEventEffectKind kind;

    [Tooltip("Optional job target (used by DisableJobSite / JobRateMultiplier).")]
    public JobType job;

    [Tooltip("Optional resource target (used by ResourceGainMultiplier).")]
    public ResourceType resource;

    [Tooltip("Generic numeric value (multipliers, etc.).")]
    public float value;

    [Tooltip("Optional boolean (used by JobCollectDisabled when you want explicit true/false).")]
    public bool flag;
}
