using UnityEngine;

[CreateAssetMenu(menuName = "Data/Exchange/Request", fileName = "ExchangeRequest_")]
public class ExchangeRequestSO : ScriptableObject
{
    [Header("Identity")]
    public string requestId;
    public string displayName;
    [TextArea(2, 4)] public string flavorText;

    [Header("Requirement (exact species OR random species type OR generic type/rarity)")]
    [Tooltip("If set, requires this exact species. This takes priority over the other requirement fields.")]
    public MonsterDataSO requiredSpecies;

    [Tooltip("If exact species is empty, picks one random species of this type when the request is generated. The chosen species becomes the exact request.")]
    public MonsterType requiredRandomSpeciesType = MonsterType.None;

    [Tooltip("For generic requests: any species of this type qualifies. Ignored if exact species or random species type is set.")]
    public MonsterType requiredType = MonsterType.None;

    [Tooltip("Minimum rarity for generic requests, and also used to filter random species type picks.")]
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
