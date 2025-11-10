using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Turn-based battle flow that matches the provided BattleBoosterController + TitlesAdapter APIs.
/// Player actions: Attack / Defend / Focus / Run / Boosters (via UI buttons calling UseBoosterFromUI).
/// </summary>
[DisallowMultipleComponent]
public class TurnBattleManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Public entry points
    // ─────────────────────────────────────────────────────────────────────────────
    public void Begin(MonsterDataSO wild, int level, Action<BattleResult> onEnded)
    {
        if (_running) StopAllCoroutines();
        _onEnd = onEnded;
        SetupTeamsAndWild(wild, level);
        StartCoroutine(Co_RevealPanelsThenStart(0.25f));
    }

    public void SetAutoMode(bool on)
    {
        _autoMode = on;
        BattleLogger.Log(on ? "Auto mode enabled." : "Auto mode disabled.");
    }

    /// <summary>Called by booster buttons (Attack/Health/Speed/Resist).</summary>
    public void UseBoosterFromUI(BoosterType type)
    {
        if (!_running || !_playerTurn || _pendingPlayerAction != BattleAction.None) return;
        if (!BattleBoosterController.I) { BattleLogger.Log("Booster system not present."); return; }

        var hooks = BuildRuntimeHooks();
        if (!BattleBoosterController.I.TryUse(type, hooks, out var msg))
        {
            if (!string.IsNullOrEmpty(msg)) BattleLogger.Log(msg);
            return;
        }

        BattleLogger.Log(BoosterUseText(type));
        if (!string.IsNullOrEmpty(msg)) BattleLogger.Log(msg);
        _pendingPlayerAction = BattleAction.BoosterUsed; // booster consumes the turn
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Inspector UI (optional)
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Wild UI")]
    [SerializeField] private GameObject wildPanel;
    [SerializeField] private Slider wildHPBar;
    [SerializeField] private Image wildIcon;
    [SerializeField] private TextMeshProUGUI wildNameText;
    [SerializeField] private TextMeshProUGUI wildLevelText;
    [SerializeField] private TextMeshProUGUI wildIdText;
    [SerializeField] private TextMeshProUGUI wildTypeText;
    [SerializeField] private TextMeshProUGUI wildRarityText;
    [SerializeField] private TextMeshProUGUI wildHPText;
    [SerializeField] private TextMeshProUGUI wildATKText;
    [SerializeField] private TextMeshProUGUI wildDEFText;
    [SerializeField] private TextMeshProUGUI wildSPDText;

    [Header("Player UI")]
    [SerializeField] private GameObject playerPanel;
    [SerializeField] private Slider playerHPBar;
    [SerializeField] private Image playerIcon;
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI playerLevelText;
    [SerializeField] private TextMeshProUGUI playerIdText;
    [SerializeField] private TextMeshProUGUI playerTypeText;
    [SerializeField] private TextMeshProUGUI playerRarityText;
    [SerializeField] private TextMeshProUGUI playerHPText;
    [SerializeField] private TextMeshProUGUI playerATKText;
    [SerializeField] private TextMeshProUGUI playerDEFText;
    [SerializeField] private TextMeshProUGUI playerSPDText;

    [Header("Bench UI")]
    [SerializeField] private Button benchBtn1;
    [SerializeField] private Button benchBtn2;
    [SerializeField] private Image benchImg1;
    [SerializeField] private Image benchImg2;
    [SerializeField] private TextMeshProUGUI benchHPText1;
    [SerializeField] private TextMeshProUGUI benchHPText2;

    [Header("Turn pacing (unscaled)")]
    [SerializeField, Min(0.05f)] private float beginRoundDelay = 0.12f;
    [SerializeField, Min(0.05f)] private float stepPause = 0.18f;
    [SerializeField, Min(0.05f)] private float endRoundDelay = 0.35f;

    [Header("Combat Tunables")]
    [Range(0f, 1f)][SerializeField] private float critChancePlayer = 0.10f;
    [Range(0f, 1f)][SerializeField] private float critChanceWild = 0.08f;
    [SerializeField] private float critMultiplier = 1.8f;
    [SerializeField] private bool showEffectivenessText = true;

    // ─────────────────────────────────────────────────────────────────────────────
    // State
    // ─────────────────────────────────────────────────────────────────────────────
    private Action<BattleResult> _onEnd;
    private bool _running, _playerTurn, _autoMode;
    private System.Random _rng = new System.Random();

    private MonsterDataSO _wildDef;
    private int   _wildLevel;
    private float _wildMaxHP, _wildHP, _wildAtk;

    private int _teamCount, _active; // active team index
    private MonsterDataSO[] _teamDefs;
    private int[] _teamLvls;
    private float[] _teamMaxHP;
    private float[] _teamHP;
    private string[] _teamIds;

    private int _turnIndex;
    private float _startTime;

    private BattleAction _pendingPlayerAction = BattleAction.None;

    // Lightweight “stances”
    private bool _playerDefendGuard;
    private int  _playerFocusTurns;
    private bool _enemyDefendGuard;
    private int  _enemyFocusTurns;

    // ─────────────────────────────────────────────────────────────────────────────
    // Types
    // ─────────────────────────────────────────────────────────────────────────────
    private enum BattleAction { None = 0, Attack, Defend, Focus, Run, BoosterUsed }

    private struct AIDecisionWeights { public int attack, defend, focus, run; }

    // ─────────────────────────────────────────────────────────────────────────────
    // Setup
    // ─────────────────────────────────────────────────────────────────────────────
    private void SetupTeamsAndWild(MonsterDataSO wild, int level)
    {
        var roster = SaveManager.Data?.team;
        if (roster == null || roster.Count == 0)
        {
            BattleLogger.Log("No team available.");
            HardFail(wild, level);
            return;
        }

        _wildDef = wild;
        _wildLevel = Mathf.Max(1, level);
        _wildMaxHP = BattleCalc.CalcHP(_wildDef, _wildLevel) * 0.9f;
        _wildHP = _wildMaxHP;
        _wildAtk = BattleCalc.CalcBaseAttack(_wildDef, _wildLevel, 0, 0) * 0.9f;

        if (wildIcon) wildIcon.sprite = _wildDef ? _wildDef.icon : null;
        if (wildNameText) wildNameText.text = _wildDef ? _wildDef.displayName : "Wild";
        if (wildLevelText) wildLevelText.text = $"Lv {_wildLevel}";
        if (wildHPBar) { wildHPBar.maxValue = _wildMaxHP; wildHPBar.value = _wildHP; }
        UpdateWildInfoUI();

        _teamCount = Mathf.Min(3, roster.Count);
        _teamDefs  = new MonsterDataSO[_teamCount];
        _teamLvls  = new int[_teamCount];
        _teamMaxHP = new float[_teamCount];
        _teamHP    = new float[_teamCount];
        _teamIds   = new string[_teamCount];

        for (int i = 0; i < _teamCount; i++)
        {
            var owned = roster[i];
            var def = MonsterLibraryLocator.GetById(owned.monsterId);
            _teamIds[i] = owned.monsterId;
            _teamDefs[i] = def;
            _teamLvls[i] = owned.level;
            _teamMaxHP[i] = BattleCalc.CalcHP(def, owned.level);
            int savedHP = owned.currentHP;
            _teamHP[i] = (savedHP >= 0) ? Mathf.Clamp(savedHP, 0, (int)_teamMaxHP[i]) : _teamMaxHP[i];
        }

        _active = -1;
        for (int i = 0; i < _teamCount; i++)
            if (_teamHP[i] > 0f) { _active = i; break; }

        if (_active < 0)
        {
            BattleLogger.Log("Your team is unable to battle.");
            HardFail(wild, level);
            return;
        }

        ApplyActiveToUI();
        PushHPBars();
        RefreshBenchUI();

        _pendingPlayerAction = BattleAction.None;
        _playerDefendGuard = false; _enemyDefendGuard = false;
        _playerFocusTurns  = 0;     _enemyFocusTurns  = 0;
    }

    private void HardFail(MonsterDataSO wild, int level)
    {
        var result = new BattleResult
        {
            victory = false,
            coinsGained = 0,
            wildDef = wild,
            wildLevel = level,
            secondsSurvived = 0f
        };
        _onEnd?.Invoke(result);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Flow
    // ─────────────────────────────────────────────────────────────────────────────
    private IEnumerator Co_RevealPanelsThenStart(float dur)
    {
        if (wildPanel)  wildPanel.SetActive(true);
        if (playerPanel) playerPanel.SetActive(true);

        var cgW = EnsureCanvasGroup(wildPanel);
        var cgP = EnsureCanvasGroup(playerPanel);
        if (cgW) { cgW.alpha = 0f; LeanTween.alphaCanvas(cgW, 1f, dur); }
        if (cgP) { cgP.alpha = 0f; LeanTween.alphaCanvas(cgP, 1f, dur); }
        yield return new WaitForSecondsRealtime(dur);

        StartCoroutine(Co_StartBattleNow());
    }

    private IEnumerator Co_StartBattleNow()
    {
        _turnIndex = 0;
        _running = true;
        _startTime = Time.unscaledTime;

        string vsName = _wildDef ? $"{_wildDef.displayName} (Lv {_wildLevel})" : "Unknown";
        BattleLogger.BeginBattle(vsName);
        BattleLogger.Log(_wildDef ? $"A wild {_wildDef.displayName} (Lv {_wildLevel}) appeared!" : "A wild foe appeared!");

        if (_active >= 0 && _teamIds != null && _active < _teamIds.Length)
            TitlesAdapter.OnBattleStart(_teamIds[_active], _wildDef, _wildLevel);

        StartCoroutine(Co_TurnLoop());
        yield break;
    }

    private IEnumerator Co_TurnLoop()
    {
        while (_running)
        {
            if (_wildHP <= 0.01f) { EndBattle(true); yield break; }
            if (IsTeamKO()) { EndBattle(false); yield break; }

            _turnIndex++;
            TitlesAdapter.OnTurnAdvanced(_turnIndex);

            // turn start → tell boosters whose turn is starting
            int pSpeed = CalcPlayerSpeed();
            int eSpeed = BattleCalc.CalcSpeed(_wildDef, _wildLevel);
            _playerTurn = pSpeed >= eSpeed;
            BattleBoosterController.I?.OnTurnStart(isPlayer: _playerTurn);

            BattleLogger.Log(_playerTurn ? $"{GetName(_active)} acts first!" : $"{_wildDef.displayName} acts first!");
            yield return new WaitForSecondsRealtime(beginRoundDelay);

            if (_playerTurn)
            {
                yield return Co_PlayerPhase();
                if (!_running) yield break;
                if (CheckEnd()) yield break;

                BattleBoosterController.I?.OnTurnEnd();
                yield return new WaitForSecondsRealtime(stepPause);

                yield return Co_EnemyPhase();
                if (!_running) yield break;
                if (CheckEnd()) yield break;
            }
            else
            {
                yield return Co_EnemyPhase();
                if (!_running) yield break;
                if (CheckEnd()) yield break;

                BattleBoosterController.I?.OnTurnEnd();
                yield return new WaitForSecondsRealtime(stepPause);

                yield return Co_PlayerPhase();
                if (!_running) yield break;
                if (CheckEnd()) yield break;
            }

            yield return new WaitForSecondsRealtime(endRoundDelay);
        }
    }

    private bool CheckEnd()
    {
        if (_wildHP <= 0.01f) { BattleLogger.Log("Wild monster fainted!"); EndBattle(true); return true; }
        if (IsTeamKO())       { BattleLogger.Log("Your team is unable to battle!"); EndBattle(false); return true; }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Player / Enemy turns
    // ─────────────────────────────────────────────────────────────────────────────
    private IEnumerator Co_PlayerPhase()
    {
        _pendingPlayerAction = BattleAction.None;

        if (_autoMode)
        {
            // simple auto: try attack booster if ready and not focused, else attack
            if (BattleBoosterController.I != null &&
                BattleBoosterController.I.CanUse(BoosterType.Attack, out _))
            {
                UseBoosterFromUI(BoosterType.Attack);
            }

            if (_pendingPlayerAction == BattleAction.None)
                _pendingPlayerAction = BattleAction.Attack;
        }
        else
        {
            // Wait for UI to set a choice (your buttons call SetPlayerActionX or UseBoosterFromUI)
            float timeout = 12f;
            while (_pendingPlayerAction == BattleAction.None && timeout > 0f && _running)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (_pendingPlayerAction == BattleAction.None) _pendingPlayerAction = BattleAction.Attack;
        }

        switch (_pendingPlayerAction)
        {
            case BattleAction.Attack: yield return Co_PlayerAttack(); break;
            case BattleAction.Defend:
                _playerDefendGuard = true;
                BattleLogger.Log($"{GetName(_active)} braces for impact.");
                break;
            case BattleAction.Focus:
                _playerFocusTurns = 1;
                BattleLogger.Log($"{GetName(_active)} focuses up! Next attack is empowered.");
                break;
            case BattleAction.Run:
                BattleLogger.Log($"{GetName(_active)} fled. Encounter ends.");
                EndBattle(true);
                break;
            case BattleAction.BoosterUsed:
                // already handled/logged in UseBoosterFromUI
                break;
            default:
                BattleLogger.Log("Turn skipped.");
                break;
        }
    }

    private IEnumerator Co_EnemyPhase()
    {
        if (_wildHP <= 0.01f) yield break;

        var ai = RollEnemyAction(_wildDef);
        switch (ai)
        {
            case BattleAction.Attack: yield return Co_EnemyAttack(); break;
            case BattleAction.Defend:
                _enemyDefendGuard = true; BattleLogger.Log($"{_wildDef.displayName} is guarding."); break;
            case BattleAction.Focus:
                _enemyFocusTurns = 1; BattleLogger.Log($"{_wildDef.displayName} is focusing!"); break;
            case BattleAction.Run:
                BattleLogger.Log($"{_wildDef.displayName} retreated!"); EndBattle(true); break;
            default:
                BattleLogger.Log($"{_wildDef.displayName} hesitates."); break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Actions
    // ─────────────────────────────────────────────────────────────────────────────
    private IEnumerator Co_PlayerAttack()
    {
        if (_teamHP[_active] <= 0.01f && !AutoSwapToAlive()) yield break;

        // Equip ATK flat (from team’s owned slot if any)
        int equippedFlat = 0;
        var roster = SaveManager.Data?.team;
        if (roster != null && _active < roster.Count && roster[_active] != null)
            equippedFlat = Mathf.Max(0, roster[_active].flatAtkBonus);

        // Base ATK
        int baseAtk = Mathf.Max(1, Mathf.RoundToInt(BattleCalc.CalcBaseAttack(_teamDefs[_active], _teamLvls[_active], equippedFlat, 0)));

        // Booster flat ATK
        int atkFlatFromBooster = BattleBoosterController.I ? BattleBoosterController.I.GetAttackBonus() : 0;

        // Focus multiplier
        float focusMult = _playerFocusTurns > 0 ? 1.35f : 1f;

        // (Optional) quick hint using chart
        float preEff = BattleTypeChart.GetMultiplier(_teamDefs[_active]?.type ?? MonsterType.None, _wildDef?.type ?? MonsterType.None);
        if (preEff >= 1.75f) BattleLogger.Log("This looks promising...");

        // Resolve baseline
        var dr = BattleCalc.ResolveHit(
            _teamIds[_active], _teamDefs[_active], _teamLvls[_active],
            null, _wildDef, _wildLevel,
            baseAtk + Mathf.Max(0, atkFlatFromBooster),
            Mathf.Clamp01(critChancePlayer), critMultiplier, 0
        );

        // Focus bonus
        dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * focusMult));

        // Enemy guarding reduces 30%
        if (_enemyDefendGuard)
        {
            _enemyDefendGuard = false;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * 0.7f));
            BattleLogger.Log($"{_wildDef.displayName} guarded some damage.");
        }

        _wildHP = Mathf.Max(0f, _wildHP - dr.damage);
        PushHPBars();

        BattleLogger.Log($"{GetName(_active)} hits {_wildDef.displayName} for {dr.damage}!");
        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) BattleLogger.Log("It's super effective!");
            else if (dr.effectiveness < 0.85f) BattleLogger.Log("It's not very effective...");
        }
        if (dr.crit) BattleLogger.Log("Critical hit!");

        if (_playerFocusTurns > 0) _playerFocusTurns = 0;

        if (playerIcon) LeanTween.scale(playerIcon.rectTransform, Vector3.one * 1.06f, 0.08f).setLoopPingPong(1);
        yield return null;
    }

    private IEnumerator Co_EnemyAttack()
    {
        if (_teamHP[_active] <= 0.01f && !AutoSwapToAlive()) yield break;

        int enemyAtk = Mathf.RoundToInt(_wildAtk);

        // Titles: defender-side filter (cannotBeCrit / %reduce / flat)
        var df = TitlesAdapter.GetDamageFilter(_teamIds[_active], _teamDefs[_active], _teamLvls[_active]);

        float critChance = df.cannotBeCrit ? 0f : Mathf.Clamp01(critChanceWild);

        var dr = BattleCalc.ResolveHit(
            null, _wildDef, _wildLevel,
            _teamIds[_active], _teamDefs[_active], _teamLvls[_active],
            enemyAtk, critChance, critMultiplier, 0
        );

        int dmg = dr.damage;

        // Player guarding: 30% less once
        if (_playerDefendGuard)
        {
            _playerDefendGuard = false;
            dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.7f));
            BattleLogger.Log($"{GetName(_active)} guarded some damage.");
        }

        // Titles incoming reduction (% then flat)
        if (df.percentReduce > 0f) dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - Mathf.Clamp01(df.percentReduce))));
        if (df.flatReduce > 0)     dmg = Mathf.Max(1, dmg - df.flatReduce);

        // Booster resist multiplier at the end (multiplicative)
        float resistMul = BattleBoosterController.I ? BattleBoosterController.I.GetResistMul() : 1f;
        if (resistMul < 1f) dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * Mathf.Clamp(resistMul, 0.1f, 1f)));

        _teamHP[_active] = Mathf.Max(0f, _teamHP[_active] - dmg);
        PushHPBars();

        BattleLogger.Log($"{_wildDef.displayName} hits {GetName(_active)} for {dmg}!");
        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) BattleLogger.Log("It's super effective!");
            else if (dr.effectiveness < 0.85f) BattleLogger.Log("It's not very effective...");
        }
        if (dr.crit && !df.cannotBeCrit) BattleLogger.Log("Critical hit!");

        if (_enemyFocusTurns > 0) _enemyFocusTurns = 0;

        if (wildIcon) LeanTween.scale(wildIcon.rectTransform, Vector3.one * 1.06f, 0.08f).setLoopPingPong(1);
        yield return null;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // UI action setters (hook buttons here)
    // ─────────────────────────────────────────────────────────────────────────────
    public void SetPlayerActionAttack() { if (_playerTurn) _pendingPlayerAction = BattleAction.Attack; }
    public void SetPlayerActionDefend() { if (_playerTurn) _pendingPlayerAction = BattleAction.Defend; }
    public void SetPlayerActionFocus()  { if (_playerTurn) _pendingPlayerAction = BattleAction.Focus;  }
    public void SetPlayerActionRun()    { if (_playerTurn) _pendingPlayerAction = BattleAction.Run;    }

    // ─────────────────────────────────────────────────────────────────────────────
    // End battle / persistence
    // ─────────────────────────────────────────────────────────────────────────────
    private void EndBattle(bool victory)
    {
        if (!_running) return;
        _running = false;

        float survived = Mathf.Max(0f, Time.unscaledTime - _startTime);

        // Coins
        int baseCoins = BattleRewards.CoinsFor(victory, _wildLevel, survived);
        int finalCoins = baseCoins;
        int coinTitleBonus = 0;

        if (victory && _active >= 0 && _teamIds != null && _active < _teamIds.Length)
        {
            float cm = TitlesAdapter.GetCoinMultOnVictory(_teamIds[_active], _wildDef, _wildLevel);
            if (cm > 0f)
            {
                finalCoins = Mathf.Max(0, Mathf.RoundToInt(baseCoins * cm));
                coinTitleBonus = Mathf.Max(0, finalCoins - baseCoins);
            }
        }

        // Growth Cores (replacing XP)
        int baseCores = Mathf.Max(1, 2 + _wildLevel);
        int growthCores = baseCores;

        if (victory && _active >= 0 && _teamIds != null && _active < _teamIds.Length)
        {
            float titleMul = TitlesAdapter.GetGrowthCoreMultOnVictory(_teamIds[_active], _wildDef, _wildLevel);
            growthCores = Mathf.Max(0, Mathf.RoundToInt(baseCores * Mathf.Max(0f, titleMul)));
            if (growthCores > 0) ResourceManager.I?.Add(ResourceType.GrowthCores, growthCores);
            BattleLogger.Log($"Gained {growthCores} Growth Cores.");
        }

        // Persist HP to save
        var teamList = SaveManager.Data.team ?? new List<OwnedMonsterData>();
        var ownedList = SaveManager.Data.owned ?? new List<OwnedMonsterData>();
        long nowUnix = SaveManager.NowUnix();

        for (int i = 0; i < _teamCount && i < teamList.Count; i++)
        {
            var t = teamList[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;
            t.currentHP = Mathf.CeilToInt(Mathf.Max(0f, _teamHP[i]));
            t.lastHPUnix = nowUnix;
            teamList[i] = t;
        }
        // Mirror to owned
        for (int i = 0; i < teamList.Count; i++)
        {
            var t = teamList[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;
            for (int j = 0; j < ownedList.Count; j++)
            {
                var o = ownedList[j];
                if (!string.IsNullOrEmpty(o.monsterId) && o.monsterId == t.monsterId)
                {
                    o.currentHP = t.currentHP;
                    o.lastHPUnix = nowUnix;
                    ownedList[j] = o;
                    break;
                }
            }
        }
        SaveManager.Data.owned = ownedList;
        SaveManager.Data.team = teamList;
        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();

        // Clear boosters
        BattleBoosterController.I?.OnTurnStart(false); // harmless reset to "not player's turn"
        BattleLogger.Log($"Battle ends: {(victory ? "Victory" : "Defeat")} (+{finalCoins} coins).");
        BattleLogger.EndBattle(victory);

        var result = new BattleResult
        {
            victory = victory,
            coinsGained = finalCoins,
            wildDef = _wildDef,
            wildLevel = _wildLevel,
            secondsSurvived = survived
        };

        if (_active >= 0 && _teamIds != null && _active < _teamIds.Length)
            TitlesAdapter.OnBattleEnd(_teamIds[_active], victory, _wildDef, _wildLevel);

        PostBattleSummaryManager.I?.NotifyBattleEnd(
            result, _autoMode, growthCores,
            monstersLeveledUp: 0,
            captured: false,
            capturedMonsterId: null,
            capturedLevel: 0,
            levelUpSummaries: null,
            coinsBase: baseCoins,
            coinsTitleBonus: coinTitleBonus,
            growthCoresBase: baseCores,
            growthCoresTitleBonus: Mathf.Max(0, growthCores - baseCores),
            growthCoresDetailLines: new List<string> { $"Gained {growthCores} Growth Cores." }
        );

        _onEnd?.Invoke(result);

        if (wildPanel) wildPanel.SetActive(false);
        if (playerPanel) playerPanel.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────
    private BattleRuntimeHooks BuildRuntimeHooks()
    {
        return new BattleRuntimeHooks
        {
            HealPlayer = (amount) =>
            {
                if (_active < 0 || _active >= _teamCount) return 0;
                int before = Mathf.CeilToInt(_teamHP[_active]);
                float max = Mathf.Max(1f, _teamMaxHP[_active]);
                _teamHP[_active] = Mathf.Min(max, _teamHP[_active] + Mathf.Max(0, amount));
                int after = Mathf.CeilToInt(_teamHP[_active]);
                PushHPBars();
                return Mathf.Max(0, after - before);
            }
        };
    }

    private bool AutoSwapToAlive()
    {
        for (int i = 0; i < _teamCount; i++)
        {
            if (i == _active) continue;
            if (_teamHP[i] > 0f)
            {
                SwapTeamSlots(_active, i);
                ApplyActiveToUI();
                PushHPBars();
                RefreshBenchUI();
                BattleLogger.Log($"Auto-swapped to {GetName(_active)}!");
                return true;
            }
        }
        return false;
    }

    private void SwapTeamSlots(int a, int b)
    {
        (_teamDefs[a], _teamDefs[b]) = (_teamDefs[b], _teamDefs[a]);
        (_teamLvls[a], _teamLvls[b]) = (_teamLvls[b], _teamLvls[a]);
        (_teamMaxHP[a], _teamMaxHP[b]) = (_teamMaxHP[b], _teamMaxHP[a]);
        (_teamHP[a], _teamHP[b]) = (_teamHP[b], _teamHP[a]);
        (_teamIds[a], _teamIds[b]) = (_teamIds[b], _teamIds[a]);

        var t = SaveManager.Data.team[a];
        SaveManager.Data.team[a] = SaveManager.Data.team[b];
        SaveManager.Data.team[b] = t;
        SaveManager.Save();

        _active = b;
    }

    private bool IsTeamKO()
    {
        for (int i = 0; i < _teamCount; i++) if (_teamHP[i] > 0.01f) return false;
        return true;
    }

    private string GetName(int idx)
    {
        var def = (_teamDefs != null && idx >= 0 && idx < _teamDefs.Length) ? _teamDefs[idx] : null;
        return def ? def.displayName : "Ally";
    }

    private int CalcPlayerSpeed()
    {
        int baseSPD = BattleCalc.CalcSpeed(_teamDefs[_active], _teamLvls[_active]);
        int spdBooster = BattleBoosterController.I ? BattleBoosterController.I.GetSpeedBonus() : 0;
        return Mathf.Max(1, baseSPD + Mathf.Max(0, spdBooster));
    }

    private void PushHPBars()
    {
        if (wildHPBar)
        {
            wildHPBar.maxValue = Mathf.Max(1f, _wildMaxHP);
            wildHPBar.value = Mathf.Clamp(_wildHP, 0f, wildHPBar.maxValue);
        }
        if (playerHPBar)
        {
            float max = Mathf.Max(1f, _teamMaxHP[_active]);
            playerHPBar.maxValue = max;
            playerHPBar.value = Mathf.Clamp(_teamHP[_active], 0f, max);
        }
        UpdatePlayerInfoUI();
    }

    private void ApplyActiveToUI()
    {
        var def = _teamDefs[_active];
        var lvl = _teamLvls[_active];
        if (playerIcon)      playerIcon.sprite = def ? (def.backIcon ? def.backIcon : def.icon) : null;
        if (playerNameText)  playerNameText.text = def ? def.displayName : "";
        if (playerLevelText) playerLevelText.text = $"Lv {lvl}";
        UpdatePlayerInfoUI();
    }

    private void UpdatePlayerInfoUI()
    {
        if (_active < 0 || _teamDefs == null || _active >= _teamDefs.Length) return;

        var def = _teamDefs[_active];
        if (!def) return;
        int lvl = _teamLvls[_active];

        int baseHP  = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
        int baseATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
        int baseDEF = BattleCalc.CalcDefense(def, lvl);
        int baseSPD = BattleCalc.CalcSpeed(def, lvl);

        int atkBooster = BattleBoosterController.I ? BattleBoosterController.I.GetAttackBonus() : 0;
        int spdBooster = BattleBoosterController.I ? BattleBoosterController.I.GetSpeedBonus()  : 0;

        int equippedFlatATK = 0;
        var roster = SaveManager.Data?.team;
        if (roster != null && _active < roster.Count && roster[_active] != null)
            equippedFlatATK = Mathf.Max(0, roster[_active].flatAtkBonus);

        if (playerIdText)     playerIdText.text     = $"ID: {def.id}";
        if (playerTypeText)   playerTypeText.text   = $"TYPE: {def.type}";
        if (playerRarityText) playerRarityText.text = $"RARITY: {def.rarity}";
        if (playerLevelText)  playerLevelText.text  = $"LVL: {lvl}";

        if (playerHPText)  playerHPText.text  = $"HP: {baseHP}";
        if (playerATKText) playerATKText.text = $"ATK: {baseATK + equippedFlatATK + Mathf.Max(0, atkBooster)}";
        if (playerDEFText) playerDEFText.text = $"DEF: {baseDEF}";
        if (playerSPDText) playerSPDText.text = $"SPD: {baseSPD + Mathf.Max(0, spdBooster)}";
    }

    private void UpdateWildInfoUI()
    {
        if (!_wildDef) return;

        int dispHP  = Mathf.RoundToInt(BattleCalc.CalcHP(_wildDef, _wildLevel));
        int dispATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(_wildDef, _wildLevel, 0, 0));
        int dispDEF = BattleCalc.CalcDefense(_wildDef, _wildLevel);
        int dispSPD = BattleCalc.CalcSpeed(_wildDef, _wildLevel);

        if (wildIdText)     wildIdText.text     = $"ID: {_wildDef.id}";
        if (wildTypeText)   wildTypeText.text   = $"TYPE: {_wildDef.type}";
        if (wildRarityText) wildRarityText.text = $"RARITY: {_wildDef.rarity}";
        if (wildLevelText)  wildLevelText.text  = $"LVL: {_wildLevel}";
        if (wildHPText)     wildHPText.text     = $"HP: {dispHP}";
        if (wildATKText)    wildATKText.text    = $"ATK: {dispATK}";
        if (wildDEFText)    wildDEFText.text    = $"DEF: {dispDEF}";
        if (wildSPDText)    wildSPDText.text    = $"SPD: {dispSPD}";
    }

    private void RefreshBenchUI()
    {
        List<int> others = new();
        for (int i = 0; i < _teamCount; i++) if (i != _active) others.Add(i);

        if (benchImg1)
        {
            if (others.Count > 0) { benchImg1.enabled = true; benchImg1.sprite = _teamDefs[others[0]]?.icon; benchImg1.color = _teamHP[others[0]] > 0 ? Color.white : new Color(1,1,1,0.35f); }
            else benchImg1.enabled = false;
        }
        if (benchBtn1) benchBtn1.interactable = others.Count > 0 && _teamHP[others[0]] > 0f;

        if (benchHPText1)
        {
            if (others.Count > 0)
            {
                float cur = Mathf.Max(0f, _teamHP[others[0]]);
                float max = Mathf.Max(1f, _teamMaxHP[others[0]]);
                benchHPText1.gameObject.SetActive(true);
                benchHPText1.text = $"{Mathf.CeilToInt(cur)}/{Mathf.CeilToInt(max)}";
                benchHPText1.alpha = cur > 0f ? 1f : 0.35f;
            }
            else benchHPText1.gameObject.SetActive(false);
        }

        if (benchImg2)
        {
            if (others.Count > 1) { benchImg2.enabled = true; benchImg2.sprite = _teamDefs[others[1]]?.icon; benchImg2.color = _teamHP[others[1]] > 0 ? Color.white : new Color(1,1,1,0.35f); }
            else benchImg2.enabled = false;
        }
        if (benchBtn2) benchBtn2.interactable = others.Count > 1 && _teamHP[others[1]] > 0f;

        if (benchHPText2)
        {
            if (others.Count > 1)
            {
                float cur = Mathf.Max(0f, _teamHP[others[1]]);
                float max = Mathf.Max(1f, _teamMaxHP[others[1]]);
                benchHPText2.gameObject.SetActive(true);
                benchHPText2.text = $"{Mathf.CeilToInt(cur)}/{Mathf.CeilToInt(max)}";
                benchHPText2.alpha = cur > 0f ? 1f : 0.35f;
            }
            else benchHPText2.gameObject.SetActive(false);
        }
    }

    private AIDecisionWeights GetAIWeights(MonsterDataSO def)
    {
        var w = new AIDecisionWeights { attack = 5, defend = 1, focus = 1, run = 0 }; // defaults
        if (!def) return w;
        var p = def.Personality;
        if (!p) return w;

        // Use the lowercase fields defined on MonsterPersonalitySO
        w.attack = Mathf.Max(0, p.attackWeight);
        w.defend = Mathf.Max(0, p.defendWeight);
        w.focus  = Mathf.Max(0, p.focusWeight);
        w.run    = Mathf.Max(0, p.runWeight);

        if (w.attack + w.defend + w.focus + w.run <= 0) w.attack = 1;
        return w;
    }


    private BattleAction RollEnemyAction(MonsterDataSO def)
    {
        if (!def || !def.Personality) return BattleAction.Attack;

        // Build context for the personality brain
        float selfHp = Mathf.Clamp01(_wildHP / Mathf.Max(1f, _wildMaxHP));

        bool hasSE = false;
        bool badMU = false;
        try
        {
            // crude matchup check based on types
            var enemyType   = def.type;
            var playerType  = _teamDefs[_active] ? _teamDefs[_active].type : MonsterType.None;

            float effEnemyVsPlayer = BattleTypeChart.GetMultiplier(enemyType, playerType);
            float effPlayerVsEnemy = BattleTypeChart.GetMultiplier(playerType, enemyType);

            hasSE = effEnemyVsPlayer > 1.1f;
            badMU = effPlayerVsEnemy > 1.1f;
        }
        catch { /* safe defaults already set */ }

        var ctx = new PersonalityContext
        {
            selfHpRatio = selfHp,
            hasSuperEffectiveMove = hasSE,
            isBadlyMatched = badMU,
            turnNumber = Mathf.Max(1, _turnIndex)
        };

        // Call the personality SO (returns the global BattleAction)
        var decided = def.Personality.ChooseAction(in ctx, _rng);

        // Map to the manager’s internal enum (same names here)
        switch (decided)
        {
            case global::BattleAction.Attack: return BattleAction.Attack;
            case global::BattleAction.Defend: return BattleAction.Defend;
            case global::BattleAction.Focus:  return BattleAction.Focus;
            case global::BattleAction.Run:    return BattleAction.Run;
            default:                          return BattleAction.Attack;
        }
    }


    private CanvasGroup EnsureCanvasGroup(GameObject go)
    {
        if (!go) return null;
        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    private string BoosterUseText(BoosterType t) => t switch
    {
        BoosterType.Attack     => $"{GetName(_active)} used Attack Booster!",
        BoosterType.Health     => $"{GetName(_active)} used HP Booster!",
        BoosterType.Speed      => $"{GetName(_active)} used Speed Booster!",
        BoosterType.TypeResist => $"{GetName(_active)} used Resist Sigil!",
        _ => "Booster used!"
    };

        // ─────────────────────────────────────────────────────────────────────────────
    // Compatibility & Healing helpers (legacy-friendly)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Legacy-friendly heal. Adds HP to the currently active player monster,
    /// clamps to that monster’s max HP, pushes UI, and returns the actual amount healed.
    /// </summary>
    public int TryAddHPToActive(float amount)
    {
        // normalize & guard
        if (amount == 0f) return 0;
        if (_active < 0 || _teamHP == null || _teamHP.Length == 0) return 0;

        float max = GetMaxHPForIndex(_active);
        float before = Mathf.Clamp(_teamHP[_active], 0f, max);
        float after  = Mathf.Clamp(before + amount, 0f, max);

        _teamHP[_active] = after;
        RefreshHpUIForActive();
        return Mathf.RoundToInt(after - before);
    }

    /// <summary>Int overload used by some callers.</summary>
    public int TryAddHPToActive(int amount) => TryAddHPToActive((float)amount);

    /// <summary>
    /// Returns the current maximum HP for a given team index. This uses the
    /// precomputed team max array if present; otherwise falls back to 1.
    /// If you already compute title-modified max HP elsewhere, feel free to
    /// swap to that function here.
    /// </summary>
    private float GetMaxHPForIndex(int idx)
    {
        if (_teamMaxHP != null && idx >= 0 && idx < _teamMaxHP.Length)
            return Mathf.Max(1f, _teamMaxHP[idx]);

        // Fallback (shouldn’t happen in normal flow).
        return 1f;
    }

    /// <summary>Pushes the active member’s HP to the HP bar / stat readouts.</summary>
    private void RefreshHpUIForActive()
    {
        try
        {
            // If you have explicit bars like in the old manager, update them:
            if (playerHPBar != null)
            {
                float max = GetMaxHPForIndex(_active);
                playerHPBar.maxValue = max;
                playerHPBar.value    = Mathf.Clamp(_teamHP[_active], 0f, max);
            }
            
        }
        catch { /* non-fatal — UI may not be wired in some scenes */ }

        // If your class already has a “full push” method (e.g., PushHPBars / ClampAndPushActiveHP),
        // you can replace the above with a single call to that instead:
        // ClampAndPushActiveHP();
    }

}
