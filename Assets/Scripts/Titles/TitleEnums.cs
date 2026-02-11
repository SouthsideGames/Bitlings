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
    HealthBelowPercent = 1,
    HealthAbovePercent = 2,
    AllyCountBelow = 3,
    AllyCountAbove = 5,
    WinStreakAbove = 4
}

public enum JobBoosterKind
{
    FatigueMultiplier = 0,
    SiteAuraPercent = 1,
    CapacityBonusFlat = 2
}
