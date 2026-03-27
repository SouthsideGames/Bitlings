using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mini card used in Forced Evolution preview (Before/After).
/// Pure view: no selection logic.
/// </summary>
public sealed class IronCareerEvolutionPreviewCardUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameTMP;
    [SerializeField] private TextMeshProUGUI levelTMP;
    [SerializeField] private TextMeshProUGUI titleTMP;

    public void Bind(MonsterDataSO def, int level, TitleSO lockedTitle, bool isShiny = false)
    {
        if (icon) icon.sprite = def ? MonsterNameFormatter.GetIcon(def, isShiny, false) : null;

        if (nameTMP) nameTMP.text = def ? MonsterNameFormatter.Format(def, isShiny) : "?";
        if (levelTMP) levelTMP.text = $"Lv {Mathf.Max(1, level)}";
        if (titleTMP) titleTMP.text = lockedTitle ? $"Title: {lockedTitle.displayName}" : "Title: —";
    }

    public void Clear()
    {
        if (icon) icon.sprite = null;
        if (nameTMP) nameTMP.text = "—";
        if (levelTMP) levelTMP.text = "";
        if (titleTMP) titleTMP.text = "";
    }
}
