using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OwnedMonsterListItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI idText;
    [SerializeField] private Button rootButton;
    [SerializeField] private TextMeshProUGUI cooldownText;

    [Header("Evolution Alert")]
    [SerializeField] private GameObject evolveAlert;

    [Header("Detail Panel (Assign Mode)")]
    [SerializeField] private MonsterDetailPanelUI detailPanel;

    private OwnedMonsterData _data;
    private MonsterDataSO _def;

    private float _nextUiRefreshAt;

    void Awake()
    {
        if (detailPanel == null)
            detailPanel = FindAnyObjectByType<MonsterDetailPanelUI>(FindObjectsInactive.Include);

        if (cooldownText) cooldownText.gameObject.SetActive(false);

        if (rootButton)
        {
            rootButton.onClick.RemoveAllListeners();
            rootButton.onClick.AddListener(OnClickOpenDetails);
        }

        if (evolveAlert)
            evolveAlert.SetActive(false);

        GameEvents.MonsterLeveled -= HandleMonsterLeveled;
        GameEvents.MonsterLeveled += HandleMonsterLeveled;
    }

    private void OnDestroy()
    {
        GameEvents.MonsterLeveled -= HandleMonsterLeveled;
    }

    void OnDisable()
    {
        _nextUiRefreshAt = 0f;
        if (cooldownText) cooldownText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!HasValidMonster(_data)) return;
        if (IsUsable(_data)) return;

        if (Time.unscaledTime >= _nextUiRefreshAt)
        {
            _nextUiRefreshAt = Time.unscaledTime + 1f;
            UpdateKOCountdown();
        }
    }

    public void Setup(OwnedMonsterData data)
    {
        var def = HasValidMonster(data) ? MonsterLibraryLocator.GetById(data.monsterId) : null;
        Setup(data, def);
    }

    public void Setup(OwnedMonsterData data, MonsterDataSO def)
    {
        _data = data;
        _def  = def;

        if (icon)
        {
            if (def && def.icon)
            {
                icon.enabled = true;
                icon.sprite  = def.icon;
            }
            else
            {
                icon.enabled = false;
                icon.sprite  = null;
            }
        }

        if (nameText)
            nameText.text = def
                ? (string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName)
                : "Unknown";

        if (idText)
            idText.text = HasValidMonster(data) ? data.monsterId : "—";

        ApplyState();

        _nextUiRefreshAt = 0f;
        if (!IsUsable(_data)) UpdateKOCountdown();

        RefreshEvolutionAlert();
    }

    public void SetInteractable(bool on)
    {
        if (rootButton) rootButton.interactable = on && IsUsable(_data);
        ApplyKOVisualsOnly();
    }

    private void OnClickOpenDetails()
    {
        if (detailPanel == null)
        {
            Debug.LogWarning("[OwnedMonsterListItemUI] MonsterDetailPanelUI not found in scene.");
            return;
        }

        if (!HasValidMonster(_data)) return;
        if (!IsUsable(_data)) return;

        AudioManager.I.PlayClick();

        detailPanel.ShowAssign(_data);
    }

    private void ApplyState()
    {
        if (rootButton) rootButton.interactable = HasValidMonster(_data) && IsUsable(_data);

        ApplyKOVisualsOnly();
        if (!IsUsable(_data)) UpdateKOCountdown();

        RefreshEvolutionAlert();
    }

    private void ApplyKOVisualsOnly()
    {
        bool isKO = HasValidMonster(_data) && !IsUsable(_data);

        if (cooldownText)
            cooldownText.gameObject.SetActive(isKO);
    }

    private void UpdateKOCountdown()
    {
        if (!cooldownText) return;
        if (!HasValidMonster(_data)) { cooldownText.gameObject.SetActive(false); return; }
        if (IsUsable(_data))        { cooldownText.gameObject.SetActive(false); return; }

        var (ok, eta) = TryGetETAForNextHP(_data, _def);
        cooldownText.gameObject.SetActive(true);
        cooldownText.text = ok ? FormatETA(eta) : "Healing…";
    }

    private void RefreshEvolutionAlert()
    {
        if (!evolveAlert) return;

        var def = _def;
        if (def == null && HasValidMonster(_data))
        {
            def = MonsterLibraryLocator.GetById(_data.monsterId);
            _def = def;
        }

        bool show = false;
        if (_data != null && def != null)
            show = EvolutionHelper.CanEvolve(_data, def);

        evolveAlert.SetActive(show);
    }

    private static bool HasValidMonster(OwnedMonsterData d)
    {
        return d != null && !string.IsNullOrEmpty(d.monsterId);
    }

    private static bool IsUsable(OwnedMonsterData d)
    {
        return HasValidMonster(d) && d.currentHP != 0;
    }

    private static (bool ok, TimeSpan eta) TryGetETAForNextHP(OwnedMonsterData d, MonsterDataSO def)
    {
        if (!HasValidMonster(d)) return (false, TimeSpan.Zero);

        float perHour = 0f;
        if (def && def.hpRegenPerHour > 0f)
            perHour = def.hpRegenPerHour;
        else
            perHour = HealthRegenSystem.GetDefaultRegenPerHour();

        if (perHour <= 0.0001f) return (false, TimeSpan.Zero);

        int secondsPerHP = Mathf.CeilToInt(3600f / perHour);

        long now  = SaveManager.NowUnix();
        long last = d.lastHPUnix > 0 ? d.lastHPUnix : now;
        long elapsed = Math.Max(0, now - last);

        int remain = Mathf.Clamp(secondsPerHP - (int)elapsed, 1, secondsPerHP);
        return (true, TimeSpan.FromSeconds(remain));
    }

    private static string FormatETA(TimeSpan span)
    {
        if (span.TotalHours >= 1.0)
            return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        return $"{span.Minutes:D2}:{span.Seconds:D2}";
    }

    private void HandleMonsterLeveled(string ownedIdOrDefId, int newLevel)
    {
        if (_data == null)
            return;

        string myKey = !string.IsNullOrEmpty(_data.ownedUID)
            ? _data.ownedUID
            : _data.monsterId;

        if (myKey != ownedIdOrDefId)
            return;

        _data.level = newLevel;

        ApplyState();
    }
}
