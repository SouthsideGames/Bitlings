using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Monsters/Bucket Library", fileName = "BucketLibrary")]
public class BucketLibrarySO : ScriptableObject
{
    public LevelUpBucketSO[] buckets;

    public LevelUpBucketSO GetById(string id, LevelUpBucketSO fallback = null)
    {
        if (string.IsNullOrEmpty(id) || buckets == null) return fallback;
        return buckets.FirstOrDefault(b => b && b.bucketId == id) ?? fallback;
    }

    public LevelUpBucketSO DefaultBucket()
    {
        // First bucket is the default (set in Inspector)
        return (buckets != null && buckets.Length > 0) ? buckets[0] : null;
    }
}
