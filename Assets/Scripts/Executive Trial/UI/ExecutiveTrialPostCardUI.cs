using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dedicated read-only monster card for the Executive Trial Post-Battle screen.
/// Displays icon, name, level, title, HP bar, type icon, and alive/dead state.
/// No selection, locking, or click behaviour — purely informational.
/// </summary>
public sealed class ExecutiveTrialPostCardUI : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private TextMeshProUGUI titleLabel;

    [Header("HP")]
    [SerializeField] private Slider hpSlider;
    [SerializeField] private Image hpFill;
    [SerializeField] private TextMeshProUGUI hpLabel;

    [Header("Type")]
    [SerializeField] private Image typeIcon;
    [SerializeField] private TypeIconLibrary typeIconLibrary;

    [Header("Dead State")]
    [Tooltip("Overlay or tint shown when the monster is dead.")]
    [SerializeField] private GameObject deadOverlay;
    [SerializeField] private CanvasGroup cardCanvasGroup;
    [SerializeField] private float deadAlpha = 0.45f;

    [Header("HP Bar Colours")]
    [SerializeField] private Color hpHealthy = new Color(0.25f, 0.85f, 0.25f, 1f);
    [SerializeField] private Color hpLow = new Color(0.95f, 0.75f, 0.15f, 1f);
    [SerializeField] private Color hpCritical = new Color(0.90f, 0.20f, 0.15f, 1f);

    public void Bind(ExecutiveTrailMonster monster)
    {
        if (monster == null || monster.def == null)
        {
            BindEmpty();
            return;
        }

        // Icon
        if (icon) icon.sprite = MonsterNameFormatter.GetIcon(monster.def, monster.isPremium, false);

        // Name / Level / Title
        if (nameLabel) nameLabel.text = MonsterNameFormatter.Format(monster.def, monster.isPremium);
        if (levelLabel) levelLabel.text = $"LV: {Mathf.Max(1, monster.level)}";
        if (titleLabel)
        {
            titleLabel.text = (monster.lockedTitle != null) ? $"Title: {monster.lockedTitle.displayName}" : "Title: Untitled";
        }

        // Type icon
        if (typeIcon != null && typeIconLibrary != null)
        {
            var sprite = typeIconLibrary.GetIcon(monster.def.type);
            typeIcon.sprite = sprite;
            typeIcon.gameObject.SetActive(sprite != null);
        }

        // HP
        float max = Mathf.Max(1f, monster.maxHp);
        float cur = Mathf.Clamp(monster.hp, 0f, max);
        float ratio = cur / max;

        if (hpSlider) hpSlider.value = ratio;
        if (hpLabel) hpLabel.text = $"{Mathf.CeilToInt(cur)} / {Mathf.CeilToInt(max)}";
        if (hpFill) hpFill.color = GetHpColor(ratio);

        // Dead state
        bool dead = monster.IsDead;
        if (deadOverlay) deadOverlay.SetActive(dead);
        if (cardCanvasGroup) cardCanvasGroup.alpha = dead ? deadAlpha : 1f;
    }

    private void BindEmpty()
    {
        if (icon) icon.sprite = null;
        if (nameLabel) nameLabel.text = "-";
        if (levelLabel) levelLabel.text = string.Empty;
        if (titleLabel) { titleLabel.text = string.Empty; titleLabel.gameObject.SetActive(false); }
        if (hpSlider) hpSlider.value = 0f;
        if (hpLabel) hpLabel.text = string.Empty;
        if (typeIcon) typeIcon.gameObject.SetActive(false);
        if (deadOverlay) deadOverlay.SetActive(false);
        if (cardCanvasGroup) cardCanvasGroup.alpha = 1f;
    }

    private Color GetHpColor(float ratio)
    {
        if (ratio > 0.5f) return hpHealthy;
        if (ratio > 0.2f) return hpLow;
        return hpCritical;
    }
}
