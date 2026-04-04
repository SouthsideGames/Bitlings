using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class StatBucketPanelUI : MonoBehaviour
{
    [Header("UI - Header")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI pointsText;
    [SerializeField] private TextMeshProUGUI costText;

    [Header("UI - Buckets")]
    [SerializeField] private GameObject presetsRoot;
    [SerializeField] private Button offenseBtn;
    [SerializeField] private Button defenseBtn;
    [SerializeField] private Button utilityBtn;
    [SerializeField] private Button balanceBtn;
    [SerializeField] private Button speedBtn;

    [Header("UI - Stats Row (−/+)")]
    [SerializeField] private TextMeshProUGUI hpVal;
    [SerializeField] private Button hpMinus;
    [SerializeField] private Button hpPlus;

    [SerializeField] private TextMeshProUGUI atkVal;
    [SerializeField] private Button atkMinus;
    [SerializeField] private Button atkPlus;

    [SerializeField] private TextMeshProUGUI defVal;
    [SerializeField] private Button defMinus;
    [SerializeField] private Button defPlus;

    [SerializeField] private TextMeshProUGUI spdVal;
    [SerializeField] private Button spdMinus;
    [SerializeField] private Button spdPlus;

    [Header("UI - Footer")]
    [SerializeField] private Button levelUpBtn;
    [SerializeField] private Button confirmBtn;
    [SerializeField] private Button closeBtn;

    [Header("Config")]
    [SerializeField, Min(1)] private int pointsPerLevel = 3;

    private TokenEconomySO tokenEconomy;
    private LevelCostCurveSO levelCostCurve;
    private bool _isConfirming;
    private OwnedMonsterData _m;

    // Display breakdown
    private int _monHP, _monATK, _monDEF, _monSPD;
    private int _trainHP, _trainATK, _trainDEF, _trainSPD;
    private int _baseHP, _baseATK, _baseDEF, _baseSPD;
    private int _allocHp, _allocAtk, _allocDef, _allocSpd;
    private int _points;
    private int _nextCostToLevel;
    const string GREEN = "#3CDE74";

    private void Awake()
    {
        AutoLoadSOsIfMissing();
        Wire();
        ClearAlloc();
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;

        RefreshPresetVisibility();
        RefreshUI();
    }

    private void OnDisable()
    {
        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) AutoLoadSOsIfMissing();
    }
#endif

    private void AutoLoadSOsIfMissing()
    {
        if (!tokenEconomy)
        {
            var direct = Resources.Load<TokenEconomySO>("TokenEconomy");
            if (direct) tokenEconomy = direct;
            else
            {
                var all = Resources.LoadAll<TokenEconomySO>("");
                if (all != null && all.Length > 0) tokenEconomy = all[0];
            }
        }
        if (!levelCostCurve)
        {
            var all = Resources.LoadAll<LevelCostCurveSO>("");
            if (all != null && all.Length > 0) levelCostCurve = all[0];
        }
    }

    private void Wire()
    {
        if (hpMinus)  hpMinus.onClick.AddListener(() => AddAlloc(ref _allocHp, -1));
        if (hpPlus)   hpPlus.onClick.AddListener(() => AddAlloc(ref _allocHp, +1));
        if (atkMinus) atkMinus.onClick.AddListener(() => AddAlloc(ref _allocAtk, -1));
        if (atkPlus)  atkPlus.onClick.AddListener(() => AddAlloc(ref _allocAtk, +1));
        if (defMinus) defMinus.onClick.AddListener(() => AddAlloc(ref _allocDef, -1));
        if (defPlus)  defPlus.onClick.AddListener(() => AddAlloc(ref _allocDef, +1));
        if (spdMinus) spdMinus.onClick.AddListener(() => AddAlloc(ref _allocSpd, -1));
        if (spdPlus)  spdPlus.onClick.AddListener(() => AddAlloc(ref _allocSpd, +1));

        if (offenseBtn) offenseBtn.onClick.AddListener(() => OnPresetClicked("Offense"));
        if (defenseBtn) defenseBtn.onClick.AddListener(() => OnPresetClicked("Defense"));
        if (utilityBtn) utilityBtn.onClick.AddListener(() => OnPresetClicked("Utility"));
        if (balanceBtn) balanceBtn.onClick.AddListener(() => OnPresetClicked("Balance"));
        if (speedBtn)   speedBtn.onClick.AddListener(() => OnPresetClicked("Speed"));

        if (levelUpBtn) levelUpBtn.onClick.AddListener(OnClickLevelUp);
        if (confirmBtn) confirmBtn.onClick.AddListener(ConfirmSpend);
        if (closeBtn)   closeBtn.onClick.AddListener(ClosePanel);
    }

    private void ClosePanel() => gameObject.SetActive(false);

    private void ClearAlloc()
    {
        _allocHp = _allocAtk = _allocDef = _allocSpd = 0;
        _points = 0;
        _nextCostToLevel = CalcNextCostForCurrentLevel();
        RefreshUI();
    }

    private int CalcNextCostForCurrentLevel()
    {
        if (_m == null || levelCostCurve == null) return 1;
        return Mathf.Max(1, levelCostCurve.CoresToNextLevel(Mathf.Max(1, _m.level)));
    }

    public void OpenFor(OwnedMonsterData m)
    {
        if (m == null || string.IsNullOrEmpty(m.monsterId))
        {
            gameObject.SetActive(false);
            return;
        }

        _m = XPManager.Resolve(m);

        ComputeCurrentStats();
        ClearAlloc();

        var def = MonsterLibraryLocator.GetById(_m.monsterId);
        if (nameText)
            nameText.text = def ? def.displayName : _m.monsterId;

        gameObject.SetActive(true);
        RefreshPresetVisibility();
        RefreshUI();
    }

    private void ComputeCurrentStats()
    {
        _monHP = _monATK = _monDEF = _monSPD = 0;
        _trainHP = _trainATK = _trainDEF = _trainSPD = 0;
        _baseHP = _baseATK = _baseDEF = _baseSPD = 0;

        if (_m == null || string.IsNullOrEmpty(_m.monsterId)) return;

        var stats = ProgressionStatCalc.Get(_m);

        _monHP  = stats.basePlusLevelHP;
        _monATK = stats.basePlusLevelATK;
        _monDEF = stats.basePlusLevelDEF;
        _monSPD = stats.basePlusLevelSPD;

        _trainHP  = stats.trainingHP;
        _trainATK = stats.trainingATK;
        _trainDEF = stats.trainingDEF;
        _trainSPD = stats.trainingSPD;

        _baseHP  = _monHP  + _trainHP;
        _baseATK = _monATK + _trainATK;
        _baseDEF = _monDEF + _trainDEF;
        _baseSPD = _monSPD + _trainSPD;
    }

    private void RefreshUI()
    {
        if (_m != null && nameText)
            nameText.text = $"{_m.monsterId}  •  Lv {_m.level}";

        int unspent = _m != null ? Mathf.Max(0, _m.unspentStatPoints) : 0;
        int remaining = Mathf.Max(0, unspent - _points);

        if (pointsText)
            pointsText.text = $"Points used: {_points}  •  Unspent: {remaining}";

        int haveCores = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCore) : 0;
        _nextCostToLevel = CalcNextCostForCurrentLevel();

        if (costText)
        {
            if (_m == null)
                costText.text = "Next Lv: -- / -- GC";
            else
                costText.text = $"Next Lv: {haveCores}/{_nextCostToLevel} GC";
        }

        int hpDelta  = _allocHp  * (tokenEconomy ? tokenEconomy.hpPerCore  : 1);
        int atkDelta = _allocAtk * (tokenEconomy ? tokenEconomy.atkPerCore : 1);
        int defDelta = _allocDef * (tokenEconomy ? tokenEconomy.defPerCore : 1);
        int spdDelta = _allocSpd * (tokenEconomy ? tokenEconomy.spdPerCore : 1);

        SetStatLabelBreakdown(hpVal,  _monHP,  _trainHP,  hpDelta);
        SetStatLabelBreakdown(atkVal, _monATK, _trainATK, atkDelta);
        SetStatLabelBreakdown(defVal, _monDEF, _trainDEF, defDelta);
        SetStatLabelBreakdown(spdVal, _monSPD, _trainSPD, spdDelta);

        bool hasPointsLeft = remaining > 0;

        if (hpPlus)   hpPlus.interactable   = hasPointsLeft;
        if (atkPlus)  atkPlus.interactable  = hasPointsLeft;
        if (defPlus)  defPlus.interactable  = hasPointsLeft;
        if (spdPlus)  spdPlus.interactable  = hasPointsLeft;

        if (hpMinus)  hpMinus.interactable  = _allocHp  > 0;
        if (atkMinus) atkMinus.interactable = _allocAtk > 0;
        if (defMinus) defMinus.interactable = _allocDef > 0;
        if (spdMinus) spdMinus.interactable = _allocSpd > 0;

        if (confirmBtn)
            confirmBtn.interactable = _points > 0;

        if (levelUpBtn)
        {
            bool canLevel = _m != null
                            && _m.level < LevelRules.MaxLevel
                            && haveCores >= _nextCostToLevel;
            levelUpBtn.interactable = canLevel;

            var label = levelUpBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (label)
                label.text = $"Level Up ({_nextCostToLevel} GC)";
        }
    }

    private void SetStatLabelBreakdown(TextMeshProUGUI label, int monsterBase, int trainingSaved, int allocDelta)
    {
        if (!label) return;

        int baseVal = Mathf.Max(0, monsterBase);
        int trainingVal = Mathf.Max(0, trainingSaved) + Mathf.Max(0, allocDelta);
        int total = baseVal + trainingVal;

        label.text = $"{total} ({baseVal} + <color={GREEN}>{trainingVal}</color>)";
    }

    private void AddAlloc(ref int field, int delta)
    {
        if (_m == null) return;

        int unspent = Mathf.Max(0, _m.unspentStatPoints);
        int remaining = Mathf.Max(0, unspent - _points);

        if (delta > 0 && remaining <= 0)
            return;

        int next = field + delta;
        if (next < 0) return;

        field = next;
        _points = _allocHp + _allocAtk + _allocDef + _allocSpd;
        RefreshUI();
    }

    private void OnPresetClicked(string bucketId)
    {
        if (_m != null)
            _m.lastBucketId = bucketId;
        ApplyPreset(bucketId);
    }

    private void ApplyPreset(string bucketId)
    {
        if (_m == null) return;

        int unspent = Mathf.Max(0, _m.unspentStatPoints);
        int remaining = Mathf.Max(0, unspent - _points);
        if (remaining <= 0) return;

        int toSpend = Mathf.Min(5, remaining);

        // Get the monster's personality to influence preset distribution
        var personality = GetMonsterPersonality();
        
        switch (bucketId)
        {
            case "Offense":
                ApplyOffensePresetWithPersonality(toSpend, personality);
                break;
            case "Defense":
                ApplyDefensePresetWithPersonality(toSpend, personality);
                break;
            case "Speed":
                ApplySpeedPresetWithPersonality(toSpend, personality);
                break;
            case "Balance":
                ApplyBalancePresetWithPersonality(toSpend, personality);
                break;
            case "Utility":
                ApplyUtilityPresetWithPersonality(toSpend, personality);
                break;
        }
    }

    private MonsterPersonalitySO GetMonsterPersonality()
    {
        if (_m == null || string.IsNullOrEmpty(_m.monsterId)) return null;
        var def = MonsterLibraryLocator.GetById(_m.monsterId);
        return def != null ? def.Personality : null;
    }

    private void ApplyOffensePresetWithPersonality(int toSpend, MonsterPersonalitySO personality)
    {
        // Offense preset: prioritizes ATK
        // Personality modifiers:
        // - Offensive: pure ATK (personality aligned)
        // - Defensive: split to DEF slightly (opposite personality)
        // - Evasive: add SPD boost (tactical adjustment)
        // - Support: add HP slightly (tactical adjustment)
        
        if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Defensive)
        {
            // Defensive monsters still attack, but they're more balanced
            for (int i = 0; i < toSpend; i++)
            {
                if (i % 3 == 0) 
                    AddAlloc(ref _allocDef, +1);  // 1/3 to defense
                else 
                    AddAlloc(ref _allocAtk, +1);  // 2/3 to attack
            }
        }
        else if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Evasive)
        {
            // Evasive monsters learn to be faster offenders
            for (int i = 0; i < toSpend; i++)
            {
                if (i % 3 == 0)
                    AddAlloc(ref _allocSpd, +1);  // 1/3 to speed
                else
                    AddAlloc(ref _allocAtk, +1);  // 2/3 to attack
            }
        }
        else if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Support)
        {
            // Support monsters gain balanced offense
            for (int i = 0; i < toSpend; i++)
            {
                if (i % 3 == 0)
                    AddAlloc(ref _allocHp, +1);   // 1/3 to HP
                else
                    AddAlloc(ref _allocAtk, +1);  // 2/3 to attack
            }
        }
        else
        {
            // Offensive, Tactical, Reactive, Chaotic: pure offensive buildup
            for (int i = 0; i < toSpend; i++) 
                AddAlloc(ref _allocAtk, +1);
        }
    }

    private void ApplyDefensePresetWithPersonality(int toSpend, MonsterPersonalitySO personality)
    {
        // Defense preset: prioritizes DEF
        // Personality modifiers:
        // - Defensive: pure DEF (personality aligned)
        // - Offensive: add ATK boost (opposite personality)
        // - Evasive: add SPD instead of pure DEF (tactical adjustment)
        // - Support: pure DEF (personality aligned)
        
        if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Offensive)
        {
            // Offensive monsters can't stay purely defensive
            for (int i = 0; i < toSpend; i++)
            {
                if (i % 3 == 0)
                    AddAlloc(ref _allocAtk, +1);  // 1/3 to attack
                else
                    AddAlloc(ref _allocDef, +1);  // 2/3 to defense
            }
        }
        else if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Evasive)
        {
            // Evasive monsters dodge instead of tank
            for (int i = 0; i < toSpend; i++)
            {
                if (i % 2 == 0)
                    AddAlloc(ref _allocSpd, +1);  // 1/2 to speed
                else
                    AddAlloc(ref _allocDef, +1);  // 1/2 to defense
            }
        }
        else
        {
            // Defensive, Support, Tactical, Reactive, Chaotic: pure defensive buildup
            for (int i = 0; i < toSpend; i++) 
                AddAlloc(ref _allocDef, +1);
        }
    }

    private void ApplySpeedPresetWithPersonality(int toSpend, MonsterPersonalitySO personality)
    {
        // Speed preset: prioritizes SPD
        // Personality modifiers:
        // - Evasive: pure SPD (personality aligned)
        // - Offensive: add ATK (tactical adjustment, speed for quick strikes)
        // - Defensive: add DEF (tactical adjustment for evasive defense)
        // - Tactical/Reactive: benefit from speed
        
        if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Offensive)
        {
            // Offensive monsters use speed for quick strikes
            for (int i = 0; i < toSpend; i++)
            {
                if (i % 3 == 0)
                    AddAlloc(ref _allocAtk, +1);  // 1/3 to attack
                else
                    AddAlloc(ref _allocSpd, +1);  // 2/3 to speed
            }
        }
        else if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Defensive)
        {
            // Defensive monsters use speed defensively
            for (int i = 0; i < toSpend; i++)
            {
                if (i % 3 == 0)
                    AddAlloc(ref _allocDef, +1);  // 1/3 to defense
                else
                    AddAlloc(ref _allocSpd, +1);  // 2/3 to speed
            }
        }
        else
        {
            // Evasive, Tactical, Reactive, Support, Chaotic: pure speed buildup
            for (int i = 0; i < toSpend; i++) 
                AddAlloc(ref _allocSpd, +1);
        }
    }

    private void ApplyBalancePresetWithPersonality(int toSpend, MonsterPersonalitySO personality)
    {
        // Balance preset: HP, ATK, DEF in equal parts
        // Personality modifiers:
        for (int i = 0; i < toSpend; i++)
        {
            int step = i % 3;
            
            if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Offensive)
            {
                // Offensive: lean more toward attack (ATK x2, HP, DEF in rotation)
                if (step == 0) 
                    AddAlloc(ref _allocAtk, +1);
                else if (step == 1) 
                    AddAlloc(ref _allocAtk, +1);
                else 
                    AddAlloc(ref _allocHp, +1);
            }
            else if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Defensive)
            {
                // Defensive: lean more toward defense (DEF x2, HP, ATK in rotation)
                if (step == 0) 
                    AddAlloc(ref _allocHp, +1);
                else if (step == 1) 
                    AddAlloc(ref _allocDef, +1);
                else 
                    AddAlloc(ref _allocDef, +1);
            }
            else if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Evasive)
            {
                // Evasive: replace defense with speed in balance (HP, ATK, SPD)
                if (step == 0) 
                    AddAlloc(ref _allocHp, +1);
                else if (step == 1) 
                    AddAlloc(ref _allocAtk, +1);
                else 
                    AddAlloc(ref _allocSpd, +1);
            }
            else if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Support)
            {
                // Support: HP emphasis (HP x2, ATK, DEF in rotation)
                if (step == 0) 
                    AddAlloc(ref _allocHp, +1);
                else if (step == 1) 
                    AddAlloc(ref _allocHp, +1);
                else 
                    AddAlloc(ref _allocDef, +1);
            }
            else
            {
                // Tactical, Reactive, Chaotic: standard balance (HP, ATK, DEF)
                if (step == 0) 
                    AddAlloc(ref _allocHp, +1);
                else if (step == 1) 
                    AddAlloc(ref _allocAtk, +1);
                else 
                    AddAlloc(ref _allocDef, +1);
            }
        }
    }

    private void ApplyUtilityPresetWithPersonality(int toSpend, MonsterPersonalitySO personality)
    {
        // Utility preset: HP and SPD alternating
        // Personality modifiers:
        for (int i = 0; i < toSpend; i++)
        {
            if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Evasive)
            {
                // Evasive: pure speed utility (SPD x2, HP)
                if (i % 3 == 2)
                    AddAlloc(ref _allocHp, +1);
                else
                    AddAlloc(ref _allocSpd, +1);
            }
            else if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Support)
            {
                // Support: pure HP utility (HP x2, SPD)
                if (i % 3 == 2)
                    AddAlloc(ref _allocSpd, +1);
                else
                    AddAlloc(ref _allocHp, +1);
            }
            else if (personality != null && personality.group == MonsterPersonalitySO.PersonalityGroup.Offensive)
            {
                // Offensive: speed utility over raw HP (SPD, ATK, HP in rotation)
                if (i % 3 == 0) 
                    AddAlloc(ref _allocSpd, +1);
                else if (i % 3 == 1) 
                    AddAlloc(ref _allocAtk, +1);
                else 
                    AddAlloc(ref _allocHp, +1);
            }
            else
            {
                // Standard utility: HP and SPD
                if (i % 2 == 0) 
                    AddAlloc(ref _allocHp, +1);
                else 
                    AddAlloc(ref _allocSpd, +1);
            }
        }
    }

    private void OnClickLevelUp()
    {
        if (_m == null || levelCostCurve == null) return;
        if (_m.level >= LevelRules.MaxLevel) return;

        _m = XPManager.Resolve(_m);
        if (_m == null) return;

        bool success = XPManager.TryManualLevelUp(
            _m,
            pointsPerLevel,
            levelCostCurve,
            monsterLibrary: null
        );

        if (!success) return;

        string key = !string.IsNullOrEmpty(_m.ownedUID) ? _m.ownedUID : _m.monsterId;
        GameEvents.MonsterLeveled?.Invoke(key, _m.level);

        ComputeCurrentStats();
        _nextCostToLevel = CalcNextCostForCurrentLevel();
        RefreshUI();
        AudioManager.I?.PlaySfx(SfxType.LevelUp);
    }

    private void ConfirmSpend()
    {
        if (_isConfirming) return;
        _isConfirming = true;

        try
        {
            // Keep the panel open in ALL cases.
            if (_m == null)
            {
                if (confirmBtn) confirmBtn.interactable = true;
                return;
            }

            if (_points <= 0)
            {
                // Nothing allocated — do not close, just inform.
                GameEvents.RaiseToast("No points allocated");
                if (confirmBtn) confirmBtn.interactable = true;
                return;
            }

            if (confirmBtn) confirmBtn.interactable = false;

            // Resolve to canonical save instance
            _m = XPManager.Resolve(_m);
            if (_m == null)
            {
                // Keep open; fail safely.
                GameEvents.RaiseToast("Could not apply changes");
                if (confirmBtn) confirmBtn.interactable = true;
                return;
            }

            // Apply training bonus (EV-like)
            TrainingBonus delta = new TrainingBonus
            {
                hp  = _allocHp  * (tokenEconomy ? tokenEconomy.hpPerCore  : 1),
                atk = _allocAtk * (tokenEconomy ? tokenEconomy.atkPerCore : 1),
                def = _allocDef * (tokenEconomy ? tokenEconomy.defPerCore : 1),
                spd = _allocSpd * (tokenEconomy ? tokenEconomy.spdPerCore : 1)
            };

            // Preserve existing behavior: apply + save (may fire team changed internally).
            XPManager.ApplyTrainingAndSave(_m, delta);

            // Spend stat points on the canonical instance
            _m.unspentStatPoints = Mathf.Max(0, _m.unspentStatPoints - _points);

            // Ensure owned list entry matches canonical instance (extra safety)
            var data = SaveManager.Data;
            if (data != null && data.owned != null)
            {
                OwnedMonsterData match = null;

                if (!string.IsNullOrEmpty(_m.ownedUID))
                    match = data.owned.Find(o => o != null && o.ownedUID == _m.ownedUID);
                else
                    match = data.owned.Find(o => o != null && o.monsterId == _m.monsterId);

                if (match != null)
                    match.unspentStatPoints = _m.unspentStatPoints;
            }

            // Persist final state (including unspent points decrement)
            SaveManager.Save();

            // IMPORTANT: raise after BOTH training AND unspent points are correct
            GameEvents.OnTeamChanged?.Invoke();

            GameEvents.RaiseToast("STATS APPLIED");

            // Refresh UI and leave panel open
            ComputeCurrentStats();
            ClearAlloc();   // also refreshes UI
            RefreshUI();
        }
        finally
        {
            _isConfirming = false;
            if (confirmBtn) confirmBtn.interactable = true;
        }
    }


    private void HandleFeatureUnlocked(FeatureId feature)
    {
        if (feature == FeatureId.AutoGrowth_UsePresets)
            RefreshPresetVisibility();
    }

    private void RefreshPresetVisibility()
    {
        if (!presetsRoot) return;

        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(FeatureId.AutoGrowth_UsePresets);

        presetsRoot.SetActive(unlocked);
    }
}
