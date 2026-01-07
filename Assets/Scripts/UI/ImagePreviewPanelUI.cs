using UnityEngine;
using UnityEngine.UI;

public sealed class ImagePreviewPanelUI : MonoBehaviour
{
    private static ImagePreviewPanelUI _i;
    public static ImagePreviewPanelUI I => _i != null ? _i : FindEvenIfInactive();

    [Header("UI")]
    [SerializeField] private Image previewImage;
    [SerializeField] private Button closeButton;

    [Header("Panel")]
    [SerializeField] private PanelId previewPanelId = PanelId.ImagePreview;

    void Awake()
    {
        _i = this;

        if (previewImage)
        {
            previewImage.preserveAspect = true;
            previewImage.raycastTarget = false;
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
            Debug.LogWarning("[ImagePreviewPanelUI] previewImage not assigned.");
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

    private static ImagePreviewPanelUI FindEvenIfInactive()
    {
        // This finds components even if their GameObjects are inactive.
        var all = Resources.FindObjectsOfTypeAll<ImagePreviewPanelUI>();
        for (int i = 0; i < all.Length; i++)
        {
            // Ignore prefabs in project view; only accept scene objects.
            if (all[i] != null && all[i].gameObject.scene.IsValid())
            {
                _i = all[i];
                return _i;
            }
        }

        return null;
    }
}
