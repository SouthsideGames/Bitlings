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
    [SerializeField] private TextMeshProUGUI pageLabelText;   // "PAGE 1 / 5"
    [SerializeField] private TextMeshProUGUI dotsText;        // "• ○ ○ ○ ○"

    [Header("Pages (in order)")]
    [SerializeField] private GameObject[] pages; // Page_1_Overview, Page_2_JobSites, etc.

    // ─────────────────────────────────────────────────────────────
    // PAGE 1 – OVERVIEW UI REFERENCES
    // ─────────────────────────────────────────────────────────────
    [Header("Page 1 - Overview")]
    [SerializeField] private Image avatarImage; // placeholder
    [SerializeField] private TextMeshProUGUI handlerNameText;
    [SerializeField] private TextMeshProUGUI rankText;
    [SerializeField] private TextMeshProUGUI operationIdText;

    [SerializeField] private TextMeshProUGUI totalBitlingsText;
    [SerializeField] private TextMeshProUGUI discoveredSpeciesText;
    [SerializeField] private TextMeshProUGUI avgLevelText;
    [SerializeField] private TextMeshProUGUI shinyCountText;

    [SerializeField] private Image careScoreFillImage;
    [SerializeField] private TextMeshProUGUI careScoreValueText;
    [SerializeField] private TextMeshProUGUI careScoreNoteText;

    // ─────────────────────────────────────────────────────────────
    // PAGE 2 – JOB NETWORK UI REFERENCES
    // ─────────────────────────────────────────────────────────────
    [Header("Page 2 - Jobs")]
    [SerializeField] private Transform jobPanelParent;           // Grid/vertical layout on Page 2
    [SerializeField] private PlayerDossierJobPanelUI jobPanelPrefab;

    private readonly List<PlayerDossierJobPanelUI> _jobPanels = new List<PlayerDossierJobPanelUI>();

    // ─────────────────────────────────────────────────────────────
    // PAGE 3 – FIELD OPERATIONS UI REFERENCES
    // ─────────────────────────────────────────────────────────────
    [Header("Page 3 - Field Ops")]
    [SerializeField] private TextMeshProUGUI encountersInitiatedText;
    [SerializeField] private TextMeshProUGUI captureSuccessRateText;
    [SerializeField] private TextMeshProUGUI riftStabilizationsText;
    [SerializeField] private TextMeshProUGUI rareBitlingsFoundText;
    [SerializeField] private TextMeshProUGUI shinyDiscoveriesText;
    [SerializeField] private TextMeshProUGUI longestCaptureStreakText;
    [SerializeField] private TextMeshProUGUI fieldOpsHighlightsText;


    [Header("Page 4 – Resources")]
    [SerializeField] private TextMeshProUGUI coinsText;
    [SerializeField] private TextMeshProUGUI energyText;
    [SerializeField] private TextMeshProUGUI medkitText;
    [SerializeField] private TextMeshProUGUI materialText;
    [SerializeField] private TextMeshProUGUI typeResBoosterText;
    [SerializeField] private TextMeshProUGUI lureText;
    [SerializeField] private TextMeshProUGUI captureBandText;
    [SerializeField] private TextMeshProUGUI luckText;
    [SerializeField] private TextMeshProUGUI atkBoosterText;
    [SerializeField] private TextMeshProUGUI hpBoosterText;
    [SerializeField] private TextMeshProUGUI speedBoosterText;
    [SerializeField] private TextMeshProUGUI shinyOrbText;
    [SerializeField] private TextMeshProUGUI blessingScaleText;
    [SerializeField] private TextMeshProUGUI restChargeText;
    [SerializeField] private TextMeshProUGUI growthCoreText;
    [SerializeField] private TextMeshProUGUI packShardText;

    [SerializeField] private Image efficiencyFill;
    [SerializeField] private TextMeshProUGUI efficiencyPercentText;
    [SerializeField] private TextMeshProUGUI brnRatingText;


    private int _currentPageIndex = 0;

    private void Awake()
    {
        if (prevButton != null)
            prevButton.onClick.AddListener(OnPrevClicked);

        if (nextButton != null)
            nextButton.onClick.AddListener(OnNextClicked);
    }

    private void OnEnable()
    {
        _currentPageIndex = 0;
        RefreshPageVisibility();
        RefreshNavigationUI();

        var manager = PlayerDossierManager.I;
        if (manager != null)
        {
            var snapshot = manager.CurrentSnapshot;
            PopulatePage1(snapshot);
            PopulatePage2(snapshot);
            PopulatePage3(snapshot);
            PopulatePage4(snapshot);
        }
        else
        {
            Debug.LogWarning("[PlayerDossierPanelUI] No PlayerDossierManager found in scene.");
            PopulatePage1(null);
            PopulatePage2(null);
            PopulatePage3(null);
            PopulatePage4(null);
        }
    }

    private void OnDestroy()
    {
        if (prevButton != null)
            prevButton.onClick.RemoveListener(OnPrevClicked);

        if (nextButton != null)
            nextButton.onClick.RemoveListener(OnNextClicked);
    }

    // ─────────────────────────────────────────────────────────────
    // Navigation
    // ─────────────────────────────────────────────────────────────

    private void OnPrevClicked()
    {
        if (pages == null || pages.Length == 0) return;

        _currentPageIndex--;
        if (_currentPageIndex < 0)
            _currentPageIndex = 0;

        RefreshPageVisibility();
        RefreshNavigationUI();
    }

    private void OnNextClicked()
    {
        if (pages == null || pages.Length == 0) return;

        _currentPageIndex++;
        if (_currentPageIndex >= pages.Length)
            _currentPageIndex = pages.Length - 1;

        RefreshPageVisibility();
        RefreshNavigationUI();
    }

    private void RefreshPageVisibility()
    {
        if (pages == null) return;

        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i] != null)
                pages[i].SetActive(i == _currentPageIndex);
        }
    }

    private void RefreshNavigationUI()
    {
        int totalPages   = (pages != null) ? pages.Length : 0;
        int displayIndex = _currentPageIndex + 1;

        if (pageLabelText != null)
            pageLabelText.text = $"PAGE {displayIndex} / {totalPages}";

        if (dotsText != null)
            dotsText.text = BuildDotsString(totalPages, _currentPageIndex);

        if (prevButton != null)
            prevButton.interactable = _currentPageIndex > 0;

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
            if (i < totalPages - 1)
                sb.Append(' ');
        }
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // PAGE 1 – APPLY SNAPSHOT TO UI
    // ─────────────────────────────────────────────────────────────

    private void PopulatePage1(PlayerDossierSnapshot stats)
    {
        if (stats == null)
        {
            if (handlerNameText != null) handlerNameText.text = "Handler: BRN Operator";
            if (rankText != null)        rankText.text = "Rank: Trainee";
            if (operationIdText != null) operationIdText.text = "Operation ID: BRN-0000-XXXX";

            if (totalBitlingsText != null)
                totalBitlingsText.text = "Total Bitlings Managed:   0";

            if (discoveredSpeciesText != null)
                discoveredSpeciesText.text = "Discovered Species:       0";

            if (avgLevelText != null)
                avgLevelText.text = "Average Bitling Level:     0";

            if (shinyCountText != null)
                shinyCountText.text = "Shiny Bitlings:            0";

            if (careScoreFillImage != null)
            {
                if (careScoreFillImage.type == Image.Type.Filled)
                    careScoreFillImage.fillAmount = 0f;
                else
                {
                    var rt = careScoreFillImage.rectTransform;
                    rt.anchorMax = new Vector2(0f, rt.anchorMax.y);
                }
            }

            if (careScoreValueText != null) careScoreValueText.text = "0%";
            if (careScoreNoteText != null)  careScoreNoteText.text = "BRN notes: No data available.";
            return;
        }

        if (handlerNameText != null)
            handlerNameText.text = stats.handlerName;

        if (rankText != null)
            rankText.text = stats.rankName;

        if (operationIdText != null)
            operationIdText.text = stats.operationId;

        if (totalBitlingsText != null)
            totalBitlingsText.text = $"Total Bitlings Managed:   {stats.totalOwnedBitlings}";

        if (discoveredSpeciesText != null)
            discoveredSpeciesText.text = $"Discovered Species:       {stats.discoveredSpecies}";

        if (avgLevelText != null)
            avgLevelText.text = $"Average Bitling Level:     {stats.averageLevel:0}";

        if (shinyCountText != null)
            shinyCountText.text = $"Shiny Bitlings:            {stats.shinyOwned}";

        float normalized = Mathf.Clamp01(stats.careScorePercent / 100f);

        if (careScoreFillImage != null)
        {
            if (careScoreFillImage.type == Image.Type.Filled)
            {
                careScoreFillImage.fillAmount = normalized;
            }
            else
            {
                var rt = careScoreFillImage.rectTransform;
                rt.anchorMax = new Vector2(normalized, rt.anchorMax.y);
            }
        }

        if (careScoreValueText != null)
            careScoreValueText.text = $"{stats.careScorePercent:0}%";

        if (careScoreNoteText != null)
            careScoreNoteText.text = stats.careScoreNote;
    }

    // ─────────────────────────────────────────────────────────────
    // PAGE 2 – JOB PANELS
    // ─────────────────────────────────────────────────────────────

    private void PopulatePage2(PlayerDossierSnapshot stats)
    {
        if (jobPanelParent == null || jobPanelPrefab == null)
            return;

        if (stats == null || stats.jobSites == null || stats.jobSites.Length == 0)
        {
            // Hide any existing panels
            for (int i = 0; i < _jobPanels.Count; i++)
                _jobPanels[i].gameObject.SetActive(false);

            return;
        }

        int needed = stats.jobSites.Length;

        // Ensure we have enough instances
        while (_jobPanels.Count < needed)
        {
            var panel = Instantiate(jobPanelPrefab, jobPanelParent);
            _jobPanels.Add(panel);
        }

        // Bind or hide
        for (int i = 0; i < _jobPanels.Count; i++)
        {
            if (i < needed)
            {
                _jobPanels[i].Bind(stats.jobSites[i]);
            }
            else
            {
                _jobPanels[i].gameObject.SetActive(false);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // PAGE 3 – FIELD OPERATIONS
    // ─────────────────────────────────────────────────────────────

    private void PopulatePage3(PlayerDossierSnapshot stats)
    {
        if (stats == null)
        {
            if (encountersInitiatedText != null)
                encountersInitiatedText.text = "Encounters Initiated:   0";

            if (captureSuccessRateText != null)
                captureSuccessRateText.text = "Capture Success Rate:   0%";

            if (riftStabilizationsText != null)
                riftStabilizationsText.text = "Rift Stabilizations:    0";

            if (rareBitlingsFoundText != null)
                rareBitlingsFoundText.text = "Rare Bitlings Found:    0";

            if (shinyDiscoveriesText != null)
                shinyDiscoveriesText.text = "Shiny Discoveries:      0";

            if (longestCaptureStreakText != null)
                longestCaptureStreakText.text = "Longest Capture Streak: 0";

            if (fieldOpsHighlightsText != null)
                fieldOpsHighlightsText.text = "Recent Highlights:\n—";

            return;
        }

        if (encountersInitiatedText != null)
            encountersInitiatedText.text =
                $"Encounters Initiated:   {stats.encountersInitiated}";

        if (captureSuccessRateText != null)
            captureSuccessRateText.text =
                $"Capture Success Rate:   {stats.captureSuccessRate}%";

        if (riftStabilizationsText != null)
            riftStabilizationsText.text =
                $"Rift Stabilizations:    {stats.riftStabilizations}";

        if (rareBitlingsFoundText != null)
            rareBitlingsFoundText.text =
                $"Rare Bitlings Found:    {stats.rareBitlingsFound}";

        if (shinyDiscoveriesText != null)
            shinyDiscoveriesText.text =
                $"Shiny Discoveries:      {stats.shinyDiscoveries}";

        if (longestCaptureStreakText != null)
            longestCaptureStreakText.text =
                $"Longest Capture Streak: {stats.longestCaptureStreak}";

        if (fieldOpsHighlightsText != null)
        {
            if (stats.fieldOpsHighlights == null || stats.fieldOpsHighlights.Length == 0)
            {
                fieldOpsHighlightsText.text = "—";
            }
            else
            {
                fieldOpsHighlightsText.text = string.Join("\n", stats.fieldOpsHighlights);
            }
        }
    }

    private void PopulatePage4(PlayerDossierSnapshot s)
    {
        if (s == null)
        {
            if (coinsText)          coinsText.text          = "0";
            if (energyText)         energyText.text         = "0";
            if (medkitText)         medkitText.text         = "0";
            if (materialText)       materialText.text       = "0";
            if (typeResBoosterText) typeResBoosterText.text = "0";
            if (lureText)           lureText.text           = "0";
            if (captureBandText)    captureBandText.text    = "0";
            if (luckText)           luckText.text           = "0";
            if (atkBoosterText)     atkBoosterText.text     = "0";
            if (hpBoosterText)      hpBoosterText.text      = "0";
            if (speedBoosterText)   speedBoosterText.text   = "0";
            if (shinyOrbText)       shinyOrbText.text       = "0";
            if (blessingScaleText)  blessingScaleText.text  = "0";
            if (restChargeText)     restChargeText.text     = "0";
            if (growthCoreText)     growthCoreText.text     = "0";
            if (packShardText)      packShardText.text      = "0";

            if (efficiencyPercentText) efficiencyPercentText.text = "0%";
            if (efficiencyFill)
            {
                var rt = efficiencyFill.rectTransform;
                rt.anchorMax = new Vector2(0f, rt.anchorMax.y);
            }
            if (brnRatingText) brnRatingText.text = "BRN Rating: Stable Operator";
            return;
        }

        if (coinsText)          coinsText.text          = s.coinCount.ToString("N0");
        if (energyText)         energyText.text         = s.energyCount.ToString();
        if (medkitText)         medkitText.text         = s.medkitCount.ToString();
        if (materialText)       materialText.text       = s.materialCount.ToString("N0");
        if (typeResBoosterText) typeResBoosterText.text = s.typeResBoosterCount.ToString();
        if (lureText)           lureText.text           = s.lureCount.ToString();
        if (captureBandText)    captureBandText.text    = s.captureBandCount.ToString();
        if (luckText)           luckText.text           = s.luckCount.ToString();
        if (atkBoosterText)     atkBoosterText.text     = s.atkBoosterCount.ToString();
        if (hpBoosterText)      hpBoosterText.text      = s.hpBoosterCount.ToString();
        if (speedBoosterText)   speedBoosterText.text   = s.speedBoosterCount.ToString();
        if (shinyOrbText)       shinyOrbText.text       = s.shinyOrbCount.ToString();
        if (blessingScaleText)  blessingScaleText.text  = s.blessingScaleCount.ToString();
        if (restChargeText)     restChargeText.text     = s.restChargeCount.ToString();
        if (growthCoreText)     growthCoreText.text     = s.growthCoreCount.ToString();
        if (packShardText)      packShardText.text      = s.packShardCount.ToString();

        // Efficiency bar + % + rating stay the same as before...
        float normalized = Mathf.Clamp01(s.conversionEfficiencyPercent / 100f);

        if (efficiencyPercentText)
            efficiencyPercentText.text = $"{s.conversionEfficiencyPercent}%";

        if (efficiencyFill)
        {
            var rt = efficiencyFill.rectTransform;
            rt.anchorMax = new Vector2(normalized, rt.anchorMax.y);
        }

        if (brnRatingText)
        {
            int eff = s.conversionEfficiencyPercent;
            string rating =
                eff >= 70 ? "BRN Rating: Critical Asset" :
                eff >= 40 ? "BRN Rating: High-Performance Handler" :
                            "BRN Rating: Stable Operator";

            brnRatingText.text = rating;
        }
    }



}
