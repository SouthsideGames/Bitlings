using UnityEngine;
using UnityEngine.UI;

public sealed class ImagePreviewPanelUI : MonoBehaviour
{
    public static ImagePreviewPanelUI I { get; private set; }

    [Header("UI")]
    [SerializeField] private Image previewImage;          // The big image on the panel
    [SerializeField] private Button closeButton;          // Optional
    [SerializeField] private Button backgroundCloseArea;  // Optional: full-screen button behind the preview image

    [Header("Panel")]
    [SerializeField] private PanelId previewPanelId = PanelId.ImagePreview;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Close);
        }

        if (backgroundCloseArea)
        {
            backgroundCloseArea.onClick.RemoveAllListeners();
            backgroundCloseArea.onClick.AddListener(Close);
        }

        // Optional safety defaults
        if (previewImage)
        {
            previewImage.preserveAspect = true;
            previewImage.raycastTarget = false; // prevents blocking the backgroundCloseArea if you use it
        }
    }

    public void Open(Sprite sprite)
    {
        if (!sprite)
        {
            Debug.LogWarning("[ImagePreviewPanelUI] Open called with null sprite.");
            return;
        }

        if (!previewImage)
        {
            Debug.LogWarning("[ImagePreviewPanelUI] previewImage is not assigned.");
            return;
        }

        previewImage.sprite = sprite;
        previewImage.color = Color.white;

        if (UIManager.I != null)
            UIManager.I.Show(previewPanelId);
        else
            gameObject.SetActive(true);
    }

    public void Close()
    {
        if (UIManager.I != null)
            UIManager.I.Hide(previewPanelId);
        else
            gameObject.SetActive(false);
    }
}
