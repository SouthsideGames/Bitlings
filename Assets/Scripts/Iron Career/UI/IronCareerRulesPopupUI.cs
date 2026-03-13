using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// </summary>
public sealed class IronCareerRulesPopupUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleTMP;
    [SerializeField] private TextMeshProUGUI subtitleTMP;

    [Header("Body")]
    [SerializeField] private TextMeshProUGUI rulesTMP;
    [SerializeField] private Toggle dontShowAgainToggle;
    [SerializeField] private TextMeshProUGUI dontShowAgainLabelTMP;

    [Header("Buttons")]
    [SerializeField] private Button understandButton;
    [SerializeField] private TextMeshProUGUI understandLabelTMP;
    [SerializeField] private Button closeButton;
    [SerializeField] private TextMeshProUGUI closeLabelTMP;

    private bool _wired;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        WireOnce();
    }

    private void OnEnable()
    {
        RefreshVisuals();
    }

    private void OnDestroy()
    {
        if (understandButton) understandButton.onClick.RemoveAllListeners();
        if (closeButton) closeButton.onClick.RemoveAllListeners();
    }

    private void WireOnce()
    {
        if (_wired) return;
        _wired = true;

        if (understandButton) understandButton.onClick.AddListener(OnUnderstand);
        if (closeButton) closeButton.onClick.AddListener(OnClose);

        if (dontShowAgainLabelTMP && string.IsNullOrWhiteSpace(dontShowAgainLabelTMP.text))
            dontShowAgainLabelTMP.text = "Don't show again (this run)";
    }

    private void RefreshVisuals()
    {
        // Block raycasts when shown.
        if (canvasGroup)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        bool isQuitConfirm = manager != null && manager.IsQuitPromptActive;
        bool hardcore = manager != null && manager.IsHardcoreMode;

        if (titleTMP) titleTMP.text = "IRON CAREER RULES";
        if (subtitleTMP) subtitleTMP.text = isQuitConfirm ? "Quit = forfeit. Confirm you understand." : "Read before starting your run";

        if (rulesTMP)
            rulesTMP.text = BuildRulesBody(hardcore);

        if (dontShowAgainToggle)
            dontShowAgainToggle.isOn = manager != null && manager.SuppressRulesThisRun;

        if (understandLabelTMP) understandLabelTMP.text = "I UNDERSTAND";
        if (closeLabelTMP) closeLabelTMP.text = "CLOSE";
    }

    private static string BuildRulesBody(bool hardcore)
    {
        string modeLine = hardcore
            ? "Hardcore: Hire is forced"
            : "Standard: Hire is optional";

        return
            "<b><u>CORE RULES</u></b>\n" +
            "• Permadeath: HP 0 = removed\n" +
            "• No healing between battles\n" +
            "• HP carries forward each fight\n" +
            "• Party size cap: 3\n\n" +
            "<b><u>RUN FLOW</u></b>\n" +
            "• Win -> Hire/Replace -> Post\n" +
            "• Forced Evolution blocks progress\n" +
            "• Rest nodes every 3 wins\n\n" +
            "<b><u>QUIT = FORFEIT</u></b>\n" +
            "• Quitting ends the run as a loss\n" +
            "• No rewards are granted\n\n" +
            "<b><u>MODE</u></b>\n" +
            modeLine;
    }

    private void ApplyTogglePreference()
    {
        if (!manager || !dontShowAgainToggle) return;
        manager.SetSuppressRulesThisRun(dontShowAgainToggle.isOn);
    }

    private void OnUnderstand()
    {
        ApplyTogglePreference();

        // If this popup was opened as a quit prompt, "I UNDERSTAND" confirms forfeit.
        if (manager != null && manager.IsQuitPromptActive)
        {
            manager.ConfirmQuitForfeit();
            return;
        }

        // Informational rules popup.
        manager?.AcknowledgeRules();
    }

    private void OnClose()
    {
        ApplyTogglePreference();

        // If this popup was opened as a quit prompt, close cancels quit.
        if (manager != null && manager.IsQuitPromptActive)
        {
            manager.CancelQuit();
            return;
        }

        manager?.CloseRules();
    }
}
