using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class BalanceSimulatorWindow : EditorWindow
{
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
        public List<PairResult> pairs = new List<PairResult>();
    }

    private MonsterLibrarySO _library;
    private MonsterDataSO _monsterA;
    private MonsterDataSO _monsterB;
    private TitleSO _titleA;
    private TitleSO _titleB;
    private readonly List<TitleSO> _titlesA = new List<TitleSO>();
    private readonly List<TitleSO> _titlesB = new List<TitleSO>();

    private bool _aVersusAll = true;
    private bool _useOpponentDefaultTitle = false;
    private int _levelA = 10;
    private int _levelB = 10;
    private int _iterations = 20;
    private int _baseSeed = 12345;

    private Vector2 _scroll;
    private string _status;
    private string _lastFilePath;
    private TitleManager _runtimeTitles;

    [MenuItem("Bitlings/Tools/Balance Simulator")]
    public static void Open()
    {
        GetWindow<BalanceSimulatorWindow>(true, "Balance Simulator", true);
    }

    private void OnEnable()
    {
        if (_library == null)
            _library = FindLibraryAsset();
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("Balance Simulator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Runs quick headless simulations using stat-driven win probabilities. Results are written to Logs as JSON for review.", MessageType.Info);

        _library = (MonsterLibrarySO)EditorGUILayout.ObjectField("Library (optional)", _library, typeof(MonsterLibrarySO), false);
        _monsterA = (MonsterDataSO)EditorGUILayout.ObjectField("Monster A", _monsterA, typeof(MonsterDataSO), false);
        DrawTitlePicker("Title A", ref _titleA, _monsterA);
        DrawTierTitlePickers("Titles A (per tier)", _monsterA, _levelA, _titlesA);

        _aVersusAll = EditorGUILayout.ToggleLeft("A vs All Others", _aVersusAll);
        using (new EditorGUI.DisabledScope(_aVersusAll))
        {
            _monsterB = (MonsterDataSO)EditorGUILayout.ObjectField("Monster B", _monsterB, typeof(MonsterDataSO), false);
            DrawTitlePicker("Title B", ref _titleB, _monsterB);
            DrawTierTitlePickers("Titles B (per tier)", _monsterB, _levelB, _titlesB);
        }

        if (_aVersusAll) _titleB = null;
        if (_aVersusAll) _titlesB.Clear();

        if (_aVersusAll)
            _useOpponentDefaultTitle = EditorGUILayout.ToggleLeft("Use opponent default title (first unlocked tier choice)", _useOpponentDefaultTitle);

        _levelA = Mathf.Max(1, EditorGUILayout.IntField("Level A", _levelA));
        using (new EditorGUI.DisabledScope(_aVersusAll))
        {
            _levelB = Mathf.Max(1, EditorGUILayout.IntField("Level B", _levelB));
        }

        _iterations = Mathf.Max(1, EditorGUILayout.IntField("Iterations per matchup", _iterations));
        _baseSeed = EditorGUILayout.IntField("Base RNG Seed", _baseSeed);

        if (GUILayout.Button("Run Simulation"))
            RunSimulation();

        if (!string.IsNullOrEmpty(_status))
            EditorGUILayout.HelpBox(_status, MessageType.None);

        if (!string.IsNullOrEmpty(_lastFilePath))
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("Reveal Last JSON"))
                EditorUtility.RevealInFinder(_lastFilePath);
        }

        EditorGUILayout.EndScrollView();
    }

    private void RunSimulation()
    {
        _status = string.Empty;

        if (_monsterA == null)
        {
            _status = "Select Monster A.";
            return;
        }

        if (_aVersusAll)
        {
            if (_library == null || _library.monsters == null || _library.monsters.Length == 0)
            {
                _status = "A vs All requires a MonsterLibrary asset.";
                return;
            }
        }
        else if (_monsterB == null)
        {
            _status = "Select Monster B or enable A vs All.";
            return;
        }

        var output = new RunOutput
        {
            createdUtc = DateTime.UtcNow.ToString("o"),
            mode = "HeadlessBattleApprox",
            baseSeed = _baseSeed
        };

        if (_aVersusAll)
        {
            foreach (var def in _library.All)
            {
                if (def == null || def == _monsterA) continue;
                var opponentTitles = _useOpponentDefaultTitle ? BuildDefaultTitles(def, _levelB) : _titlesB;
                var pr = SimulatePairInternal(_monsterA, _levelA, def, _levelB, opponentTitles, _iterations, _baseSeed);
                output.pairs.Add(pr);
            }
        }
        else
        {
            var pr = SimulatePairInternal(_monsterA, _levelA, _monsterB, _levelB, _titlesB, _iterations, _baseSeed);
            output.pairs.Add(pr);
        }

        string logsDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "BalanceSim"));
        Directory.CreateDirectory(logsDir);

        string fileName = $"balance_sim_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        string path = Path.Combine(logsDir, fileName);

        var json = JsonUtility.ToJson(output, true);
        File.WriteAllText(path, json);

        _lastFilePath = path;
        _status = $"Sim complete. Saved to {path}";
        Debug.Log($"[BalanceSimulator] Wrote results to {path}");
    }

    private PairResult SimulatePairInternal(MonsterDataSO a, int levelA, MonsterDataSO b, int levelB, List<TitleSO> titlesB, int iterations, int seed)
    {
        int winsA = 0;
        int winsB = 0;
        EnsureTitleRuntime();

        var stats = BuildInput(a, levelA, b, levelB, _titleA, _titleB, _titlesA, titlesB, seed);
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
            monsterA = SafeName(a),
            monsterB = SafeName(b),
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
            titleAId = _titleA ? _titleA.titleId : null,
            titleBId = FirstTitleId(stats.titlesB) ?? (_titleB ? _titleB.titleId : null),
            titlesA = ToIds(stats.titlesA),
            titlesB = ToIds(stats.titlesB)
        };
    }

    private (HeadlessBattle.Input input, float offenseMul, float defenseMul, float incomingTypeResistMul, List<TitleSO> titlesA, List<TitleSO> titlesB) BuildInput(
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
        // Stable synthetic ids so titles can bind to combat context
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

    private static string SafeName(MonsterDataSO def)
    {
        if (def == null) return "NULL";
        if (!string.IsNullOrEmpty(def.id)) return def.id;
        return def.name;
    }

    private static List<TitleSO> GetTitleOptions(MonsterDataSO def)
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

    private void DrawTitlePicker(string label, ref TitleSO selected, MonsterDataSO def)
    {
        var options = GetTitleOptions(def);
        if (options.Count == 0) selected = null;
        var names = new string[options.Count + 1];
        names[0] = "None";
        for (int i = 0; i < options.Count; i++)
        {
            var t = options[i];
            names[i + 1] = t ? (!string.IsNullOrEmpty(t.displayName) ? t.displayName : t.titleId) : "";
        }

        int current = 0;
        if (selected)
        {
            int idx = options.IndexOf(selected);
            if (idx >= 0) current = idx + 1;
        }

        int next = EditorGUILayout.Popup(label, current, names);
        selected = (next <= 0 || next > options.Count) ? null : options[next - 1];
    }

    private void DrawTierTitlePickers(string label, MonsterDataSO def, int level, List<TitleSO> selections)
    {
        if (def == null || def.titleTrack == null || def.titleTrack.tiers == null)
        {
            selections.Clear();
            return;
        }

        var tiers = def.titleTrack.tiers;
        while (selections.Count < tiers.Count) selections.Add(null);
        while (selections.Count > tiers.Count) selections.RemoveAt(selections.Count - 1);
        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            if (tier == null) { selections[i] = null; continue; }

            bool unlocked = level >= Mathf.Max(1, tier.levelRequired);
            var choices = tier.unlockChoices ?? new List<TitleSO>();

            var names = new string[choices.Count + 1];
            names[0] = unlocked ? "None" : $"Locked (Lv {tier.levelRequired})";
            for (int j = 0; j < choices.Count; j++)
            {
                var t = choices[j];
                names[j + 1] = t ? (!string.IsNullOrEmpty(t.displayName) ? t.displayName : t.titleId) : "";
            }

            int current = 0;
            if (selections[i])
            {
                int idx = choices.IndexOf(selections[i]);
                if (idx >= 0) current = idx + 1;
            }

            using (new EditorGUI.DisabledScope(!unlocked))
            {
                int next = EditorGUILayout.Popup($"Tier {i + 1}", current, names);
                selections[i] = (!unlocked || next <= 0 || next > choices.Count) ? null : choices[next - 1];
            }
        }
    }

    private static List<TitleSO> BuildTitleListFor(MonsterDataSO def, int level, TitleSO single, List<TitleSO> tierSelections)
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

    private static List<TitleSO> BuildDefaultTitles(MonsterDataSO def, int level)
    {
        var list = new List<TitleSO>();
        if (def == null || def.titleTrack == null || def.titleTrack.tiers == null) return list;

        var tiers = def.titleTrack.tiers;
        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            if (tier == null || tier.unlockChoices == null || tier.unlockChoices.Count == 0) { list.Add(null); continue; }
            bool unlocked = level >= Mathf.Max(1, tier.levelRequired);
            list.Add(unlocked ? tier.unlockChoices[0] : null);
        }

        return list;
    }

    private static string[] ToIds(List<TitleSO> titles)
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

    private static string FirstTitleId(List<TitleSO> titles)
    {
        if (titles == null) return null;
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (t != null && !string.IsNullOrEmpty(t.titleId)) return t.titleId;
        }
        return null;
    }

    private void EnsureTitleRuntime()
    {
        if (_runtimeTitles != null && TitleManager.I == _runtimeTitles) return;

        if (TitleManager.I != null)
        {
            _runtimeTitles = TitleManager.I;
            return;
        }

        var found = Object.FindFirstObjectByType<TitleManager>(FindObjectsInactive.Include);
        if (found != null)
        {
            _runtimeTitles = found;
            return;
        }

        var go = new GameObject("TitleManager (Editor)") { hideFlags = HideFlags.HideAndDontSave };
        _runtimeTitles = go.AddComponent<TitleManager>();
    }

    private static MonsterLibrarySO FindLibraryAsset()
    {
        var guids = AssetDatabase.FindAssets("t:MonsterLibrarySO");
        if (guids == null || guids.Length == 0) return null;
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<MonsterLibrarySO>(path);
    }
}
