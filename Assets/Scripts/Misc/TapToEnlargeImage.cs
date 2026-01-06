using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public sealed class TapToEnlargeImage : MonoBehaviour, IPointerClickHandler
{
    [Header("Source")]
    [SerializeField] private Image sourceImage; // if null, auto-grab same object Image

    [Header("Behavior")]
    [SerializeField] private bool requireSprite = true;
    [SerializeField] private bool ignoreIfPreviewAlreadyOpen = true;

    void Awake()
    {
        if (!sourceImage) sourceImage = GetComponent<Image>();

        // Ensure this can receive taps
        if (sourceImage) sourceImage.raycastTarget = true;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (ImagePreviewPanelUI.I == null)
        {
            Debug.LogWarning("[TapToEnlargeImage] No ImagePreviewPanelUI in scene.");
            return;
        }

        if (ignoreIfPreviewAlreadyOpen && UIManager.I != null && UIManager.I.IsOpen(PanelId.ImagePreview))
            return;

        if (!sourceImage)
            return;

        var sprite = sourceImage.sprite;

        if (requireSprite && sprite == null)
            return;

        ImagePreviewPanelUI.I.Open(sprite);
    }
}
