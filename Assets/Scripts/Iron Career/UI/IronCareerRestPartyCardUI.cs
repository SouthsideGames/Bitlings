using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Party card view used by the Iron Career Rest panel.
/// Displays only image, level, type, name, title, and health.
/// </summary>
public sealed class IronCareerRestPartyCardUI : MonoBehaviour
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

    public void Bind(IronMonster monster)
    {
        if (monster == null || monster.def == null)
        {
            Clear();
            return;
        }

        if (icon) icon.sprite = MonsterNameFormatter.GetIcon(monster.def, monster.isPremium, false);
        if (nameLabel) nameLabel.text = MonsterNameFormatter.Format(monster.def, monster.isPremium);
        if (levelLabel) levelLabel.text = $"LV: {Mathf.Max(1, monster.level)}";

        string titleText = (monster.lockedTitle != null && !string.IsNullOrWhiteSpace(monster.lockedTitle.displayName))
            ? monster.lockedTitle.displayName
            : "Untitled";
        if (titleLabel) titleLabel.text = $"Title: {titleText}";

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
    }

    public void Clear()
    {
        if (icon) icon.sprite = null;
        if (nameLabel) nameLabel.text = "-";
        if (levelLabel) levelLabel.text = string.Empty;
        if (titleLabel) titleLabel.text = "Title: Untitled";
        if (hpLabel) hpLabel.text = string.Empty;
        if (hpSlider) hpSlider.value = 0f;

        if (typeIcon)
        {
            typeIcon.sprite = null;
            typeIcon.gameObject.SetActive(false);
        }
    }
}