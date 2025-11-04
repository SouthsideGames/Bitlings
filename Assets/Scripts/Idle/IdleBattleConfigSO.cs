using UnityEngine;

[CreateAssetMenu(menuName = "Data/Config/Idle Battle Config", fileName = "IdleBattleConfig")]
public class IdleBattleConfigSO : ScriptableObject
{
    [Header("Offline")]
    [Min(0)] public int maxOfflineHours = 8;

    [Header("Pacing & Cost")]
    [Min(0.25f)] public float secondsPerEncounter = 4f;
    [Min(1)] public int energyPerEncounter = 1;

    [Header("Rewards")]
    [Range(0.1f, 5f)] public float rewardMultiplier = 1f;
    public int baseCoinPerWin = 5;

    [Header("Capture")]
    public bool allowCapturesOffline = false; 

    [Header("Log")]
    [Min(10)] public int encounterLogMaxEntries = 60;
}
