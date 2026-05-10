using System;
using UnityEngine;


[Serializable]
public sealed class ExecutiveTrailMonster
{
    public MonsterDataSO def;
    public int level = 1;

    public float hp = 1f;
    public float maxHp = 1f;

    [Tooltip("Locked title for this instance. Immutable in Iron.")]
    public TitleSO lockedTitle;

    public bool isPremium;

    public ExecutiveTrailMonster() { }

    public ExecutiveTrailMonster(MonsterDataSO d, int lvl, float curHp, TitleSO locked, bool premium = false)
    {
        def = d;
        level = Mathf.Max(1, lvl);
        maxHp = Mathf.Max(1f, (def != null) ? BattleCalc.CalcHP(def, level) : 1f);
        hp = Mathf.Clamp(curHp, 0f, maxHp);
        lockedTitle = locked;
        isPremium = premium && def != null && def.premiumIcon != null;
    }

    public bool IsValid => def != null;
    public bool IsDead => hp <= 0.01f;

    public float Hp01 => (maxHp > 0.01f) ? Mathf.Clamp01(hp / maxHp) : 0f;

    public void RecomputeMaxHpPreservePct()
    {
        float pct = Hp01;
        maxHp = Mathf.Max(1f, (def != null) ? BattleCalc.CalcHP(def, Mathf.Max(1, level)) : 1f);
        hp = Mathf.Clamp(maxHp * pct, 0f, maxHp);
    }
}
