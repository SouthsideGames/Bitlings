using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class EncounterPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Button encounterBtn;
    [SerializeField] private TextMeshProUGUI energyLabel;
    [SerializeField] private TextMeshProUGUI energyEtaLabel;
    [SerializeField, Min(1f)] private float energySecondsPerPoint = 1200f;


    // ─────────────────────────────────────────────────────────────
    // Blinder (localized + weighted random)
    // ─────────────────────────────────────────────────────────────
    [Header("Blinder")]
    [SerializeField] private CanvasGroup blinderGroup;
    [SerializeField] private TextMeshProUGUI blinderText;
    [SerializeField, Range(0.05f, 1.5f)] private float preFadeDelay = 0.25f;
    [SerializeField, Range(0.1f, 2.0f)] private float fadeDuration = 0.6f;

    [Header("Blinder Localization & Random")]
    [SerializeField] private bool useRandomBlinder = true;
    [SerializeField] private string preferredLanguageCode = "";
    [SerializeField] private BlinderMessagePackSO overridePack;
    [SerializeField] private BlinderMessageLibrarySO blinderLibrary;
    [SerializeField] private string hardFallbackLine = "I WONDER WHAT WE WILL ENCOUNTER";

    string _lastBlinderLine = null;

    // ─────────────────────────────────────────────────────────────
    // Team Preview
    // ─────────────────────────────────────────────────────────────
    [Header("Team Preview")]
    [SerializeField] private Transform teamPreviewRoot;
    [SerializeField] private TeamPreviewItemUI teamItemPrefab;
    [SerializeField, Range(1, 12)] private int maxTeamShown = 6;
    [SerializeField, Range(0f, 0.25f)] private float teamStaggerFade = 0.05f;

    private TextMeshProUGUI encounterLabel;
    float _etaTickAccum = 0f;
    bool _isFading;
    Coroutine _fadeCo;
    readonly List<TeamPreviewItemUI> _previewItems = new();

    void Awake()
    {
        encounterLabel = encounterBtn ? encounterBtn.GetComponentInChildren<TextMeshProUGUI>() : null;

        if (blinderGroup)
        {
            blinderGroup.alpha = 1f;
            blinderGroup.blocksRaycasts = true;
            blinderGroup.interactable = true;
        }

        // Initial line (random or fallback)
        PickAndApplyBlinderLine(forcePick: true);
    }

    void OnEnable()
    {
        if (encounterBtn)
        {
            encounterBtn.onClick.RemoveAllListeners();
            encounterBtn.onClick.AddListener(OnClickEncounter);
        }

        if (EncounterManager.I != null)
        {
            EncounterManager.I.OnStateChanged += OnEncounterStateChanged;
            GameEvents.EnergyChanged += RefreshEnergy;
        }

        // Log the current win streak immediately on enable
        LogCurrentWinStreak("Status");

        // Subscribe to streak changes → log to BattleLogger
        GameEvents.WinStreakChanged += OnWinStreakChanged;

        if (!IsInBattle())
        {
            ShowBlinder(true, instant: true);
            BuildTeamPreview();
            PickAndApplyBlinderLine(); // fresh line when shown
        }
        else
        {
            ShowBlinder(false, instant: true);
            ClearTeamPreview();
        }

        RefreshAll();

        GameEvents.BattleFinished += OnBattleFinished;
        UpdateEnergyEtaUI(); // seed ETA
    }

    void OnDisable()
    {
        if (EncounterManager.I != null)
        {
            EncounterManager.I.OnStateChanged -= OnEncounterStateChanged;
            GameEvents.EnergyChanged -= RefreshEnergy;
        }

        GameEvents.BattleFinished -= OnBattleFinished;
        GameEvents.WinStreakChanged -= OnWinStreakChanged;

        if (encounterBtn) encounterBtn.onClick.RemoveAllListeners();
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        _isFading = false;
    }

    void Update()
    {
        _etaTickAccum += Time.unscaledDeltaTime;
        if (_etaTickAccum >= 1f)
        {
            _etaTickAccum = 0f;
            UpdateEnergyEtaUI();
        }
    }

    public void RefreshAll()
    {
        RefreshButtonAndLabel();
        RefreshEnergy();
    }

    // Win streak → Battle Log
    void OnWinStreakChanged(int value)
    {
        BattleLogger.Log($"Win Streak: {value}", LogScope.Encounter);
    }

    void LogCurrentWinStreak(string prefix = null)
    {
        int streak = (EncounterManager.I != null) ? EncounterManager.I.CurrentWinStreak : 0;
        string label = string.IsNullOrEmpty(prefix) ? "Win Streak" : $"{prefix} • Win Streak";
        BattleLogger.Log($"{label}: {streak}", LogScope.Encounter);
    }

    void OnBattleFinished(BattleResult _)
    {
        // After every battle, log the new streak value.
        LogCurrentWinStreak("Updated");

        if (!IsInBattle())
        {
            ShowBlinder(true, instant: true);
            BuildTeamPreview();
            PickAndApplyBlinderLine();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Button/Energy/ETA
    // ─────────────────────────────────────────────────────────────
    void RefreshButtonAndLabel()
    {
        bool nextFree = NextEncounterIsFree();
        bool auto     = IsAutoMode();
        bool inBattle = IsInBattle();

        bool canManualStart = (nextFree || HasEnergy());
        bool interactable =
            !inBattle &&
            !auto &&
            canManualStart &&
            !_isFading;

        if (encounterBtn) encounterBtn.interactable = interactable;

        if (encounterLabel)
        {
            if (auto) encounterLabel.text = "AUTO: ON";
            else if (nextFree) encounterLabel.text = "NEXT";
            else encounterLabel.text = "ENCOUNTER";
        }
    }

    void RefreshEnergy()
    {
        if (!energyLabel) return;

        int cur = GetEnergyPoints();
        int max = GetEncounterMax();
        bool has = cur >= GetEncounterCost();

        energyLabel.text = $"{cur} / {max}";
        energyLabel.color = has ? Color.white : new Color(1f, 0.5f, 0.5f);

        UpdateEnergyEtaUI();
    }

    void UpdateEnergyEtaUI()
    {
        if (!energyEtaLabel) return;

        int cur  = GetEnergyPoints();
        int max  = GetEncounterMax();

        if (cur >= max)
        {
            energyEtaLabel.text = "Energy full";
            return;
        }

        int seconds = EncounterManager.I != null
            ? EncounterManager.I.GetSecondsUntilFull()
            : (int)((max - cur) * Mathf.Max(1f, energySecondsPerPoint)); // fallback

        int hours = seconds / 3600;
        int minutes = (seconds % 3600) / 60;

        energyEtaLabel.text = (hours > 0)
            ? $"Full in ~ {hours}h {minutes:D2}m"
            : $"Full in ~ {minutes}m";
    }

    // ─────────────────────────────────────────────────────────────
    // Blinder Flow
    // ─────────────────────────────────────────────────────────────
    void OnEncounterStateChanged()
    {
        if (IsInBattle())
        {
            ShowBlinder(false, instant: true);
            ClearTeamPreview();
        }
        else
        {
            RefreshAll();
            ShowBlinder(true, instant: true);
            BuildTeamPreview();
            PickAndApplyBlinderLine(); // fresh line when returning from battle
        }
    }

    void OnClickEncounter()
    {
        if (_isFading) return;

        if (blinderGroup && blinderGroup.alpha > 0.01f)
        {
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(Co_FadeOutBlinderThenStartEncounter());
        }
        else
        {
            RequestEncounterTap();
        }
    }

    IEnumerator Co_FadeOutBlinderThenStartEncounter()
    {
        _isFading = true;
        RefreshButtonAndLabel();

        if (blinderText) blinderText.raycastTarget = true;
        if (blinderGroup)
        {
            blinderGroup.blocksRaycasts = true;
            blinderGroup.interactable = true;
        }

        yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, preFadeDelay));

        if (blinderGroup)
        {
            float t = 0f;
            float dur = Mathf.Max(0.1f, fadeDuration);
            float start = blinderGroup.alpha;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                blinderGroup.alpha = Mathf.Lerp(start, 0f, t / dur);
                yield return null;
            }
            blinderGroup.alpha = 0f;
            blinderGroup.blocksRaycasts = false;
            blinderGroup.interactable = false;
        }

        _isFading = false;
        RefreshButtonAndLabel();
        RequestEncounterTap();
    }

    void ShowBlinder(bool show, bool instant)
    {
        if (!blinderGroup) return;

        if (instant)
        {
            blinderGroup.alpha = show ? 1f : 0f;
            blinderGroup.blocksRaycasts = show;
            blinderGroup.interactable = show;

            if (show)
            {
                PickAndApplyBlinderLine();
                BuildTeamPreview();
            }
            else
            {
                ClearTeamPreview();
            }
        }
        else
        {
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(show ? Co_FadeTo(1f) : Co_FadeTo(0f));
        }
    }

    IEnumerator Co_FadeTo(float target)
    {
        _isFading = true;
        float start = blinderGroup.alpha;
        float dur = Mathf.Max(0.1f, fadeDuration);
        float t = 0f;

        blinderGroup.blocksRaycasts = true;
        blinderGroup.interactable = true;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            blinderGroup.alpha = Mathf.Lerp(start, target, t / dur);
            yield return null;
        }
        blinderGroup.alpha = target;

        blinderGroup.blocksRaycasts = target > 0.01f;
        blinderGroup.interactable = target > 0.01f;

        if (target > 0.01f)
        {
            PickAndApplyBlinderLine();
            BuildTeamPreview();
        }
        else
        {
            ClearTeamPreview();
        }

        _isFading = false;
        RefreshButtonAndLabel();
    }

    // ─────────────────────────────────────────────────────────────
    // Team Preview
    // ─────────────────────────────────────────────────────────────
    void BuildTeamPreview()
    {
        if (!teamPreviewRoot || !teamItemPrefab) return;

        ClearTeamPreview();

        var team = SafeTeamList();
        if (team == null || team.Count == 0) return;

        int shown = 0;
        for (int i = 0; i < team.Count && shown < maxTeamShown; i++)
        {
            var om = team[i];
            if (om == null || string.IsNullOrEmpty(om.monsterId)) continue;

            var item = Instantiate(teamItemPrefab, teamPreviewRoot);
            item.Bind(om);
            _previewItems.Add(item);
            shown++;
        }

        if (_previewItems.Count > 0 && blinderGroup && blinderGroup.alpha > 0.01f && teamStaggerFade > 0f)
            StartCoroutine(Co_FadeInTeamPreview());
        else
            SetTeamAlpha(1f);
    }

    void ClearTeamPreview()
    {
        for (int i = 0; i < _previewItems.Count; i++)
            if (_previewItems[i])
                Destroy(_previewItems[i].gameObject);
        _previewItems.Clear();
    }

    IEnumerator Co_FadeInTeamPreview()
    {
        SetTeamAlpha(0f);
        for (int i = 0; i < _previewItems.Count; i++)
        {
            var item = _previewItems[i];
            if (!item) continue;

            float t = 0f;
            const float dur = 0.15f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                item.SetAlpha(Mathf.Clamp01(t / dur));
                yield return null;
            }
            item.SetAlpha(1f);

            if (teamStaggerFade > 0f)
                yield return new WaitForSecondsRealtime(teamStaggerFade);
        }
    }

    void SetTeamAlpha(float a)
    {
        for (int i = 0; i < _previewItems.Count; i++)
            if (_previewItems[i]) _previewItems[i].SetAlpha(a);
    }

    List<OwnedMonsterData> SafeTeamList()
    {
        if (SaveManager.Data == null) return null;
        return SaveManager.Data.team;
    }

    // ─────────────────────────────────────────────────────────────
    // EncounterManager passthrough helpers (null-safe)
    // ─────────────────────────────────────────────────────────────
    bool IsInBattle()          => EncounterManager.I != null && EncounterManager.I.IsInBattle;
    bool IsAutoMode()          => EncounterManager.I != null && EncounterManager.I.IsAutoMode;
    bool NextEncounterIsFree() => EncounterManager.I != null && EncounterManager.I.NextEncounterIsFree;
    bool HasEnergy()           => EncounterManager.I != null && EncounterManager.I.HasEnergy();
    int  GetEnergyPoints()     => EncounterManager.I != null ? EncounterManager.I.GetEnergyPoints() : 0;
    int  GetEncounterMax()     => EncounterManager.I != null ? EncounterManager.I.GetEncounterMax() : 0;
    int  GetEncounterCost()    => EncounterManager.I != null ? EncounterManager.I.GetEncounterCost() : 0;

    void RequestEncounterTap() => EncounterManager.I?.RequestEncounterTap();

    public void OnClickToggleAuto() => EncounterManager.I?.ToggleAutoMode();

    // ─────────────────────────────────────────────────────────────
    // Localization + Weighted Picker
    // ─────────────────────────────────────────────────────────────
    void PickAndApplyBlinderLine(bool forcePick = false)
    {
        if (!blinderText) return;

        if (!useRandomBlinder && !forcePick)
        {
            blinderText.text = hardFallbackLine;
            return;
        }

        BlinderMessagePackSO pack = overridePack;
        if (!pack && blinderLibrary)
        {
            string lang = preferredLanguageCode;
            if (string.IsNullOrWhiteSpace(lang))
            {
                lang = Application.systemLanguage.ToString().ToLowerInvariant();
                if (lang.StartsWith("english")) lang = "en";
                else if (lang.StartsWith("spanish")) lang = "es";
                else if (lang.StartsWith("french")) lang = "fr";
                else if (lang.StartsWith("portuguese")) lang = "pt";
                else if (lang.StartsWith("german")) lang = "de";
                else if (lang.StartsWith("italian")) lang = "it";
                else if (lang.StartsWith("japanese")) lang = "ja";
                else if (lang.StartsWith("korean")) lang = "ko";
                else if (lang.StartsWith("chinese")) lang = "zh";
            }
            pack = blinderLibrary.ResolvePack(lang?.ToLowerInvariant());
        }

        if (!pack || pack.entries == null || pack.entries.Count == 0)
        {
            blinderText.text = hardFallbackLine;
            return;
        }

        float totalWeight = 0f;
        int validCount = 0;
        for (int i = 0; i < pack.entries.Count; i++)
        {
            var e = pack.entries[i];
            if (string.IsNullOrWhiteSpace(e.line)) continue;
            if (e.weight <= 0f) continue;
            totalWeight += e.weight;
            validCount++;
        }

        if (validCount == 0 || totalWeight <= 0f)
        {
            blinderText.text = pack.GetAnyNonEmptyFallback(hardFallbackLine);
            return;
        }

        string chosen = WeightedPick(pack, totalWeight);
        if (validCount > 1 && !string.IsNullOrEmpty(_lastBlinderLine) && chosen == _lastBlinderLine)
        {
            string reroll = WeightedPick(pack, totalWeight);
            if (!string.IsNullOrEmpty(reroll)) chosen = reroll;
        }

        _lastBlinderLine = chosen;
        blinderText.text = string.IsNullOrEmpty(chosen) ? pack.GetAnyNonEmptyFallback(hardFallbackLine) : chosen;
    }

    string WeightedPick(BlinderMessagePackSO pack, float totalWeight)
    {
        float r = Random.value * totalWeight;
        float acc = 0f;

        for (int i = 0; i < pack.entries.Count; i++)
        {
            var e = pack.entries[i];
            if (string.IsNullOrWhiteSpace(e.line) || e.weight <= 0f) continue;

            acc += e.weight;
            if (r <= acc) return e.line;
        }
        return pack.GetAnyNonEmptyFallback(hardFallbackLine);
    }
}
