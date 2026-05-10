using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable monster card view for Executive Trial UI.
/// Used by Replace panel (party cards + incoming recruit), and can be reused elsewhere.
///
/// Design goals:
/// - Pure view layer: display icon/name/level/title/hp
/// - Optional click handler (for selectable cards)
/// - Optional lock badge + selected frame
/// </summary>
public sealed class ExecutiveTrialMonsterCardUI : MonoBehaviour
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

    [Header("State")]
    [Tooltip("Shown when this card is selected.")]
    [SerializeField] private GameObject selectedFrame;

    [Tooltip("Shown when this card is locked (incoming recruit).")]
    [SerializeField] private GameObject lockedTag;

    [Header("Type")]
    [SerializeField] private Image typeIcon;
    [SerializeField] private TypeIconLibrary typeIconLibrary;

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

    public void Bind(ExecutiveTrailMonster monster, bool isLocked, bool isSelectable)
    {
        if (monster == null || monster.def == null)
        {
            if (icon) icon.sprite = null;
            if (nameLabel) nameLabel.text = "-";
            if (levelLabel) levelLabel.text = string.Empty;
            if (titleLabel) titleLabel.text = string.Empty;
            if (hpLabel) hpLabel.text = string.Empty;
            if (hpSlider) hpSlider.value = 0f;
            SetLocked(isLocked);
            SetSelectable(isSelectable);
            SetSelected(false);
            return;
        }

        if (icon) icon.sprite = MonsterNameFormatter.GetIcon(monster.def, monster.isPremium, false);
        if (nameLabel) nameLabel.text = MonsterNameFormatter.Format(monster.def, monster.isPremium);
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

        SetLocked(isLocked);
        SetSelectable(isSelectable);
        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        if (selectedFrame) selectedFrame.SetActive(selected);
    }

    public void SetLocked(bool locked)
    {
        if (lockedTag) lockedTag.SetActive(locked);
    }

    public void SetSelectable(bool selectable)
    {
        if (button) button.interactable = selectable;
    }

    private void HandleClick()
    {
        _onClick?.Invoke();
    }
}
