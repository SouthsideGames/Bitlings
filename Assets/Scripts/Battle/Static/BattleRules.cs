using System;
using UnityEngine;

/// <summary>
/// Flags that define which systems are allowed to participate in a battle.
/// Normal mode uses Default. Iron Career uses Iron.
/// </summary>
[Serializable]
public struct BattleRules
{
    public bool allowTitles;
    public bool allowJobPassives;
    public bool allowBoosters;
    public bool allowPreferredVariants;
    public bool allowAutoBattle;
    public bool allowRewardsAndPersistence;

    public static BattleRules Default => new BattleRules
    {
        allowTitles = true,
        allowJobPassives = true,
        allowBoosters = true,
        allowPreferredVariants = true,
        allowAutoBattle = true,
        allowRewardsAndPersistence = true,
    };

    public static BattleRules Iron => new BattleRules
    {
        allowTitles = true,
        allowJobPassives = false,
        allowBoosters = false,
        allowPreferredVariants = false,
        allowAutoBattle = false,
        allowRewardsAndPersistence = false,
    };
}

public interface IBattleContext
{
    BattleRules Rules { get; }
    void OnBattleResolved(IronBattleOutcome outcome);
}

public interface IBattleRosterProvider
{
    /// <summary>Returns the player's team combatants for this battle (<= 3 slots).</summary>
    System.Collections.Generic.IReadOnlyList<BattleCombatant> GetPlayerTeam();

    /// <summary>Returns the wild combatant (required). If null, caller should forfeit.</summary>
    BattleCombatant GetWild();
}

[Serializable]
public sealed class BattleCombatant
{
    public MonsterDataSO def;
    public int level = 1;
    public float hp = -1f;

    /// <summary>
    /// Unique per-instance identifier used ONLY for Titles/status carry-over in sealed modes.
    /// Must be stable for the duration of a run. Example: "IRON::P::0::<guid>".
    /// </summary>
    public string combatantId;

    /// <summary>
    /// Optional locked title for this instance (Iron rules).
    /// If set, it is injected via TitlesAdapter.SetLocalTitles(combatantId, ...).
    /// </summary>
    public TitleSO lockedTitle;
}

/// <summary>
/// Minimal end-of-battle snapshot for sealed modes (Iron).
/// No rewards, no Save writes, no global events.
/// </summary>
[Serializable]
public struct IronBattleOutcome
{
    public bool victory;
    public bool escaped;

    public MonsterDataSO wildDef;
    public int wildLevel;

    public float secondsSurvived;
    public int turnsSurvived;

    public float[] teamHP;
    public float[] teamMaxHP;

    // Carry-over pools (player-only)
    public float[] shieldHP;
    public IronFieldStatusSnapshot playerFieldStatus;
}

[Serializable]
public struct IronFieldStatusSnapshot
{
    public StatusType type;
    public int turns;
    public float magnitude;
    public bool persistent;

    public static IronFieldStatusSnapshot None => new IronFieldStatusSnapshot
    {
        type = StatusType.None,
        turns = 0,
        magnitude = 0f,
        persistent = false
    };
}
