using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class DiagnosticsOverlayUI : MonoBehaviour
{
    public static DiagnosticsOverlayUI I { get; private set; }

    [Serializable]
    private sealed class PairResult
    {
        public string monsterA;
        public string monsterB;
        public int levelA;
        public int levelB;
        public int iterations;
        public int winsA;
        public int winsB;
        public float winRateA;
        public float winRateB;
        public float offenseMulA;
        public float defenseMulA;
        public float incomingTypeResistMulA;
        public string titleAId;
        public string titleBId;
        public string[] titlesA;
        public string[] titlesB;
    }

    [Serializable]
    private sealed class RunOutput
    {
        public string createdUtc;
        public string mode;
        public int baseSeed;
        public List<PairResult> pairs;
    }

    [Header("Unlock Behavior")]
    [SerializeField] private bool hideButtonUntilUnlocked = true;

    [Header("Button (child)")]
    [SerializeField] private Button diagnosticsButton;
    [SerializeField] private CanvasGroup mainGroup;

    [Header("Main Group (child)")]
    [SerializeField] private CanvasGroup diagnosticsGroup;    
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private Button closeButton;

    [Header("Balance Panel")]
    [SerializeField] private CanvasGroup balanceGroup;
    [SerializeField] private Button balanceToggleButton;
    [SerializeField] private TextMeshProUGUI balanceButtonLabel;
    [SerializeField] private Image backgroundImage;
    [SerializeField, Range(0f, 1f)] private float backgroundAlphaNormal = 0.6f;
    [SerializeField, Range(0f, 1f)] private float backgroundAlphaFull = 1f;

    [Header("Balance Sim (dev)")]
    [SerializeField] private bool enableBalanceSim = false;
    [SerializeField] private TMP_Dropdown simMonsterADropdown;
    [SerializeField] private TMP_Dropdown simMonsterBDropdown;
    [SerializeField] private TMP_InputField simLevelA;
    [SerializeField] private TMP_InputField simLevelB;
    [SerializeField] private TMP_InputField simIterations;
    [SerializeField] private TMP_InputField simSeed;
    [SerializeField] private TMP_Dropdown simTitleADropdown;
    [SerializeField] private TMP_Dropdown simTitleBDropdown;
    [SerializeField] private TMP_InputField simTitleAId;
    [SerializeField] private TMP_InputField simTitleBId;
    [SerializeField] private Toggle simAVsAllToggle;
    [SerializeField] private Button simRunButton;
    [SerializeField] private TextMeshProUGUI simStatusText;

    [Header("Behavior")]
    [SerializeField, Min(0.05f)] private float refreshSeconds = 0.25f;
    [SerializeField] private bool autoScrollToBottom = true;

    float _t;
    bool _panelVisible;
    bool _balanceVisible;
    readonly Dictionary<string, TitleSO> _titleCache = new Dictionary<string, TitleSO>(StringComparer.Ordinal);
    readonly List<string> _monsterDropdownIds = new List<string>(128);
    readonly List<string> _titleOptionIdsA = new List<string>(16);
    readonly List<string> _titleOptionIdsB = new List<string>(16);
    bool _simRunning;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (diagnosticsButton)
        {
            diagnosticsButton.onClick.RemoveAllListeners();
            diagnosticsButton.onClick.AddListener(OnDiagnosticsButtonPressed);
        }

        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(ClosePanel);
        }

        if (balanceToggleButton)
        {
            balanceToggleButton.onClick.RemoveAllListeners();
            balanceToggleButton.onClick.AddListener(ToggleBalancePanel);
        }

        if (enableBalanceSim && simRunButton)
        {
            simRunButton.onClick.RemoveAllListeners();
            simRunButton.onClick.AddListener(OnRunSimPressed);
        }

        if (enableBalanceSim)
        {
            SetupBalanceSimListeners();
            EnsureMonsterDropdownsPopulated();
            RefreshTitleDropdowns();
        }

        SetPanelVisible(false, instant: true);

        ApplyUnlockedState(IsUnlocked());
        SetBalanceVisible(false);
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    void Update()
    {
        // Handles late unlock state changes (e.g. cheat/save changes after Awake).
        if (diagnosticsButton && IsUnlocked())
        {
            if (hideButtonUntilUnlocked && !diagnosticsButton.gameObject.activeSelf)
                diagnosticsButton.gameObject.SetActive(true);

            if (!_panelVisible && !diagnosticsButton.interactable)
                diagnosticsButton.interactable = true;
        }

        if (!_panelVisible) return;

        _t += Time.unscaledDeltaTime;
        if (_t >= refreshSeconds)
        {
            _t = 0f;
            Refresh("Tick");
        }
    }

    bool IsUnlocked()
    {
        try
        {
            return SaveManager.Data != null && SaveManager.Data.diagnosticsUnlocked;
        }
        catch
        {
            return false;
        }
    }

    public void Unlock()
    {
        if (SaveManager.Data != null)
        {
            SaveManager.Data.diagnosticsUnlocked = true;
            SaveManager.Save();
        }

        ApplyUnlockedState(true);
    }

    // Called by CheatCodeManager
    public void UnlockFromCheat() => Unlock();

    void ApplyUnlockedState(bool unlocked)
    {
        if (!diagnosticsButton) return;

        if (hideButtonUntilUnlocked)
            diagnosticsButton.gameObject.SetActive(unlocked);
        else
            diagnosticsButton.gameObject.SetActive(true);

        // When unlocked and panel is not open, button should be interactable.
        diagnosticsButton.interactable = unlocked && !_panelVisible;
    }

    void OnDiagnosticsButtonPressed()
    {
        if (!IsUnlocked())
            return;

        OpenPanel();
    }

    public void OpenPanel()
    {
        SetPanelVisible(true, instant: true);
        Refresh("Open");

        SetBalanceVisible(false);

        // Requirement: when panel opens, button becomes inactive
        if (diagnosticsButton)
            diagnosticsButton.interactable = false;
    }

    public void ClosePanel()
    {
        SetPanelVisible(false, instant: true);

        ApplyUnlockedState(IsUnlocked());
    }

    public void TogglePanel()
    {
        if (_panelVisible) ClosePanel();
        else OpenPanel();
    }

    void SetPanelVisible(bool on, bool instant)
    {
        _panelVisible = on;
        _t = 0f;

        if (mainGroup)
        {
            mainGroup.alpha = on ? 1f : 0f;
            mainGroup.blocksRaycasts = on;
            mainGroup.interactable = on;
        }
    }

    public void Refresh(string context = "")
    {
        if (!text) return;
        if (_balanceVisible) return; // balance view owns its own UI

        text.text = DiagnosticsSnapshot.Build(context);

        if (autoScrollToBottom && scrollRect)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f; 
        }
    }


    void ToggleBalancePanel()
    {
        if (!enableBalanceSim)
        {
            if (simStatusText) simStatusText.text = "Balance sim disabled (enable in inspector)";
            return;
        }

        SetBalanceVisible(!_balanceVisible);
    }

    void SetBalanceVisible(bool on)
    {
        _balanceVisible = on;

        if (on && enableBalanceSim)
            EnsureMonsterDropdownsPopulated();

        if (diagnosticsGroup)
        {
            diagnosticsGroup.alpha = on ? 0f : 1f;
            diagnosticsGroup.blocksRaycasts = !on;
            diagnosticsGroup.interactable = !on;
        }

        if (balanceGroup)
        {
            balanceGroup.alpha = on ? 1f : 0f;
            balanceGroup.blocksRaycasts = on;
            balanceGroup.interactable = on;
        }

        if (backgroundImage)
        {
            var c = backgroundImage.color;
            c.a = on ? backgroundAlphaFull : backgroundAlphaNormal;
            backgroundImage.color = c;
        }

        if (balanceButtonLabel)
            balanceButtonLabel.text = on ? "Test" : "Balance";
    }

    System.Collections.IEnumerator Co_RunBalanceSim()
    {
        _simRunning = true;
        SetSimStatus("Running...");

        yield return null; // allow UI update

        try
        {
            RunBalanceSim();
            SetSimStatus("Done. See Logs/BalanceSim");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DIAG] BalanceSim error: {ex.Message}\n{ex.StackTrace}");
            SetSimStatus("Error - see console");
        }
        finally
        {
            _simRunning = false;
        }
    }

    void RunBalanceSim()
    {
        EnsureMonsterDropdownsPopulated();

        string aId = GetSelectedMonsterId(simMonsterADropdown);
        string bId = GetSelectedMonsterId(simMonsterBDropdown);
        int levelA = ParseInt(simLevelA, 10);
        int levelB = ParseInt(simLevelB, 10);
        int iterations = Mathf.Max(1, ParseInt(simIterations, 20));
        int seed = ParseInt(simSeed, 12345);
        bool aVsAll = simAVsAllToggle ? simAVsAllToggle.isOn : true;

        var lib = MonsterLibraryLocator.Lib;
        if (lib == null || lib.monsters == null || lib.monsters.Length == 0)
            throw new InvalidOperationException("MonsterLibrary not found");

        var defA = MonsterLibraryLocator.GetById(aId);
        if (defA == null)
            throw new InvalidOperationException($"Monster A id not found: {aId}");

        var titleA = ResolveTitle(simTitleAId ? simTitleAId.text : null);
        var titleB = ResolveTitle(simTitleBId ? simTitleBId.text : null);

        var pairs = new List<PairResult>();

        if (aVsAll)
        {
            foreach (var def in lib.monsters)
            {
                if (def == null || def == defA) continue;
                var pr = SimulatePair(defA, levelA, def, levelB, iterations, seed, titleA, titleB);
                pairs.Add(pr);
            }
        }
        else
        {
            var defB = MonsterLibraryLocator.GetById(bId);
            if (defB == null)
                throw new InvalidOperationException($"Monster B id not found: {bId}");

            var pr = SimulatePair(defA, levelA, defB, levelB, iterations, seed, titleA, titleB);
            pairs.Add(pr);
        }

        var output = new RunOutput
        {
            createdUtc = DateTime.UtcNow.ToString("o"),
            mode = "HeadlessBattleApprox-Diagnostics",
            baseSeed = seed,
            pairs = pairs
        };

        string logsDir = GetBalanceSimDir();
        Directory.CreateDirectory(logsDir);
        string fileName = $"diag_balance_sim_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        string path = Path.Combine(logsDir, fileName);

        var json = JsonUtility.ToJson(output, true);
        File.WriteAllText(path, json);

        Debug.Log($"[DIAG] BalanceSim wrote {path}");
    }

    PairResult SimulatePair(MonsterDataSO a, int levelA, MonsterDataSO b, int levelB, int iterations, int seed, TitleSO titleA, TitleSO titleB)
    {
        int winsA = 0;
        int winsB = 0;

        var stats = BuildInput(a, levelA, b, levelB, titleA, titleB, null, null, seed);
        for (int i = 0; i < iterations; i++)
        {
            var input = stats.input;
            input.rngSeed = seed + i;
            var result = HeadlessBattle.Resolve(input);
            if (result.victory) winsA++; else winsB++;
        }

        float total = iterations;
        float rateA = (total > 0f) ? winsA / total : 0f;
        float rateB = (total > 0f) ? winsB / total : 0f;

        return new PairResult
        {
            monsterA = a ? a.id : "A",
            monsterB = b ? b.id : "B",
            levelA = levelA,
            levelB = levelB,
            iterations = iterations,
            winsA = winsA,
            winsB = winsB,
            winRateA = rateA,
            winRateB = rateB,
            offenseMulA = stats.offenseMul,
            defenseMulA = stats.defenseMul,
            incomingTypeResistMulA = stats.incomingTypeResistMul,
            titleAId = titleA ? titleA.titleId : null,
            titleBId = titleB ? titleB.titleId : null,
            titlesA = ToIds(stats.titlesA),
            titlesB = ToIds(stats.titlesB)
        };
    }

    TitleSO ResolveTitle(string titleId)
    {
        if (string.IsNullOrWhiteSpace(titleId)) return null;
        if (_titleCache.TryGetValue(titleId, out var cached)) return cached;

        TitleSO found = null;
        var all = Resources.LoadAll<TitleSO>(string.Empty);
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t != null && string.Equals(t.titleId, titleId, StringComparison.Ordinal))
            {
                found = t;
                break;
            }
        }

        _titleCache[titleId] = found;
        return found;
    }

    int ParseInt(TMP_InputField field, int fallback)
    {
        if (field == null || string.IsNullOrWhiteSpace(field.text)) return fallback;
        if (int.TryParse(field.text, out var v)) return v;
        return fallback;
    }

    void EnsureMonsterDropdownsPopulated()
    {
        if (!enableBalanceSim) return;

        var collected = new List<MonsterDataSO>(256);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        // Source 1: Explicit MonsterLibrary in Resources (declared by user)
        var lib = MonsterLibraryLocator.Lib;
        if (lib != null && lib.monsters != null)
        {
            for (int i = 0; i < lib.monsters.Length; i++)
            {
                var def = lib.monsters[i];
                if (def == null || string.IsNullOrWhiteSpace(def.id)) continue;
                if (seenIds.Add(def.id)) collected.Add(def);
            }
            if (simStatusText && collected.Count > 0)
                simStatusText.text = $"Loaded {collected.Count} from MonsterLibrary";
        }

        // Source 2: Catalog union (in case it has more)
        var catalogMonsters = MonsterLibraryLocator.AllMonsters;
        if (catalogMonsters != null)
        {
            for (int i = 0; i < catalogMonsters.Count; i++)
            {
                var def = catalogMonsters[i];
                if (def == null || string.IsNullOrWhiteSpace(def.id)) continue;
                if (seenIds.Add(def.id)) collected.Add(def);
            }
        }

        // Fallback 1: Resources sweep (build-safe)
        if (collected.Count == 0)
        {
            var resourcesDefs = Resources.LoadAll<MonsterDataSO>(string.Empty);
            var list = new List<MonsterDataSO>(resourcesDefs.Length);
            for (int i = 0; i < resourcesDefs.Length; i++)
            {
                var def = resourcesDefs[i];
                if (def != null && seenIds.Add(def.id)) list.Add(def);
            }
            collected.AddRange(list);
            if (simStatusText && list.Count > 0) simStatusText.text = "Loaded monsters via Resources sweep";
        }

#if UNITY_EDITOR
        // Fallback 2 (editor only): AssetDatabase search (works even if not in Resources)
        if (collected.Count == 0 && UnityEditor.AssetDatabase.IsValidFolder("Assets"))
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:MonsterDataSO");
            var list = new List<MonsterDataSO>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                var def = UnityEditor.AssetDatabase.LoadAssetAtPath<MonsterDataSO>(path);
                if (def != null && seenIds.Add(def.id)) list.Add(def);
            }
            collected.AddRange(list);
            if (simStatusText && list.Count > 0) simStatusText.text = "Loaded monsters via AssetDatabase";
        }
