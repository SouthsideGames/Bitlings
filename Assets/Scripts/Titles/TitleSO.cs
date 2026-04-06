using UnityEngine;

public enum TitleRarity { Common, Rare, Epic, Mythic }

public abstract class TitleSO : ScriptableObject
{
    [Header("Identity")]
    public string titleId;     
    public string displayName;      
    [TextArea] public string description;

    [Header("UI")]
    public Sprite icon;

    [Header("Wild Encounters")]
    public TitleRarity rarity = TitleRarity.Common;
    [Tooltip("If true, this title is eligible to be rolled onto wild monsters per encounter (battle-only).")]
    public bool canRollOnWild = false;

    public TitleRarity Rarity => rarity;

    public string DisplayOrId => !string.IsNullOrEmpty(displayName) ? displayName : titleId;

    [Header("Arena")]
    [Tooltip("Arena score contributed when this title is equipped on an arena battle team. Stacks with the species arenaScore.")]
    [Min(0)] public int arenaScore;
}