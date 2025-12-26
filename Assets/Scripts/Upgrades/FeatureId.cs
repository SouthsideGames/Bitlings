// Assets/Scripts/Static/FeatureId.cs
public enum FeatureId
{
    None = 0,

    // ───── Idle Battle System ─────
    IdleBattle_Basic = 1,          // Unlocks idle battles UI / access
    IdleBattle_RewardBoost = 2,    // Improves idle rewards
    IdleBattle_OfflineCapture = 3,  // Unlock offline monster captures

    // ───── Auto-Growth System ─────
    AutoGrowth_Basic = 4,          // Auto-spend Growth Cores over threshold
    AutoGrowth_UsePresets = 5,     // Auto-apply bucket presets

    // ───── Title Fusion System ─────
    Recycle_Basic = 6,         // Combine 2 titles into 1 hybrid

    // ───── Daily Seeds / Custom Seeds ─────
    Seeds_DailyBasic = 8,          // Daily seed runs
    Seeds_CustomInput = 9,         // Custom seed input
    Seeds_RerollDailyOnce = 10,     // Reroll daily seed once per day
    // ───── Codex Upgrades ─────
    Codex_Favorites = 12,           // Favorite / pin sorting
    Codex_CaptureOnlyFilter = 13       // "Capture only" filter
}
