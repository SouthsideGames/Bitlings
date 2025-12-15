using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class HarborUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private TMP_Dropdown typeDropdown;
    [SerializeField] private Button useFlyerButton;
    [SerializeField] private TextMeshProUGUI flyersLabel;
    [SerializeField] private TextMeshProUGUI flyerTimerText;
    [SerializeField] private TextMeshProUGUI chanceLabel;

    [Header("Active Flyer Icon")]
    [SerializeField] private Image activeFlyerIcon;

    [Serializable]
    public struct TypeIcon
    {
        public MonsterType type;
        public Sprite sprite;
    }

    [SerializeField] private List<TypeIcon> typeIcons = new List<TypeIcon>();
    Dictionary<MonsterType, Sprite> _iconMap;

    [Header("Flyer Settings")]
    [SerializeField, Range(0f, 1f)] private float bonus = 0.30f;
    [SerializeField, Min(1)] private int durationHours = 2;
    [SerializeField] private bool consumeFlyerItem = true;

    Coroutine _ticker;

    void OnEnable()
    {
        if (SaveManager.Data == null) SaveManager.LoadOrCreate();

        BuildTypeOptions();
        BuildIconMap();
        Wire();

        Refresh();
        UpdateTexts();
        RefreshActiveFlyerIcon();

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
    }

    void BuildTypeOptions()
    {
        if (!typeDropdown) return;
        typeDropdown.ClearOptions();
        typeDropdown.AddOptions(new List<string>(Enum.GetNames(typeof(MonsterType))));
    }

    void BuildIconMap()
    {
        _iconMap = new Dictionary<MonsterType, Sprite>();
        if (typeIcons == null) return;

        for (int i = 0; i < typeIcons.Count; i++)
        {
            var entry = typeIcons[i];
            if (entry.sprite == null) continue;
            _iconMap[entry.type] = entry.sprite;
        }
    }

    void Refresh()
    {
        if (flyersLabel == null || useFlyerButton == null) return;

        if (SaveManager.Data == null)
        {
            flyersLabel.text = "Flyers: -";
            useFlyerButton.interactable = false;
            return;
        }

        int have = ResourceBank.Get(ResourceType.Flyer);
        flyersLabel.text = $"Flyers: {have}";
        useFlyerButton.interactable = !consumeFlyerItem || have > 0;
    }

    void OnClickUseFlyer()
    {
        var type = (MonsterType)typeDropdown.value;

        if (consumeFlyerItem && !ResourceBank.TrySpend(ResourceType.Flyer, 1))
        {
            Refresh();
            return;
        }

        float clampedBonus = Mathf.Clamp(bonus, 0f, 2f);
        int hours = Mathf.Max(1, durationHours);

        EncounterManager.I?.AddFlyer(type, clampedBonus, hours);

        Refresh();
        UpdateTexts();
        RefreshActiveFlyerIcon();
    }

    void RefreshActiveFlyerIcon()
    {
        if (!activeFlyerIcon) return;

        var cur = EncounterManager.I?.CurrentFlyer;

        // Treat expired as "none"
        if (cur != null)
        {
            long secs = EncounterManager.I.GetFlyerSecondsRemaining();
            if (secs <= 0) cur = null;
        }

        // No active flyer => hide icon entirely
        if (cur == null)
        {
            if (activeFlyerIcon.gameObject.activeSelf)
                activeFlyerIcon.gameObject.SetActive(false);
            return;
        }

        if (_iconMap == null) BuildIconMap();

        // If we have a configured sprite for this flyer type, show it; otherwise hide the icon.
        if (_iconMap != null && _iconMap.TryGetValue(cur.type, out var spr) && spr != null)
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
        while (true)
        {
            if (flyerTimerText)
            {
                long rem = EncounterManager.I ? EncounterManager.I.GetFlyerSecondsRemaining() : 0;
                flyerTimerText.text = rem > 0 ? FormatHMS(rem) : "No active flyers";
            }

            // If flyer expires while this panel is open, reflect it immediately.
            RefreshActiveFlyerIcon();

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

        var curFlyer = EncounterManager.I?.CurrentFlyer;
        string clock = "";
        if (curFlyer != null)
        {
            long secs = EncounterManager.I.GetFlyerSecondsRemaining();
            if (secs <= 0) clock = "Active Flyer: (expired)";
            else clock = $"Active Flyer: {curFlyer.type} (+{Mathf.RoundToInt(curFlyer.bonus * 100)}%)";
        }
        else
        {
            clock = "No active flyer.";
        }

        chanceLabel.text =
            $"Chance to encounter {selected}: ~{curPct:0.#}%\n" +
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
        var cur = EncounterManager.I?.CurrentFlyer;
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
