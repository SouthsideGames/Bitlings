using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;

public class HarborUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Dropdown typeDropdown;
    [SerializeField] private Button useFlyerButton;
    [SerializeField] private TextMeshProUGUI flyersLabel;
    [SerializeField] private TextMeshProUGUI flyerTimerText;
    [SerializeField] private TextMeshProUGUI chanceLabel;
    [SerializeField] private TMP_Text useFlyerButtonLabel;
    [SerializeField] private Image activeFlyerIcon;

    [Header("Effect")]
    [SerializeField, Range(0f, 1f)] private float bonus = 0.30f;
    [SerializeField, Min(1)] private int durationHours = 2;
    [SerializeField] private bool consumeFlyerItem = true;
    [Tooltip("Resources.Load() path (without extension). Example: Resources/TypeIconLibrary.asset => \"TypeIconLibrary\"")]
    [SerializeField] private string typeIconLibraryResourcePath = "TypeIconLibrary";

    private TypeIconLibrary _typeIconLib;
    private Coroutine _ticker;

    void OnEnable()
    {
        if (SaveManager.Data == null) SaveManager.LoadOrCreate();

        _typeIconLib = Resources.Load<TypeIconLibrary>(typeIconLibraryResourcePath);
        if (_typeIconLib == null)
            Debug.LogWarning($"HarborUI: TypeIconLibrary not found at Resources path '{typeIconLibraryResourcePath}'. Icons will be blank until fixed.");

        BuildTypeOptions();
        Wire();

        Refresh();
        UpdateTexts();
        RefreshActiveFlyerIcon();
        RefreshButtonLabel();
        RefreshUseButtonVisibility();

        StartTicker();
        GameEvents.OnResourcesChanged += OnResourcesChanged;
    }

    void OnDisable()
    {
        StopTicker();
        GameEvents.OnResourcesChanged -= OnResourcesChanged;
    }

    void OnResourcesChanged()
    {
        Refresh();
        UpdateTexts();
        RefreshActiveFlyerIcon();
        RefreshButtonLabel();
        RefreshUseButtonVisibility();
    }

    void Wire()
    {
        if (useFlyerButton)
        {
            useFlyerButton.onClick.RemoveAllListeners();
            useFlyerButton.onClick.AddListener(OnClickUseFlyer);
        }

        if (typeDropdown)
        {
            typeDropdown.onValueChanged.RemoveAllListeners();
            typeDropdown.onValueChanged.AddListener(_ => UpdateTexts());
        }

        if (useFlyerButtonLabel == null && useFlyerButton != null)
            useFlyerButtonLabel = useFlyerButton.GetComponentInChildren<TMP_Text>(true);
    }

    void BuildTypeOptions()
    {
        if (!typeDropdown) return;

        typeDropdown.ClearOptions();
        typeDropdown.AddOptions(new List<string>(Enum.GetNames(typeof(MonsterType))));
    }

    void Refresh()
    {
        if (flyersLabel == null || useFlyerButton == null) return;

        if (SaveManager.Data == null)
        {
            flyersLabel.text = "Flyers: -";
            useFlyerButton.interactable = false;
            RefreshUseButtonVisibility(); // keep consistent even if save missing
            return;
        }

        int have = ResourceBank.Get(ResourceType.Flyer);

        bool active = GetFlyerSecondsRemaining() > 0;
        useFlyerButton.interactable = (!consumeFlyerItem || have > 0); // allow replace even while active, per your current behavior

        flyersLabel.text = $"Flyers: {have}";

        RefreshUseButtonVisibility();
    }

    /// <summary>
    /// If we do not have any flyers (and we consume flyers), hide the entire Use Flyer button GameObject.
    /// If consumeFlyerItem is false, we always show the button.
    /// </summary>
    void RefreshUseButtonVisibility()
    {
        if (!useFlyerButton) return;

        if (SaveManager.Data == null)
        {
            // If save isn't ready, keep it hidden when consumption is enabled (safe default).
            useFlyerButton.gameObject.SetActive(!consumeFlyerItem);
            return;
        }

        int have = ResourceBank.Get(ResourceType.Flyer);

        bool shouldShow = !consumeFlyerItem || have > 0;
        if (useFlyerButton.gameObject.activeSelf != shouldShow)
            useFlyerButton.gameObject.SetActive(shouldShow);
    }

    void RefreshButtonLabel()
    {
        if (!useFlyerButtonLabel) return;

        bool active = GetFlyerSecondsRemaining() > 0;
        useFlyerButtonLabel.text = active ? "Replace" : "Use";
    }

    long GetFlyerSecondsRemaining()
    {
        if (!RiftManager.I) return 0;
        return RiftManager.I.GetFlyerSecondsRemaining();
    }

    void OnClickUseFlyer()
    {
        var type = (MonsterType)(typeDropdown ? typeDropdown.value : 0);

        if (consumeFlyerItem && !ResourceBank.TrySpend(ResourceType.Flyer, 1))
        {
            Refresh();
            RefreshButtonLabel();
            RefreshUseButtonVisibility();
            return;
        }

        float clampedBonus = Mathf.Clamp(bonus, 0f, 2f);
        int hours = Mathf.Max(1, durationHours);

        RiftManager.I?.AddFlyer(type, clampedBonus, hours);

        GameEvents.RaiseToast("FLYER ACTIVATED");

        Refresh();
        UpdateTexts();
        RefreshActiveFlyerIcon();
        RefreshButtonLabel();
        RefreshUseButtonVisibility();
    }

    void RefreshActiveFlyerIcon()
    {
        if (!activeFlyerIcon) return;

        var cur = RiftManager.I?.CurrentFlyer;

        if (cur != null)
        {
            long secs = RiftManager.I.GetFlyerSecondsRemaining();
            if (secs <= 0) cur = null;
        }

        if (cur == null)
        {
            if (activeFlyerIcon.gameObject.activeSelf)
                activeFlyerIcon.gameObject.SetActive(false);
            return;
        }

        if (_typeIconLib == null)
        {
            if (activeFlyerIcon.gameObject.activeSelf)
                activeFlyerIcon.gameObject.SetActive(false);
            return;
        }

        var spr = _typeIconLib.GetIcon(cur.type);
        if (spr != null)
        {
            activeFlyerIcon.sprite = spr;
            if (!activeFlyerIcon.gameObject.activeSelf)
                activeFlyerIcon.gameObject.SetActive(true);
        }
        else
        {
            if (activeFlyerIcon.gameObject.activeSelf)
                activeFlyerIcon.gameObject.SetActive(false);
        }
    }

    void StartTicker()
    {
        if (_ticker != null) StopCoroutine(_ticker);
        _ticker = StartCoroutine(TickRoutine());
    }

    void StopTicker()
    {
        if (_ticker != null) { StopCoroutine(_ticker); _ticker = null; }
    }

    IEnumerator TickRoutine()
    {
        var wait = new WaitForSecondsRealtime(1f);
        while (true)
        {
            if (flyerTimerText)
            {
                long rem = RiftManager.I ? RiftManager.I.GetFlyerSecondsRemaining() : 0;
                flyerTimerText.text = rem > 0 ? FormatHMS(rem) : "No active flyers";
            }

            RefreshActiveFlyerIcon();
            RefreshButtonLabel();
            RefreshUseButtonVisibility();

            yield return wait;
        }
    }

    void UpdateTexts()
    {
        if (!chanceLabel) return;

        var lib = MonsterLibraryLocator.Lib;
        if (!lib || lib.monsters == null || lib.monsters.Length == 0)
        {
            chanceLabel.text = "No monsters in library.";
            return;
        }

        var selected = (MonsterType)(typeDropdown ? typeDropdown.value : 0);
        var (curPct, afterPct) = EstimateCurrentAndAfter(selected, bonus);

        var curFlyer = RiftManager.I?.CurrentFlyer;
        string clock;
        if (curFlyer != null)
        {
            long secs = RiftManager.I.GetFlyerSecondsRemaining();
            if (secs <= 0) clock = "Active Flyer: (expired)";
            else clock = $"Active Flyer: {curFlyer.type} (+{Mathf.RoundToInt(curFlyer.bonus * 100)}%)";
        }
        else
        {
            clock = "No active flyer.";
        }

        chanceLabel.text =
            $"Chance to rift {selected}: ~{curPct:0.#}%\n" +
            $"After using flyer: ~{afterPct:0.#}% for {Mathf.Max(1, durationHours)}h\n" +
            $"{clock}\n" +
            $"(Using a new flyer replaces the current one.)";
    }

    (float currentPct, float afterPct) EstimateCurrentAndAfter(MonsterType chosen, float bonusToApply)
    {
        var currentMult = BuildTypeMultipliersFromActiveFlyer();
        float current = ComputeTypeChance(chosen, currentMult);

        var previewMult = new Dictionary<MonsterType, float>(currentMult);
        float add = Mathf.Clamp(1f + Mathf.Max(0f, bonusToApply), 1f, 3f);
        if (previewMult.TryGetValue(chosen, out float existing))
            previewMult[chosen] = Mathf.Max(existing, add);
        else
            previewMult[chosen] = add;

        float after = ComputeTypeChance(chosen, previewMult);
        return (current * 100f, after * 100f);
    }

    Dictionary<MonsterType, float> BuildTypeMultipliersFromActiveFlyer()
    {
        var map = new Dictionary<MonsterType, float>();
        var cur = RiftManager.I?.CurrentFlyer;
        if (cur == null) return map;

        float mult = Mathf.Clamp(1f + Mathf.Max(0f, cur.bonus), 1f, 3f);
        map[cur.type] = mult;
        return map;
    }

    float ComputeTypeChance(MonsterType targetType, Dictionary<MonsterType, float> typeMult)
    {
        var lib = MonsterLibraryLocator.Lib;
        var src = lib?.monsters;
        if (src == null || src.Length == 0) return 0f;

        // Build pool inline — spawnable monsters with spawnWeight > 0
        int poolCount = 0;
        for (int i = 0; i < src.Length; i++)
            if (src[i] != null && !string.IsNullOrEmpty(src[i].id) && src[i].spawnWeight > 0) poolCount++;

        if (poolCount == 0)
        {
            // Fallback: count valid monsters
            int validCount = 0;
            int targetCount = 0;
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] == null || string.IsNullOrEmpty(src[i].id)) continue;
                validCount++;
                if (src[i].type == targetType) targetCount++;
            }
            if (validCount == 0) return 0f;
            return Mathf.Clamp01((float)targetCount / validCount);
        }

        float total = 0f;
        float totalTarget = 0f;

        for (int i = 0; i < src.Length; i++)
        {
            var m = src[i];
            if (m == null || string.IsNullOrEmpty(m.id) || m.spawnWeight <= 0) continue;

            float baseW = Mathf.Max(0, m.spawnWeight);
            float mult = 1f;
            if (typeMult != null && typeMult.TryGetValue(m.type, out var k))
                mult = Mathf.Max(0f, k);

            float w = baseW * mult;
            total += w;
            if (m.type == targetType) totalTarget += w;
        }

        if (total <= 0f) return 0f;
        return Mathf.Clamp01(totalTarget / total);
    }

    string FormatHMS(long seconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1.0)
            return $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s";
        else if (t.TotalMinutes >= 1.0)
            return $"{t.Minutes}m {t.Seconds}s";
        else
            return $"{t.Seconds}s";
    }
}
