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

    public void Bind(OwnedMonsterData data, Sprite iconSprite, string displayName, Action<OwnedMonsterData> onClick)
    {
        _data = data;
        _onClick = onClick;
        if (icon) icon.sprite = iconSprite;
        if (nameText) nameText.text = displayName;
        if (levelText) levelText.text = $"Lv {_data.level}";
        if (autoApplyToggle)
        {
            autoApplyToggle.isOn = _data.autoApply;
            autoApplyToggle.onValueChanged.RemoveAllListeners();
            autoApplyToggle.onValueChanged.AddListener(v =>
            {
                _data.autoApply = v;
                // If enabling and you want to set a default target, do it here
                if (v && _data.autoApplyTargetLevel < _data.level + 1)
                    _data.autoApplyTargetLevel = _data.level + 1;
            });
        }
        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => _onClick?.Invoke(_data));
        }
    }

    public void RefreshLevel() { if (levelText && _data != null) levelText.text = $"Lv {_data.level}"; }
}
