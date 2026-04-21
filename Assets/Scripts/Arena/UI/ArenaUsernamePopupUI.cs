using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ArenaUsernamePopupUI : MonoBehaviour
{
    public static ArenaUsernamePopupUI I { get; private set; }

    [Header("UI Refs")]
    [SerializeField] private CanvasGroup popupCanvasGroup;
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TextMeshProUGUI errorLabel;
    [SerializeField] private TextMeshProUGUI charCountLabel;
    [SerializeField] private Button confirmButton;
    [SerializeField] private TextMeshProUGUI hintLabel;

    [Header("Loading")]
    [SerializeField] private GameObject loadingSpinner;

    private bool _isSubmitting;

    // ═════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═════════════════════════════════════════════════════════════

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        SetCanvasGroupVisible(false);
    }

    void OnEnable()
    {
        if (confirmButton) { confirmButton.onClick.RemoveAllListeners(); confirmButton.onClick.AddListener(HandleConfirm); }
        if (usernameInput)
        {
            usernameInput.onValueChanged.RemoveAllListeners();
            usernameInput.onValueChanged.AddListener(OnInputChanged);
            usernameInput.characterLimit = ArenaOnboardingManager.UsernameMaxLength;
        }
    }

    void OnDisable()
    {
        if (confirmButton) confirmButton.onClick.RemoveListener(HandleConfirm);
        if (usernameInput) usernameInput.onValueChanged.RemoveListener(OnInputChanged);
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════

    /// <summary>Shows the username creation popup.</summary>
    public void Show()
    {
        _isSubmitting = false;
        SetCanvasGroupVisible(true);
        if (usernameInput) usernameInput.text = "";
        if (errorLabel) errorLabel.text = "";
        if (loadingSpinner) loadingSpinner.SetActive(false);
        UpdateCharCount("");
        UpdateConfirmButtonState("");

        if (hintLabel)
            hintLabel.text = $"Choose a display name ({ArenaOnboardingManager.UsernameMinLength}–{ArenaOnboardingManager.UsernameMaxLength} characters). This is permanent.";
    }

    /// <summary>Hides the popup.</summary>
    public void Hide()
    {
        SetCanvasGroupVisible(false);
    }

    private void SetCanvasGroupVisible(bool visible)
    {
        if (!popupCanvasGroup) return;
        popupCanvasGroup.alpha = visible ? 1f : 0f;
        popupCanvasGroup.interactable = visible;
        popupCanvasGroup.blocksRaycasts = visible;
    }

    // ═════════════════════════════════════════════════════════════
    //  Input handling
    // ═════════════════════════════════════════════════════════════

    private void OnInputChanged(string value)
    {
        if (errorLabel) errorLabel.text = "";
        UpdateCharCount(value);
        UpdateConfirmButtonState(value);
    }

    private void UpdateCharCount(string value)
    {
        if (!charCountLabel) return;
        int len = string.IsNullOrEmpty(value) ? 0 : value.Trim().Length;
        charCountLabel.text = $"{len} / {ArenaOnboardingManager.UsernameMaxLength}";
    }

    private void UpdateConfirmButtonState(string value)
    {
        if (!confirmButton) return;
        string trimmed = value != null ? value.Trim() : "";
        confirmButton.interactable = trimmed.Length >= ArenaOnboardingManager.UsernameMinLength
                                  && trimmed.Length <= ArenaOnboardingManager.UsernameMaxLength
                                  && ArenaOnboardingManager.IsUsernameSafe(trimmed);
    }

    private void HandleConfirm()
    {
        if (_isSubmitting) return;
        if (usernameInput == null) return;

        string value = usernameInput.text;
        if (string.IsNullOrWhiteSpace(value))
        {
            ShowError("Please enter a name.");
            return;
        }

        string trimmed = value.Trim();

        if (trimmed.Length < ArenaOnboardingManager.UsernameMinLength)
        {
            ShowError($"Name must be at least {ArenaOnboardingManager.UsernameMinLength} characters.");
            return;
        }

        if (trimmed.Length > ArenaOnboardingManager.UsernameMaxLength)
        {
            ShowError($"Name must be at most {ArenaOnboardingManager.UsernameMaxLength} characters.");
            return;
        }

        if (!ArenaOnboardingManager.IsUsernameSafe(trimmed))
        {
            ShowError("Name contains invalid characters.");
            return;
        }

        // Use server-validated path when online, local fallback when offline.
        if (ArenaNetworkGuard.IsOnline)
        {
            SubmitUsernameAsync(trimmed);
        }
        else
        {
            if (!ArenaOnboardingManager.TrySetUsername(trimmed))
            {
                ShowError("Unable to set name. It may already be set.");
                return;
            }
            OnUsernameAccepted(trimmed);
        }
    }

    private async void SubmitUsernameAsync(string trimmed)
    {
        _isSubmitting = true;
        if (confirmButton) confirmButton.interactable = false;
        if (loadingSpinner) loadingSpinner.SetActive(true);
        if (errorLabel) errorLabel.text = "";

        try
        {
            var (success, error) = await ArenaOnboardingManager.TrySetUsernameAsync(trimmed);

            if (!success)
            {
                ShowError(error ?? "Unable to set name.");
                return;
            }

            OnUsernameAccepted(trimmed);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ArenaUsernamePopupUI] SubmitUsernameAsync failed: {ex.Message}");
            ShowError("Unable to set name right now. Please try again.");
        }
        finally
        {
            _isSubmitting = false;
            if (loadingSpinner) loadingSpinner.SetActive(false);
            UpdateConfirmButtonState(usernameInput != null ? usernameInput.text : "");
        }
    }

    private void OnUsernameAccepted(string trimmed)
    {
        GameEvents.RaiseToast($"Arena name set: {trimmed}");
        Hide();
    }

    private void ShowError(string msg)
    {
        if (errorLabel) errorLabel.text = msg;
    }
}
