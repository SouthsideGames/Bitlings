public enum WorldEventEffectKind
{
    None = 0,

    // Jobs
    DisableJobSite = 10,
    JobRateMultiplier = 11,
    JobStorageCapMultiplier = 12,
    JobCollectDisabled = 13,
    JobFatigueRateMultiplier = 14,

    // Encounters
    DisableEncounters = 20,
    EncounterEnergyCostMultiplier = 21,
    WildShinyChanceMultiplier = 22,
    BossCadenceMultiplier = 23,

    // Meta / Economy
    ShopPriceMultiplier = 30,
    ResourceGainMultiplier = 31,

    // Exchange
    ExchangeDemandMultiplier = 40,
    ExchangeValueMultiplier = 41,
}
