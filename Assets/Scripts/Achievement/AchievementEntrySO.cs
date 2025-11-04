using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Data/Achievements/Achievement Entry", fileName = "Achievement_")]
public class AchievementEntrySO : ScriptableObject
{
    public string id;
    public string title;
    [TextArea] public string description;
    public Sprite icon;
    public bool hiddenUntilComplete;
    public AchievementConditionKind condition = AchievementConditionKind.CounterAtLeast;
    public int targetValue = 1;
    public string counterKey;
    public MonsterType requiredType;
    public List<string> requiredMonsterIds;
    public int gemsReward = 1;
}
public enum AchievementConditionKind
{
    CounterAtLeast,
    Boolean,
    OwnAllOfType,
    OwnAllOfIds
}
