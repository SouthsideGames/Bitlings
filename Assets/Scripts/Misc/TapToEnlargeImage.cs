using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public sealed class TapToEnlargeImage : MonoBehaviour, IPointerClickHandler
{
    [Header("Source")]
    [SerializeField] private Image sourceImage;

    [Header("Behavior")]
    [SerializeField] private bool requireSprite = true;

    void Awake()
    {
        if (!sourceImage) sourceImage = GetComponent<Image>();
        if (sourceImage) sourceImage.raycastTarget = true;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!sourceImage)
        {
            Debug.LogWarning("[TapToEnlargeImage] Missing sourceImage.");
            return;
        }

        var sprite = sourceImage.sprite;

        if (requireSprite && sprite == null)
        {
            Debug.LogWarning($"[TapToEnlargeImage] No sprite on '{name}'.");
            return;
        }

        var preview = ImagePreviewPanelUI.I;
        if (preview == null)
        {
            Debug.LogWarning("[TapToEnlargeImage] No ImagePreviewPanelUI found in scene (even inactive).");
            return;
        }

        preview.Open(sprite);
    }
}
