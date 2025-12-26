using System;   

[Serializable]
public class SettingsState
{
    // ───────── Audio ─────────
    public float masterVolume = 0.25f;
    public float musicVolume  = 0.25f;
    public float sfxVolume    = 0.8f;

    public bool muteAll   = false;
    public bool muteMusic = false;
    public bool muteSfx   = false;

    // ───────── Gameplay ─────────
    public bool  autoBenchEnabled        = true;
    public float autoBenchThreshold01    = 0.20f;
    public bool  autoBenchAutoFill       = true;
    public bool  autoClinicReliefEnabled = true;

    // ───────── Miscellaneous ─────────
    public bool  logProductionBreakdown = false;
    public int   monstersSortMode = 0;
    public bool  autoConvertDuplicates = true;
    public bool  autoScrollBattleLog = true;
    public float battleSpeed = 1f;

    // ───────── Seeds / RNG ─────────
    // If true, systems that support seeding should prefer customSeed (when feature is unlocked).
    public bool   useCustomSeed = false;
    public string customSeed    = "";

    // ───────── Battle UX / Accessibility ─────────
    public bool showInlineBattleIcons = true;     // Crit/Shield/Effectiveness icons in narration
    public bool condensedBattleText = false;      // Removes flavor lines (keeps results)
    public bool compressAutoBattleText = true;    // Auto mode removes extra explanation lines
    public bool battleHistoryEnabled = true;      // If false, hide the History button/modal

}
