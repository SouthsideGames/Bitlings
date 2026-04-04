using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class UIAdaptiveRootSizer : MonoBehaviour
{
    public enum DeviceClass
    {
        Phone,
        Tablet,
        WideTablet
    }

    [System.Serializable]
    public class LayoutProfile
    {
        [Header("Size (Non-Stretch Anchors)")]

        [Tooltip("Only use this when the RectTransform is NOT stretched horizontally. Maps to RectTransform.sizeDelta.x, which appears as Width in the Inspector.")]
        public bool overrideWidth;

        [Tooltip("Width value for non-stretch horizontal anchors. This is the Inspector 'Width' field, not Left/Right.")]
        public float width;

        [Tooltip("Only use this when the RectTransform is NOT stretched vertically. Maps to RectTransform.sizeDelta.y, which appears as Height in the Inspector.")]
        public bool overrideHeight;

        [Tooltip("Height value for non-stretch vertical anchors. This is the Inspector 'Height' field, not Top/Bottom.")]
        public float height;

        [Header("Position (Non-Stretch Anchors)")]

        [Tooltip("Only use this when the RectTransform is NOT stretched horizontally. Maps to RectTransform.anchoredPosition.x, which appears as Pos X in the Inspector.")]
        public bool overridePosX;

        [Tooltip("Horizontal anchored position for non-stretch anchors. This is the Inspector 'Pos X' field.")]
        public float posX;

        [Tooltip("Only use this when the RectTransform is NOT stretched vertically. Maps to RectTransform.anchoredPosition.y, which appears as Pos Y in the Inspector.")]
        public bool overridePosY;

        [Tooltip("Vertical anchored position for non-stretch anchors. This is the Inspector 'Pos Y' field.")]
        public float posY;

        [Header("Offsets (Stretch Anchors)")]

        [Tooltip("Use this when the RectTransform is stretched horizontally. Maps to RectTransform.offsetMin.x, which appears as Left in the Inspector.")]
        public bool overrideLeft;

        [Tooltip("Left offset for stretch anchors. Inspector Left = RectTransform.offsetMin.x")]
        public float left;

        [Tooltip("Use this when the RectTransform is stretched horizontally. Maps to negative RectTransform.offsetMax.x, which appears as Right in the Inspector.")]
        public bool overrideRight;

        [Tooltip("Right offset for stretch anchors. Inspector Right = -RectTransform.offsetMax.x")]
        public float right;

        [Tooltip("Use this when the RectTransform is stretched vertically. Maps to negative RectTransform.offsetMax.y, which appears as Top in the Inspector.")]
        public bool overrideTop;

        [Tooltip("Top offset for stretch anchors. Inspector Top = -RectTransform.offsetMax.y")]
        public float top;

        [Tooltip("Use this when the RectTransform is stretched vertically. Maps to RectTransform.offsetMin.y, which appears as Bottom in the Inspector.")]
        public bool overrideBottom;

        [Tooltip("Bottom offset for stretch anchors. Inspector Bottom = RectTransform.offsetMin.y")]
        public float bottom;

        [Header("Scale")]

        [Tooltip("Overrides RectTransform.localScale. Usually leave this off unless you intentionally want different visual scaling per device.")]
        public bool overrideScale;

        [Tooltip("Local scale applied to this RectTransform.")]
        public Vector3 scale = Vector3.one;
    }

    [Header("Thresholds")]
    [Tooltip("If the long side / short side aspect ratio is less than or equal to this value, the device is treated as a tablet.")]
    [SerializeField] private float tabletAspectThreshold = 1.5f;

    [Tooltip("If the aspect ratio is less than or equal to this value, the device is treated as a wide tablet.")]
    [SerializeField] private float wideTabletAspectThreshold = 1.34f;

    [Header("Profiles")]
    [Tooltip("Layout values used when the device is classified as a phone.")]
    [SerializeField] private LayoutProfile phone = new LayoutProfile();

    [Tooltip("Layout values used when the device is classified as a tablet.")]
    [SerializeField] private LayoutProfile tablet = new LayoutProfile();

    [Tooltip("Layout values used when the device is classified as a wide tablet, such as many iPads.")]
    [SerializeField] private LayoutProfile wideTablet = new LayoutProfile();

    [Header("Options")]
    [Tooltip("If enabled, the script reapplies the selected layout whenever the screen resolution changes.")]
    [SerializeField] private bool reapplyOnResolutionChange = true;

    private RectTransform rectTransform;
    private int lastWidth;
    private int lastHeight;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        Apply();
    }

    private void OnEnable()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        Apply();
    }

    private void Update()
    {
        if (!reapplyOnResolutionChange)
            return;

        if (Screen.width != lastWidth || Screen.height != lastHeight)
            Apply();
    }

#if UNITY_EDITOR
    private void OnDisable()
    {
        UnityEditor.EditorApplication.delayCall -= ApplyEditorSafe;
    }

    private void OnDestroy()
    {
        UnityEditor.EditorApplication.delayCall -= ApplyEditorSafe;
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        if (!isActiveAndEnabled)
            return;

        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        if (rectTransform == null)
            return;

        UnityEditor.EditorApplication.delayCall -= ApplyEditorSafe;
        UnityEditor.EditorApplication.delayCall += ApplyEditorSafe;
    }

    private void ApplyEditorSafe()
    {
        if (this == null)
            return;

        if (!isActiveAndEnabled)
            return;

        if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
            return;

        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        if (rectTransform == null)
            return;

        Apply();
    }
#endif

    [ContextMenu("Apply")]
    public void Apply()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        if (rectTransform == null)
            return;

        LayoutProfile profile = GetProfile();

        Vector2 sizeDelta = rectTransform.sizeDelta;
        Vector2 pos = rectTransform.anchoredPosition;
        Vector2 offsetMin = rectTransform.offsetMin;
        Vector2 offsetMax = rectTransform.offsetMax;
        Vector3 scale = rectTransform.localScale;

        if (profile.overrideWidth)
            sizeDelta.x = profile.width;

        if (profile.overrideHeight)
            sizeDelta.y = profile.height;

        if (profile.overridePosX)
            pos.x = profile.posX;

        if (profile.overridePosY)
            pos.y = profile.posY;

        if (profile.overrideLeft)
            offsetMin.x = profile.left;

        if (profile.overrideBottom)
            offsetMin.y = profile.bottom;

        if (profile.overrideRight)
            offsetMax.x = -profile.right;

        if (profile.overrideTop)
            offsetMax.y = -profile.top;

        if (profile.overrideScale)
            scale = profile.scale;

        rectTransform.sizeDelta = sizeDelta;
        rectTransform.anchoredPosition = pos;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;
        rectTransform.localScale = scale;

        lastWidth = Screen.width;
        lastHeight = Screen.height;
    }

    private LayoutProfile GetProfile()
    {
        float longSide = Mathf.Max(Screen.width, Screen.height);
        float shortSide = Mathf.Min(Screen.width, Screen.height);

        if (shortSide <= 0f)
            return phone;

        float aspect = longSide / shortSide;

        if (aspect <= wideTabletAspectThreshold)
            return wideTablet;

        if (aspect <= tabletAspectThreshold)
            return tablet;

        return phone;
    }
}