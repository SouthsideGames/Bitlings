using UnityEngine;

public enum AchievementTrigger
{
    TotalCaptures,
    CapturesByType,
    TotalEvolutions,
    BossDefeats,
    TotalBattles,
    BattleWins,
    PerfectBattles,   
    CreditsEarned,       
    IdleBatchesCompleted,
    WinStreakMax,       
    FavoritesCount,     
    OwnMonstersCount,    
    DiscoverTypesCount,
    ResourcesEarned,
    JobsAssigned,
    BattleWinsWithTypeAdvantage,
    BattleWinsWithTypeDisadvantage,
    CriticalHits,
    StatusesApplied,
    StatusesAppliedByType,
    SoloBattleWins,
    TitlesEquipped,
    TitlesEquippedSimultaneous,
    TitlesUnlocked,
    PlayerRank,
    ExecutiveTrialRunsStarted,
    ExecutiveTrialBattleWins,
    ExecutiveTrialRunsCompleted,
    ExecutiveTrialPerfectRuns,
    BattlesWatched,
    DirectoryOpened,
    SecretAchievementsUnlocked,
    AchievementsUnlocked
}

[CreateAssetMenu(menuName = "Data/Achievements/Achievement Entry", fileName = "Achievement_")]
public sealed class AchievementEntrySO : ScriptableObject
{
    [Header("Identity")]
    public string id = "A-001";
    public string displayName;
    [TextArea(2, 5)] public string description;

    [Header("Visual")]
    public Sprite icon;

    [Header("Rules")]
    public AchievementTrigger trigger = AchievementTrigger.TotalCaptures;

    [Min(1)]
    public int goal = 1;

    [Header("Optional Filters")]
    public bool useTypeFilter = false;
    public MonsterType typeFilter;

    public bool useResourceFilter = false;
    public ResourceType resourceFilter;

    [Header("Behavior")]
    public bool secretUntilUnlocked = false;

    [Header("Metadata")]
    public string category;
}
