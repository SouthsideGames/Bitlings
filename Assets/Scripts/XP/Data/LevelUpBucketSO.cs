using UnityEngine;

[CreateAssetMenu(menuName = "Data/XP System/Level Up Bucket", fileName = "LevelUpBucket")]
public class LevelUpBucketSO : ScriptableObject
{
     [Header("Identity")]
    public string bucketId;         
    [TextArea] public string description;

    [Header("Pick Rules")]
    [Min(1)] public int picksPerLevel = 1;
    public bool allowDuplicatePicks = true; 

    [Header("Weighted Chances (integers)")]
    [Min(0)] public int hpWeight  = 1;
    [Min(0)] public int atkWeight = 1;
    [Min(0)] public int defWeight = 1;
    [Min(0)] public int spdWeight = 1;

    public int TotalWeight => Mathf.Max(0, hpWeight + atkWeight + defWeight + spdWeight);
}
