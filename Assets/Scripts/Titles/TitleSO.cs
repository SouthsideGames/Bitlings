using UnityEngine;

public abstract class TitleSO : ScriptableObject
{
    [Header("Identity")]
    public string titleId;                 // unique key (e.g., T-001)
    public string displayName;             // UI name
    [TextArea] public string description;  // UI description

    [Header("UI")]
    public Sprite icon;                    // used by BattleTitleStatusBarUI + toast
    public bool showInBattleStatusBar = true;
    public bool showProcToast = true;

    [Tooltip("Optional short label (e.g., 'Clutch', 'Shield', 'Stacks'). If empty, displayName is used.")]
    public string shortLabel;

    public string DisplayOrId => string.IsNullOrEmpty(displayName) ? titleId : displayName;
    public string ShortOrName => string.IsNullOrEmpty(shortLabel) ? DisplayOrId : shortLabel;
}
