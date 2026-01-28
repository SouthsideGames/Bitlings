// Assets/Scripts/DiagnosticsSnapshot.cs
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class DiagnosticsSnapshot
{
    // If you want more/less refresh spam, your UI can call BuildText() on a timer.
    public static string BuildText()
    {
        var sb = new StringBuilder(2048);

        var data = SaveManager.Data;
        long now = SaveManager.NowUnix();

        sb.AppendLine("=== BITLINGS DIAGNOSTICS ===");
        sb.AppendLine($"UTC Now: {now}  ({UnixToIso(now)})");
        sb.AppendLine();

        if (data == null)
        {
            sb.AppendLine("SaveManager.Data is NULL (save not loaded yet).");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────
        // Player / Save
        // ─────────────────────────────────────────────
        sb.AppendLine("— Player —");
        sb.AppendLine($"Name: {NullDash(data.playerName)}");
        sb.AppendLine($"ID:   {NullDash(data.playerId)}");
        sb.AppendLine($"Last Saved Unix: {data.lastSavedUnix}  ({UnixToIso(data.lastSavedUnix)})");
        sb.AppendLine($"Last Closed Unix: {data.lastClosedUnix}  ({UnixToIso(data.lastClosedUnix)})");
        sb.AppendLine();

        // ─────────────────────────────────────────────
        // Encounter / Energy
        // ─────────────────────────────────────────────
        sb.AppendLine("— Encounter / Energy —");
        sb.AppendLine($"Energy: {SafeRes(ResourceType.Energy)}/{data.encounterMax}  (cost: {data.encounterCost})");
        sb.AppendLine($"Energy Last Unix: {data.energyLastUnix}  ({UnixToIso(data.energyLastUnix)})");
        sb.AppendLine($"Energy Remainder Secs: {data.energyRemainderSecs:0.00}");
        sb.AppendLine($"Encounters Since Boss: {data.encountersSinceBoss}  (bossEveryN: {data.bossEveryN})");
        sb.AppendLine($"Last Boss: {NullDash(data.lastBossId)}");
        sb.AppendLine();

        // ─────────────────────────────────────────────
        // Team / Owned
        // ─────────────────────────────────────────────
        sb.AppendLine("— Monsters —");
        sb.AppendLine($"Team Count: {SafeCount(data.team)}");
        sb.AppendLine($"Owned Count: {SafeCount(data.owned)}");
        sb.AppendLine($"Discovered: {SafeCount(data.discoveredMonsterIdsList)}");
        sb.AppendLine($"Favorites: {SafeCount(data.favoriteMonsterIdsList)}");
        sb.AppendLine();

        // ─────────────────────────────────────────────
        // Resources (quick sample)
        // ─────────────────────────────────────────────
        sb.AppendLine("— Resources —");
        sb.AppendLine($"Credits: {SafeRes(ResourceType.Credits)}");
        sb.AppendLine($"PackVoucher: {SafeRes(ResourceType.PackVoucher)}");
        sb.AppendLine($"Medkit: {SafeRes(ResourceType.Medkit)}");
        sb.AppendLine();

        // ─────────────────────────────────────────────
        // Timed Buffs
        // ─────────────────────────────────────────────
        sb.AppendLine("— Timed Boosts / Mods —");

        AppendTimedList(sb, "Flyers (activeFlyers)", data.activeFlyers, now,
            item =>
            {
                if (item == null) return "null";
                return $"{item.type}  bonus:{item.bonus:0.###}  exp:{item.expireUnix} ({Remain(item.expireUnix, now)})";
            });

        AppendTimedList(sb, "Work Orders (activeWorkOrders)", data.activeWorkOrders, now,
            item =>
            {
                if (item == null) return "null";
                return $"bonus:{item.bonus:0.###}  exp:{item.expireUnix} ({Remain(item.expireUnix, now)})";
            });

        // ✅ FIXED: JobGlobalMod uses jobType / multiplier / expiresUnix
        AppendTimedList(sb, "Job Mods (activeJobMods)", data.activeJobMods, now,
            item =>
            {
                if (item == null) return "null";
                // JobGlobalMod fields per your BossDebuffTypes.cs
                return $"{item.jobType}  mult:{item.multiplier:0.###}  exp:{item.expiresUnix} ({Remain(item.expiresUnix, now)})";
            });

        AppendTimedList(sb, "Favor Boosts (activeFavorBoosts)", data.activeFavorBoosts, now,
            item =>
            {
                if (item == null) return "null";
                return $"bonus:{item.bonus:0.###}  exp:{item.expireUnix} ({Remain(item.expireUnix, now)})";
            });

        AppendTimedList(sb, "Shiny Boosts (activeShinyBoosts)", data.activeShinyBoosts, now,
            item =>
            {
                if (item == null) return "null";
                return $"bonus:{item.bonus:0.###}  exp:{item.expireUnix} ({Remain(item.expireUnix, now)})";
            });

        sb.AppendLine();

        // ─────────────────────────────────────────────
        // Cheats lockout
        // ─────────────────────────────────────────────
        sb.AppendLine("— Cheats —");
        sb.AppendLine($"Invalid Attempts: {data.cheatInvalidAttempts}");
        sb.AppendLine($"Locked Until Unix: {data.cheatLockedUntilUnix} ({Remain(data.cheatLockedUntilUnix, now)})");
        sb.AppendLine();

        // ─────────────────────────────────────────────
        // Field Ops quick stats
        // ─────────────────────────────────────────────
        if (data.fieldOps != null)
        {
            sb.AppendLine("— Field Ops —");
            sb.AppendLine($"Encounters Initiated: {data.fieldOps.encountersInitiated}");
            sb.AppendLine($"Capture Attempts: {data.fieldOps.captureAttempts}");
            sb.AppendLine($"Captures Successful: {data.fieldOps.capturesSuccessful}");
            sb.AppendLine($"Rare Found: {data.fieldOps.rareBitlingsFound}");
            sb.AppendLine($"Shiny Discoveries: {data.fieldOps.shinyDiscoveries}");
            sb.AppendLine($"Longest Streak: {data.fieldOps.longestCaptureStreak}");
            sb.AppendLine($"Current Streak: {data.fieldOps.currentCaptureStreak}");
        }

        return sb.ToString();
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    static void AppendTimedList<T>(StringBuilder sb, string label, List<T> list, long now, Func<T, string> format)
    {
        int count = SafeCount(list);
        sb.AppendLine($"{label}: {count}");

        if (list == null || list.Count == 0)
        {
            sb.AppendLine("  (none)");
            return;
        }

        int shown = 0;
        for (int i = 0; i < list.Count; i++)
        {
            sb.AppendLine($"  - {format(list[i])}");
            shown++;
            // prevent runaway spam if list explodes
            if (shown >= 40)
            {
                sb.AppendLine("  ... (trimmed)");
                break;
            }
        }
    }

    static int SafeRes(ResourceType t)
    {
        try
        {
            ResourceBank.EnsureSize();
            return ResourceBank.Get(t);
        }
        catch
        {
            return 0;
        }
    }

    static int SafeCount<T>(List<T> list) => list == null ? 0 : list.Count;

    static string NullDash(string s) => string.IsNullOrEmpty(s) ? "—" : s;

    static string UnixToIso(long unix)
    {
        if (unix <= 0) return "—";
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        }
        catch { return "—"; }
    }

    static string Remain(long expireUnix, long now)
    {
        if (expireUnix <= 0) return "—";
        long delta = expireUnix - now;
        if (delta <= 0) return "expired";

        var ts = TimeSpan.FromSeconds(delta);
        long hours = (long)ts.TotalHours;
        return $"{hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    public static string Build(string context = "")
    {

        string body = BuildText();

        if (string.IsNullOrEmpty(context))
            return body;

        return $"[Context: {context}]\n{body}";
    }
}
