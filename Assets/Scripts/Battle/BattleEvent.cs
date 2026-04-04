using System;

public enum BattleSide
{
    Player = 0,
    Wild = 1
}

/// <summary>
/// Lightweight battle event payload emitted by BattleManager and consumed by UI/FX systems.
/// Intentionally plain-data (no UnityEngine refs) to keep combat logic decoupled.
/// </summary>
public readonly struct BattleEvent
{
    public enum Kind
    {
        Damage = 0,
        StatusApplied = 1,
        Swap = 2,
        KO = 3,
        Reward = 4,
        ActionWindup = 5,
        DefendResult = 6,
        GuardChanged = 7,
        ChargeChanged = 8,
        IntentTelegraph = 9,
        ActionQueued = 10,
        UIRefreshHP = 11
    }

    public readonly Kind kind;

    // Common sides
    public readonly BattleSide source;
    public readonly BattleSide target;

    // Damage
    public readonly int amount;
    public readonly bool crit;
    public readonly float effectiveness;
    public readonly float ratio01;            // amount / maxHP (for shake scaling)
    public readonly bool wasGuardedOrShielded;

    // Status / state
    public readonly string statusId;          // e.g., "Burn", "Freeze", "Guard", "Charge"
    public readonly int statusStacks;
    public readonly float statusSeconds;
    public readonly bool stateEnabled;        // for GuardChanged / ChargeChanged
    public readonly bool success;             // for DefendResult, RunResult, etc.

    // Swap
    public readonly int fromSlot;
    public readonly int toSlot;

    // Reward
    public readonly int credits;

    private BattleEvent(
        Kind kind,
        BattleSide source,
        BattleSide target,
        int amount,
        bool crit,
        float effectiveness,
        float ratio01,
        bool wasGuardedOrShielded,
        string statusId,
        int statusStacks,
        float statusSeconds,
        bool stateEnabled,
        bool success,
        int fromSlot,
        int toSlot,
        int credits)
    {
        this.kind = kind;
        this.source = source;
        this.target = target;
        this.amount = amount;
        this.crit = crit;
        this.effectiveness = effectiveness;
        this.ratio01 = ratio01;
        this.wasGuardedOrShielded = wasGuardedOrShielded;
        this.statusId = statusId;
        this.statusStacks = statusStacks;
        this.statusSeconds = statusSeconds;
        this.stateEnabled = stateEnabled;
        this.success = success;
        this.fromSlot = fromSlot;
        this.toSlot = toSlot;
        this.credits = credits;
    }

    // Factories

    public static BattleEvent Damage(BattleSide source, BattleSide target, int amount, bool crit, float effectiveness, float ratio01, bool guardedOrShielded)
        => new BattleEvent(Kind.Damage, source, target, amount, crit, effectiveness, ratio01, guardedOrShielded, null, 0, 0f, false, false, -1, -1, 0);

    public static BattleEvent StatusApplied(BattleSide source, BattleSide target, string statusId, int stacks = 1, float seconds = 0f)
        => new BattleEvent(Kind.StatusApplied, source, target, 0, false, 1f, 0f, false, statusId, stacks, seconds, false, false, -1, -1, 0);

    public static BattleEvent Swap(BattleSide source, int fromSlot, int toSlot)
        => new BattleEvent(Kind.Swap, source, source, 0, false, 1f, 0f, false, null, 0, 0f, false, false, fromSlot, toSlot, 0);

    public static BattleEvent KO(BattleSide target)
        => new BattleEvent(Kind.KO, target, target, 0, false, 1f, 0f, false, null, 0, 0f, false, false, -1, -1, 0);

    public static BattleEvent Reward(int credits)
        => new BattleEvent(Kind.Reward, BattleSide.Player, BattleSide.Player, 0, false, 1f, 0f, false, null, 0, 0f, false, false, -1, -1, credits);

    public static BattleEvent ActionWindup(BattleSide source)
        => new BattleEvent(Kind.ActionWindup, source, source, 0, false, 1f, 0f, false, null, 0, 0f, false, false, -1, -1, 0);

    public static BattleEvent DefendResult(BattleSide side, bool success)
        => new BattleEvent(Kind.DefendResult, side, side, 0, false, 1f, 0f, false, null, 0, 0f, false, success, -1, -1, 0);

    public static BattleEvent GuardChanged(BattleSide side, bool enabled)
        => new BattleEvent(Kind.GuardChanged, side, side, 0, false, 1f, 0f, false, null, 0, 0f, enabled, false, -1, -1, 0);

    public static BattleEvent ChargeChanged(BattleSide side, bool enabled)
        => new BattleEvent(Kind.ChargeChanged, side, side, 0, false, 1f, 0f, false, null, 0, 0f, enabled, false, -1, -1, 0);

    public static BattleEvent IntentTelegraph(BattleSide side, string intentId)
        => new BattleEvent(Kind.IntentTelegraph, side, side, 0, false, 1f, 0f, false, intentId, 0, 0f, false, false, -1, -1, 0);

    public static BattleEvent ActionQueued(BattleSide side, string actionId)
        => new BattleEvent(Kind.ActionQueued, side, side, 0, false, 1f, 0f, false, actionId, 0, 0f, false, false, -1, -1, 0);

    public static BattleEvent UIRefreshHP()
        => new BattleEvent(Kind.UIRefreshHP, BattleSide.Player, BattleSide.Player, 0, false, 1f, 0f, false, null, 0, 0f, false, false, -1, -1, 0);
}