#endif

        if (collected.Count == 0)
        {
            if (simStatusText) simStatusText.text = "No monsters found";
            return;
        }

        _monsterDropdownIds.Clear();

        var entries = new List<KeyValuePair<string, string>>(collected.Count);
        for (int i = 0; i < collected.Count; i++)
        {
            var def = collected[i];
            if (def == null || string.IsNullOrWhiteSpace(def.id)) continue;
            entries.Add(new KeyValuePair<string, string>(def.id, BuildMonsterOptionLabel(def)));
        }

        if (entries.Count == 0)
        {
            if (simStatusText) simStatusText.text = "No monsters with IDs";
            return;
        }

        entries.Sort((a, b) =>
        {
            int byLabel = string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase);
            if (byLabel != 0) return byLabel;
            return string.Compare(a.Key, b.Key, StringComparison.Ordinal);
        });

        var options = new List<TMP_Dropdown.OptionData>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            _monsterDropdownIds.Add(entries[i].Key);
            options.Add(new TMP_Dropdown.OptionData(entries[i].Value));
        }

        ConfigureMonsterDropdown(simMonsterADropdown, options);
        ConfigureMonsterDropdown(simMonsterBDropdown, options);

        RefreshTitleDropdowns();

        if (simStatusText) simStatusText.text = $"Loaded {entries.Count} monsters";

    }

    void ConfigureMonsterDropdown(TMP_Dropdown dropdown, List<TMP_Dropdown.OptionData> options)
    {
        if (!dropdown) return;

        dropdown.ClearOptions();
        dropdown.AddOptions(options);

        int index = Mathf.Clamp(dropdown.value, 0, options.Count - 1);
        dropdown.SetValueWithoutNotify(index);
        dropdown.RefreshShownValue();
    }

    void SetupBalanceSimListeners()
    {
        if (simMonsterADropdown)
        {
            simMonsterADropdown.onValueChanged.RemoveAllListeners();
            simMonsterADropdown.onValueChanged.AddListener(_ => OnMonsterSelectionChanged(true));
        }

        if (simMonsterBDropdown)
        {
            simMonsterBDropdown.onValueChanged.RemoveAllListeners();
            simMonsterBDropdown.onValueChanged.AddListener(_ => OnMonsterSelectionChanged(false));
        }

        if (simTitleADropdown)
        {
            simTitleADropdown.onValueChanged.RemoveAllListeners();
            simTitleADropdown.onValueChanged.AddListener(idx => ApplyTitleSelection(true, idx));
        }

        if (simTitleBDropdown)
        {
            simTitleBDropdown.onValueChanged.RemoveAllListeners();
            simTitleBDropdown.onValueChanged.AddListener(idx => ApplyTitleSelection(false, idx));
        }
    }

    void OnMonsterSelectionChanged(bool isA)
    {
        RefreshTitleDropdownFor(isA);
    }

    void RefreshTitleDropdowns()
    {
        RefreshTitleDropdownFor(true);
        RefreshTitleDropdownFor(false);
    }

    void RefreshTitleDropdownFor(bool isA)
    {
        var dropdown = isA ? simTitleADropdown : simTitleBDropdown;
        var input = isA ? simTitleAId : simTitleBId;
        var ids = isA ? _titleOptionIdsA : _titleOptionIdsB;

        if (!enableBalanceSim || dropdown == null)
            return;

        var monster = isA ? GetSelectedMonster(simMonsterADropdown) : GetSelectedMonster(simMonsterBDropdown);
        var options = BuildTitleOptions(monster);

        ids.Clear();
        dropdown.ClearOptions();

        var uiOptions = new List<TMP_Dropdown.OptionData>(options.Count + 1);
        uiOptions.Add(new TMP_Dropdown.OptionData(monster ? "None" : "None (no track)"));
        ids.Add(string.Empty);

        for (int i = 0; i < options.Count; i++)
        {
            var t = options[i];
            string label = string.IsNullOrWhiteSpace(t.displayName) ? t.titleId : t.displayName;
            uiOptions.Add(new TMP_Dropdown.OptionData(label));
            ids.Add(t.titleId);
        }

        dropdown.AddOptions(uiOptions);

        int current = 0;
        if (input && !string.IsNullOrWhiteSpace(input.text))
        {
            int idx = ids.IndexOf(input.text);
            if (idx >= 0) current = idx;
        }

        dropdown.SetValueWithoutNotify(Mathf.Clamp(current, 0, uiOptions.Count - 1));
        dropdown.RefreshShownValue();
    }

    void ApplyTitleSelection(bool isA, int index)
    {
        var ids = isA ? _titleOptionIdsA : _titleOptionIdsB;
        var input = isA ? simTitleAId : simTitleBId;

        if (input == null || ids.Count == 0)
            return;

        int clamped = Mathf.Clamp(index, 0, ids.Count - 1);
        input.text = ids[clamped];
    }

    MonsterDataSO GetSelectedMonster(TMP_Dropdown dropdown)
    {
        var id = GetSelectedMonsterId(dropdown);
        if (string.IsNullOrWhiteSpace(id)) return null;
        return MonsterLibraryLocator.GetById(id);
    }

    static List<TitleSO> BuildTitleOptions(MonsterDataSO def)
    {
        var list = new List<TitleSO>();
        if (!def || def.titleTrack == null || def.titleTrack.tiers == null) return list;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tiers = def.titleTrack.tiers;
        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            if (tier == null || tier.unlockChoices == null) continue;

            for (int j = 0; j < tier.unlockChoices.Count; j++)
            {
                var t = tier.unlockChoices[j];
                if (t == null || string.IsNullOrEmpty(t.titleId)) continue;
                if (seen.Add(t.titleId)) list.Add(t);
            }
        }

        return list;
    }

    string GetSelectedMonsterId(TMP_Dropdown dropdown)
    {
        if (!dropdown || _monsterDropdownIds.Count == 0)
            return string.Empty;

        int index = Mathf.Clamp(dropdown.value, 0, _monsterDropdownIds.Count - 1);
        return _monsterDropdownIds[index];
    }

    static string BuildMonsterOptionLabel(MonsterDataSO def)
    {
        if (def == null) return string.Empty;

        string readableName = string.IsNullOrWhiteSpace(def.name) ? def.id : def.name;
        if (string.Equals(readableName, def.id, StringComparison.Ordinal))
            return def.id;

        return $"{readableName} ({def.id})";
    }

    void SetSimStatus(string msg)
    {
        if (simStatusText) simStatusText.text = msg;
    }

    (HeadlessBattle.Input input, float offenseMul, float defenseMul, float incomingTypeResistMul, List<TitleSO> titlesA, List<TitleSO> titlesB) BuildInput(
        MonsterDataSO a,
        int levelA,
        MonsterDataSO b,
        int levelB,
        TitleSO titleA,
        TitleSO titleB,
        List<TitleSO> tierTitlesA,
        List<TitleSO> tierTitlesB,
        int seed)
    {
        const string idA = "SIM::A";
        const string idB = "SIM::B";

        TitlesAdapter.ClearLocalTitles(idA);
        TitlesAdapter.ClearLocalTitles(idB);

        var localA = BuildTitleListFor(a, levelA, titleA, tierTitlesA);
        var localB = BuildTitleListFor(b, levelB, titleB, tierTitlesB);

        if (localA.Count > 0) TitlesAdapter.SetLocalTitles(idA, localA);
        if (localB.Count > 0) TitlesAdapter.SetLocalTitles(idB, localB);

        if (a) TitlesAdapter.RegisterBattleContext(idA, a, levelA);
        if (b) TitlesAdapter.RegisterBattleContext(idB, b, levelB);

        float atkA = BattleCalc.CalcBaseAttack(a, levelA, 0, 0, idA);
        float atkB = BattleCalc.CalcBaseAttack(b, levelB, 0, 0, idB);
        float hpA = BattleCalc.CalcHP(a, levelA, idA);
        float hpB = BattleCalc.CalcHP(b, levelB, idB);
        float defA = BattleCalc.CalcDefense(a, levelA, idA);
        float defB = BattleCalc.CalcDefense(b, levelB, idB);

        float offenseMul = Mathf.Max(0.1f, atkA / Mathf.Max(1f, atkB));
        float defenseMul = Mathf.Max(0.1f, (hpA * (100f / (100f + defA))) / Mathf.Max(1f, hpB * (100f / (100f + defB))));

        float incomingTypeResistMul = 1f;
        try
        {
            float eff = BattleTypeChart.GetMultiplier(b ? b.type : default, a ? a.type : default);
            incomingTypeResistMul = Mathf.Clamp(1f / Mathf.Max(0.1f, eff), 0.1f, 4f);
        }
        catch { }

        var input = new HeadlessBattle.Input
        {
            avgTeamLevel = levelA,
            wildLevel = levelB,
            basecreditPerWin = 0,
            rewardMultiplier = 1f,
            rngSeed = seed,
            offenseMul = offenseMul,
            defenseMul = defenseMul,
            incomingTypeResistMul = incomingTypeResistMul,
            earlyEdge = 0f,
            creditMul = 1f
        };

        return (input, offenseMul, defenseMul, incomingTypeResistMul, localA, localB);
    }

    static List<TitleSO> BuildTitleListFor(MonsterDataSO def, int level, TitleSO single, List<TitleSO> tierSelections)
    {
        var list = new List<TitleSO>(8);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddIfValid(TitleSO t)
        {
            if (t == null || string.IsNullOrEmpty(t.titleId)) return;
            if (seen.Add(t.titleId)) list.Add(t);
        }

        AddIfValid(single);

        if (def != null && def.titleTrack != null && def.titleTrack.tiers != null && tierSelections != null)
        {
            var tiers = def.titleTrack.tiers;
            int count = Mathf.Min(tiers.Count, tierSelections.Count);
            for (int i = 0; i < count; i++)
            {
                var tier = tiers[i];
                if (tier == null) continue;
                if (level < Mathf.Max(1, tier.levelRequired)) continue;
                AddIfValid(tierSelections[i]);
            }
        }

        return list;
    }

    static string[] ToIds(List<TitleSO> titles)
    {
        if (titles == null) return Array.Empty<string>();
        var arr = new string[titles.Count];
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            arr[i] = t ? t.titleId : null;
        }
        return arr;
    }

    static string GetBalanceSimDir()
    {
#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "BalanceSim"));
#else
        // In builds (including mobile), prefer persistent data so it’s writable.
        return Path.Combine(Application.persistentDataPath, "BalanceSim");
#endif
    }

    void OnRunSimPressed()
    {
        if (!enableBalanceSim || _simRunning) return;
        StartCoroutine(Co_RunBalanceSim());
    }
}
