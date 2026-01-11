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

    // Existing battle/UI settings that your battle scripts reference
    public bool  autoScrollBattleLog = true;
    public float battleSpeed = 1f;

    public bool condensedBattleText = false;        // GetCondensedBattleText()
    public bool compressAutoBattleText = false;     // GetCompressAutoBattleText()
    public bool battleHistoryEnabled = true;        // GetBattleHistoryEnabled()
    public bool showInlineBattleIcons = true;       // GetShowInlineBattleIcons()

    // ───────── Seeds / RNG ─────────
    public bool   useCustomSeed = false;
    public string customSeed    = "";

    // ───────── Notifications ─────────
    // Master switch: if OFF, NotificationManager schedules nothing.
    public bool notificationsEnabled = true;

    // Fine-grained toggles
    public bool notifyJobStorageFull = true;
    public bool notifyEnergyFull     = true;
    public bool notifyBoostExpiry    = true;

    // Optional generic fallback reminder
    public bool notifyFallback24h    = true;
}
