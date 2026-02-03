using System;
using System.Collections.Generic;
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

    public enum HorizontalSizing
    {
        KeepWidth = 0,            // Default: do not touch width
        AutoFromLayoutGroup = 1,  // Uses HorizontalLayoutGroup/GridLayoutGroup preferred width
        FixedColumnWidth = 2      // Uses columnWidth * columns (+ spacing/padding)
    }

    [Header("Wiring")]
    [Tooltip("ScrollRect to drive sizing. If left empty, will search on this GameObject.")]
    [SerializeField] private ScrollRect scrollRect;

    [Tooltip("Content RectTransform. If left empty, uses scrollRect.content.")]
    [SerializeField] private RectTransform content;

    [Header("Sizing Mode")]
    [SerializeField] private Mode mode = Mode.AutoFromLayoutGroup;

    [Header("Horizontal Sizing (Optional)")]
    [SerializeField] private HorizontalSizing horizontalSizing = HorizontalSizing.KeepWidth;

    [Header("FixedRowHeight Settings (only used when Mode = FixedRowHeight)")]
    [SerializeField] private float rowHeight = 110f;
    [SerializeField] private float rowSpacing = 12f;
    [SerializeField] private int columns = 1;
    [SerializeField] private int topPadding = 0;
    [SerializeField] private int bottomPadding = 0;

    [Header("FixedColumnWidth Settings (only used when HorizontalSizing = FixedColumnWidth)")]
    [SerializeField] private float columnWidth = 220f;
    [SerializeField] private float columnSpacing = 12f;
    [SerializeField] private int fixedColumns = 1; // only used for FixedColumnWidth mode
    [SerializeField] private int leftPadding = 0;
    [SerializeField] private int rightPadding = 0;

    [Header("Behavior")]
    [Tooltip("If true, ignores inactive children when counting items.")]
    [SerializeField] private bool ignoreInactiveChildren = true;

    [Tooltip("If true, updates every frame (safe but more expensive). If false, call Refresh() after you instantiate.")]
    [SerializeField] private bool refreshEveryFrame = false;

    [Tooltip("Extra pixels added at the end so content never clips.")]
    [SerializeField] private float extraBottomBuffer = 4f;

    [Tooltip("Extra pixels added at the right so content never clips.")]
    [SerializeField] private float extraRightBuffer = 4f;

    private VerticalLayoutGroup _vlg;
    private HorizontalLayoutGroup _hlg;
    private GridLayoutGroup _glg;

    private int _lastCount = -1;
    private float _lastHeight = -1f;
    private float _lastWidth = -1f;

    private void Awake()
    {
        if (!scrollRect) scrollRect = GetComponent<ScrollRect>();
        if (!content && scrollRect) content = scrollRect.content;

        if (content)
        {
            _vlg = content.GetComponent<VerticalLayoutGroup>();
            _hlg = content.GetComponent<HorizontalLayoutGroup>();
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

        float? targetWidth = horizontalSizing switch
        {
            HorizontalSizing.KeepWidth => null,
            HorizontalSizing.AutoFromLayoutGroup => ComputeWidthFromLayoutGroup(count),
            HorizontalSizing.FixedColumnWidth => ComputeWidthFixedColumns(count),
            _ => null
        };

        // Apply only if changed (avoid thrashing)
        bool heightChanged = force || Mathf.Abs(targetHeight - _lastHeight) >= 0.5f;
        bool widthChanged = false;

        if (targetWidth.HasValue)
            widthChanged = force || Mathf.Abs(targetWidth.Value - _lastWidth) >= 0.5f;

        if (!heightChanged && !widthChanged)
        {
            _lastCount = count;
            return;
        }

        var size = content.sizeDelta;

        if (heightChanged)
        {
            size.y = Mathf.Max(0f, targetHeight);
            _lastHeight = targetHeight;
        }

        if (targetWidth.HasValue && widthChanged)
        {
            size.x = Mathf.Max(0f, targetWidth.Value);
            _lastWidth = targetWidth.Value;
        }

        content.sizeDelta = size;
        _lastCount = count;
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

            if (preferred > 0.01f)
                return preferred + extraBottomBuffer;

            float padTop = _vlg.padding.top;
            float padBottom = _vlg.padding.bottom;
            float spacing = _vlg.spacing;

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

    private float ComputeWidthFromLayoutGroup(int childCount)
    {
        if (!content) return 0f;

        // Grid layout: compute columns * (cell + spacing) + padding
        if (_glg != null)
        {
            int cols = GetGridColumnCountForWidthSizing(_glg, childCount);

            float cellW = _glg.cellSize.x;
            float spacingX = _glg.spacing.x;

            float padLeft = _glg.padding.left;
            float padRight = _glg.padding.right;

            if (cols <= 0)
                return padLeft + padRight;

            float width =
                padLeft +
                (cols * cellW) +
                ((cols - 1) * spacingX) +
                padRight +
                extraRightBuffer;

            return width;
        }

        // Horizontal layout: ask preferred width
        if (_hlg != null)
        {
            float preferred = LayoutUtility.GetPreferredWidth(content);
            if (preferred > 0.01f)
                return preferred + extraRightBuffer;

            // Fallback manual calc
            float padLeft = _hlg.padding.left;
            float padRight = _hlg.padding.right;
            float spacing = _hlg.spacing;

            float itemW = GetFirstChildWidth(content);
            if (itemW <= 0f) itemW = columnWidth;

            if (childCount <= 0)
                return padLeft + padRight;

            return padLeft + (childCount * itemW) + ((childCount - 1) * spacing) + padRight + extraRightBuffer;
        }

        // If we have no horizontal or grid layout group, do a basic fallback
        return ComputeWidthFixedColumns(childCount);
    }

    private float ComputeWidthFixedColumns(int childCount)
    {
        int cols = Mathf.Max(1, fixedColumns);

        if (cols <= 0)
            return leftPadding + rightPadding;

        float width =
            leftPadding +
            (cols * columnWidth) +
            ((cols - 1) * columnSpacing) +
            rightPadding +
            extraRightBuffer;

        return width;
    }

    private static int GetGridColumnCountForWidthSizing(GridLayoutGroup glg, int childCount)
    {
        if (!glg) return 1;

        // If constrained, use it.
        if (glg.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
            return Mathf.Max(1, glg.constraintCount);

        // Fixed row count means columns depend on item count.
        if (glg.constraint == GridLayoutGroup.Constraint.FixedRowCount)
        {
            int rows = Mathf.Max(1, glg.constraintCount);
            return Mathf.Max(1, Mathf.CeilToInt(childCount / (float)rows));
        }

        // Flexible: best guess based on available width
        return Mathf.Max(1, GuessColumnsFromWidth(glg));
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

            var le = rt.GetComponent<LayoutElement>();
            if (le != null && le.preferredHeight > 0f)
                return le.preferredHeight;

            float h = rt.rect.height;
            if (h > 0.1f) return h;
        }
        return 0f;
    }

    private static float GetFirstChildWidth(RectTransform parent)
    {
        if (!parent) return 0f;

        for (int i = 0; i < parent.childCount; i++)
        {
            var rt = parent.GetChild(i) as RectTransform;
            if (!rt) continue;
            if (!rt.gameObject.activeSelf) continue;

            var le = rt.GetComponent<LayoutElement>();
            if (le != null && le.preferredWidth > 0f)
                return le.preferredWidth;

            float w = rt.rect.width;
            if (w > 0.1f) return w;
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
