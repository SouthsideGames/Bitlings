using UnityEngine;

[CreateAssetMenu(menuName = "Data/Config/Idle Battle Config", fileName = "IdleBattleConfig")]
public class IdleBattleConfigSO : ScriptableObject
{
    [Header("Offline")]
    [Min(0)] public int maxOfflineHours = 8;

    [Header("Pacing & Cost")]
    [Min(0.25f)] public float secondsPerRift = 4f;
    [Min(1)] public int energyPerRift = 1;

    [Header("Rewards")]
    [Range(0.1f, 5f)] public float rewardMultiplier = 1f;
    public int basecreditPerWin = 5;

    [Header("Capture")]
    public bool allowCapturesOffline = false; 

    [Header("Log")]
    [Min(10)] public int riftLogMaxEntries = 60;

    [Header("Feature Unlocks")]
    [Range(1f, 5f)] public float rewardBoostMultiplier = 1.5f;

    [Header("Premium Pity")]
    [Tooltip("Bad-luck protection for premium rolls in idle rifts. After this many " +
             "consecutive premium-eligible rifts without a premium, the next roll is " +
             "guaranteed. 0 = disabled (pure 1/512 memoryless odds, the pre-pity behavior).")]
    [Min(0)] public int premiumPityThreshold = 0;
}
