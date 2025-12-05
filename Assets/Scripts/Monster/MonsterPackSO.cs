using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Data/Monster/Monster Pack", fileName = "MonsterPack_")]
public class MonsterPackSO : ScriptableObject
{
    [Header("Identity")]
    public string id;
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Cost")]
    [FormerlySerializedAs("tokenCost")]
    public int baseCost = 5;

    [Tooltip("Which currency this pack uses. If None/0, manager's defaultCurrency is used.")]
    public ResourceType costType = ResourceType.PackShard;

    [Tooltip("Per-pack sale: 0..1 → 0.20 = 20% off")]
    [Range(0f, 1f)] public float saleOff01 = 0f;

    [Header("Unlock Flags")]
    [Tooltip("If true, the game will auto-unlock this pack at startup (useful for starter/basic packs).")]
    public bool unlockByDefault = false;

    [Header("Contents")]
    public List<MonsterDataSO> monsters = new List<MonsterDataSO>();
}
