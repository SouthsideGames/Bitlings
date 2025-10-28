using UnityEngine;

[CreateAssetMenu(menuName = "Data/Tags/Monster Tag Track", fileName = "TagTrack_")]
public class MonsterTagTrackSO : ScriptableObject
{
    [Header("Unlock schedule (levels 1..15)")]
    [Min(1)] public int maxLevel = 15;

    [Tooltip("Tags that unlock as this monster levels job XP.")]
    public TagSO[] tags = new TagSO[5];

    [Tooltip("Levels when each tag unlocks.")]
    public int[] unlockLevels = new int[5] { 2, 5, 8, 12, 15 };

    void OnValidate()
    {
        if (tags == null || unlockLevels == null) return;
        if (tags.Length != unlockLevels.Length)
            Debug.LogWarning($"{name}: tags and unlockLevels should be the same length.");
        for (int i = 0; i < unlockLevels.Length; i++)
        {
            unlockLevels[i] = Mathf.Clamp(unlockLevels[i], 1, Mathf.Max(1, maxLevel));
        }
    }
}
