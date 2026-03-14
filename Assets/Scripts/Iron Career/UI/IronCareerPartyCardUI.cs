using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Individual party member card for the Iron Career Replace panel.
/// Has its own prefab and layout, separate from IronCareerMonsterCardUI.
/// Designed to be tapped to select a party member for dismissal.
/// </summary>
public sealed class IronCareerPartyCardUI : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private TextMeshProUGUI titleLabel;

    [Header("HP")]
    [SerializeField] private Slider hpSlider;
    [SerializeField] private TextMeshProUGUI hpLabel;

    [Header("Type")]
    [SerializeField] private Image typeIcon;
    [SerializeField] private TypeIconLibrary typeIconLibrary;

    [Header("Selection")]
    [Tooltip("Shown when this card is selected for dismissal.")]
    [SerializeField] private GameObject selectedFrame;

    private Action _onClick;

    private void Awake()
    {
        if (button) button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button) button.onClick.RemoveAllListeners();
        _onClick = null;
    }

    public void SetOnClick(Action onClick)
    {
        _onClick = onClick;
    }

    public void Bind(IronMonster monster)
    {
        if (monster == null || monster.def == null)
        {
            if (icon) icon.sprite = null;
            if (nameLabel) nameLabel.text = "-";
            if (levelLabel) levelLabel.text = string.Empty;
            if (titleLabel) titleLabel.text = string.Empty;
            if (hpLabel) hpLabel.text = string.Empty;
            if (hpSlider) hpSlider.value = 0f;
            SetSelected(false);
            return;
        }

        if (icon) icon.sprite = monster.def.icon;
        if (nameLabel) nameLabel.text = monster.def.displayName;
        if (levelLabel) levelLabel.text = $"Lv {Mathf.Max(1, monster.level)}";
        if (titleLabel) titleLabel.text = (monster.lockedTitle != null) ? monster.lockedTitle.displayName : string.Empty;

        if (typeIcon != null && typeIconLibrary != null)
        {
            var sprite = typeIconLibrary.GetIcon(monster.def.type);
            typeIcon.sprite = sprite;
            typeIcon.gameObject.SetActive(sprite != null);
        }

        float max = Mathf.Max(1f, monster.maxHp);
        float cur = Mathf.Clamp(monster.hp, 0f, max);

        if (hpSlider) hpSlider.value = cur / max;
        if (hpLabel) hpLabel.text = $"{Mathf.CeilToInt(cur)}/{Mathf.CeilToInt(max)}";

        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        if (selectedFrame) selectedFrame.SetActive(selected);
    }

    private void HandleClick()
    {
        _onClick?.Invoke();
    }
}
