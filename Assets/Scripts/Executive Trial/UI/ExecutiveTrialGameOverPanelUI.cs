using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ExecutiveTrialGameOverPanelUI (Phase 3.A)
/// Vertical mobile layout:
/// Header (Title/Sub/Meta) + Scroll body (Run Result + Stats + Tip) + BottomBar (Return/TryAgain).
/// No final party list (run ends only when wiped).
/// </summary>
public sealed class ExecutiveTrialGameOverPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private ExecutiveTrialManager manager;

    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleTMP;
    [SerializeField] private TextMeshProUGUI subtitleTMP;

    [Header("Body Sections")]
    [SerializeField] private GameObject runResultSection;
    [SerializeField] private TextMeshProUGUI runResultTMP;
    [SerializeField] private GameObject statsSection;
    [SerializeField] private TextMeshProUGUI statsTMP;
    [SerializeField] private GameObject tipSection;
    [SerializeField] private TextMeshProUGUI tipTMP;

    [Header("Timeline")]
    [SerializeField] private GameObject       timelineSection;
    [SerializeField] private IronRunTimelineUI timelineUI;

    [Header("Bottom Bar")]
    [SerializeField] private Button returnHomeButton;
    [SerializeField] private Button tryAgainButton;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<ExecutiveTrialManager>();

        if (returnHomeButton)
            returnHomeButton.onClick.AddListener(() => manager?.ReturnToMenuFromGameOver());

        if (tryAgainButton)
            tryAgainButton.onClick.AddListener(() => manager?.RestartIronFromGameOver());
    }

    private void OnDestroy()
    {
        if (returnHomeButton) returnHomeButton.onClick.RemoveAllListeners();
        if (tryAgainButton) tryAgainButton.onClick.RemoveAllListeners();
    }

    /// <summary>
    /// Back-compat. Defaults to Standard mode.
    /// </summary>
    public void Bind(int wins, bool forfeited)
    {
        Bind(ExecutiveTrialRunState.ExecutiveTrialMode.Standard, wins, ExecutiveTrialRunSummary.Empty, forfeited);
    }

    public void Bind(ExecutiveTrialRunState.ExecutiveTrialMode mode, int wins, bool forfeited)
    {
        Bind(mode, wins, ExecutiveTrialRunSummary.Empty, forfeited);
    }

    public void Bind(ExecutiveTrialRunState.ExecutiveTrialMode mode, int wins, ExecutiveTrialRunSummary summary, bool forfeited, string defeatCauseOverride = null)
    {
        wins = Mathf.Max(0, wins);

        // Root visibility helpers (safe if not wired)
        if (canvasGroup)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        if (titleTMP) titleTMP.text = forfeited ? "FORFEIT" : "GAME OVER";
        if (subtitleTMP)
            subtitleTMP.text = forfeited
                ? "Quit counts as a loss in Executive Trial."
                : "Your party was wiped. The run has ended.";


        if (runResultSection) runResultSection.SetActive(true);
        if (runResultTMP)
        {
            string outcome = forfeited ? "FORFEIT" : "DEFEAT";
            string cause = forfeited
                ? "Quit Run"
                : (!string.IsNullOrWhiteSpace(defeatCauseOverride) ? defeatCauseOverride : "Party Defeated");
            string modeLabel = (mode == ExecutiveTrialRunState.ExecutiveTrialMode.Hardcore) ? "Hardcore" : "Standard";
            int floorReached = wins + 1;

            runResultTMP.text =
                $"Outcome: {outcome}\n" +
                $"Cause: {cause}\n" +
                $"Mode: {modeLabel}\n" +
                $"Floor Reached: {floorReached}\n" +
                $"Win Streak: {wins}\n" +
                $"Time Survived: {FormatDuration(summary.totalSecondsSurvived)}";
        }

        if (statsSection) statsSection.SetActive(true);
        if (statsTMP)
        {
            statsTMP.text =
                $"Total Battles: {Mathf.Max(0, summary.totalBattles):N0}\n" +
                $"Total Damage Dealt: {Mathf.Max(0, summary.totalDamageDealt):N0}\n" +
                $"Total Damage Taken: {Mathf.Max(0, summary.totalDamageTaken):N0}\n" +
                $"Total Crits: {Mathf.Max(0, summary.totalCrits):N0}\n" +
                $"Total Growth Cores: {Mathf.Max(0, summary.totalGrowthCores):N0}\n" +
                $"Total Credits: {Mathf.Max(0, summary.totalCredits):N0}";
        }

        if (tipSection) tipSection.SetActive(true);
        if (tipTMP)
        {
            tipTMP.text = "If forced evolve exists → Continue opens evolve";
        }

        // Buttons
        if (returnHomeButton) returnHomeButton.gameObject.SetActive(true);
        if (tryAgainButton) tryAgainButton.gameObject.SetActive(true);
    }

    private static string FormatDuration(float totalSeconds)
    {
        int seconds = Mathf.Max(0, Mathf.RoundToInt(totalSeconds));
        int mins = seconds / 60;
        int secs = seconds % 60;
        return $"{mins:00}:{secs:00}";
    }

    public void Bind(
        ExecutiveTrialRunState.ExecutiveTrialMode mode,
        int wins,
        ExecutiveTrialRunSummary summary,
        bool forfeited,
        string defeatCauseOverride,
        List<IronBattleLogEntry> battleLog)
    {
        // Delegate all existing behaviour unchanged
        Bind(mode, wins, summary, forfeited, defeatCauseOverride);

        // Timeline
        bool hasLog = battleLog != null && battleLog.Count > 0;
        if (timelineSection != null) timelineSection.SetActive(hasLog);
        if (timelineUI != null && hasLog)
            timelineUI.Bind(battleLog, forfeited, mode);
    }
}
