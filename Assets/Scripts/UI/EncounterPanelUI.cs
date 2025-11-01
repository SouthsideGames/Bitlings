using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class EncounterPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Button encounterBtn;
    [SerializeField] private TextMeshProUGUI energyLabel;
    [SerializeField] private TextMeshProUGUI energyEtaLabel;
    [SerializeField, Min(1f)] private float energySecondsPerPoint = 1200f;
    [SerializeField] private TextMeshProUGUI winStreakText;

    [Header("Blinder")]
    [SerializeField] private CanvasGroup blinderGroup;             
    [SerializeField] private TextMeshProUGUI blinderText; 
    [SerializeField] private string blinderMessage = "I WONDER WHAT WE WILL ENCOUNTER";
    [SerializeField, Range(0.05f, 1.5f)] private float preFadeDelay = 0.25f;
    [SerializeField, Range(0.1f, 2.0f)] private float fadeDuration = 0.6f;

    // Internal
    private TextMeshProUGUI encounterLabel;
    float _etaTickAccum = 0f;
    bool _isFading;
    Coroutine _fadeCo;

    void Awake()
    {
        encounterLabel = encounterBtn ? encounterBtn.GetComponentInChildren<TextMeshProUGUI>() : null;

        if (blinderGroup)
        {
            blinderGroup.alpha = 1f;
            blinderGroup.blocksRaycasts = true;
            blinderGroup.interactable = true;
        }
        if (blinderText)
            blinderText.text = string.IsNullOrEmpty(blinderMessage) ? "I WONDER WHAT WE WILL ENCOUNTER" : blinderMessage;
    }

    void OnEnable()
    {
        RefreshWinStreak();
        if (EncounterManager.I != null)
            EncounterManager.I.OnStateChanged += RefreshWinStreak;

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

        if (!IsInBattle())
            ShowBlinder(true, instant: true);
        else
            ShowBlinder(false, instant: true);

        RefreshAll();

        GameEvents.BattleFinished += OnBattleFinished;
        GameEvents.WinStreakChanged += Handle; 
        UpdateNow();
    }

    void OnDisable()
    {
        if (EncounterManager.I != null)
        {
            EncounterManager.I.OnStateChanged -= OnEncounterStateChanged;
            GameEvents.EnergyChanged -= RefreshEnergy;
        }
        
        if (EncounterManager.I != null)
            EncounterManager.I.OnStateChanged -= RefreshWinStreak;

        GameEvents.BattleFinished -= OnBattleFinished;
        GameEvents.WinStreakChanged -= Handle; 

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

    private void Handle(int value) => UpdateNow();

    private void UpdateNow()
    {
        if (!winStreakText) return;
        int v = EncounterManager.I ? EncounterManager.I.CurrentWinStreak : 0;
    }

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
            !_isFading; // lock while blinder animates

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
        int need = Mathf.Max(0, max - cur);

        if (need <= 0)
        {
            energyEtaLabel.text = "Energy full";
            return;
        }

        double totalSec = need * Mathf.Max(1f, energySecondsPerPoint);
        int hours   = (int)(totalSec / 3600.0);
        int minutes = (int)((totalSec % 3600.0) / 60.0);

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
        }
        else
        {
            RefreshAll();
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

        if (blinderText) blinderText.raycastTarget = true; // soak clicks during delay
        if (blinderGroup)
        {
            blinderGroup.blocksRaycasts = true;
            blinderGroup.interactable = true;
        }

        // small suspense delay
        yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, preFadeDelay));

        // fade out
        if (blinderGroup)
        {
            float t = 0f;
            float dur = Mathf.Max(0.1f, fadeDuration);
            float start = blinderGroup.alpha;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Lerp(start, 0f, t / dur);
                blinderGroup.alpha = a;
                yield return null;
            }
            blinderGroup.alpha = 0f;
            blinderGroup.blocksRaycasts = false;
            blinderGroup.interactable = false;
        }

        _isFading = false;
        RefreshButtonAndLabel();

        // Now actually start the encounter
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
        }
        else
        {
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(show
                ? Co_FadeTo(1f)
                : Co_FadeTo(0f));
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

        _isFading = false;
        RefreshButtonAndLabel();
    }

    // ─────────────────────────────────────────────────────────────
    // EncounterManager passthrough helpers (null-safe)
    // ─────────────────────────────────────────────────────────────

    bool IsInBattle()       => EncounterManager.I != null && EncounterManager.I.IsInBattle;
    bool IsAutoMode()       => EncounterManager.I != null && EncounterManager.I.IsAutoMode;
    bool NextEncounterIsFree()=> EncounterManager.I != null && EncounterManager.I.NextEncounterIsFree;
    bool HasEnergy()        => EncounterManager.I != null && EncounterManager.I.HasEnergy();
    int GetEnergyPoints()   => EncounterManager.I != null ? EncounterManager.I.GetEnergyPoints() : 0;
    int GetEncounterMax()   => EncounterManager.I != null ? EncounterManager.I.GetEncounterMax() : 0;
    int GetEncounterCost()  => EncounterManager.I != null ? EncounterManager.I.GetEncounterCost() : 0;

    void RequestEncounterTap()
    {
        EncounterManager.I?.RequestEncounterTap();
    }

    public void OnClickToggleAuto() => EncounterManager.I?.ToggleAutoMode();

     void OnBattleFinished(BattleResult _)
    {
        RefreshWinStreak();
    }

    void RefreshWinStreak()
    {
        if (!winStreakText) return;
        int streak = (EncounterManager.I != null) ? EncounterManager.I.CurrentWinStreak : 0;

        // If you want to hide at zero, uncomment the next two lines:
        // winStreakText.gameObject.SetActive(streak > 0);
        // if (streak <= 0) return;

        winStreakText.text = $"Streak: {streak}";
    }
}
