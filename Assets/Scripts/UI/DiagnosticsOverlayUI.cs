using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class DiagnosticsOverlayUI : MonoBehaviour
{
    public static DiagnosticsOverlayUI I { get; private set; }

    [Header("Unlock Behavior")]
    [SerializeField] private bool hideButtonUntilUnlocked = true;

    [Header("Button (child)")]
    [SerializeField] private Button diagnosticsButton;

    [Header("Main Group (child)")]
    [SerializeField] private CanvasGroup mainGroup;    
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private Button closeButton;

    [Header("Optional Actions")]
    [SerializeField] private Button copyButton;

    [Header("Behavior")]
    [SerializeField, Min(0.05f)] private float refreshSeconds = 0.25f;
    [SerializeField] private bool autoScrollToBottom = true;

    float _t;
    bool _panelVisible;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (diagnosticsButton)
        {
            diagnosticsButton.onClick.RemoveAllListeners();
            diagnosticsButton.onClick.AddListener(OnDiagnosticsButtonPressed);
        }

        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(ClosePanel);
        }

        if (copyButton)
        {
            copyButton.onClick.RemoveAllListeners();
            copyButton.onClick.AddListener(OnCopyPressed);
        }

        SetPanelVisible(false, instant: true);

        ApplyUnlockedState(IsUnlocked());
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    void Update()
    {
        if (!_panelVisible) return;

        _t += Time.unscaledDeltaTime;
        if (_t >= refreshSeconds)
        {
            _t = 0f;
            Refresh("Tick");
        }
    }

    bool IsUnlocked()
    {
        try
        {
            return SaveManager.Data != null && SaveManager.Data.diagnosticsUnlocked;
        }
        catch
        {
            return false;
        }
    }

    public void Unlock()
    {
        if (SaveManager.Data != null)
        {
            SaveManager.Data.diagnosticsUnlocked = true;
            SaveManager.Save();
        }

        ApplyUnlockedState(true);
    }

    // Called by CheatCodeManager
    public void UnlockFromCheat() => Unlock();

    void ApplyUnlockedState(bool unlocked)
    {
        if (!diagnosticsButton) return;

        if (hideButtonUntilUnlocked)
            diagnosticsButton.gameObject.SetActive(unlocked);
        else
            diagnosticsButton.gameObject.SetActive(true);

        // When unlocked and panel is not open, button should be interactable.
        diagnosticsButton.interactable = unlocked && !_panelVisible;
    }

    void OnDiagnosticsButtonPressed()
    {
        if (!IsUnlocked())
            return;

        OpenPanel();
    }

    public void OpenPanel()
    {
        SetPanelVisible(true, instant: true);
        Refresh("Open");

        // Requirement: when panel opens, button becomes inactive
        if (diagnosticsButton)
            diagnosticsButton.interactable = false;
    }

    public void ClosePanel()
    {
        SetPanelVisible(false, instant: true);

        ApplyUnlockedState(IsUnlocked());
    }

    public void TogglePanel()
    {
        if (_panelVisible) ClosePanel();
        else OpenPanel();
    }

    void SetPanelVisible(bool on, bool instant)
    {
        _panelVisible = on;
        _t = 0f;

        if (mainGroup)
        {
            mainGroup.alpha = on ? 1f : 0f;
            mainGroup.blocksRaycasts = on;
            mainGroup.interactable = on;
        }
    }

    public void Refresh(string context = "")
    {
        if (!text) return;

        text.text = DiagnosticsSnapshot.Build(context);

        if (autoScrollToBottom && scrollRect)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f; 
        }
    }

    void OnCopyPressed()
    {
        string snapshot = DiagnosticsSnapshot.Build("Copy");
        GUIUtility.systemCopyBuffer = snapshot;
        Debug.Log("[DIAG] Copied diagnostics snapshot to clipboard.");
    }
}
