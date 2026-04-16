public enum WorldEventEffectKind
{
    None = 0,

    // Jobs
    DisableJobSite = 10,
    JobRateMultiplier = 11,
    JobStorageCapMultiplier = 12,
    JobCollectDisabled = 13,
    JobFatigueRateMultiplier = 14,

    // Rifts
    DisableRifts = 20,
    RiftEnergyCostMultiplier = 21,
    WildPremiumChanceMultiplier = 22,
    BossCadenceMultiplier = 23,

    // Meta / Economy
    ShopPriceMultiplier = 30,
    ResourceGainMultiplier = 31,

    // Exchange
    ExchangeDemandMultiplier = 40,
    ExchangeValueMultiplier = 41,

    // Idle
    IdleRewardMultiplier = 50,

    // Battle
    BattleRewardMultiplier = 51,

    // Monster type boost (pair these two effects on the same event)
    BoostedMonsterType = 52,    // monsterType field = which type; no value needed
    TypeDamageMultiplier = 53,  // value = damage multiplier for the boosted type
}
