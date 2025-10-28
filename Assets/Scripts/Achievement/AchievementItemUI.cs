using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AchievementItemUI : MonoBehaviour
{
    [SerializeField] Image icon;
    [SerializeField] TextMeshProUGUI title;
    [SerializeField] TextMeshProUGUI desc;
    [SerializeField] TextMeshProUGUI progressLabel;
    [SerializeField] TextMeshProUGUI rewardLabel;
    [SerializeField] GameObject completedBadge;

    AchievementEntrySO data;
    MonsterLibrarySO monLib;

    public void Bind(AchievementEntrySO entry, MonsterLibrarySO lib)
    {
        data = entry;
        monLib = lib;

        if (icon) icon.sprite = entry.icon;
        if (title) title.text = entry.title;
        if (desc)  desc.text  = entry.description;
        if (rewardLabel) rewardLabel.text = (entry.gemsReward > 0) ? $"+{entry.gemsReward}" : "";

        Refresh();
    }

    public void Refresh()
    {
        if (data == null) return;

        bool isDone = AchievementService.I != null && AchievementService.I.IsCompleted(data.id);
        if (completedBadge) completedBadge.SetActive(isDone);

        string progText = "-";
        int cur = 0, max = Mathf.Max(1, data.targetValue);

        switch (data.condition)
        {
            case AchievementConditionKind.Boolean:
                cur = isDone ? 1 : AchievementService.I.GetProgress(data.id);
                progText = $"{Mathf.Clamp(cur,0,1)}/1";
                break;

            case AchievementConditionKind.CounterAtLeast:
                cur = AchievementService.I.GetProgress(data.counterKey);
                progText = $"{Mathf.Min(cur, max)}/{max}";
                break;

            case AchievementConditionKind.OwnAllOfType:
            {
                if (monLib != null)
                {
                    var all = monLib.GetAllOfType(data.requiredType, true);
                    int total = (all != null) ? all.Length : 0;
                    int owned = 0;
                    if (all != null)
                    {
                        for (int i = 0; i < all.Length; i++)
                        {
                            var def = all[i];
                            if (def == null) continue;
                            if (!monLib.IsAvailable(def)) continue;
                            if (Owns(def.id)) owned++;
                        }
                    }
                    progText = $"{owned}/{Mathf.Max(0,total)}";
                }
                break;
            }

            case AchievementConditionKind.OwnAllOfIds:
            {
                int total = (data.requiredMonsterIds != null) ? data.requiredMonsterIds.Count : 0;
                int owned = 0;
                if (data.requiredMonsterIds != null)
                    for (int i = 0; i < data.requiredMonsterIds.Count; i++)
                        if (Owns(data.requiredMonsterIds[i])) owned++;
                progText = $"{owned}/{Mathf.Max(0,total)}";
                break;
            }
        }

        if (progressLabel) progressLabel.text = progText;
        if (data.hiddenUntilComplete && !isDone)
        {
            if (title) title.text = "???";
            if (desc)  desc.text  = "Locked";
            if (icon)  icon.sprite = null;
        }
    }

    bool Owns(string id)
    {
        var pd = SaveManager.Data;
        if (pd == null || pd.owned == null) return false;
        for (int i = 0; i < pd.owned.Count; i++)
            if (pd.owned[i].monsterId == id) return true;
        return false;
    }
}
