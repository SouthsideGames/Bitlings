using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class StatChipUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI valueText;

    public void Bind(Sprite iconSprite, string label, string value)
    {
        if (icon != null) icon.sprite = iconSprite;
        if (labelText != null) labelText.text = label ?? string.Empty;
        if (valueText != null) valueText.text = value ?? string.Empty;
    }
}
