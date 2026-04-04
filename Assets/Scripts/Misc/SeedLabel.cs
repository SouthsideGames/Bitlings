using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class SeedLabel : MonoBehaviour, IPointerClickHandler
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI seedLabel;

    [Header("Format")]
    [SerializeField] private string prefix = "SEED:";
    [SerializeField] private bool includeModeTag = false;
    [SerializeField] private bool copyFullText = true;

    [Header("Copy Feedback (optional)")]
    [SerializeField] private bool logOnCopy = true;
    [SerializeField] private string copiedMessage = "Seed copied.";

    private FeatureUnlockManager _subscribedTo;
    private bool _subscribeChecked;

    void OnEnable()
    {
        _subscribeChecked = false;
        TrySubscribe();
        Refresh();
    }

    void OnDisable()
    {
        Unsubscribe();
        _subscribeChecked = false;
    }

    void Update()
    {
        if (_subscribeChecked) return;
        if (FeatureUnlockManager.I == null) return;
        _subscribeChecked = true;
        TrySubscribe();
        Refresh();
    }

    private void TrySubscribe()
    {
        var fu = FeatureUnlockManager.I;
        if (fu == null) return;

        if (_subscribedTo == fu)
            return;

        Unsubscribe();

        fu.OnFeatureUnlocked += HandleFeatureUnlocked;
        _subscribedTo = fu;
    }

    private void Unsubscribe()
    {
        if (_subscribedTo != null)
        {
            _subscribedTo.OnFeatureUnlocked -= HandleFeatureUnlocked;
            _subscribedTo = null;
        }
    }

    private void HandleFeatureUnlocked(FeatureId feature)
    {
        if (feature == FeatureId.Seeds_DailyBasic ||
            feature == FeatureId.Seeds_CustomInput ||
            feature == FeatureId.Seeds_RerollDailyOnce)
        {
            Refresh();
        }
    }

    public void Refresh()
    {
        if (seedLabel == null)
            return;

        var fu = FeatureUnlockManager.I;
        bool dailyUnlocked = fu != null && fu.IsUnlocked(FeatureId.Seeds_DailyBasic);

        seedLabel.gameObject.SetActive(dailyUnlocked);
        if (!dailyUnlocked)
            return;

        SeedService.ApplyGlobalSeedForSession();

        string shownPrefix = string.IsNullOrWhiteSpace(prefix)
            ? SeedService.GetDisplaySeedPrefix()
            : prefix;
        string token = SeedService.GetDisplaySeedToken();

        if (string.IsNullOrWhiteSpace(token))
        {
            seedLabel.text = $"{shownPrefix} ----";
            return;
        }

        if (includeModeTag)
        {
            string tag = SeedService.ActiveMode.ToString().ToUpperInvariant();
            seedLabel.text = $"{shownPrefix} {token} ({tag})";
        }
        else
        {
            seedLabel.text = $"{shownPrefix} {token}";
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (seedLabel == null || !seedLabel.gameObject.activeInHierarchy)
            return;

        SeedService.ApplyGlobalSeedForSession();

        string shownPrefix = SeedService.GetDisplaySeedPrefix();
        string token = SeedService.GetDisplaySeedToken();

        if (string.IsNullOrWhiteSpace(token))
            return;

        string toCopy = copyFullText ? $"{shownPrefix} {token}" : token;

        GUIUtility.systemCopyBuffer = toCopy;

        if (logOnCopy)
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            DevLog.Log($"[SeedLabel] {copiedMessage} ({toCopy})");
            #endif

        GameEvents.RaiseToast(copiedMessage);
    }
}