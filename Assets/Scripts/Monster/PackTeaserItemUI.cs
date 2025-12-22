using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PackTeaserItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI unlockedTag;
    [SerializeField] private CanvasGroup group;

    [Header("Look")]
    [Range(0f, 1f)]
    [SerializeField] private float lockedAlpha = 0.35f;

    public void Bind(MonsterPackSO pack, bool isUnlocked)
    {
        if (pack == null) return;

        if (icon) icon.sprite = pack.icon;
        if (nameText) nameText.text = pack.displayName;

        // Upcoming packs are intentionally "greyed/silhouette-like".
        if (group) group.alpha = lockedAlpha;

        if (unlockedTag)
        {
            unlockedTag.gameObject.SetActive(isUnlocked);
            unlockedTag.text = "Unlocked";
        }
    }
}
