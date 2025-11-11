using UnityEngine;

public enum BattleAction
{
    Attack,
    Defend,
    Focus,
    Run
}

[CreateAssetMenu(fileName = "MonsterPersonality", menuName = "Data/Monster/MonsterPersonality")]
public class MonsterPersonalitySO : ScriptableObject
{

    public enum PersonalityGroup { None, Offensive, Defensive, Tactical, Reactive, Evasive, Support, Chaotic }

    public PersonalityGroup group = PersonalityGroup.None;

    [Header("Base Weights (do not need to sum to 100)")]
    [Min(0)] public int attackWeight = 5;
    [Min(0)] public int defendWeight = 1;
    [Min(0)] public int focusWeight  = 1;
    [Min(0)] public int runWeight    = 0;

    [Header("Context Modifiers")]
    [Range(0f,1f)] public float lowHpThreshold = 0.30f;
    [Min(0)] public int lowHpDefendBonus = 2;
    [Min(0)] public int lowHpRunBonus    = 1;

    [Min(0)] public int superEffectiveAttackBonus = 2; // if we have a good matchup, bias attack
    [Min(0)] public int badMatchDefendBonus = 1;       // if we have a bad matchup, bias defend
    [Min(0)] public int badMatchRunBonus    = 1;       // optionally nudge run

    [Tooltip("Optional pressure that increases aggression as turns pass.")]
    [Min(0)] public int eachTurnAttackBonus = 0;

    [TextArea(2,4)] 
    public string description = "Default personality description.";


    public BattleAction ChooseAction(in PersonalityContext ctx, System.Random rng)
    {
        int a = attackWeight;
        int d = defendWeight;
        int f = focusWeight;
        int r = runWeight;

        if (ctx.selfHpRatio <= lowHpThreshold) { d += lowHpDefendBonus; r += lowHpRunBonus; }
        if (ctx.hasSuperEffectiveMove) a += superEffectiveAttackBonus;
        if (ctx.isBadlyMatched) { d += badMatchDefendBonus; r += badMatchRunBonus; }

        a += eachTurnAttackBonus * Mathf.Max(0, ctx.turnNumber - 1);

        int total = Mathf.Max(0, a) + Mathf.Max(0, d) + Mathf.Max(0, f) + Mathf.Max(0, r);
        if (total <= 0) return BattleAction.Attack;

        int roll = rng.Next(0, total);
        if (roll < a) return BattleAction.Attack; roll -= a;
        if (roll < d) return BattleAction.Defend; roll -= d;
        if (roll < f) return BattleAction.Focus;  roll -= f;
        return BattleAction.Run;
    }
}

public struct PersonalityContext
{
    public float selfHpRatio;          // 0..1
    public bool hasSuperEffectiveMove; // based on type matchup or moves
    public bool isBadlyMatched;        // inverse of above
    public int turnNumber;             // 1-based
}
