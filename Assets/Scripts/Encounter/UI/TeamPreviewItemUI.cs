using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TeamPreviewItemUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private CanvasGroup cg; // optional fade

    public void Bind(OwnedMonsterData om)
    {
        if (om == null) return;

        // Lookup definition (icon + display name)
        string displayName = om.monsterId;
        Sprite sprite = null;

        try
        {
            var def = MonsterLibraryLocator.GetById(om.monsterId);
            if (def != null)
            {
                if (!string.IsNullOrEmpty(def.displayName))
                    displayName = def.displayName;
                sprite = def.icon; // assumes MonsterDataSO has "public Sprite icon;"
            }
        }
        catch { }

        if (icon)
        {
            icon.sprite = sprite;
            icon.enabled = (sprite != null);
        }

        if (nameText)  nameText.text  = string.IsNullOrEmpty(displayName) ? "Unknown" : displayName;
        if (levelText) levelText.text = $"Lv {Mathf.Max(1, om.level)}";
    }

    public void SetAlpha(float a)
    {
        if (!cg) return;
        a = Mathf.Clamp01(a);
        cg.alpha = a;
        cg.blocksRaycasts = a >= 0.99f;
        cg.interactable   = a >= 0.99f;
    }
}
