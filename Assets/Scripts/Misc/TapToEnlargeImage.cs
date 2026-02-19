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
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[TapToEnlargeImage] Missing sourceImage.");
            #endif
            return;
        }

        var sprite = sourceImage.sprite;

        if (requireSprite && sprite == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[TapToEnlargeImage] No sprite on '{name}'.");
            #endif
            return;
        }

        var preview = ImagePreviewPanelUI.I;
        if (preview == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[TapToEnlargeImage] No ImagePreviewPanelUI found in scene (even inactive).");
            #endif
            return;
        }

        preview.Open(sprite);
    }
}