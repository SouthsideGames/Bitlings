using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel-based spotlight tutorial (Option 1):
/// - This script lives on the Tutorial panel root (PanelId.Tutorial)
/// - Panel is opened after starter selection (StarterSelector) or when routing to Home (IntroManager)
/// - When this panel is enabled, it starts the tutorial if not completed
/// - Uses GameEvents tutorial signals for "WaitForEvent" steps
/// - Gates other UI buttons so only the current target can be interacted with
/// </summary>
public sealed class TutorialManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Static state (used by UI guards, e.g., EncounterButtonGuard)
    // ─────────────────────────────────────────────────────────────────────────────
    public static bool IsActive { get; private set; }

    private const string PrefKey = "tutorial_complete_v1";

    public static bool ShouldShowTutorial()
    {
        return PlayerPrefs.GetInt(PrefKey, 0) != 1;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Step model
    // ─────────────────────────────────────────────────────────────────────────────
    public enum StepKind
    {
        ExplainOnly,     // spotlight + tooltip; tap anywhere to advance
        ClickTarget,     // must click the highlighted target
        WaitForEvent     // waits for a tutorial event (from GameEvents)
    }

    public enum TutorialEvent
    {
        None,
        PlayerDossierOpened,
        PlayerDossierClosed,
        ResourcePanelOpened,
        ResourcePanelClosed,
        JobAssignOpened,
        FirstJobAssigned
    }

    [Serializable]
    public sealed class Step
    {
        [Header("Step")]
        public string id;

        public StepKind kind = StepKind.ExplainOnly;

        [Header("Highlight Target")]
        public RectTransform target;
        public Vector2 padding = new Vector2(20f, 20f);

        [Header("Tooltip")]
        [TextArea(2, 4)]
        public string tooltip;

        [Header("WaitForEvent Settings")]
        public TutorialEvent waitForEvent = TutorialEvent.None;

        [Header("Optional: Auto click before step (e.g., open a panel)")]
        public Button autoClickBeforeStep;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Inspector: panel + overlay content (inside your Tutorial panel)
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Panel Id")]
    [SerializeField] private PanelId tutorialPanelId = PanelId.Tutorial;

    [Header("Overlay")]
    [SerializeField] private Image dimmer;                 // full-screen dark image (Raycast Target ON)
    [SerializeField] private RectTransform spotlightRect;  // outline frame, raycast OFF

    [Header("Tooltip")]
    [SerializeField] private RectTransform tooltipPanel;
    [SerializeField] private TMP_Text tooltipText;

    [Header("Explain-only Tap Area")]
    [Tooltip("Full-screen invisible button used only for ExplainOnly steps.")]
    [SerializeField] private Button tapAnywhereButton;

    [Header("Buttons to Gate")]
    [Tooltip("All buttons you want disabled during tutorial (bottom nav, top buttons, etc.).")]
    [SerializeField] private List<Button> buttonsToGate = new();

    [Header("Steps")]
    [SerializeField] private List<Step> steps = new();

    // ─────────────────────────────────────────────────────────────────────────────
    // Runtime
    // ─────────────────────────────────────────────────────────────────────────────
    private int _index = -1;
    private bool _running;

    private Button _activeButtonListener;
    private TutorialClickable _activeClickableListener;

    // Stored delegates so we can unsubscribe safely
    private Action _evDossierOpened;
    private Action _evDossierClosed;
    private Action _evResourcesOpened;
    private Action _evResourcesClosed;
    private Action _evJobAssignOpened;
    private Action _evFirstJobAssigned;

    // ─────────────────────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        // Safety defaults
        if (tapAnywhereButton != null)
            tapAnywhereButton.gameObject.SetActive(false);

        if (spotlightRect != null)
            spotlightRect.gameObject.SetActive(false);

        if (tooltipPanel != null)
            tooltipPanel.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        // If tutorial already completed, close this panel immediately.
        if (!ShouldShowTutorial())
        {
            IsActive = false;
            _running = false;

            // In case something opened it accidentally
            if (UIManager.I != null && tutorialPanelId != PanelId.None)
                UIManager.I.Hide(tutorialPanelId);

            return;
        }

        IsActive = true;

        SubscribeTutorialEvents();

        StartTutorial();
    }

    private void OnDisable()
    {
        UnsubscribeTutorialEvents();

        CleanupListeners();
        GateAll(true);

        _running = false;
        IsActive = false;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Public controls (optional)
    // ─────────────────────────────────────────────────────────────────────────────
    public void SkipTutorial()
    {
        if (!_running) return;
        CompleteTutorial();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Core flow
    // ─────────────────────────────────────────────────────────────────────────────
    private void StartTutorial()
    {
        if (_running) return;
        if (steps == null || steps.Count == 0)
        {
            // Nothing to do; mark complete to avoid soft-lock
            CompleteTutorial();
            return;
        }

        _running = true;
        _index = -1;

        // Ensure overlay blocks input (dimmer raycast target should be ON)
        if (dimmer != null) dimmer.raycastTarget = true;

        // Disable all gated buttons at start
        GateAll(false);

        NextStep();
    }

    private void NextStep()
    {
        CleanupListeners();

        _index++;
        if (_index >= steps.Count)
        {
            CompleteTutorial();
            return;
        }

        var s = steps[_index];
        if (s == null || s.target == null)
        {
            // Skip invalid steps safely
            NextStep();
            return;
        }

        // Optional: open a panel before highlighting something inside it
        if (s.autoClickBeforeStep != null && s.autoClickBeforeStep.interactable)
            s.autoClickBeforeStep.onClick.Invoke();

        SetTooltip(s.tooltip);
        PositionSpotlight(s.target, s.padding);

        // Gate buttons for this step
        GateAll(false);

        // For ClickTarget, allow only the target button (if it is a button)
        if (s.kind == StepKind.ClickTarget)
            AllowTargetIfButton(s.target);

        // ExplainOnly uses tapAnywhereButton
        if (tapAnywhereButton != null)
        {
            tapAnywhereButton.onClick.RemoveAllListeners();

            bool showTap = (s.kind == StepKind.ExplainOnly);
            tapAnywhereButton.gameObject.SetActive(showTap);

            if (showTap)
                tapAnywhereButton.onClick.AddListener(NextStep);
        }

        if (s.kind == StepKind.ClickTarget)
            AttachClickListener(s.target);

        // WaitForEvent advances only when we receive the correct event.
    }

    private void CompleteTutorial()
    {
        _running = false;

        CleanupListeners();
        GateAll(true);

        PlayerPrefs.SetInt(PrefKey, 1);
        PlayerPrefs.Save();

        IsActive = false;

        // Close tutorial panel via UIManager (panel-based requirement)
        if (UIManager.I != null && tutorialPanelId != PanelId.None)
            UIManager.I.Hide(tutorialPanelId);
        else
            gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Events (WaitForEvent)
    // ─────────────────────────────────────────────────────────────────────────────
    private void SubscribeTutorialEvents()
    {
        // Create and store delegates so we can unsubscribe properly.
        _evDossierOpened   = () => OnTutorialEvent(TutorialEvent.PlayerDossierOpened);
        _evDossierClosed   = () => OnTutorialEvent(TutorialEvent.PlayerDossierClosed);
        _evResourcesOpened = () => OnTutorialEvent(TutorialEvent.ResourcePanelOpened);
        _evResourcesClosed = () => OnTutorialEvent(TutorialEvent.ResourcePanelClosed);
        _evJobAssignOpened = () => OnTutorialEvent(TutorialEvent.JobAssignOpened);
        _evFirstJobAssigned = () => OnTutorialEvent(TutorialEvent.FirstJobAssigned);

        GameEvents.Tutorial_PlayerDossierOpened  += _evDossierOpened;
        GameEvents.Tutorial_PlayerDossierClosed  += _evDossierClosed;
        GameEvents.Tutorial_ResourcePanelOpened  += _evResourcesOpened;
        GameEvents.Tutorial_ResourcePanelClosed  += _evResourcesClosed;
        GameEvents.Tutorial_JobAssignOpened      += _evJobAssignOpened;
        GameEvents.Tutorial_FirstJobAssigned     += _evFirstJobAssigned;
    }

    private void UnsubscribeTutorialEvents()
    {
        if (_evDossierOpened != null)    GameEvents.Tutorial_PlayerDossierOpened  -= _evDossierOpened;
        if (_evDossierClosed != null)    GameEvents.Tutorial_PlayerDossierClosed  -= _evDossierClosed;
        if (_evResourcesOpened != null)  GameEvents.Tutorial_ResourcePanelOpened  -= _evResourcesOpened;
        if (_evResourcesClosed != null)  GameEvents.Tutorial_ResourcePanelClosed  -= _evResourcesClosed;
        if (_evJobAssignOpened != null)  GameEvents.Tutorial_JobAssignOpened      -= _evJobAssignOpened;
        if (_evFirstJobAssigned != null) GameEvents.Tutorial_FirstJobAssigned     -= _evFirstJobAssigned;

        _evDossierOpened = null;
        _evDossierClosed = null;
        _evResourcesOpened = null;
        _evResourcesClosed = null;
        _evJobAssignOpened = null;
        _evFirstJobAssigned = null;
    }

    private void OnTutorialEvent(TutorialEvent ev)
    {
        if (!_running) return;
        if (_index < 0 || _index >= steps.Count) return;

        var s = steps[_index];
        if (s == null) return;

        if (s.kind != StepKind.WaitForEvent) return;
        if (s.waitForEvent != ev) return;

        NextStep();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // UI helpers
    // ─────────────────────────────────────────────────────────────────────────────
    private void SetTooltip(string msg)
    {
        if (tooltipText != null)
            tooltipText.text = msg ?? string.Empty;

        if (tooltipPanel != null)
            tooltipPanel.gameObject.SetActive(!string.IsNullOrWhiteSpace(msg));
    }

    private void GateAll(bool interactable)
    {
        if (buttonsToGate == null) return;
        for (int i = 0; i < buttonsToGate.Count; i++)
        {
            var b = buttonsToGate[i];
            if (!b) continue;
            b.interactable = interactable;
        }
    }

    private void AllowTargetIfButton(RectTransform target)
    {
        if (!target) return;
        var b = target.GetComponent<Button>();
        if (b) b.interactable = true;
    }

    private void AttachClickListener(RectTransform target)
    {
        if (!target) return;

        // Prefer Button
        var btn = target.GetComponent<Button>();
        if (btn != null)
        {
            _activeButtonListener = btn;
            btn.onClick.AddListener(NextStep);
            return;
        }

        // Otherwise require TutorialClickable
        var clickable = target.GetComponent<TutorialClickable>();
        if (!clickable) clickable = target.gameObject.AddComponent<TutorialClickable>();

        _activeClickableListener = clickable;
        clickable.Clicked += NextStep;
    }

    private void CleanupListeners()
    {
        if (_activeButtonListener != null)
        {
            _activeButtonListener.onClick.RemoveListener(NextStep);
            _activeButtonListener = null;
        }

        if (_activeClickableListener != null)
        {
            _activeClickableListener.Clicked -= NextStep;
            _activeClickableListener = null;
        }

        if (tapAnywhereButton != null)
        {
            tapAnywhereButton.onClick.RemoveAllListeners();
            tapAnywhereButton.gameObject.SetActive(false);
        }
    }

    private void PositionSpotlight(RectTransform target, Vector2 padding)
    {
        if (spotlightRect == null || target == null) return;

        var canvas = spotlightRect.GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return;

        var corners = new Vector3[4];
        target.GetWorldCorners(corners);

        Camera cam = (canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : canvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            RectTransformUtility.WorldToScreenPoint(cam, corners[0]),
            cam,
            out var p0);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            RectTransformUtility.WorldToScreenPoint(cam, corners[2]),
            cam,
            out var p2);

        var min = p0 - padding;
        var max = p2 + padding;

        spotlightRect.anchoredPosition = (min + max) * 0.5f;
        spotlightRect.sizeDelta = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));

        spotlightRect.gameObject.SetActive(true);
    }
}
