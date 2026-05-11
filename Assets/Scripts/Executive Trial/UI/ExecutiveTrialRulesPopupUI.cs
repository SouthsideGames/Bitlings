using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// </summary>
public sealed class ExecutiveTrialRulesPopupUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private ExecutiveTrialManager manager;

    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleTMP;
    [SerializeField] private TextMeshProUGUI subtitleTMP;

    [Header("Body")]
    [SerializeField] private TextMeshProUGUI rulesTMP;

    [Header("Buttons")]
    [SerializeField] private Button understandButton;
    [SerializeField] private TextMeshProUGUI understandLabelTMP;
    [SerializeField] private Button closeButton;
    [SerializeField] private TextMeshProUGUI closeLabelTMP;

    private bool _wired;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<ExecutiveTrialManager>();
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

        if (titleTMP) titleTMP.text = "EXECUTIVE TRIAL RULES";
        if (subtitleTMP) subtitleTMP.text = isQuitConfirm ? "Quit = forfeit. Confirm you understand." : "Read before starting your run";

        if (rulesTMP)
            rulesTMP.text = BuildRulesBody(hardcore);


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
            "• HP carries forward each fight\n" +
            "• No healing between battles\n" +
            "• Party size cap: 3\n" +
            "• Every win: all members gain +1 level\n\n" +
            "<b><u>RUN FLOW</u></b>\n" +
            "• Win -> Hire/Replace -> Post\n" +
            "• Forced Evolution blocks progress\n" +
            "• Rest nodes every 3 wins\n" +
            "• Rest: heal 25% HP or level up a random member\n\n" +
            "<b><u>QUIT = FORFEIT</u></b>\n" +
            "• Quitting ends the run as a loss\n" +
            "• No rewards are granted\n\n" +
            "<b><u>MODE</u></b>\n" +
            modeLine;
    }


    private void OnUnderstand()
    {
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
        // If this popup was opened as a quit prompt, close cancels quit.
        if (manager != null && manager.IsQuitPromptActive)
        {
            manager.CancelQuit();
            return;
        }

        manager?.CloseRules();
    }
}
