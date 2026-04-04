using System;

[Serializable]
public struct BattleResult
{
    public bool victory;
    public bool escaped;

    public int creditsGained;
    public int creditsBase;
    public int creditsTitleBonus;
    public float creditsMultiplier;

    public int growthCoresGained;
    public int growthCoresBase;
    public int growthCoresTitleBonus;

    public string activeMonsterOwnedId;
    public MonsterDataSO wildDef;
    public int wildLevel;
    public float secondsSurvived;

    public int critCount;
    public int turnsSurvived;
    public int damageTaken;
    public int damageDealt;
    public bool gotFirstHit;

    public int statusesAppliedToWild;
    public bool hadTypeAdvantage;
    public bool hadTypeDisadvantage;
    public bool isSoloBattle;
    public bool wasManualBattle;
}
