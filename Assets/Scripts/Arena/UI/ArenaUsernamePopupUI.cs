// Assets/Scripts/Arena/UI/ArenaUsernamePopupUI.cs
// BRN Arena v1 — Username creation popup shown during first-open onboarding.
// Username becomes permanent once committed. No global uniqueness required in v1.

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small modal popup for creating the player's arena display name.
/// Shown by <see cref="ArenaOnboardingManager"/> during onboarding step 2.
/// </summary>
public class ArenaUsernamePopupUI : MonoBehaviour
{
    public static ArenaUsernamePopupUI I { get; private set; }

    [Header("UI Refs")]
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TextMeshProUGUI errorLabel;
    [SerializeField] private TextMeshProUGUI charCountLabel;
    [SerializeField] private Button confirmButton;
    [SerializeField] private TextMeshProUGUI hintLabel;

    [Header("Settings")]
    [SerializeField] private bool redirectToDirectoryAfterConfirm = true;

    // ═════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═════════════════════════════════════════════════════════════

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        if (popupRoot) popupRoot.SetActive(false);
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
        if (popupRoot) popupRoot.SetActive(true);
        if (usernameInput) usernameInput.text = "";
        if (errorLabel) errorLabel.text = "";
        UpdateCharCount("");
        UpdateConfirmButtonState("");

        if (hintLabel)
            hintLabel.text = $"Choose a display name ({ArenaOnboardingManager.UsernameMinLength}–{ArenaOnboardingManager.UsernameMaxLength} characters). This is permanent.";
    }

    /// <summary>Hides the popup.</summary>
    public void Hide()
    {
        if (popupRoot) popupRoot.SetActive(false);
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

        if (!ArenaOnboardingManager.TrySetUsername(trimmed))
        {
            ShowError("Unable to set name. It may already be set.");
            return;
        }

        // Success.
        GameEvents.RaiseToast($"Arena name set: {trimmed}");
        Hide();

        // Suggest Battle Team setup.
        if (redirectToDirectoryAfterConfirm && !ArenaSaveHelper.IsBattleTeamComplete())
        {
            GameEvents.RaiseToast("Set up your Battle Team in the Directory!");
            ArenaOnboardingManager.OpenDirectoryForTeamSetup();
        }
    }

    private void ShowError(string msg)
    {
        if (errorLabel) errorLabel.text = msg;
    }
}
