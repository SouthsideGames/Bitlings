// Assets/Scripts/Static/FeatureId.cs
public enum FeatureId
{
    None = 0,

    // ───── Idle Battle System ─────
    IdleBattle_Basic,          // Unlocks idle battles UI / access
    IdleBattle_RewardBoost,    // Improves idle rewards

    // ───── Auto-Growth System ─────
    AutoGrowth_Basic,          // Auto-spend Growth Cores over threshold
    AutoGrowth_UsePresets,     // Auto-apply bucket presets

    // ───── Title Fusion System ─────
    TitleFusion_Basic,         // Combine 2 titles into 1 hybrid
    TitleFusion_SaveRecipes,   // Save/load fusion recipes

    // ───── Daily Seeds / Custom Seeds ─────
    Seeds_DailyBasic,          // Daily seed runs
    Seeds_CustomInput,         // Custom seed input
    Seeds_RerollDailyOnce,     // Reroll daily seed once per day

    // ───── Codex Upgrades ─────
    Codex_EvolutionViewer,     // Evolution viewer UI
    Codex_Favorites,           // Favorite / pin sorting
    Codex_SeenOnlyFilter       // "Seen only" filter
}
