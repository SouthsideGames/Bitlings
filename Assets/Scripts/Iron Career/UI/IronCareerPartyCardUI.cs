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
        if (!button) button = GetComponentInChildren<Button>(true);
        if (button) button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button) button.onClick.RemoveAllListeners();
        _onClick = null;
    }

    public void SetOnClick(Action onClick)
    {
        EnsureButtonHierarchyActive();
        _onClick = onClick;

        if (button)
        {
            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
            button.interactable = onClick != null;
        }
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
            if (button) button.interactable = false;
            SetSelected(false);
            return;
        }

        if (icon) icon.sprite = MonsterNameFormatter.GetIcon(monster.def, monster.isShiny, false);
        if (nameLabel) nameLabel.text = MonsterNameFormatter.Format(monster.def, monster.isShiny);
        if (levelLabel) levelLabel.text = $"Lv {Mathf.Max(1, monster.level)}";
        if (titleLabel) titleLabel.text = (monster.lockedTitle != null) ? monster.lockedTitle.displayName : string.Empty;
        EnsureButtonHierarchyActive();
        if (button) button.interactable = true;

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
        if (selectedFrame && selectedFrame != gameObject) selectedFrame.SetActive(selected);
        if (!selected) EnsureButtonHierarchyActive();
    }

    private void EnsureButtonHierarchyActive()
    {
        if (!button) button = GetComponentInChildren<Button>(true);
        if (!button) return;

        var current = button.transform;
        while (current)
        {
            if (!current.gameObject.activeSelf) current.gameObject.SetActive(true);
            if (current == transform) break;
            current = current.parent;
        }

        RouteRaycastsToButton();
    }

    private void RouteRaycastsToButton()
    {
        if (!button) return;

        var targetGraphic = button.targetGraphic;
        var allGraphics = GetComponentsInChildren<Graphic>(true);

        foreach (var g in allGraphics)
            g.raycastTarget = (g == targetGraphic);

        if (targetGraphic == null)
        {
            var img = button.GetComponent<Image>();
            if (img == null) img = GetComponent<Image>();
            if (img != null)
            {
                button.targetGraphic = img;
                img.raycastTarget = true;
            }
        }
    }

    private void HandleClick()
    {
        AudioManager.I?.PlayClick();
        _onClick?.Invoke();
    }
}
