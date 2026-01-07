using UnityEngine;

public abstract class TitleSO : ScriptableObject
{
    [Header("Identity")]
    public string titleId;     
    public string displayName;      
    [TextArea] public string description;

    [Header("UI")]
    public Sprite icon;

    [Header("Wild Encounters")]
    [Tooltip("If true, this title is eligible to be rolled onto wild monsters per encounter (battle-only).")]
    public bool canRollOnWild = false;

    public string DisplayOrId => !string.IsNullOrEmpty(displayName) ? displayName : titleId;
}