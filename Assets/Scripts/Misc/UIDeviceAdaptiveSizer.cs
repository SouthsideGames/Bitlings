using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class UIDeviceAdaptiveSizer : MonoBehaviour
{
    public enum DeviceType
    {
        Phone,
        Tablet
    }

    [System.Serializable]
    public class LayoutSettings
    {
        public Vector2 sizeDelta = new Vector2(600f, 800f);
        public Vector2 anchoredPosition = Vector2.zero;
        public Vector3 localScale = Vector3.one;
    }

    [Header("Detection")]
    [Tooltip("If aspect ratio is wider than this, treat it as a tablet.")]
    [SerializeField] private float tabletAspectThreshold = 0.65f;

    [Tooltip("Optional: use screen diagonal estimate to help identify tablets.")]
    [SerializeField] private bool useDpiCheck = false;

    [Tooltip("Minimum estimated diagonal inches to count as tablet when DPI is available.")]
    [SerializeField] private float tabletMinInches = 7.6f;

    [Header("Phone Layout")]
    [SerializeField] private LayoutSettings phoneLayout = new LayoutSettings();

    [Header("Tablet Layout")]
    [SerializeField] private LayoutSettings tabletLayout = new LayoutSettings();

    [Header("Options")]
    [SerializeField] private bool applyOnEnable = false;
    [SerializeField] private bool updateContinuouslyInEditor = false;

    private RectTransform rectTransform;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        if (applyOnEnable)
        {
            ApplyLayout();
        }
    }

#if UNITY_EDITOR
    private void Update()
    {
        if (!Application.isPlaying && updateContinuouslyInEditor)
        {
            if (rectTransform == null)
                rectTransform = GetComponent<RectTransform>();

            ApplyLayout();
        }
    }
#endif

    [ContextMenu("Apply Layout")]
    public void ApplyLayout()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        LayoutSettings selectedLayout = GetDeviceType() == DeviceType.Tablet
            ? tabletLayout
            : phoneLayout;

        rectTransform.sizeDelta = selectedLayout.sizeDelta;
        rectTransform.anchoredPosition = selectedLayout.anchoredPosition;
        rectTransform.localScale = selectedLayout.localScale;
    }

    public DeviceType GetDeviceType()
    {
        float width = Screen.width;
        float height = Screen.height;

        if (width <= 0 || height <= 0)
            return DeviceType.Phone;

        float shortSide = Mathf.Min(width, height);
        float longSide = Mathf.Max(width, height);
        float aspectRatio = shortSide / longSide;

        bool looksLikeTabletByAspect = aspectRatio > tabletAspectThreshold;

        if (useDpiCheck && Screen.dpi > 0)
        {
            float diagonalPixels = Mathf.Sqrt((width * width) + (height * height));
            float diagonalInches = diagonalPixels / Screen.dpi;

            // Require both wide aspect ratio AND large screen to classify as tablet.
            // Prevents large phones (tall, narrow screens) from being misdetected.
            return (looksLikeTabletByAspect && diagonalInches >= tabletMinInches)
                ? DeviceType.Tablet
                : DeviceType.Phone;
        }

        return looksLikeTabletByAspect ? DeviceType.Tablet : DeviceType.Phone;
    }
}