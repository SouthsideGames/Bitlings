using UnityEngine;

[CreateAssetMenu(menuName = "Data/Config/Healing Config", fileName = "HealingConfig")]
public class HealingConfigSO : ScriptableObject
{
    [Header("Pricing")]
    [Tooltip("Coins per HP at level 1")]
    public float baseCoinsPerHP = 0.5f;
    [Tooltip("Additional coins per HP per level (linear growth)")]
    public float coinsPerHPPerLevel = 0.15f;
    [Tooltip("Minimum total cost for any heal action")]
    public int minimumCost = 1;

    [Header("Rules")]
    [Tooltip("Allow healing a KO'd monster (currentHP <= 0)")]
    public bool allowHealingIfKO = true;
}
