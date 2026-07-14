// Assets/Scripts/Arena/ArenaTicketManager.cs
// BRN Arena v1 — Backend ticket logic: unlock checks, purchase, reward grants, weekly reset.

using System;
using UnityEngine;

/// <summary>
/// Static helper class for all arena ticket operations.
/// No MonoBehaviour — pure logic the UI can call later.
/// All timing uses Eastern Time via <see cref="ArenaConstants.EasternTimeZone"/>.
/// </summary>
public static class ArenaTicketManager
{
    private static bool _listening;

    /// <summary>
    /// Subscribes to <see cref="GameEvents.PromotionRankChanged"/> so the arena
    /// auto-unlocks when the player reaches the required promotion rank.
    /// Safe to call multiple times — will only subscribe once.
    /// Should be called early (e.g. from a boot MonoBehaviour).
    /// </summary>
    public static void StartListening()
    {
        if (_listening) return;
        _listening = true;
        GameEvents.PromotionRankChanged += OnPromotionRankChanged;
        RecoverPendingTicket();
    }

    /// <summary>
    /// Called at startup. If the last registration attempt was interrupted (crash between
    /// ticket-spend and cloud-code confirmation), refunds the ticket.
    /// </summary>
    public static void RecoverPendingTicket() // FIXED: recovers tickets lost to mid-registration crashes
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena == null || !arena.ticketSpentPendingRegistration) return;

        var status = arena.currentTournamentCache?.playerStatus
                     ?? ArenaPlayerTournamentStatus.NotEntered;

        if (status == ArenaPlayerTournamentStatus.NotEntered)
        {
            arena.arenaTickets = Math.Min(arena.arenaTickets + 1, ArenaConstants.MaxTickets);
            DevLog.Log("[ArenaTicketManager] Refunded ticket from interrupted registration."); // FIXED
        }

        arena.ticketSpentPendingRegistration = false;
        SaveManager.Save();
    }

    /// <summary>Unsubscribes from promotion events. Call on teardown if needed.</summary>
    public static void StopListening()
    {
        if (!_listening) return;
        _listening = false;
        GameEvents.PromotionRankChanged -= OnPromotionRankChanged;
    }

    private static void OnPromotionRankChanged(int oldRank, int newRank)
    {
        if (newRank >= ArenaConstants.ArenaUnlockLevel)
            UnlockArenaIfEligible();
    }
    // ═════════════════════════════════════════════════════════════
    //  Unlock
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>true</c> if the player's promotion rank meets or exceeds the arena unlock threshold.
    /// Does NOT check whether the feature has already been unlocked.
    /// </summary>
    public static bool CanUnlockArena()
    {
        var data = SaveManager.Data;
        if (data == null) return false;

        return data.promotionRank >= ArenaConstants.ArenaUnlockLevel;
    }

    /// <summary>
    /// Unlocks the arena feature and grants the one-time unlock reward ticket
    /// if the player is eligible and hasn't already unlocked.
    /// Returns <c>true</c> if the unlock actually happened this call.
    /// </summary>
    public static bool UnlockArenaIfEligible()
    {
        if (!CanUnlockArena()) return false;

        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return false;

        // Already unlocked — nothing to do.
        if (arena.arenaUnlocked) return false;

        // Mark unlocked in arena save data.
        arena.arenaUnlocked = true;

        // Unlock the feature gate so the rest of the game sees it.
        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.Unlock(FeatureId.Arena_Basic);

        // Grant one-time reward ticket (idempotent via flag).
        GrantUnlockRewardTicket(arena);

        SaveManager.Save();

        // Notify systems.
        GameEvents.RaiseToast("BRN Arena unlocked! You received 1 Arena Ticket.");
        GameEvents.ArenaDataChanged?.Invoke();
        GameEvents.OnResourcesChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// Grants the one-time unlock reward ticket if it hasn't been claimed yet.
    /// Clamps to cap. Safe to call multiple times — the flag prevents double-grant.
    /// </summary>
    private static void GrantUnlockRewardTicket(ArenaSaveData arena)
    {
        if (arena.unlockRewardClaimed) return;

        arena.unlockRewardClaimed = true;
        arena.arenaTickets = Mathf.Min(arena.arenaTickets + 1, ArenaConstants.MaxTickets);
    }

    // ═════════════════════════════════════════════════════════════
    //  Ticket queries
    // ═════════════════════════════════════════════════════════════

    /// <summary>Returns the current arena ticket count (0 if data unavailable).</summary>
    public static int GetTicketCount()
    {
        return ArenaSaveHelper.GetArenaTicketCount();
    }

    /// <summary>Returns the fixed credit cost for purchasing one arena ticket.</summary>
    public static int GetArenaTicketCost()
    {
        return ArenaConstants.TicketCreditCost;
    }

    /// <summary>
    /// Returns <c>true</c> if the player has at least one remaining weekly ticket purchase
    /// after accounting for any needed weekly reset.
    /// </summary>
    public static bool HasRemainingWeeklyArenaTicketPurchase()
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return false;

        ApplyWeeklyResetIfNeeded(arena);
        return arena.weeklyTicketsPurchased < ArenaConstants.WeeklyTicketPurchaseLimit;
    }

    /// <summary>
    /// Returns <c>true</c> if the player can buy a ticket right now:
    /// arena unlocked, under cap, within weekly limit, and has enough credits.
    /// </summary>
    public static bool CanBuyArenaTicket()
    {
        if (!ArenaSaveHelper.IsArenaUnlocked()) return false;

        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return false;

        ApplyWeeklyResetIfNeeded(arena);

        if (arena.arenaTickets >= ArenaConstants.MaxTickets) return false;
        if (arena.weeklyTicketsPurchased >= ArenaConstants.WeeklyTicketPurchaseLimit) return false;
        if (ResourceBank.Get(ResourceType.Credits) < ArenaConstants.TicketCreditCost) return false;

        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  Ticket purchase
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Attempts to purchase one arena ticket with Credits.
    /// Returns <c>true</c> on success. Credits are only deducted on success.
    /// </summary>
    public static bool TryBuyArenaTicket()
    {
        if (!ArenaSaveHelper.IsArenaUnlocked()) return false;

        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return false;

        ApplyWeeklyResetIfNeeded(arena);

        if (arena.arenaTickets >= ArenaConstants.MaxTickets) return false;
        if (arena.weeklyTicketsPurchased >= ArenaConstants.WeeklyTicketPurchaseLimit) return false;

        // Deduct credits atomically — fails if insufficient.
        if (!ResourceBank.TrySpend(ResourceType.Credits, ArenaConstants.TicketCreditCost))
            return false;

        arena.arenaTickets = Mathf.Min(arena.arenaTickets + 1, ArenaConstants.MaxTickets);
        arena.weeklyTicketsPurchased++;

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  Ticket reward grant (placement rewards)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Grants a ticket as a tournament reward for placements 1st–5th.
    /// Clamps to cap — excess is silently discarded.
    /// Returns <c>true</c> if at least one ticket was actually added.
    /// </summary>
    public static bool TryGrantArenaTicket(int placement)
    {
        if (placement < 1 || placement > 5) return false;

        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return false;

        int before = arena.arenaTickets;
        arena.arenaTickets = Mathf.Min(arena.arenaTickets + 1, ArenaConstants.MaxTickets);

        if (arena.arenaTickets == before) return false; // already at cap

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Spends one ticket for tournament entry.
    /// Returns <c>true</c> if the ticket was successfully consumed.
    /// </summary>
    public static bool TrySpendTicket()
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return false;

        if (arena.arenaTickets <= 0) return false;

        arena.arenaTickets--;
        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  Weekly reset
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks whether the weekly purchase allowance should reset by comparing
    /// the stored reset timestamp against the most recent Monday 00:00 ET.
    /// If a new week has started, resets <see cref="ArenaSaveData.weeklyTicketsPurchased"/>
    /// to 0 and updates the timestamp. Safe to call frequently — no-ops if still same week.
    /// </summary>
    public static void ApplyWeeklyResetIfNeeded(ArenaSaveData arena)
    {
        if (arena == null) return;

        long nowUtcEpoch = SaveManager.NowUnix();
        long weekStartUtc = GetCurrentWeekStartUtcEpoch();

        // If the stored reset timestamp is already within the current week, nothing to do.
        if (arena.lastTicketResetUtc >= weekStartUtc) return;

        arena.weeklyTicketsPurchased = 0;
        arena.lastTicketResetUtc = nowUtcEpoch;
    }

    /// <summary>
    /// Returns the UTC epoch of the most recent Monday 00:00:00 Eastern Time.
    /// All arena weekly cycles reset on Monday midnight ET.
    /// </summary>
    private static long GetCurrentWeekStartUtcEpoch()
    {
        TimeZoneInfo et = ArenaConstants.EasternTimeZone;
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset nowET = TimeZoneInfo.ConvertTime(nowUtc, et);

        // Roll back to Monday 00:00 ET.
        int daysSinceMonday = ((int)nowET.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        DateTime mondayMidnightET = nowET.Date.AddDays(-daysSinceMonday);

        // Convert back to UTC.
        DateTimeOffset mondayET = new DateTimeOffset(mondayMidnightET, et.GetUtcOffset(mondayMidnightET));
        return mondayET.ToUnixTimeSeconds();
    }
}
