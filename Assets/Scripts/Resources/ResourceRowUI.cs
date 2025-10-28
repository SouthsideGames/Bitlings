using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ResourceRowUI : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI amountLabel;

    [Header("Interaction")]
    [SerializeField] private Button infoButton;
    [SerializeField] private string infoId; // kept serialized so prefab can still set it if needed

    private ResourceType _type;
    private string _displayName;
    private Sprite _icon;

    // --- NEW: primary bind that includes infoId ---
    public void BindStatic(string displayName, Sprite icon, ResourceType type, string infoId)
    {
        _type = type;
        _displayName = displayName;
        _icon = icon;
        this.infoId = infoId; // store incoming id

        if (nameLabel) nameLabel.text = displayName;
        if (iconImage) iconImage.sprite = icon;

        if (infoButton)
        {
            infoButton.onClick.RemoveAllListeners();
            infoButton.onClick.AddListener(OpenInfo);
        }
        else
        {
            Debug.LogWarning($"[ResourceRowUI] InfoButton not wired on {name}");
        }

        RefreshAmount();
    }

    // --- Backward compatibility overload (old call sites still work) ---
    public void BindStatic(string displayName, Sprite icon, ResourceType type)
        => BindStatic(displayName, icon, type, null);

    public void RefreshAmount()
    {
        if (!amountLabel) return;
        int amt = ResourceBank.Get(_type);
        amountLabel.text = Format(amt);
    }

    void OpenInfo()
    {
        // Prefer catalog/prefab-supplied id; fallback to res.{EnumName}
        var id = string.IsNullOrWhiteSpace(infoId) ? $"res.{_type}" : infoId;

        // Minimal fallbacks (SO text will override these when present)
        const string fallbackSubtitle = "Resource";
        const string fallbackBody = "Comes from: —\nUsed for: —";

        InfoRouter.Open(id, _displayName, fallbackSubtitle, fallbackBody, _icon);
    }

    string Format(int n)
    {
        if (n >= 1_000_000) return (n / 1_000_000f).ToString("0.0") + "M";
        if (n >= 1_000)     return (n / 1_000f).ToString("0.0") + "K";
        return n.ToString();
    }
}
