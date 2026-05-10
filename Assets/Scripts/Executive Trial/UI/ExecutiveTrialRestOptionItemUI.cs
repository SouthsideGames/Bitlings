using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI row/card for a Rest option (RestOptionPrefab).
/// Pure view: displays title/description/preview and handles selection visuals.
/// </summary>
public sealed class ExecutiveTrialRestOptionItemUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI titleTMP;
    [SerializeField] private TextMeshProUGUI descTMP;
    [SerializeField] private TextMeshProUGUI previewTMP;

    [Header("Selection Visuals")]
    [SerializeField] private GameObject selectedFrame;
    [SerializeField] private Image background;
    [SerializeField] private Color selectedTint = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color unselectedTint = new Color(0.9f, 0.9f, 0.9f, 1f);

    public ExecutiveTrialRestPanelUI.RestOption Option { get; private set; } = ExecutiveTrialRestPanelUI.RestOption.None;

    private Action _onClick;

    private void Awake()
    {
        if (!button) button = GetComponentInChildren<Button>(true);
        if (button) button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button) button.onClick.RemoveListener(HandleClick);
    }

    public void Bind(ExecutiveTrialRestPanelUI.RestOption option, string title, string desc, string preview)
    {
        EnsureButtonHierarchyActive();
        DevLog.Log($"[RestOptionItem] Bind({option}) button={(button != null ? button.name : "NULL")} interactable={(button ? button.interactable : false)} gameObject.active={gameObject.activeSelf}");

        Option = option;

        if (titleTMP) titleTMP.text = title ?? string.Empty;
        if (descTMP) descTMP.text = desc ?? string.Empty;
        if (previewTMP) previewTMP.text = preview ?? string.Empty;
    }

    public void SetOnClick(Action onClick)
    {
        EnsureButtonHierarchyActive();

        _onClick = onClick;

        if (button)
        {
            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
            button.interactable = onClick != null;
            DevLog.Log($"[RestOptionItem] SetOnClick({Option}) button={button.name} interactable={button.interactable} listeners={button.onClick.GetPersistentEventCount()} btnActive={button.gameObject.activeSelf} btnEnabled={button.enabled}");
        }
        else
        {
            Debug.LogWarning($"[RestOptionItem] SetOnClick({Option}) — button is NULL!");
        }
    }

    public void SetSelected(bool selected)
    {
        DevLog.Log($"[RestOptionItem] SetSelected({selected}) option={Option} selectedFrame={(selectedFrame ? selectedFrame.name : "NULL")} background={(background ? "yes" : "NULL")}");
        if (selectedFrame && selectedFrame != gameObject) selectedFrame.SetActive(selected);
        if (background) background.color = selected ? selectedTint : unselectedTint;
        if (!selected) EnsureButtonHierarchyActive();
    }

    public void SetInteractable(bool interactable)
    {
        EnsureButtonHierarchyActive();
        if (button) button.interactable = interactable;
    }

    private void OnEnable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // One-frame delayed diagnostic: log full button + CanvasGroup state after panel is shown.
        StartCoroutine(DiagnosticNextFrame());
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private System.Collections.IEnumerator DiagnosticNextFrame()
    {
        yield return null; // wait one frame so all panels finish showing

        // Button state
        bool btnExists = button != null;
        bool btnActive = btnExists && button.gameObject.activeInHierarchy;
        bool btnInteractable = btnExists && button.interactable;
        bool btnEnabled = btnExists && button.enabled;

        // Check if Button's target graphic has raycastTarget
        bool graphicOk = false;
        if (btnExists && button.targetGraphic != null)
            graphicOk = button.targetGraphic.raycastTarget;

        // Button RectTransform size
        string rectInfo = "N/A";
        if (btnExists)
        {
            var rt = button.GetComponent<RectTransform>();
            if (rt != null)
                rectInfo = $"size=({rt.rect.width:F0}x{rt.rect.height:F0}) pos={rt.position}";
        }

        // Check ALL ancestor CanvasGroups
        var cgs = GetComponentsInParent<CanvasGroup>(true);
        string cgInfo = "";
        foreach (var cg in cgs)
            cgInfo += $"\n    CG={cg.name} active={cg.gameObject.activeInHierarchy} interactable={cg.interactable} blocksRaycasts={cg.blocksRaycasts} alpha={cg.alpha}";

        // Check ALL sibling/cousin panels under rift panel that might overlay with blocksRaycasts
        string siblingInfo = "";
        var riftPanel = GetComponentInParent<ExecutiveTrialRiftPanelUI>();
        if (riftPanel != null)
        {
            var allCGs = riftPanel.GetComponentsInChildren<CanvasGroup>(true);
            foreach (var cg in allCGs)
            {
                if (cg.gameObject.activeInHierarchy && cg.blocksRaycasts && cg.alpha > 0f)
                {
                    // Skip our own ancestors (already logged)
                    bool isAncestor = transform.IsChildOf(cg.transform);
                    if (!isAncestor)
                        siblingInfo += $"\n    SIBLING_CG={cg.name} active=True blocksRaycasts=True alpha={cg.alpha} interactable={cg.interactable}";
                }
            }
        }
        if (string.IsNullOrEmpty(siblingInfo)) siblingInfo = "\n    (none blocking)";

        // Check for any Image/Graphic with raycastTarget on ancestors between button and canvas
        string blockerInfo = "";
        if (btnExists)
        {
            var allGraphics = GetComponentsInParent<Graphic>(true);
            foreach (var g in allGraphics)
            {
                if (g.raycastTarget && g.gameObject != button.gameObject && g.gameObject.activeInHierarchy)
                    blockerInfo += $"\n    GRAPHIC={g.gameObject.name} type={g.GetType().Name} raycast=True";
            }
        }
        if (string.IsNullOrEmpty(blockerInfo)) blockerInfo = "\n    (none)";

        // EventSystem check
        var es = UnityEngine.EventSystems.EventSystem.current;
        string esInfo = (es != null) ? $"name={es.name} enabled={es.enabled}" : "NULL";

        Debug.Log($"[RestOptionItem DIAG] option={Option} " +
                  $"selfActive={gameObject.activeInHierarchy} " +
                  $"btnExists={btnExists} btnActive={btnActive} btnInteractable={btnInteractable} btnEnabled={btnEnabled} " +
                  $"graphicRaycast={graphicOk} rect={rectInfo} " +
                  $"selectedFrame={(selectedFrame ? selectedFrame.name : "NULL")} selectedFrameIsSelf={selectedFrame == gameObject} " +
                  $"EventSystem={esInfo}" +
                  $"\nAncestor CanvasGroups:{cgInfo}" +
                  $"\nActive sibling panels with blocksRaycasts:{siblingInfo}" +
                  $"\nAncestor Graphics with raycastTarget:{blockerInfo}");
    }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void Update()
    {
        // Every-frame tap diagnostic: what does EventSystem actually hit?
        // Support both mouse and touch via the new Input System
        Vector2 pos = Vector2.zero;
        bool pressed = false;

        var pointer = UnityEngine.InputSystem.Pointer.current;
        if (pointer != null && pointer.press.wasPressedThisFrame)
        {
            pos = pointer.position.ReadValue();
            pressed = true;
        }

        if (!pressed) return;

        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem == null)
        {
            DevLog.Log("[RestOptionItem TAP] EventSystem.current is NULL!");
            return;
        }

        var pointerData = new UnityEngine.EventSystems.PointerEventData(eventSystem)
        {
            position = pos
        };

        var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        eventSystem.RaycastAll(pointerData, results);

        string hitInfo = "";
        for (int i = 0; i < Mathf.Min(results.Count, 8); i++)
        {
            var r = results[i];
            hitInfo += $"\n    [{i}] {r.gameObject.name} (depth={r.depth}, sortOrder={r.sortingOrder})";
        }

        if (results.Count == 0)
            hitInfo = "\n    (nothing hit)";

        DevLog.Log($"[RestOptionItem TAP] pos={pos} hits={results.Count}{hitInfo}");
    }
