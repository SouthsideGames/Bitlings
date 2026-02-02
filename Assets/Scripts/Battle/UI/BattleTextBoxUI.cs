using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class BattleTextBoxUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI lineText;

    [Header("Inline Icons")]
    [SerializeField] private GameObject iconsRoot; // optional holder
    [SerializeField] private Image critIcon;
    [SerializeField] private Image shieldIcon;
    [SerializeField] private Image effectiveIcon;      // reuse for both SE / NVE (swap sprite)
    [SerializeField] private Sprite superEffectiveSprite;
    [SerializeField] private Sprite notEffectiveSprite;

    [Header("Timing")]
    [SerializeField] private float typeSecondsPerChar = 0.02f;
    [SerializeField] private float lineHoldSeconds = 0.25f;

    public IEnumerator ShowLine(string line, float battleSpeed)
        => ShowLine(new BattleLine(line, BattleLineTag.None), battleSpeed);

    public IEnumerator ShowLine(BattleLine line, float battleSpeed)
    {
        if (lineText == null) yield break;

        bool showIcons = SettingsManager.I == null || SettingsManager.I.GetShowInlineBattleIcons();

        if (iconsRoot) iconsRoot.SetActive(showIcons);

        if (showIcons)
        {
            if (critIcon)   critIcon.enabled   = (line.tags & BattleLineTag.Crit) != 0;
            if (shieldIcon) shieldIcon.enabled = (line.tags & BattleLineTag.Shield) != 0;

            bool se  = (line.tags & BattleLineTag.SuperEffective) != 0;
            bool nve = (line.tags & BattleLineTag.NotEffective) != 0;

            if (effectiveIcon)
            {
                effectiveIcon.enabled = se || nve;
                if (se && superEffectiveSprite) effectiveIcon.sprite = superEffectiveSprite;
                else if (nve && notEffectiveSprite) effectiveIcon.sprite = notEffectiveSprite;
            }
        }
        else
        {
            if (critIcon) critIcon.enabled = false;
            if (shieldIcon) shieldIcon.enabled = false;
            if (effectiveIcon) effectiveIcon.enabled = false;
        }

        // Simple typewriter (use your existing logic if different)
        lineText.text = "";
        string full = line.text ?? "";

        bool isAuto = (EncounterManager.I != null && EncounterManager.I.IsAutoMode);
        bool compressAuto = isAuto && (SettingsManager.I == null || SettingsManager.I.GetCompressAutoBattleText());

        float cps = Mathf.Max(0.001f, typeSecondsPerChar);
        float scaled = cps / Mathf.Max(0.25f, battleSpeed);

        // Auto-battle should never feel "stuck" on long lines.
        // If the player enabled compressed auto-battle text, we render instantly and only hold briefly.
        if (compressAuto)
        {
            lineText.text = full;
            float autoHold = Mathf.Max(0.05f, 0.2f / Mathf.Max(0.25f, battleSpeed));
            yield return new WaitForSecondsRealtime(autoHold);
            yield break;
        }

        // Otherwise, still speed up the typewriter while in auto-mode.
        if (isAuto) scaled *= 0.25f;

        // Hard-cap total type time so very long lines don't drag.
        if (full.Length * scaled > 0.75f)
        {
            lineText.text = full;
            yield return new WaitForSecondsRealtime(0.25f / Mathf.Max(0.25f, battleSpeed));
            yield break;
        }

        for (int i = 0; i < full.Length; i++)
        {
            lineText.text += full[i];
            yield return new WaitForSecondsRealtime(scaled);
        }

        float hold = Mathf.Max(0f, lineHoldSeconds / Mathf.Max(0.25f, battleSpeed));
        if (hold > 0f) yield return new WaitForSecondsRealtime(hold);
    }
}
