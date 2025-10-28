using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class MonsterTrainingItemUI : MonoBehaviour
{
    [Header("Refs (assign in prefab)")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI subText;
    [SerializeField] private Button levelUpBtn;
    [SerializeField] private Button selectBtn;

    private OwnedMonsterData data;
    private Action<OwnedMonsterData> onSelected;

    public void Setup(OwnedMonsterData om, Action<OwnedMonsterData> onSelectedCallback = null)
    {
        data = om;
        onSelected = onSelectedCallback;

        if (selectBtn)
        {
            selectBtn.onClick.RemoveAllListeners();
            selectBtn.onClick.AddListener(() => onSelected?.Invoke(data));
        }

        if (levelUpBtn)
        {
            levelUpBtn.onClick.RemoveAllListeners();
            levelUpBtn.onClick.AddListener(OnClickClaimOneLevel);
        }

        RefreshUI();
    }

    public void RefreshUI()
    {
        if (data == null) return;

        var def = MonsterLibraryLocator.GetById(data.monsterId);
        if (def)
        {
            if (icon) icon.sprite = def.icon ? def.icon : def.shinyIcon;
            if (nameText) nameText.text = def.displayName;
        }
        else
        {
            if (nameText) nameText.text = string.IsNullOrEmpty(data.monsterId) ? "Unknown" : data.monsterId;
        }

        if (subText)
            subText.text = $"L{data.level}  •  {NextXpText(data)}";

        bool canClaim = TrainingManager.I && TrainingManager.I.CanClaimLevel(data);
        bool atCap = data.level >= LevelRules.MaxLevel;

        if (levelUpBtn)
        {
            levelUpBtn.interactable = canClaim && !atCap;
            var label = levelUpBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (label)
                label.text = atCap ? "MAX" : "Claim";
        }
    }

    private void OnClickClaimOneLevel()
    {
        if (!TrainingManager.I || data == null) return;

        if (data.level >= LevelRules.MaxLevel)
        {
            RefreshUI();
            return;
        }

        if (!TrainingManager.I.CanClaimLevel(data))
            return;

        TrainingManager.I.ClaimOneLevel(data);

        if (subText)
            subText.text = $"L{data.level}  •  {NextXpText(data)}";

        bool atCap = data.level >= LevelRules.MaxLevel;
        if (levelUpBtn)
        {
            levelUpBtn.interactable = TrainingManager.I.CanClaimLevel(data) && !atCap;
            var label = levelUpBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (label)
                label.text = atCap ? "MAX" : "Claim";
        }
    }

    private static string NextXpText(OwnedMonsterData om)
    {
        if (om == null) return string.Empty;
        if (om.level >= LevelRules.MaxLevel) return "MAX";

        int next = LevelRules.XPToNext(om.level);
        return $"{om.currentXP}/{next} XP";
    }
}
