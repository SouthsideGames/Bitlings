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

    [Tooltip("Countdown text that shows when a KO'd monster will be usable again.")]
    [SerializeField] private TextMeshProUGUI cooldownText;

    [Header("Detail Panel (Assign Mode)")]
    [SerializeField] private MonsterDetailPanelUI detailPanel;

    // data
    private OwnedMonsterData _data;
    private MonsterDataSO _def;

    // refresh cadence for countdown (seconds)
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
    }

    void OnDisable()
    {
        // stop any residual countdown flicker when pooled/disabled
        _nextUiRefreshAt = 0f;
        if (cooldownText) cooldownText.gameObject.SetActive(false);
    }

    void Update()
    {
        // While KO'd, update the countdown ~1Hz
        if (!HasValidMonster(_data)) return;
        if (IsUsable(_data)) return;

        if (Time.unscaledTime >= _nextUiRefreshAt)
        {
            _nextUiRefreshAt = Time.unscaledTime + 1f;
            UpdateKOCountdown();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────
    public void Setup(OwnedMonsterData data)
    {
        var def = HasValidMonster(data) ? MonsterLibraryLocator.GetById(data.monsterId) : null;
        Setup(data, def);
    }

    public void Setup(OwnedMonsterData data, MonsterDataSO def)
    {
        _data = data;
        _def  = def;

        // Icon
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

        // Text
        if (nameText)
            nameText.text = def
                ? (string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName)
                : "Unknown";

        if (idText)
            idText.text = HasValidMonster(data) ? data.monsterId : "—";

        // Apply unified state (interactable + visuals + countdown)
        ApplyState();

        // Force an immediate countdown render when KO
        _nextUiRefreshAt = 0f;
        if (!IsUsable(_data)) UpdateKOCountdown();
    }

    /// <summary>Manually toggle base interactivity (still respects KO lock).</summary>
    public void SetInteractable(bool on)
    {
        if (rootButton) rootButton.interactable = on && IsUsable(_data);
        ApplyKOVisualsOnly(); // keep visuals consistent
    }

    // ─────────────────────────────────────────────────────────────
    // Click
    // ─────────────────────────────────────────────────────────────
    private void OnClickOpenDetails()
    {
        if (detailPanel == null)
        {
            Debug.LogWarning("[OwnedMonsterListItemUI] MonsterDetailPanelUI not found in scene.");
            return;
        }

        if (!HasValidMonster(_data)) return;
        if (!IsUsable(_data))
        {
            // Optional: Toast "Monster is KO’d — heal or wait for regen."
            return;
        }

        detailPanel.ShowAssign(_data);
    }

    // ─────────────────────────────────────────────────────────────
    // State/Visuals
    // ─────────────────────────────────────────────────────────────
    private void ApplyState()
    {
        // Button interactable gates everything through IsUsable
        if (rootButton) rootButton.interactable = HasValidMonster(_data) && IsUsable(_data);

        // Visuals / countdown
        ApplyKOVisualsOnly();
        if (!IsUsable(_data)) UpdateKOCountdown();
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

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────
    private static bool HasValidMonster(OwnedMonsterData d)
    {
        return d != null && !string.IsNullOrEmpty(d.monsterId);
    }

    private static bool IsUsable(OwnedMonsterData d)
    {
        // HP == 0 → KO (not usable)
        // HP  > 0 → usable
        // HP == -1 → uninitialized (treat as full/usable per team rules)
        return HasValidMonster(d) && d.currentHP != 0;
    }

    /// <summary>
    /// Calculates the ETA until +1 HP via passive regen.
    /// Mirrors HealthRegenSystem integer tick behavior.
    /// </summary>
    private static (bool ok, TimeSpan eta) TryGetETAForNextHP(OwnedMonsterData d, MonsterDataSO def)
    {
        if (!HasValidMonster(d)) return (false, TimeSpan.Zero);

        float perHour = 0f;
        if (def && def.hpRegenPerHour > 0f)
            perHour = def.hpRegenPerHour;
        else
            perHour = HealthRegenSystem.GetDefaultRegenPerHour(); // accessor provided in HealthRegenSystem

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
}
