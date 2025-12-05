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
        }
        else
        {
            Debug.LogWarning("[PlayerDossierPanelUI] No PlayerDossierManager found in scene.");
            PopulatePage1(null);
            PopulatePage2(null);
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
}
