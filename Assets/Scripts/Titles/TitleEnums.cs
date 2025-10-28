using UnityEngine;

public enum StatKind
{
    HP = 0,
    Attack = 1,
    Defense = 2,
    Speed = 3
}

public enum OpKind
{
    Add = 0,
    Subtract = 1,
    Multiply = 2,
    Divide = 3
}

public enum ConditionKind
{
    None = 0,
    HealthBelowPercent = 1,  // threshold01 used
    HealthAbovePercent = 2,  // threshold01 used
    AllyCountBelow     = 3,  // countN used
    WinStreakAbove     = 4   // countN used
}
public enum JobBoosterKind
{
    FatigueMultiplier = 0,
    SiteAuraPercent = 1,
    CapacityBonusFlat = 2
}
