using System;
using UnityEngine;

/// <summary>
/// Centralized energy (rift points) regeneration.
///
/// Why this exists:
/// - RiftManager already has regen logic, but that only runs if RiftManager is active.
/// - On app resume (or launching into a menu scene), players still expect Energy to catch up.
///
/// This system applies offline catch-up using SaveManager.Data.energyLastUnix + energyRemainderSecs
/// and writes the result into ResourceBank (ResourceType.Energy).
/// </summary>
public static class EnergyRegenSystem
{
    // Keep this consistent with RiftPanelUI + RiftManager defaults.
    public const float DefaultSecondsPerPoint = 1200f; // 20 minutes

    /// <summary>
    /// Applies offline regen once based on the elapsed time since the last energy tick.
    /// Safe to call multiple times; subsequent calls in the same frame will gain 0 due to lastUnix update.
    /// </summary>
    public static int TryApplyOfflineRegen(float secondsPerPoint = DefaultSecondsPerPoint)
    {
        SaveManager.LoadOrCreate();
        if (SaveManager.Data == null) return 0;

        ResourceBank.EnsureSize();

        int max = Mathf.Max(1, SaveManager.Data.riftMax > 0 ? SaveManager.Data.riftMax : 50);
        int cur = ResourceBank.Get(ResourceType.Energy);

        // If already full, advance lastUnix so time spent at cap is never banked
        // and credited back as an instant refill after spending.
        if (cur >= max)
        {
            long nowFull = SaveManager.NowUnix();
            if (SaveManager.Data.energyRemainderSecs != 0f || SaveManager.Data.energyLastUnix != nowFull)
            {
                SaveManager.Data.energyRemainderSecs = 0f;
                SaveManager.Data.energyLastUnix = nowFull;
                SaveManager.Save();
            }
            return 0;
        }

        long now = SaveManager.NowUnix();
        long last = SaveManager.Data.energyLastUnix > 0 ? SaveManager.Data.energyLastUnix : now;

        // Device clock moved backwards (the ledger is in the future). Do NOT forgive
        // by resetting the ledger to now — that made clock-forward→collect→roll-back
        // cycling an infinite energy exploit. Freeze regen until real time catches up,
        // bounded so a wildly-wrong clock can't freeze regen forever.
        if (last > now)
        {
            long cap = now + SaveManager.MaxRollbackFreezeSeconds;
            if (last > cap)
            {
                SaveManager.Data.energyLastUnix = cap;
                SaveManager.Save();
            }
            return 0;
        }

        long elapsed = now - last;
        if (elapsed <= 0) return 0;

        double total = Math.Max(0.0, SaveManager.Data.energyRemainderSecs) + elapsed;
        int gained = (int)Math.Floor(total / Mathf.Max(1f, secondsPerPoint));
        float newRem = (float)(total - (gained * secondsPerPoint));

        // Clamp remainder to < secondsPerPoint to prevent drift.
        float remCap = Mathf.Max(0f, secondsPerPoint - 0.001f);
        newRem = Mathf.Clamp(newRem, 0f, remCap);

        ResourceBank.BeginBatch();
        try
        {
            if (gained > 0)
            {
                int next = Mathf.Min(max, cur + gained);
                ResourceBank.Set(ResourceType.Energy, next);

                // If we hit full, remainder should be 0
                if (next >= max) newRem = 0f;
            }

            SaveManager.Data.energyLastUnix = now;
            SaveManager.Data.energyRemainderSecs = newRem;

            SaveManager.Save();
        }
        finally
        {
            ResourceBank.EndBatch();
        }

        if (gained > 0)
            GameEvents.EnergyChanged?.Invoke();

        return gained;
    }
}
