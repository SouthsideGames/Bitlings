using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Achievements/Achievement Library", fileName = "AchievementLibrary")]
public sealed class AchievementLibrarySO : ScriptableObject
{
    public List<AchievementEntrySO> entries = new List<AchievementEntrySO>();
}
