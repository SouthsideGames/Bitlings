using UnityEngine;

[CreateAssetMenu(menuName = "Data/Exchange/Request", fileName = "ExchangeRequest_")]
public class ExchangeRequestSO : ScriptableObject
{
    [Header("Identity")]
    public string requestId;
    public string displayName;
    [TextArea(2, 4)] public string flavorText;

    [Header("Requirement (set species OR type/rarity for generic)")]
    [Tooltip("If set, requires this exact species. Leave empty for generic requests.")]
    public MonsterDataSO requiredSpecies;

    [Tooltip("For generic requests: any species of this type qualifies. Ignored if requiredSpecies is set.")]
    public MonsterType requiredType = MonsterType.None;

    [Tooltip("For generic requests: minimum rarity required. Ignored if requiredSpecies is set.")]
    public Rarity requiredMinRarity = Rarity.Common;

    [Header("Reward")]
    [Min(1)] public int creditReward = 50;
    public ResourceType bonusResourceType;
    [Min(0)] public int bonusResourceAmount;

    [Header("Rotation")]
    [Tooltip("Weight for random rotation selection. Higher = more likely to appear.")]
    [Min(1)] public int weight = 1;

    [Tooltip("Duration in hours before this request expires.")]
    [Min(1)] public int durationHours = 24;
}
