using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InfoPanelUI : MonoBehaviour
{
    public static InfoPanelUI I { get; private set; }

    [Header("Wire to the object registered as PanelId.Info in UIManager")]
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI subtitleLabel;
    [SerializeField] private TextMeshProUGUI bodyLabel;
    [SerializeField] private Button closeButton;

    void Awake()
    {
        I = this;
        if (closeButton) closeButton.onClick.AddListener(Close);
    }

    public void Set(string title, string subtitle, string body)
    {
        if (titleLabel)    titleLabel.text  = title ?? "";
        if (subtitleLabel) subtitleLabel.text = subtitle ?? "";
        if (bodyLabel)     bodyLabel.text   = body ?? "";
    }

    public void Open()
    {
        if (UIManager.I) UIManager.I.Show(PanelId.Info);
        else gameObject.SetActive(true);
    }

    public void Close()
    {
        if (UIManager.I) UIManager.I.Hide(PanelId.Info);
        else gameObject.SetActive(false);
    }
}
