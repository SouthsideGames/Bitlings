using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class LurePickerUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private TMP_Dropdown typeDropdown;
    [SerializeField] private Button useLureBtn;
    [SerializeField] private TextMeshProUGUI luresLabel;
    [SerializeField] private TextMeshProUGUI lureTimerText;
    [SerializeField] private TextMeshProUGUI chanceLabel;

    [Header("Lure Settings")]
    [SerializeField, Range(0f, 1f)] private float bonus = 0.30f;
    [SerializeField, Min(1)] private int durationHours = 2;
    [SerializeField] private bool consumeLureItem = true;

    Coroutine _ticker;

    void OnEnable()
    {
        if (SaveManager.Data == null) SaveManager.LoadOrCreate();

        BuildTypeOptions();
        Wire();
        Refresh();
        UpdateTexts();
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
    }

    void Wire()
    {
        if (useLureBtn)
        {
            useLureBtn.onClick.RemoveAllListeners();
            useLureBtn.onClick.AddListener(OnClickUseLure);
        }

        if (typeDropdown)
        {
            typeDropdown.onValueChanged.RemoveAllListeners();
            typeDropdown.onValueChanged.AddListener(_ => UpdateTexts());
        }
    }

    void BuildTypeOptions()
    {
        if (!typeDropdown) return;
        typeDropdown.ClearOptions();
        typeDropdown.AddOptions(new List<string>(Enum.GetNames(typeof(MonsterType))));
    }

    void Refresh()
    {
        if (luresLabel == null || useLureBtn == null) return;

        if (SaveManager.Data == null)
        {
            luresLabel.text = "Lures: -";
            useLureBtn.interactable = false;
            return;
        }

        int have = ResourceBank.Get(ResourceType.Flyer);
        luresLabel.text = $"Lures: {have}";
        useLureBtn.interactable = !consumeLureItem || have > 0;
    }

    void OnClickUseLure()
    {
        var type = (MonsterType)typeDropdown.value;

        if (consumeLureItem && !ResourceBank.TrySpend(ResourceType.Flyer, 1))
        {
            Refresh();
            return;
        }

        float clampedBonus = Mathf.Clamp(bonus, 0f, 2f);
        int hours = Mathf.Max(1, durationHours);

        EncounterManager.I?.AddLure(type, clampedBonus, hours);

        Refresh();
        UpdateTexts();
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
        while (true)
        {
            if (lureTimerText)
            {
                long rem = EncounterManager.I ? EncounterManager.I.GetLureSecondsRemaining() : 0;
                lureTimerText.text = rem > 0 ? FormatHMS(rem) : "No active lure";
            }
            yield return new WaitForSecondsRealtime(1f);
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

        var curLure = EncounterManager.I?.CurrentLure;
        string clock = "";
        if (curLure != null)
        {
            long secs = EncounterManager.I.GetLureSecondsRemaining();
            if (secs <= 0) clock = "Active Lure: (expired)";
            else
            {
                TimeSpan t = TimeSpan.FromSeconds(secs);
                clock = $"Active Lure: {curLure.type} (+{Mathf.RoundToInt(curLure.bonus * 100)}%)";
            }
        }
        else
        {
            clock = "No active lure.";
        }

        chanceLabel.text =
            $"Chance to encounter {selected}: ~{curPct:0.#}%\n" +
            $"After using lure: ~{afterPct:0.#}% for {Mathf.Max(1, durationHours)}h\n" +
            $"{clock}\n" +
            $"(Using a new lure replaces the current one.)";
    }

    (float currentPct, float afterPct) EstimateCurrentAndAfter(MonsterType chosen, float bonusToApply)
    {
        var currentMult = BuildTypeMultipliersFromActiveLure();

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

    Dictionary<MonsterType, float> BuildTypeMultipliersFromActiveLure()
    {
        var map = new Dictionary<MonsterType, float>();
        var cur = EncounterManager.I?.CurrentLure;
        if (cur == null) return map;

        float mult = Mathf.Clamp(1f + Mathf.Max(0f, cur.bonus), 1f, 3f);
        map[cur.type] = mult;
        return map;
    }

    float ComputeTypeChance(MonsterType targetType, Dictionary<MonsterType, float> typeMult)
    {
        var lib = MonsterLibraryLocator.Lib;
        var pool = lib?.monsters?.Where(m => m != null && !string.IsNullOrEmpty(m.id) && m.spawnWeight > 0).ToArray();
        if (pool == null || pool.Length == 0)
        {
            var backup = lib?.monsters?.Where(m => m != null && !string.IsNullOrEmpty(m.id)).ToArray();
            if (backup == null || backup.Length == 0) return 0f;
            int countTarget = backup.Count(m => m.type == targetType);
            return Mathf.Clamp01((float)countTarget / backup.Length);
        }

        float total = 0f;
        float totalTarget = 0f;

        for (int i = 0; i < pool.Length; i++)
        {
            var m = pool[i];
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
