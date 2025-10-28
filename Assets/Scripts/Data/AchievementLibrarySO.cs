using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Data/Achievements/Achievement Library", fileName = "AchievementLibrary")]
public class AchievementLibrarySO : ScriptableObject
{
    public List<AchievementEntrySO> entries = new List<AchievementEntrySO>();
    public AchievementEntrySO Get(string id)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e != null && e.id == id) return e;
        }
        return null;
    }
}
