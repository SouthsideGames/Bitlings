using UnityEngine;

[CreateAssetMenu(menuName = "Data/Monsters/Level Up Bucket", fileName = "LevelUpBucket")]
public class LevelUpBucketSO : ScriptableObject
{
    public string bucketId;   // "Offense" / "Defense" / "Utility"
    public Sprite icon;
    [TextArea] public string description;

    [Header("Weights")]
    public float hp  = 1f;
    public float atk = 1f;
    public float def = 1f;
    public float spd = 1f;

    public float Total => Mathf.Max(0.0001f, hp + atk + def + spd);
}
