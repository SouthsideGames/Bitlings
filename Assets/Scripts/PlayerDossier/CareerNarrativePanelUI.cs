using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CareerNarrativePanelUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private CanvasGroup panelCanvasGroup;
    [SerializeField] private Image portrait;
    [SerializeField] private Image typeIcon;
    [SerializeField] private TextMeshProUGUI namePlate;
    [SerializeField] private TextMeshProUGUI narrativeText;
    [SerializeField] private Button narrativeTapSkipButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button honorButton;

    [Header("Stat Chips")]
    [SerializeField] private StatChipUI battlesChip;
    [SerializeField] private StatChipUI winRateChip;
    [SerializeField] private StatChipUI jobHoursChip;
    [SerializeField] private StatChipUI riftsChip;
    [SerializeField] private StatChipUI daysChip;

    [Header("Legacy")]
    [SerializeField] private GameObject legacyRowRoot;
    [SerializeField] private TextMeshProUGUI legacyText;

    [Header("Lookups")]
    [SerializeField] private TypeIconLibrary typeIconLibrary;

    private string _mentorUid;
    private Coroutine _revealCo;

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);

        if (honorButton != null)
            honorButton.onClick.AddListener(ApplyHonor);

        if (narrativeTapSkipButton != null)
            narrativeTapSkipButton.onClick.AddListener(SkipReveal);

        gameObject.SetActive(false);
    }

    public void Show(string mentorUID)
    {
        if (!SaveManager.TryGetMentorRecord(mentorUID, out var mentor) || mentor == null)
            return;

        _mentorUid = mentorUID;

        var stats = mentor.lifetimeStatsSnapshot;
        if (!string.IsNullOrEmpty(mentor.ownedUID) && SaveManager.TryGetStats(mentor.ownedUID, out var liveStats))
            stats = liveStats;

        var def = !string.IsNullOrEmpty(mentor.monsterId) ? MonsterLibraryLocator.GetById(mentor.monsterId) : null;
        if (portrait != null) portrait.sprite = def != null ? def.icon : null;
        if (typeIcon != null) typeIcon.sprite = typeIconLibrary != null ? typeIconLibrary.GetIcon(mentor.monsterType) : null;

        string baseName = string.IsNullOrWhiteSpace(mentor.displayName) ? "Unknown" : mentor.displayName;
        bool hasEpithet = !string.IsNullOrWhiteSpace(mentor.epithet) && mentor.driftTier > 0;
        if (namePlate != null)
            namePlate.text = hasEpithet ? (baseName + " the " + mentor.epithet) : baseName;

        string narrative = CareerNarrativeGenerator.GenerateNarrative(mentor, stats);
        if (narrativeText != null)
        {
            narrativeText.text = narrative;
            narrativeText.maxVisibleCharacters = 0;
        }

        BindChips(stats);
        BindLegacy(stats);

        if (honorButton != null)
            honorButton.gameObject.SetActive(HonorService.CanHonor(mentorUID));

        gameObject.SetActive(true);
        PlayOpen();

        if (_revealCo != null)
            StopCoroutine(_revealCo);
        _revealCo = StartCoroutine(RevealNarrative());
    }

    public void Hide()
    {
        if (!gameObject.activeSelf) return;
        StartCoroutine(PlayClose());
    }

    private void ApplyHonor()
    {
        if (string.IsNullOrEmpty(_mentorUid)) return;
        string err = HonorService.HonorLegend(_mentorUid);
        if (!string.IsNullOrEmpty(err))
        {
            GameEvents.RaiseToast(err);
            return;
        }

        if (honorButton != null)
            honorButton.gameObject.SetActive(false);
    }

    private void SkipReveal()
    {
        if (narrativeText == null) return;
        if (_revealCo != null)
        {
            StopCoroutine(_revealCo);
            _revealCo = null;
        }

        narrativeText.ForceMeshUpdate();
        narrativeText.maxVisibleCharacters = narrativeText.textInfo.characterCount;
    }

    private void PlayOpen()
    {
        if (panelRoot == null || panelCanvasGroup == null)
            return;

        StopAllCoroutines();
        StartCoroutine(AnimateOpen());
    }

    private IEnumerator AnimateOpen()
    {
        float t = 0f;
        Vector2 target = Vector2.zero;
        Vector2 start = new Vector2(0f, -Screen.height);

        panelRoot.anchoredPosition = start;
        panelCanvasGroup.alpha = 0f;

        while (t < 0.3f)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / 0.3f);
            panelRoot.anchoredPosition = Vector2.Lerp(start, target, p);
            panelCanvasGroup.alpha = p;
            yield return null;
        }

        panelRoot.anchoredPosition = target;
        panelCanvasGroup.alpha = 1f;
    }

    private IEnumerator PlayClose()
    {
        if (panelRoot == null || panelCanvasGroup == null)
        {
            gameObject.SetActive(false);
            yield break;
        }

        float t = 0f;
        Vector2 start = panelRoot.anchoredPosition;
        Vector2 target = new Vector2(0f, -Screen.height);

        while (t < 0.25f)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / 0.25f);
            panelRoot.anchoredPosition = Vector2.Lerp(start, target, p);
            panelCanvasGroup.alpha = 1f - p;
            yield return null;
        }

        gameObject.SetActive(false);
    }

    private IEnumerator RevealNarrative()
    {
        if (narrativeText == null)
            yield break;

        narrativeText.ForceMeshUpdate();
        int total = narrativeText.textInfo.characterCount;
        if (total <= 0)
            yield break;

        float duration = 1.5f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / duration);
            narrativeText.maxVisibleCharacters = Mathf.RoundToInt(total * p);
            yield return null;
        }

        narrativeText.maxVisibleCharacters = total;
    }

    private void BindChips(LifetimeMonsterStats stats)
    {
        stats ??= new LifetimeMonsterStats();

        int battles = Mathf.Max(0, stats.lifetimeBattles);
        int winPct = battles > 0 ? (stats.lifetimeWins * 100) / battles : 0;
        int days = 0;
        if (stats.firstCaptureUnix > 0 && stats.retiredAtUnix > 0)
            days = Mathf.Max(0, (int)((stats.retiredAtUnix - stats.firstCaptureUnix) / 86400L));

        if (battlesChip != null) battlesChip.Bind(null, "Battles", battles.ToString("N0"));
        if (winRateChip != null) winRateChip.Bind(null, "Win Rate", winPct + "%");
        if (jobHoursChip != null) jobHoursChip.Bind(null, "Job Hours", Mathf.RoundToInt(stats.lifetimeJobHours).ToString("N0") + " hrs");
        if (riftsChip != null) riftsChip.Bind(null, "Rifts", stats.riftsCompleted.ToString("N0"));
        if (daysChip != null) daysChip.Bind(null, "Days", days.ToString("N0"));
    }

    private void BindLegacy(LifetimeMonsterStats stats)
    {
        bool hasHeir = stats != null && !string.IsNullOrEmpty(stats.willRecipientUID);

        if (legacyRowRoot != null)
            legacyRowRoot.SetActive(hasHeir);

        if (!hasHeir || legacyText == null)
            return;

        string heir = string.IsNullOrWhiteSpace(stats.willRecipientName) ? "Unknown" : stats.willRecipientName;
        legacyText.text = "Legacy: passed " + stats.willType + " to " + heir + ".";
    }
}
