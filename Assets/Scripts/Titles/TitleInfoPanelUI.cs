using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class TitleInfoPanelUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup rootGroup;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descText;
    [SerializeField] private Button closeButton;

    [Header("Anim")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.12f;

    private int _lt;

    void Awake()
    {
        if (closeButton) closeButton.onClick.AddListener(Hide);
        HideImmediate();
    }

    public void Show(TitleSO title)
    {
        if (!title) return;

        if (iconImage)
        {
            iconImage.sprite = title.icon;
            iconImage.gameObject.SetActive(title.icon != null);
        }

        if (nameText) nameText.text = title.DisplayOrId;
        if (descText) descText.text = title.description ?? "";

        Show();
    }

    public void Show()
    {
        if (!rootGroup) { gameObject.SetActive(true); return; }

        gameObject.SetActive(true);
        rootGroup.blocksRaycasts = true;
        rootGroup.interactable = true;

        LeanTween.cancel(_lt);
        rootGroup.alpha = 0f;
        _lt = LeanTween.alphaCanvas(rootGroup, 1f, fadeDuration)
            .setIgnoreTimeScale(true)
            .id;
    }

    public void Hide()
    {
        if (!rootGroup) { gameObject.SetActive(false); return; }

        rootGroup.blocksRaycasts = false;
        rootGroup.interactable = false;

        LeanTween.cancel(_lt);
        _lt = LeanTween.alphaCanvas(rootGroup, 0f, fadeDuration)
            .setIgnoreTimeScale(true)
            .setOnComplete(() => gameObject.SetActive(false))
            .id;
    }

    private void HideImmediate()
    {
        if (rootGroup)
        {
            rootGroup.alpha = 0f;
            rootGroup.blocksRaycasts = false;
            rootGroup.interactable = false;
        }
        gameObject.SetActive(false);
    }
}