#endif

    private void EnsureButtonHierarchyActive()
    {
        if (!button) button = GetComponentInChildren<Button>(true);
        if (!button) return;

        var current = button.transform;
        while (current)
        {
            if (!current.gameObject.activeSelf) current.gameObject.SetActive(true);
            if (current == transform) break;
            current = current.parent;
        }

        // Disable raycastTarget on all Graphics EXCEPT the button's targetGraphic,
        // so clicks pass through text/images and reach the button.
        RouteRaycastsToButton();
    }

    /// <summary>
    /// Ensures only the button's targetGraphic receives raycasts.
    /// All other Graphics (text labels, decorative images) have raycastTarget disabled
    /// so they don't block clicks from reaching the button.
    /// </summary>
    private void RouteRaycastsToButton()
    {
        if (!button) return;

        var targetGraphic = button.targetGraphic;
        var allGraphics = GetComponentsInChildren<Graphic>(true);

        foreach (var g in allGraphics)
        {
            g.raycastTarget = (g == targetGraphic);
        }

        // If the button has no targetGraphic, assign the first Image we find on/above the button
        if (targetGraphic == null)
        {
            var img = button.GetComponent<Image>();
            if (img == null) img = GetComponent<Image>();
            if (img != null)
            {
                button.targetGraphic = img;
                img.raycastTarget = true;
            }
        }
    }

    private void HandleClick()
    {
        DevLog.Log($"[RestOptionItem] HandleClick! option={Option} hasCallback={(_onClick != null)}");
        AudioManager.I?.PlayClick();
        _onClick?.Invoke();
    }
}
