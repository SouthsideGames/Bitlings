using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerDossierPanelUI : MonoBehaviour
{
    [Header("Navigation")]
    [SerializeField] private Button prevButton;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button closeButton;

    [SerializeField] private TextMeshProUGUI pageLabelText;  
    [SerializeField] private TextMeshProUGUI dotsText;      

    [Header("Pages (in order)")]
    [Tooltip("Order MUST be: Page_1_Overview, Page_2_JobSites, Page_3_FieldOps, Page_4_Resources, Page_5_Resume, Page_6_Achievements, Page_7_Ranks")]
    [SerializeField] private GameObject[] pages;

    // ─────────────────────────────────────────────────────────────
    // PAGE 1 – OVERVIEW UI REFERENCES
    // ─────────────────────────────────────────────────────────────
    [Header("Page 1 - Overview")]
    [SerializeField] private Image avatarImage;
    [SerializeField] private TextMeshProUGUI rankText;
    [SerializeField] private TextMeshProUGUI operationIdText;

    [SerializeField] private TextMeshProUGUI totalBitlingsText;
    [SerializeField] private TextMeshProUGUI discoveredSpeciesText;
    [SerializeField] private TextMeshProUGUI avgLevelText;
    [SerializeField] private TextMeshProUGUI premiumCountText;

    [SerializeField] private Image careScoreFillImage;
    [SerializeField] private TextMeshProUGUI careScoreValueText;
    [SerializeField] private TextMeshProUGUI careScoreNoteText;
    [SerializeField] private Button careScoreInfoButton;

    // ─────────────────────────────────────────────────────────────
    // PAGE 2 – JOB NETWORK
    // ─────────────────────────────────────────────────────────────
    [Header("Page 2 - Jobs")]
    [SerializeField] private Transform jobPanelParent;
    [SerializeField] private PlayerDossierJobPanelUI jobPanelPrefab;
    private readonly List<PlayerDossierJobPanelUI> _jobPanels = new List<PlayerDossierJobPanelUI>();

    // ─────────────────────────────────────────────────────────────
    // PAGE 3 – FIELD OPERATIONS
    // ─────────────────────────────────────────────────────────────
    [Header("Page 3 - Field Ops")]
    [SerializeField] private TextMeshProUGUI encountersInitiatedText;
    [SerializeField] private TextMeshProUGUI captureSuccessRateText;
    [SerializeField] private TextMeshProUGUI riftStabilizationsText;
    [SerializeField] private TextMeshProUGUI rareBitlingsFoundText;
    [SerializeField] private TextMeshProUGUI premiumDiscoveriesText;
    [SerializeField] private TextMeshProUGUI longestCaptureStreakText;
    [SerializeField] private TextMeshProUGUI fieldOpsHighlightsText;

    // ─────────────────────────────────────────────────────────────
    // PAGE 4 – RESOURCES
    // ─────────────────────────────────────────────────────────────
    [Header("Page 4 – Resources")]
    [SerializeField] private TextMeshProUGUI creditsText;
    [SerializeField] private TextMeshProUGUI energyText;
    [SerializeField] private TextMeshProUGUI medkitText;
    [SerializeField] private TextMeshProUGUI materialText;
    [SerializeField] private TextMeshProUGUI pPEPermitText;
    [SerializeField] private TextMeshProUGUI flyerText;
    [SerializeField] private TextMeshProUGUI workOrderText;
    [SerializeField] private TextMeshProUGUI favorText;
    [SerializeField] private TextMeshProUGUI trainingVoucherText;
    [SerializeField] private TextMeshProUGUI wellnessVoucherText;
    [SerializeField] private TextMeshProUGUI efficiencyVoucherText;
    [SerializeField] private TextMeshProUGUI premiumOrbText;
    [SerializeField] private TextMeshProUGUI blessingScaleText;
    [SerializeField] private TextMeshProUGUI coffeeText;
    [SerializeField] private TextMeshProUGUI growthCoreText;
    [SerializeField] private TextMeshProUGUI packVoucherText;

    [SerializeField] private GameObject bullTokenRow;
    [SerializeField] private TextMeshProUGUI bullTokenText;
    [SerializeField] private GameObject bearTokenRow;
    [SerializeField] private TextMeshProUGUI bearTokenText;
    [SerializeField] private GameObject arenaTicketRow;
    [SerializeField] private TextMeshProUGUI arenaTicketText;

    [SerializeField] private Image efficiencyFill;
    [SerializeField] private TextMeshProUGUI efficiencyPercentText;
    [SerializeField] private TextMeshProUGUI brnRatingText;

    // ─────────────────────────────────────────────────────────────
    // PAGE 5 – BRN RÉSUMÉ
    // ─────────────────────────────────────────────────────────────
    [Header("Page 5 - BRN Résumé")]
    [SerializeField] private TextMeshProUGUI resumeLinesText;
    [SerializeField] private TextMeshProUGUI resumeNoteText;

    // ─────────────────────────────────────────────────────────────
    // PAGE 6 – ACHIEVEMENTS (FINAL PAGE)
    // ─────────────────────────────────────────────────────────────
    [Header("Page 6 - Achievements")]
    [SerializeField] private Transform achievementRowParent;
    [SerializeField] private PlayerDossierAchievementRowUI achievementRowPrefab;
    [SerializeField] private Slider achievementCompletionSlider;
    [SerializeField] private TextMeshProUGUI achievementCompletionText;

    private readonly List<PlayerDossierAchievementRowUI> _achievementRows = new List<PlayerDossierAchievementRowUI>();

    // ─────────────────────────────────────────────────────────────
    // PAGE 7 – RANKS
    // ─────────────────────────────────────────────────────────────
    [Header("Page 7 - Ranks")]
    [SerializeField] private PromotionTableSO promotionTable;
    [SerializeField] private TextMeshProUGUI rankHeaderText;
    [SerializeField] private TextMeshProUGUI rankProgressText;
    [SerializeField] private RectTransform rankListContent;
    [SerializeField] private PromotionRankRowUI rankRowPrefab;

    private readonly List<PromotionRankRowUI> _rankRows = new List<PromotionRankRowUI>();

    private int _currentPageIndex = 0;
    private static int _pendingPageIndex = -1;

    private void Awake()
    {
        if (prevButton != null) prevButton.onClick.AddListener(OnPrevClicked);
        if (nextButton != null) nextButton.onClick.AddListener(OnNextClicked);
        if (closeButton != null) closeButton.onClick.AddListener(OnCloseClicked);
    }

    /// <summary>
    /// Navigate to a 1-based page number (1-7).
    /// Safe to call before the panel is shown — the page will be applied in OnEnable.
    /// </summary>
    public static void SetPendingPage(int oneBasedPage)
    {
        _pendingPageIndex = Mathf.Max(0, oneBasedPage - 1);
    }

    public void GoToPage(int oneBasedPage)
    {
        if (pages == null || pages.Length == 0) return;
        _currentPageIndex = Mathf.Clamp(oneBasedPage - 1, 0, pages.Length - 1);
        RefreshPageVisibility();
        RefreshNavigationUI();
    }

    private void OnEnable()
    {
        if (_pendingPageIndex >= 0)
        {
            _currentPageIndex = (pages != null) ? Mathf.Clamp(_pendingPageIndex, 0, pages.Length - 1) : 0;
            _pendingPageIndex = -1;
        }
        else
        {
            _currentPageIndex = 0;
        }
        RefreshPageVisibility();
        RefreshNavigationUI();

        // Clear "NEW" flags when dossier opens (central + simple)
        if (AchievementManager.I != null)
            AchievementManager.I.MarkAllUnlockedAsSeen();

        var manager = PlayerDossierManager.I;
        if (manager != null)
        {
            manager.RefreshSnapshot();
            var snapshot = manager.CurrentSnapshot;

            
            PopulatePage1(snapshot);

            if (careScoreInfoButton)
            {
                careScoreInfoButton.onClick.RemoveAllListeners();
                careScoreInfoButton.onClick.AddListener(() =>
                {
                    if (TooltipUI.I == null) return;

                    string msg =
                        "CARE SCORE BREAKDOWN\n" +
                        $"• Development: {snapshot.careDevelopmentPercent:0}%\n" +
                        $"• Balance: {snapshot.careBalancePercent:0}%\n" +
                        $"• Recovery: {snapshot.careRecoveryPercent:0}%\n" +
                        $"• Assignment: {snapshot.careAssignmentPercent:0}%";

                    TooltipUI.I.Show(msg);
                });
            }

            PopulatePage2(snapshot);
            PopulatePage3(snapshot);
            PopulatePage4Resources(snapshot);
            PopulatePage5Resume(snapshot);
            PopulatePage6Achievements(snapshot);
            PopulatePage7Ranks(snapshot);
        }
        else
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[PlayerDossierPanelUI] No PlayerDossierManager found in scene.");
            #endif
            PopulatePage1(null);
            PopulatePage2(null);
            PopulatePage3(null);
            PopulatePage4Resources(null);
            PopulatePage5Resume(null);
            PopulatePage6Achievements(null);
            PopulatePage7Ranks(null);
        }
    }

    private void OnDestroy()
    {
        if (prevButton != null) prevButton.onClick.RemoveListener(OnPrevClicked);
        if (nextButton != null) nextButton.onClick.RemoveListener(OnNextClicked);
        if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);
    }

    // ─────────────────────────────────────────────────────────────
    // Navigation
    // ─────────────────────────────────────────────────────────────

    private void OnPrevClicked()
    {
        if (pages == null || pages.Length == 0) return;

        _currentPageIndex = Mathf.Max(0, _currentPageIndex - 1);
        RefreshPageVisibility();
        RefreshNavigationUI();
    }

    private void OnNextClicked()
    {
        if (pages == null || pages.Length == 0) return;

        _currentPageIndex = Mathf.Min(pages.Length - 1, _currentPageIndex + 1);
        RefreshPageVisibility();
        RefreshNavigationUI();
    }

    private void OnCloseClicked()
    {
        gameObject.SetActive(false);
    }

    private void RefreshPageVisibility()
    {
        if (pages == null) return;

        for (int i = 0; i < pages.Length; i++)
            if (pages[i] != null)
                pages[i].SetActive(i == _currentPageIndex);
    }

    private void RefreshNavigationUI()
    {
        int totalPages = (pages != null) ? pages.Length : 0;
        int displayIndex = _currentPageIndex + 1;

        if (pageLabelText != null)
            pageLabelText.text = $"PAGE {displayIndex} / {totalPages}";

        if (dotsText != null)
            dotsText.text = BuildDotsString(totalPages, _currentPageIndex);

        bool isFirstPage = (_currentPageIndex <= 0);

        if (prevButton != null)
            prevButton.gameObject.SetActive(!isFirstPage);

        if (closeButton != null)
            closeButton.gameObject.SetActive(isFirstPage);

        if (nextButton != null)
            nextButton.interactable = _currentPageIndex < totalPages - 1;
    }

    private string BuildDotsString(int totalPages, int currentIndex)
    {
        if (totalPages <= 0) return string.Empty;

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < totalPages; i++)
        {
            sb.Append(i == currentIndex ? '•' : '○');
            if (i < totalPages - 1) sb.Append(' ');
        }
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // Page 1
    // ─────────────────────────────────────────────────────────────

    private void PopulatePage1(PlayerDossierSnapshot stats)
    {
        if (stats == null)
        {
            if (rankText != null) rankText.text = "Rank: Trainee";
            if (operationIdText != null) operationIdText.text = "Operation ID: BRN-0000-XXXX";

            if (totalBitlingsText != null) totalBitlingsText.text = "Total Bitlings Managed:   0";
            if (discoveredSpeciesText != null) discoveredSpeciesText.text = "Discovered Species:       0";
            if (avgLevelText != null) avgLevelText.text = "Average Bitling Level:     0";
            if (premiumCountText != null) premiumCountText.text = "Premium Bitlings:            0";

            if (careScoreFillImage != null)
            {
                if (careScoreFillImage.type == Image.Type.Filled)
                    careScoreFillImage.fillAmount = 0f;
                else
                    careScoreFillImage.rectTransform.anchorMax = new Vector2(0f, careScoreFillImage.rectTransform.anchorMax.y);
            }

            if (careScoreValueText != null) careScoreValueText.text = "0%";
            if (careScoreNoteText != null) careScoreNoteText.text = "BRN notes: No data available.";
            return;
        }

        if (rankText != null) rankText.text = stats.rankName;
        if (operationIdText != null) operationIdText.text = stats.operationId;

        if (totalBitlingsText != null) totalBitlingsText.text = $"Total Bitlings Managed:   {stats.totalOwnedBitlings}";
        if (discoveredSpeciesText != null) discoveredSpeciesText.text = $"Discovered Species:       {stats.discoveredSpecies}";
        if (avgLevelText != null) avgLevelText.text = $"Average Bitling Level:     {stats.averageLevel:0}";
        if (premiumCountText != null) premiumCountText.text = $"Premium Bitlings:            {stats.premiumOwned}";

        float normalized = Mathf.Clamp01(stats.careScorePercent / 100f);

        if (careScoreFillImage != null)
        {
            if (careScoreFillImage.type == Image.Type.Filled)
                careScoreFillImage.fillAmount = normalized;
            else
                careScoreFillImage.rectTransform.anchorMax = new Vector2(normalized, careScoreFillImage.rectTransform.anchorMax.y);
        }

        if (careScoreValueText != null) careScoreValueText.text = $"{stats.careScorePercent:0}%";
        if (careScoreNoteText != null) careScoreNoteText.text = stats.careScoreNote;
    }

    // ─────────────────────────────────────────────────────────────
    // Page 2 – Jobs
    // ─────────────────────────────────────────────────────────────

    private void PopulatePage2(PlayerDossierSnapshot stats)
    {
        if (jobPanelParent == null || jobPanelPrefab == null)
            return;

        if (stats == null || stats.jobSites == null || stats.jobSites.Length == 0)
        {
            for (int i = 0; i < _jobPanels.Count; i++)
                _jobPanels[i].gameObject.SetActive(false);
            return;
        }

        int needed = stats.jobSites.Length;

        while (_jobPanels.Count < needed)
            _jobPanels.Add(Instantiate(jobPanelPrefab, jobPanelParent));

        for (int i = 0; i < _jobPanels.Count; i++)
        {
            if (i < needed) _jobPanels[i].Bind(stats.jobSites[i]);
            else _jobPanels[i].gameObject.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Page 3 – Field Ops
    // ─────────────────────────────────────────────────────────────

    private void PopulatePage3(PlayerDossierSnapshot stats)
    {
        if (stats == null)
        {
            if (encountersInitiatedText != null) encountersInitiatedText.text = "Encounters Initiated:   0";
            if (captureSuccessRateText != null) captureSuccessRateText.text = "Capture Success Rate:   0%";
            if (riftStabilizationsText != null) riftStabilizationsText.text = "Boss Rifts Cleared:     0";
            if (rareBitlingsFoundText != null) rareBitlingsFoundText.text = "Rare Bitlings Found:    0";
            if (premiumDiscoveriesText != null) premiumDiscoveriesText.text = "Premium Discoveries:      0";
            if (longestCaptureStreakText != null) longestCaptureStreakText.text = "Longest Capture Streak: 0";
            if (fieldOpsHighlightsText != null) fieldOpsHighlightsText.text = "Recent Highlights:\n—";
            return;
        }

        if (encountersInitiatedText != null) encountersInitiatedText.text = $"Encounters Initiated:   {stats.encountersInitiated}";
        if (captureSuccessRateText != null) captureSuccessRateText.text = $"Capture Success Rate:   {stats.captureSuccessRate}%";
        if (riftStabilizationsText != null) riftStabilizationsText.text = $"Boss Rifts Cleared:     {stats.riftStabilizations}";
        if (rareBitlingsFoundText != null) rareBitlingsFoundText.text = $"Rare Bitlings Found:    {stats.rareBitlingsFound}";
        if (premiumDiscoveriesText != null) premiumDiscoveriesText.text = $"Premium Discoveries:      {stats.premiumDiscoveries}";
        if (longestCaptureStreakText != null) longestCaptureStreakText.text = $"Longest Capture Streak: {stats.longestCaptureStreak}";

        if (fieldOpsHighlightsText != null)
            fieldOpsHighlightsText.text = (stats.fieldOpsHighlights == null || stats.fieldOpsHighlights.Length == 0)
                ? "—"
                : string.Join("\n", stats.fieldOpsHighlights);
    }

    // ─────────────────────────────────────────────────────────────
    // Page 4 – Resources
    // ─────────────────────────────────────────────────────────────

    private void PopulatePage4Resources(PlayerDossierSnapshot s)
    {
        if (s == null) return;

        if (materialText) materialText.text = $"Ingot Materials: {s.materialCount:N0}";
        if (energyText) energyText.text = $"Harbor Cargo: {s.energyCount:N0}";
        if (workOrderText) workOrderText.text = $"Capture Bands: {s.captureBandCount}";
        if (blessingScaleText) blessingScaleText.text = $"Blessing Tokens: {s.blessingScaleCount}";
        if (favorText) favorText.text = $"Luck Orbs: {s.luckCount}";
        if (flyerText) flyerText.text = $"Premium Lures: {s.lureCount}";
        if (growthCoreText) growthCoreText.text = $"Growth Cores: {s.growthCoreCount}";
        if (packVoucherText) packVoucherText.text = $"Shards: {s.packVoucherCount}";
        if (creditsText) creditsText.text = $"Credits: {s.creditCount:N0}";
        if (medkitText) medkitText.text = $"Medkits: {s.medkitCount}";
        if (pPEPermitText) pPEPermitText.text = $"Type Shields: {s.typeResBoosterCount}";
        if (trainingVoucherText) trainingVoucherText.text = $"Attack Boosters: {s.atkBoosterCount}";
        if (wellnessVoucherText) wellnessVoucherText.text = $"HP Boosters: {s.hpBoosterCount}";
        if (efficiencyVoucherText) efficiencyVoucherText.text = $"Speed Boosters: {s.speedBoosterCount}";
        if (premiumOrbText) premiumOrbText.text = $"Premium Orbs: {s.premiumOrbCount}";
        if (coffeeText) coffeeText.text = $"Rest Charges: {s.restChargeCount}";

        if (bullTokenRow) bullTokenRow.SetActive(s.bullTokensUnlocked);
        if (bullTokenText && s.bullTokensUnlocked) bullTokenText.text = $"Bull Tokens: {s.bullTokenCount}";

        if (bearTokenRow) bearTokenRow.SetActive(s.bearTokensUnlocked);
        if (bearTokenText && s.bearTokensUnlocked) bearTokenText.text = $"Bear Tokens: {s.bearTokenCount}";

        if (arenaTicketRow) arenaTicketRow.SetActive(s.arenaTicketsUnlocked);
        if (arenaTicketText && s.arenaTicketsUnlocked) arenaTicketText.text = $"Arena Tickets: {s.arenaTicketCount}";

        float normalized = Mathf.Clamp01(s.conversionEfficiencyPercent / 100f);

        if (efficiencyPercentText)
            efficiencyPercentText.text = $"{s.conversionEfficiencyPercent}%";

        if (efficiencyFill)
            efficiencyFill.rectTransform.anchorMax = new Vector2(normalized, efficiencyFill.rectTransform.anchorMax.y);

        if (brnRatingText)
        {
            int eff = s.conversionEfficiencyPercent;
            brnRatingText.text =
                eff >= 70 ? "BRN Rating: Critical Asset" :
                eff >= 40 ? "BRN Rating: High-Performance Handler" :
                            "BRN Rating: Stable Operator";
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Page 5 – Résumé
    // ─────────────────────────────────────────────────────────────

    private void PopulatePage5Resume(PlayerDossierSnapshot stats)
    {
        if (resumeLinesText == null && resumeNoteText == null) return;

        if (stats == null) return;

        if (resumeLinesText != null)
        {
            if (stats.resumeLines == null || stats.resumeLines.Length == 0)
            {
                resumeLinesText.text =
                    "• No significant events recorded yet.\n" +
                    "• Continue operating job sites and handling field encounters.";
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < stats.resumeLines.Length; i++)
                {
                    if (i > 0) sb.AppendLine();
                    sb.Append("• ");
                    sb.Append(stats.resumeLines[i]);
                }
                resumeLinesText.text = sb.ToString();
            }
        }

        if (resumeNoteText != null)
        {
            resumeNoteText.text = string.IsNullOrEmpty(stats.brnResumeNote)
                ? "Handler performance remains under review."
                : stats.brnResumeNote;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Page 6 – Achievements (FINAL PAGE)
    // ─────────────────────────────────────────────────────────────

    private void PopulatePage6Achievements(PlayerDossierSnapshot stats)
    {
        if (achievementRowParent == null || achievementRowPrefab == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[PlayerDossierPanelUI] Achievements refs missing: achievementRowParent or achievementRowPrefab.");
            #endif
            return;
        }

        if (stats == null || stats.achievements == null)
        {
            for (int i = 0; i < _achievementRows.Count; i++)
                _achievementRows[i].gameObject.SetActive(false);

            if (achievementCompletionText) achievementCompletionText.text = "Commendations Issued: 0 / 0";
            if (achievementCompletionSlider)
            {
                achievementCompletionSlider.minValue = 0;
                achievementCompletionSlider.maxValue = 1;
                achievementCompletionSlider.value = 0;
            }
            return;
        }

        int unlocked = Mathf.Max(0, stats.achievementsUnlocked);
        int total = Mathf.Max(1, stats.achievementsTotal);

        if (achievementCompletionText)
            achievementCompletionText.text = $"Commendations Issued: {unlocked} / {total}";

        if (achievementCompletionSlider)
        {
            LeanTween.cancel(achievementCompletionSlider.gameObject);

            achievementCompletionSlider.minValue = 0;
            achievementCompletionSlider.maxValue = total;

            float from = achievementCompletionSlider.value;
            float to = unlocked;

            // If the slider hasn't been initialized yet, snap then animate
            if (from <= 0f && unlocked > 0)
                from = 0f;

            achievementCompletionSlider.value = from;

            LeanTween.value(achievementCompletionSlider.gameObject, from, to, 0.25f)
                .setOnUpdate(v => { if (achievementCompletionSlider) achievementCompletionSlider.value = v; });
        }

        int needed = stats.achievements.Length;

        while (_achievementRows.Count < needed)
            _achievementRows.Add(Instantiate(achievementRowPrefab, achievementRowParent));

        for (int i = 0; i < _achievementRows.Count; i++)
        {
            if (i < needed)
                _achievementRows[i].Bind(stats.achievements[i]);
            else
                _achievementRows[i].gameObject.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Page 7 - Ranks
    // ─────────────────────────────────────────────────────────────
    private void PopulatePage7Ranks(PlayerDossierSnapshot stats)
    {
        if (rankHeaderText)
        {
            int r = (stats != null) ? Mathf.Max(1, stats.promotionRank) : 1;
            rankHeaderText.text = $"Promotion Rank: {r}";
        }

        if (rankProgressText)
        {
            if (stats == null)
                rankProgressText.text = "XP: 0";
            else
            {
                if (stats.promotionXpToNext <= 0)
                    rankProgressText.text = $"XP: {Mathf.Max(0, stats.promotionXP)} (MAX)";
                else
                    rankProgressText.text = $"XP: {Mathf.Max(0, stats.promotionXP)}  (+{Mathf.Max(0, stats.promotionXpIntoRank)} / {Mathf.Max(0, stats.promotionXpIntoRank + stats.promotionXpToNext)})";
            }
        }

        if (rankListContent == null || rankRowPrefab == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[PlayerDossierPanelUI] Rank page refs missing: rankListContent or rankRowPrefab.");
            #endif
            return;
        }

        int currentRank = (stats != null) ? Mathf.Max(1, stats.promotionRank) : 1;

        int maxRank = 20;
        if (promotionTable != null)
            maxRank = Mathf.Max(1, promotionTable.MaxRank);

        // Ensure rows
        while (_rankRows.Count < maxRank)
            _rankRows.Add(Instantiate(rankRowPrefab, rankListContent));

        for (int i = 0; i < _rankRows.Count; i++)
        {
            int rank = i + 1;
            if (rank <= maxRank)
            {
                var row = _rankRows[i];
                if (row == null) continue;

                bool unlocked = rank <= currentRank;
                bool isCurrent = rank == currentRank;

                int totalXpToReach = 0;
                string displayName = null;
                string reward = null;

                if (promotionTable != null)
                {
                    var e = promotionTable.Get(rank);
                    if (e != null)
                    {
                        totalXpToReach = Mathf.Max(0, e.totalXpToReach);
                        displayName = e.displayName;
                        reward = e.rewardSummary;
                    }
                    else
                    {
                        // If the table doesn't define this rank, show fallback numbers.
                        totalXpToReach = (PromotionManager.I != null)
                            ? PromotionManager.I.GetTotalXpToReach(rank)
                            : 0;
                    }
                }
                else
                {
                    totalXpToReach = (PromotionManager.I != null)
                        ? PromotionManager.I.GetTotalXpToReach(rank)
                        : 0;
                }

                row.gameObject.SetActive(true);
                row.Bind(rank, unlocked, isCurrent, totalXpToReach, displayName, reward);
            }
            else
            {
                if (_rankRows[i] != null)
                    _rankRows[i].gameObject.SetActive(false);
            }
        }
    }
}