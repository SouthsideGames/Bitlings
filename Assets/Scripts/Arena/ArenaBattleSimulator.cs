// Assets/Scripts/Arena/ArenaBattleSimulator.cs
// BRN Arena v1 — Deterministic turn-by-turn battle engine for async match resolution.
// Operates entirely on frozen ArenaTeamSnapshot data.  No live OwnedMonsterData,
// no coroutines, no UI, no player input.
//
// Uses BattleCalc (stat formulas + damage), BattleTypeChart (effectiveness),
// and TitlesAdapter (stat mods, damage filters, effectiveness mods) for full
// battle correctness parity with the real-time BattleManager.

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure deterministic battle simulation between two frozen <see cref="ArenaTeamSnapshot"/>s.
/// All randomness is seeded via <see cref="System.Random"/> — never uses <c>UnityEngine.Random</c>.
/// Produces a <see cref="ArenaBattleResult"/> containing winner, turn count, and full battle log.
/// </summary>
public static class ArenaBattleSimulator
{
    // ═════════════════════════════════════════════════════════════
    //  Tuning
    // ═════════════════════════════════════════════════════════════

    /// <summary>Hard turn cap to prevent infinite loops (stalemates).</summary>
    private const int MaxTurns = 50;

    /// <summary>Arena battles use a fixed level for all combatants.</summary>
    private const int ArenaLevel = 50;

    /// <summary>Base attack damage (ATK stat used as the base hit value).</summary>
    private const float CritChance = 0.08f;

    /// <summary>Crit damage multiplier.</summary>
    private const float CritMultiplier = 1.5f;

    // ═════════════════════════════════════════════════════════════
    //  Result type
    // ═════════════════════════════════════════════════════════════

    /// <summary>Immutable result produced by a single battle simulation.</summary>
    public sealed class ArenaBattleResult
    {
        /// <summary>0 = left side won, 1 = right side won.</summary>
        public int winningSide;
        public int turnCount;
        public List<ArenaBattleLogEvent> battleLog;
    }

    // ═════════════════════════════════════════════════════════════
    //  Internal combatant state
    // ═════════════════════════════════════════════════════════════

    private sealed class Combatant
    {
        public string combatantId;    // synthetic id for TitlesAdapter
        public MonsterDataSO def;
        public MonsterType monsterType;
        public string monsterName;
        public string titleId;
        public TitleSO titleDef;

        public int maxHp;
        public int currentHp;
        public int attack;
        public int defense;
        public int speed;

        public bool isKnockedOut => currentHp <= 0;
    }

    private sealed class TeamState
    {
        public int side;              // 0 = left, 1 = right
        public Combatant[] members;   // always length 3
        public int activeIndex;       // which member is fighting
        public string ownerName;

        public Combatant Active => members[activeIndex];

        public int AliveCount
        {
            get
            {
                int c = 0;
                for (int i = 0; i < members.Length; i++)
                    if (!members[i].isKnockedOut) c++;
                return c;
            }
        }

        public bool AllKnockedOut => AliveCount == 0;

