using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Individual incoming recruit card for the Iron Career Replace panel.
/// Uses its own prefab/layout and binds offer data directly.
/// </summary>
public sealed class IronCareerIncomingCardUI : MonoBehaviour
{
    [Header("Core")]
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

    public void Bind(IronMonster offer)
    {
        if (offer == null || offer.def == null)
        {
            Clear();
            return;
        }

        if (icon) icon.sprite = MonsterNameFormatter.GetIcon(offer.def, offer.isPremium, false);
        if (nameLabel) nameLabel.text = MonsterNameFormatter.Format(offer.def, offer.isPremium);
        if (levelLabel) levelLabel.text = $"Lv {Mathf.Max(1, offer.level)}";
        if (titleLabel) titleLabel.text = (offer.lockedTitle != null) ? offer.lockedTitle.displayName : string.Empty;

        if (typeIcon != null && typeIconLibrary != null)
        {
            var sprite = typeIconLibrary.GetIcon(offer.def.type);
            typeIcon.sprite = sprite;
            typeIcon.gameObject.SetActive(sprite != null);
        }

        float max = Mathf.Max(1f, offer.maxHp);
        float cur = Mathf.Clamp(offer.hp, 0f, max);

        if (hpSlider) hpSlider.value = cur / max;
        if (hpLabel) hpLabel.text = $"{Mathf.CeilToInt(cur)}/{Mathf.CeilToInt(max)}";
    }

    public void Clear()
    {
        if (icon) icon.sprite = null;
        if (nameLabel) nameLabel.text = "-";
        if (levelLabel) levelLabel.text = string.Empty;
        if (titleLabel) titleLabel.text = string.Empty;
        if (hpLabel) hpLabel.text = string.Empty;
        if (hpSlider) hpSlider.value = 0f;

        if (typeIcon)
        {
            typeIcon.sprite = null;
            typeIcon.gameObject.SetActive(false);
        }
    }
}
