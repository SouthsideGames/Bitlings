using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class LevelUpListItemUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private Toggle autoApplyToggle;

    OwnedMonsterData _data;
    Action<OwnedMonsterData> _onClick;

    bool _suppressToggle;

    public void Bind(OwnedMonsterData data, Sprite iconSprite, string displayName, Action<OwnedMonsterData> onClick)
    {
        _data = data;
        _onClick = onClick;

        if (icon) icon.sprite = iconSprite;
        if (nameText) nameText.text = displayName;
        if (levelText) levelText.text = $"Lv {_data.level}";

        if (autoApplyToggle)
        {
            // Feature gate
            bool unlocked = IsAutoGrowthUnlocked();
            autoApplyToggle.gameObject.SetActive(unlocked);

            _suppressToggle = true;
            autoApplyToggle.SetIsOnWithoutNotify(_data.autoApply);
            _suppressToggle = false;

            autoApplyToggle.onValueChanged.RemoveAllListeners();
            autoApplyToggle.onValueChanged.AddListener(OnAutoToggleChanged);
        }

        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => _onClick?.Invoke(_data));
        }
    }

    private bool IsAutoGrowthUnlocked()
    {
        return FeatureUnlockManager.I != null &&
               FeatureUnlockManager.I.IsUnlocked(FeatureId.AutoGrowth_Basic);
    }

    private void OnAutoToggleChanged(bool v)
    {
        if (_suppressToggle) return;
        if (_data == null) return;

        if (!IsAutoGrowthUnlocked())
        {
            _suppressToggle = true;
            if (autoApplyToggle) autoApplyToggle.SetIsOnWithoutNotify(false);
            _suppressToggle = false;

            _data.autoApply = false;
            SaveManager.Save();
            return;
        }

        _data.autoApply = v;

        // If enabling, set a safe default target: at least +1 level
        if (v && _data.autoApplyTargetLevel < _data.level + 1)
            _data.autoApplyTargetLevel = _data.level + 1;

        SaveManager.Save();

        // Wake up listeners
        GameEvents.OnTeamChanged?.Invoke();

        // NEW: request auto-apply through the centralized GameEvent
        GameEvents.RaiseAutoApplyRequested();
    }

    public void RefreshLevel()
    {
        if (levelText && _data != null)
            levelText.text = $"Lv {_data.level}";
    }
}
