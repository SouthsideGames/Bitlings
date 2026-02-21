using UnityEngine;

[CreateAssetMenu(menuName = "Data/Config/Game Balance", fileName = "GameBalance")]
public sealed class GameBalanceSO : ScriptableObject
{
    [Header("Encounter")]
    [Min(0)] public int encounterEnergyCost = 1;

    [Header("Energy")]
    [Min(1)] public int energyMax = 20;
    [Min(1)] public int energySecondsPerPoint = 1200;
    public bool clampEnergyOnLoad = true;

    [Header("Jobs")]
    [Range(0.1f, 5f)] public float jobYieldMultiplier = 1f;

    [Header("Battle Rewards")]
    [Range(0.1f, 5f)] public float xpGainMultiplier = 1f;      // applies to Growth Cores / XP-type rewards
    [Range(0.1f, 5f)] public float creditGainMultiplier = 1f;  // applies to Credits

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Header("Debug")]
    public bool logBalanceOverrides = false;
#endif
}
