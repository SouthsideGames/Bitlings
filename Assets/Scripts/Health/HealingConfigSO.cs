using UnityEngine;

[CreateAssetMenu(menuName = "Data/Config/Healing Config", fileName = "HealingConfig")]
public class HealingConfigSO : ScriptableObject
{
    [Header("Pricing")]
    [Tooltip("credits per HP at level 1")]
    public float basecreditsPerHP = 0.5f;
    [Tooltip("Additional credits per HP per level (linear growth)")]
    public float creditsPerHPPerLevel = 0.15f;
    [Tooltip("Minimum total cost for any heal action")]
    public int minimumCost = 1;

    [Header("Rules")]
    [Tooltip("Allow healing a KO'd monster (currentHP <= 0)")]
    public bool allowHealingIfKO = true;
}
