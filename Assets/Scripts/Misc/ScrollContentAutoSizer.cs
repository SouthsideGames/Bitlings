using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ScrollContentAutoSizer : MonoBehaviour
{
    public enum Mode
    {
        AutoFromLayoutGroup = 0,   // Uses VerticalLayoutGroup/GridLayoutGroup calculations
        FixedRowHeight = 1         // Uses rowHeight * itemCount (+ spacing/padding)
    }

    [Header("Wiring")]
    [Tooltip("ScrollRect to drive sizing. If left empty, will search on this GameObject.")]
    [SerializeField] private ScrollRect scrollRect;

    [Tooltip("Content RectTransform. If left empty, uses scrollRect.content.")]
    [SerializeField] private RectTransform content;

    [Header("Sizing Mode")]
    [SerializeField] private Mode mode = Mode.AutoFromLayoutGroup;

    [Header("FixedRowHeight Settings (only used when Mode = FixedRowHeight)")]
    [SerializeField] private float rowHeight = 110f;
    [SerializeField] private float rowSpacing = 12f;
    [SerializeField] private int columns = 1;
    [SerializeField] private int topPadding = 0;
    [SerializeField] private int bottomPadding = 0;

    [Header("Behavior")]
    [Tooltip("If true, ignores inactive children when counting items.")]
    [SerializeField] private bool ignoreInactiveChildren = true;

    [Tooltip("If true, updates every frame (safe but more expensive). If false, call Refresh() after you instantiate.")]
    [SerializeField] private bool refreshEveryFrame = false;

    [Tooltip("Extra pixels added at the end so content never clips.")]
    [SerializeField] private float extraBottomBuffer = 4f;

    private VerticalLayoutGroup _vlg;
    private GridLayoutGroup _glg;

    private int _lastCount = -1;
    private float _lastHeight = -1f;

    private void Awake()
    {
        if (!scrollRect) scrollRect = GetComponent<ScrollRect>();
        if (!content && scrollRect) content = scrollRect.content;

        if (content)
        {
            _vlg = content.GetComponent<VerticalLayoutGroup>();
            _glg = content.GetComponent<GridLayoutGroup>();
        }
    }

    private void OnEnable()
    {
        Refresh(force: true);
    }

    private void LateUpdate()
    {
        if (!refreshEveryFrame)
            return;

        Refresh(force: false);
    }

    /// <summary>
    /// Call this after instantiating or destroying items under Content.
    /// </summary>
    public void Refresh(bool force = false)
    {
        if (!content) return;

        int count = CountChildren(content, ignoreInactiveChildren);
        if (!force && count == _lastCount && !refreshEveryFrame)
            return;

        // Ensure layout has a chance to compute sizes before we read them.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        float targetHeight = mode switch
        {
            Mode.AutoFromLayoutGroup => ComputeHeightFromLayoutGroup(count),
            Mode.FixedRowHeight => ComputeHeightFixed(count),
            _ => ComputeHeightFromLayoutGroup(count)
        };

        // Apply only if changed (avoid thrashing)
        if (!force && Mathf.Abs(targetHeight - _lastHeight) < 0.5f)
        {
            _lastCount = count;
            return;
        }

        // Keep current width; update height.
        var size = content.sizeDelta;
        size.y = Mathf.Max(0f, targetHeight);
        content.sizeDelta = size;

        _lastCount = count;
        _lastHeight = targetHeight;
    }

    private float ComputeHeightFromLayoutGroup(int childCount)
    {
        if (!content) return 0f;

        // Grid layout: compute rows * (cell + spacing) + padding
        if (_glg != null)
        {
            int cols = Mathf.Max(1, _glg.constraint == GridLayoutGroup.Constraint.FixedColumnCount
                ? _glg.constraintCount
                : Mathf.Max(1, GuessColumnsFromWidth(_glg)));

            int rows = Mathf.CeilToInt(childCount / (float)cols);

            float cellH = _glg.cellSize.y;
            float spacingY = _glg.spacing.y;

            float padTop = _glg.padding.top;
            float padBottom = _glg.padding.bottom;

            if (rows <= 0)
                return padTop + padBottom;

            float height =
                padTop +
                (rows * cellH) +
                ((rows - 1) * spacingY) +
                padBottom +
                extraBottomBuffer;

            return height;
        }

        // Vertical layout: easiest is to ask LayoutUtility preferred height.
        if (_vlg != null)
        {
            float preferred = LayoutUtility.GetPreferredHeight(content);

            // Some setups return 0 until a rebuild; we already forced rebuild above.
            if (preferred > 0.01f)
                return preferred + extraBottomBuffer;

            // Fallback manual calc if preferred fails
            float padTop = _vlg.padding.top;
            float padBottom = _vlg.padding.bottom;
            float spacing = _vlg.spacing;

            // Attempt to use first active child's height as baseline
            float itemH = GetFirstChildHeight(content);
            if (itemH <= 0f) itemH = rowHeight;

            if (childCount <= 0)
                return padTop + padBottom;

            return padTop + (childCount * itemH) + ((childCount - 1) * spacing) + padBottom + extraBottomBuffer;
        }

        // No layout group on content: treat as fixed rows.
        return ComputeHeightFixed(childCount);
    }

    private float ComputeHeightFixed(int childCount)
    {
        int cols = Mathf.Max(1, columns);
        int rows = Mathf.CeilToInt(childCount / (float)cols);

        if (rows <= 0)
            return topPadding + bottomPadding;

        float height =
            topPadding +
            (rows * rowHeight) +
            ((rows - 1) * rowSpacing) +
            bottomPadding +
            extraBottomBuffer;

        return height;
    }

    private static int CountChildren(RectTransform parent, bool ignoreInactive)
    {
        if (!parent) return 0;

        int c = 0;
        for (int i = 0; i < parent.childCount; i++)
        {
            var t = parent.GetChild(i);
            if (!t) continue;
            if (ignoreInactive && !t.gameObject.activeSelf) continue;
            c++;
        }
        return c;
    }

    private static float GetFirstChildHeight(RectTransform parent)
    {
        if (!parent) return 0f;

        for (int i = 0; i < parent.childCount; i++)
        {
            var rt = parent.GetChild(i) as RectTransform;
            if (!rt) continue;
            if (!rt.gameObject.activeSelf) continue;

            // prefer LayoutElement if present
            var le = rt.GetComponent<LayoutElement>();
            if (le != null && le.preferredHeight > 0f)
                return le.preferredHeight;

            // fallback to rect height
            float h = rt.rect.height;
            if (h > 0.1f) return h;
        }
        return 0f;
    }

    private static int GuessColumnsFromWidth(GridLayoutGroup glg)
    {
        if (!glg) return 1;

        var parent = glg.GetComponent<RectTransform>();
        if (!parent) return 1;

        float width = parent.rect.width - glg.padding.left - glg.padding.right;
        float cell = glg.cellSize.x;
        float space = glg.spacing.x;

        if (cell <= 0f) return 1;

        // columns = floor((width + spacing) / (cell + spacing))
        int cols = Mathf.FloorToInt((width + space) / (cell + space));
        return Mathf.Max(1, cols);
    }
}
