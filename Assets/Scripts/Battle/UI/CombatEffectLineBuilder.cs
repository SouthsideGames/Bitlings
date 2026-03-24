using System.Collections.Generic;
using UnityEngine;

public static class CombatEffectLineBuilder
{
    public struct CombatEffectLine
    {
        public string label;
        public string value;
        public bool isPositive;
        public bool isConsumed;
    }

    public static List<CombatEffectLine> Build(JobBattlePassives.Ctx ctx)
    {
        var lines = new List<CombatEffectLine>(8);
        if (ctx == null || ctx.job == JobType.None) return lines;

        // ── Stat-modifying passives (already shown in stat deltas, but label the source) ──

        if (ctx.maxHpBonusPct > 0f)
            lines.Add(Positive($"+{PctStr(ctx.maxHpBonusPct)} Max HP"));

        if (ctx.attackBonusPct > 0f)
            lines.Add(Positive($"+{PctStr(ctx.attackBonusPct)} ATK"));

        if (ctx.defenseBonusPct > 0f)
            lines.Add(Positive($"+{PctStr(ctx.defenseBonusPct)} DEF"));

        // ── Speed buff (turn-limited) ──

        if (ctx.speedBonusPctFirstTurns > 0f)
        {
            if (ctx.speedBuffTurns > 0)
                lines.Add(Positive($"+{PctStr(ctx.speedBonusPctFirstTurns)} SPD ({ctx.speedBuffTurns}t left)"));
            else
                lines.Add(Consumed($"+{PctStr(ctx.speedBonusPctFirstTurns)} SPD (expired)"));
        }

        // ── Crit chance ──

        if (ctx.critChanceFlat > 0f)
            lines.Add(Positive($"+{PctStr(ctx.critChanceFlat)} Crit Chance"));

        if (ctx.critChanceBonusFirstTurns > 0f)
        {
            if (ctx.critBuffTurns > 0)
                lines.Add(Positive($"+{PctStr(ctx.critChanceBonusFirstTurns)} Crit Chance ({ctx.critBuffTurns}t left)"));
            else
                lines.Add(Consumed($"+{PctStr(ctx.critChanceBonusFirstTurns)} Crit Chance (expired)"));
        }

        // ── Crit resist ──

        if (ctx.critResistFlat > 0f)
            lines.Add(Positive($"+{PctStr(ctx.critResistFlat)} Crit Resist"));

        if (ctx.critResistBonusFirstTurns > 0f)
        {
            if (ctx.critResistBuffTurns > 0)
                lines.Add(Positive($"+{PctStr(ctx.critResistBonusFirstTurns)} Crit Resist ({ctx.critResistBuffTurns}t left)"));
            else
                lines.Add(Consumed($"+{PctStr(ctx.critResistBonusFirstTurns)} Crit Resist (expired)"));
        }

        // ── Damage reduction ──

        if (ctx.baseDamageReducePct > 0f)
            lines.Add(Positive($"-{PctStr(ctx.baseDamageReducePct)} Incoming Damage"));

        if (ctx.dmgReduceFirstTurns > 0f)
        {
            if (ctx.dmgReduceBuffTurns > 0)
                lines.Add(Positive($"-{PctStr(ctx.dmgReduceFirstTurns)} Incoming Damage ({ctx.dmgReduceBuffTurns}t left)"));
            else
                lines.Add(Consumed($"-{PctStr(ctx.dmgReduceFirstTurns)} Incoming Damage (expired)"));
        }

        // ── First outgoing hit bonus (Forge) ──

        if (ctx.firstOutgoingBonus > 0f)
        {
            if (!ctx.usedFirstOutgoing)
                lines.Add(Positive($"+{PctStr(ctx.firstOutgoingBonus)} First Hit Damage"));
            else
                lines.Add(Consumed($"+{PctStr(ctx.firstOutgoingBonus)} First Hit Damage (used)"));
        }

        // ── First incoming hit reduction (Shadow Market) ──

        if (ctx.firstIncomingReduce > 0f)
        {
            if (!ctx.usedFirstIncoming)
                lines.Add(Positive($"-{PctStr(ctx.firstIncomingReduce)} First Hit Taken"));
            else
                lines.Add(Consumed($"-{PctStr(ctx.firstIncomingReduce)} First Hit Taken (used)"));
        }

        // ── Shield at start ──

        if (ctx.startShieldPctMaxHp > 0f)
            lines.Add(Positive($"+{PctStr(ctx.startShieldPctMaxHp)} Max HP as Shield"));

        // ── Regen ──

        if (ctx.endTurnHealPct > 0f)
        {
            string turnsLabel = (ctx.regenTurns == int.MaxValue || ctx.regenTurns < 0)
                ? ""
                : (ctx.regenTurns > 0 ? $" ({ctx.regenTurns}t left)" : " (expired)");

            bool active = ctx.regenTurns == int.MaxValue || ctx.regenTurns > 0;
            if (active)
                lines.Add(Positive($"+{PctStr(ctx.endTurnHealPct)} HP/turn{turnsLabel}"));
            else
                lines.Add(Consumed($"+{PctStr(ctx.endTurnHealPct)} HP/turn{turnsLabel}"));
        }

        // ── Surge ATK (Wyrm Den — below 50% HP) ──

        if (ctx.surgeAtkBonusPct > 0f)
        {
            if (ctx.surgeApplied)
                lines.Add(Positive($"+{PctStr(ctx.surgeAtkBonusPct)} ATK Surge (active)"));
            else
                lines.Add(Positive($"+{PctStr(ctx.surgeAtkBonusPct)} ATK below 50% HP"));
        }

        // ── Rescue heal (Clinic) ──

        if (ctx.rescueHealPct > 0f)
        {
            if (!ctx.rescueUsed)
                lines.Add(Positive($"+{PctStr(ctx.rescueHealPct)} Emergency Heal at {PctStr(ctx.rescueThreshold)} HP"));
            else
                lines.Add(Consumed($"+{PctStr(ctx.rescueHealPct)} Emergency Heal (used)"));
        }

        return lines;
    }

    private static string PctStr(float pct) => $"{Mathf.RoundToInt(pct * 100f)}%";

    private static CombatEffectLine Positive(string label) =>
        new CombatEffectLine { label = label, value = "", isPositive = true, isConsumed = false };

    private static CombatEffectLine Consumed(string label) =>
        new CombatEffectLine { label = label, value = "", isPositive = false, isConsumed = true };
}