        /// <summary>Swaps to the next alive member if the active one is knocked out.
        /// Returns true if a swap occurred.</summary>
        public bool SwapToNextAlive()
        {
            if (!Active.isKnockedOut) return false;
            for (int i = 0; i < members.Length; i++)
            {
                if (i == activeIndex) continue;
                if (!members[i].isKnockedOut)
                {
                    activeIndex = i;
                    return true;
                }
            }
            return false;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Simulates a full battle between two frozen team snapshots.
    /// Entirely deterministic given the same <paramref name="matchSeed"/>.
    /// </summary>
    /// <param name="left">Left side team snapshot (never null).</param>
    /// <param name="right">Right side team snapshot (never null).</param>
    /// <param name="matchSeed">Deterministic seed for all RNG.</param>
    /// <returns>Battle result including winner, turn count, and full log.</returns>
    public static ArenaBattleResult Simulate(
        ArenaTeamSnapshot left,
        ArenaTeamSnapshot right,
        int matchSeed)
    {
        ArenaBattleSimulationScope.Enter();
        var rng = new System.Random(matchSeed);
        var log = new List<ArenaBattleLogEvent>(64);

        // ── Set up deterministic RNG for BattleCalc ──
        BattleCalc.SetRng(() => (float)rng.NextDouble());

        try
        {
            // ── Build team states from snapshots ──
            var leftTeam = BuildTeamState(left, 0, rng);
            var rightTeam = BuildTeamState(right, 1, rng);

            // ── Register title contexts ──
            RegisterTitles(leftTeam);
            RegisterTitles(rightTeam);

            // ── Turn loop ──
            int turn = 0;
            while (turn < MaxTurns)
            {
                // Turn start
                log.Add(MakeEvent(ArenaBattleLogEventType.TurnStart, turn, -1,
                    $"Turn {turn + 1} begins", 0, ""));

                TitlesAdapter.OnTurnAdvanced(turn);

                var first = leftTeam;
                var second = rightTeam;

                // Speed determines who attacks first; ties broken by seed.
                int leftSpd = leftTeam.Active.speed;
                int rightSpd = rightTeam.Active.speed;
                if (rightSpd > leftSpd || (rightSpd == leftSpd && rng.Next(2) == 1))
                {
                    first = rightTeam;
                    second = leftTeam;
                }

                // ── First attacker's turn ──
                ResolveAttack(first, second, turn, log, rng);

                if (CheckTeamWipeout(second, first, turn, log))
                {
                    turn++;
                    break;
                }

                // ── Second attacker's turn ──
                ResolveAttack(second, first, turn, log, rng);

                if (CheckTeamWipeout(first, second, turn, log))
                {
                    turn++;
                    break;
                }

                // ── End-of-turn title hooks ──
                TitlesAdapter.OnCombatantTurnEnded(leftTeam.Active.combatantId);
                TitlesAdapter.OnCombatantTurnEnded(rightTeam.Active.combatantId);

                turn++;
            }

            // ── Determine winner ──
            int winningSide;
            if (rightTeam.AllKnockedOut && !leftTeam.AllKnockedOut)
                winningSide = 0;
            else if (leftTeam.AllKnockedOut && !rightTeam.AllKnockedOut)
                winningSide = 1;
            else
            {
                // Stalemate / turn cap — team with more total remaining HP wins.
                int leftHp = TotalRemainingHp(leftTeam);
                int rightHp = TotalRemainingHp(rightTeam);
                if (leftHp > rightHp)
                    winningSide = 0;
                else if (rightHp > leftHp)
                    winningSide = 1;
                else
                    winningSide = rng.Next(2); // true tiebreak
            }

            string winnerName = winningSide == 0 ? leftTeam.ownerName : rightTeam.ownerName;
            log.Add(MakeEvent(ArenaBattleLogEventType.Victory, turn > 0 ? turn - 1 : 0,
                winningSide, $"{winnerName} wins the match", 0, ""));

            return new ArenaBattleResult
            {
                winningSide = winningSide,
                turnCount = turn,
                battleLog = log
            };
        }
        finally
        {
            // ── Cleanup ──
            BattleCalc.ResetRng();
            TitlesAdapter.ClearAllLocalTitles();
            ArenaBattleSimulationScope.Exit();
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Team state construction
    // ═════════════════════════════════════════════════════════════

    private static TeamState BuildTeamState(ArenaTeamSnapshot snap, int side, System.Random rng)
    {
        var members = new Combatant[ArenaConstants.BattleTeamSize];

        for (int i = 0; i < ArenaConstants.BattleTeamSize; i++)
        {
            ArenaBitlingSnapshot slot = (i < snap.slotSnapshots.Count) ? snap.slotSnapshots[i] : null;

            string combatantId = $"ARENA::{side}_{i}_{(slot != null ? slot.monsterId : "empty")}";
            MonsterDataSO def = null;
            TitleSO titleDef = null;

            if (slot != null && !string.IsNullOrEmpty(slot.monsterId))
                def = MonsterCatalog.GetById(slot.monsterId);

            if (slot != null && !string.IsNullOrEmpty(slot.titleId) && TitleManager.I != null)
                titleDef = TitleManager.I.GetTitleById(slot.titleId);

            // Compute stats at arena level using BattleCalc.
            int maxHp, atk, defStat, spd;

            if (def != null)
            {
                // Register title context BEFORE stat calculation.
                if (titleDef != null)
                    TitlesAdapter.SetLocalTitles(combatantId, new TitleSO[] { titleDef });

                TitlesAdapter.RegisterBattleContext(combatantId, def, ArenaLevel);

                maxHp  = Mathf.Max(1, Mathf.RoundToInt(BattleCalc.CalcHP(def, ArenaLevel, combatantId)));
                atk    = Mathf.Max(1, Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, ArenaLevel, 0, 0, combatantId)));
                defStat = BattleCalc.CalcDefense(def, ArenaLevel, combatantId);
                spd    = BattleCalc.CalcSpeed(def, ArenaLevel, combatantId);
            }
            else
            {
                // Fallback for missing data — should never happen in practice.
                maxHp = 100;
                atk = 10;
                defStat = 5;
                spd = 5;
            }

            members[i] = new Combatant
            {
                combatantId = combatantId,
                def = def,
                monsterType = slot != null ? slot.monsterType : MonsterType.None,
                monsterName = slot != null ? slot.monsterName : "Unknown",
                titleId = slot != null ? slot.titleId : "",
                titleDef = titleDef,
                maxHp = maxHp,
                currentHp = maxHp,
                attack = atk,
                defense = defStat,
                speed = spd
            };
        }

        return new TeamState
        {
            side = side,
            members = members,
            activeIndex = 0,
            ownerName = snap.ownerDisplayName ?? "Player"
        };
    }

    // ═════════════════════════════════════════════════════════════
    //  Title registration
    // ═════════════════════════════════════════════════════════════

    private static void RegisterTitles(TeamState team)
    {
        for (int i = 0; i < team.members.Length; i++)
        {
            var m = team.members[i];
            if (m.def == null) continue;

            // Set titles via local injection (already done in BuildTeamState for stat calc,
            // but ensure they persist for damage resolution hooks).
            if (m.titleDef != null)
                TitlesAdapter.SetLocalTitles(m.combatantId, new TitleSO[] { m.titleDef });

            TitlesAdapter.RegisterBattleContext(m.combatantId, m.def, ArenaLevel);
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Attack resolution
    // ═════════════════════════════════════════════════════════════

    private static void ResolveAttack(
        TeamState attacker, TeamState defender,
        int turn, List<ArenaBattleLogEvent> log,
        System.Random rng)
    {
        var atk = attacker.Active;
        var defC = defender.Active;

        if (atk.isKnockedOut || defC.isKnockedOut) return;

        // Log action.
        log.Add(MakeEvent(ArenaBattleLogEventType.ActionUsed, turn, attacker.side,
            $"{atk.monsterName} uses {(atk.def != null ? atk.def.basicAttackName : "Attack")}",
            0, atk.combatantId));

        // ── Title-triggered effects (pre-attack) ──
        if (atk.titleDef != null)
        {
            log.Add(MakeEvent(ArenaBattleLogEventType.TitleTriggered, turn, attacker.side,
                $"{atk.monsterName}'s title {GetTitleDisplayName(atk.titleDef)} is active",
                0, atk.titleId));
        }

        // ── Calculate damage via BattleCalc.ResolveHit (full title-aware path) ──
        float baseDamage = Mathf.Max(1f, atk.attack);
        var result = BattleCalc.ResolveHit(
            attackerMonsterId: atk.combatantId,
            atkDef: atk.def,
            atkLevel: ArenaLevel,
            defenderMonsterId: defC.combatantId,
            defDef: defC.def,
            defLevel: ArenaLevel,
            baseDamage: baseDamage,
            critChance: CritChance,
            critMultiplier: CritMultiplier,
            defenderFlatDefenseBonus: 0,
            defenderEffectiveDefenseStat: null
        );

        int damage = result.damage;

        // ── Apply shield (from BattleStartShieldTitleSO) ──
        float shieldHp = TitlesAdapter.GetBattleStartShieldRemaining(defC.combatantId);
        if (shieldHp > 0f)
        {
            int shieldAbsorb = Mathf.Min(damage, Mathf.RoundToInt(shieldHp));
            damage -= shieldAbsorb;
            damage = Mathf.Max(0, damage);
        }

        // ── Apply damage ──
        defC.currentHp = Mathf.Max(0, defC.currentHp - damage);

        // Build description.
        string effText = "";
        if (result.effectiveness > 1.1f) effText = " (super effective!)";
        else if (result.effectiveness < 0.9f) effText = " (not very effective)";

        string critText = result.crit ? " Critical hit!" : "";
        string desc = $"{atk.monsterName} deals {damage} damage to {defC.monsterName}{effText}{critText}";

        log.Add(MakeEvent(ArenaBattleLogEventType.Damage, turn, attacker.side,
            desc, damage, defC.combatantId));

        // ── Title hooks ──
        TitlesAdapter.OnAttackLanded(atk.combatantId, result.crit);
        TitlesAdapter.OnHitTaken(defC.combatantId, damage, result.crit);

        // ── Check knockout ──
        if (defC.isKnockedOut)
        {
            log.Add(MakeEvent(ArenaBattleLogEventType.Knockout, turn, defender.side,
                $"{defC.monsterName} is knocked out", 0, defC.combatantId));

            // Swap to next alive member.
            if (defender.SwapToNextAlive())
            {
                var next = defender.Active;
                log.Add(MakeEvent(ArenaBattleLogEventType.ActionUsed, turn, defender.side,
                    $"{defender.ownerName} sends out {next.monsterName}", 0, next.combatantId));
            }
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Wipeout check
    // ═════════════════════════════════════════════════════════════

    /// <summary>Returns true if <paramref name="check"/> has been wiped out.</summary>
    private static bool CheckTeamWipeout(
        TeamState check, TeamState other,
        int turn, List<ArenaBattleLogEvent> log)
    {
        return check.AllKnockedOut;
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════

    private static int TotalRemainingHp(TeamState team)
    {
        int total = 0;
        for (int i = 0; i < team.members.Length; i++)
            total += Mathf.Max(0, team.members[i].currentHp);
        return total;
    }

    private static ArenaBattleLogEvent MakeEvent(
        ArenaBattleLogEventType type, int turn, int side,
        string description, int value, string referenceId)
    {
        return new ArenaBattleLogEvent
        {
            eventType = type,
            turn = turn,
            side = side,
            description = description ?? "",
            value = value,
            referenceId = referenceId ?? ""
        };
    }

    private static string GetTitleDisplayName(TitleSO title)
    {
        if (title == null) return "";
        return !string.IsNullOrEmpty(title.displayName) ? title.displayName : title.titleId;
    }
}
