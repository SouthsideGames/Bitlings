// Assets/Scripts/Iron Career/IronCareerManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class IronCareerManager : MonoBehaviour, IronBattleBridge.IIronBattleBridgeHost
{
    public enum Mode { Standard, Hardcore }

    [Header("Battle Refs")]
    [SerializeField] private BattleManager battle;
    [SerializeField] private IronBattleBridge bridge;

    [Header("Iron Systems (Phase 3)")]
    [Tooltip("Reference to the Iron encounter panel controller on Panel_IronCareerEncounter.")]
    [SerializeField] private IronCareerEncounterPanelUI ironEncounterUI;

    [Tooltip("Reference to the Iron battle UI root (Panel_IronCareerEncounter/IronCareerBattle).")]
    [SerializeField] private IronBattleUIRoot ironBattleUI;

    [Header("Iron Panels (Phase 3)")]
    [SerializeField] private IronCareerStarterPanelUI starterPanel;
    [SerializeField] private IronCareerHirePanelUI hirePanel;
    [SerializeField] private IronCareerReplacePanelUI replacePanel;
    [SerializeField] private IronCareerPostScreenUI postPanel;
    [SerializeField] private IronCareerForcedEvolutionUI forcedEvolvePanel;
    [SerializeField] private IronCareerRestPanelUI restPanel;
    [SerializeField] private IronCareerGameOverPanelUI gameOverPanel;
    [SerializeField] private IronCareerRulesPopupUI rulesPopup;

    [Header("Mode")]
    [SerializeField] private Mode mode = Mode.Standard;

    [Header("Seed (runtime-only)")]
    [SerializeField] private int seed = 0;

    [Header("Debug Starter Party (Phase 2)")]
    [SerializeField] private List<MonsterDataSO> debugStarterParty = new List<MonsterDataSO>();
    [SerializeField] private List<int> debugStarterLevels = new List<int>();

    [Header("Debug Wild Pool (Phase 2)")]
    [SerializeField] private List<MonsterDataSO> debugWildPool = new List<MonsterDataSO>();
    [SerializeField] private MonsterDataSO debugFallbackWild;
    [SerializeField] private int debugFallbackWildLevel = 1;

#if UNITY_EDITOR
    [Header("DEV ONLY")]
    [SerializeField] private bool devForceUnlockIron = false;
#endif

    private readonly IronCareerRunState _state = new IronCareerRunState();
    private IronRoster _roster;
    private IronRngStream _rng;
    private IronTitleRoller _titleRoller;
    private IronEncounterService _encounters;
    private IronMonster _pendingHire;
    private bool _forfeited;

    public int Wins => Mathf.Max(0, _state.wins);

    public bool IsUnlocked()
    {
    #if UNITY_EDITOR
        if (devForceUnlockIron) return true;
    #endif

        return SaveManager.Data != null && SaveManager.Data.HasIronCareerUnlocked;
    }
    public IReadOnlyList<BattleCombatant> GetPartyForNextBattle()
    {
        if (!_state.runActive) return Array.Empty<BattleCombatant>();

        var party = _state.party;
        if (party == null || party.Count == 0) return Array.Empty<BattleCombatant>();

        var list = new List<BattleCombatant>(Mathf.Min(3, party.Count));
        int active = _roster != null ? _roster.ActiveIndex : 0;

        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < party.Count && list.Count < 3; i++)
            {
                bool isActive = (i == active);
                if ((pass == 0 && !isActive) || (pass == 1 && isActive)) continue;

                var m = party[i];
                if (m == null || m.def == null || m.IsDead) continue;

                list.Add(new BattleCombatant
                {
                    def = m.def,
                    level = Mathf.Max(1, m.level),
                    hp = Mathf.Max(0f, m.hp),
                    combatantId = null,
                    lockedTitle = m.lockedTitle
                });
            }
        }

        return list;
    }

    public BattleCombatant GetWildForNextBattle()
    {
        if (!_state.runActive) return null;

        var wild = _encounters != null ? _encounters.RollNextWild() : null;
        if (wild == null || wild.def == null) return null;

        _state.lastRolledWild = wild;

        return new BattleCombatant
        {
            def = wild.def,
            level = Mathf.Max(1, wild.level),
            hp = Mathf.Max(0f, wild.hp),
            combatantId = null,
            lockedTitle = wild.lockedTitle
        };
    }

    public IronFieldStatusSnapshot GetCarryStatus() => _state.carryStatus;
    public float[] GetCarryShields() => _state.carryShields;

    public void OnIronBattleResolved(IronBattleOutcome outcome)
    {
        if (!_state.runActive) return;

        if (outcome.teamHP != null)
        {
            for (int i = 0; i < _state.party.Count; i++)
            {
                if (i >= outcome.teamHP.Length) break;
                _state.party[i].hp = Mathf.Max(0f, outcome.teamHP[i]);
            }
        }

        _roster?.RemoveDead();

        _state.carryStatus = outcome.playerFieldStatus;
        _state.carryShields = outcome.shieldHP ?? new float[3];

        if (_roster == null || _roster.IsPartyEmpty)
        {
            ShowGameOver(forfeit: false);
            return;
        }

        if (!outcome.victory)
        {
            ShowGameOver(forfeit: false);
            return;
        }

        _state.wins++;
        _roster.RotateActiveAfterWin();

        _pendingHire = _state.lastRolledWild;

        ShowHireOrReplace();
    }

    private void Awake()
    {
        if (!battle) battle = FindFirstObjectByType<BattleManager>();
        if (!bridge) bridge = FindFirstObjectByType<IronBattleBridge>();
        if (!ironEncounterUI) ironEncounterUI = FindFirstObjectByType<IronCareerEncounterPanelUI>(FindObjectsInactive.Include);
        if (!ironBattleUI) ironBattleUI = FindFirstObjectByType<IronBattleUIRoot>(FindObjectsInactive.Include);

        if (!starterPanel) starterPanel = FindFirstObjectByType<IronCareerStarterPanelUI>(FindObjectsInactive.Include);
        if (!hirePanel) hirePanel = FindFirstObjectByType<IronCareerHirePanelUI>(FindObjectsInactive.Include);
        if (!replacePanel) replacePanel = FindFirstObjectByType<IronCareerReplacePanelUI>(FindObjectsInactive.Include);
        if (!postPanel) postPanel = FindFirstObjectByType<IronCareerPostScreenUI>(FindObjectsInactive.Include);
        if (!forcedEvolvePanel) forcedEvolvePanel = FindFirstObjectByType<IronCareerForcedEvolutionUI>(FindObjectsInactive.Include);
        if (!restPanel) restPanel = FindFirstObjectByType<IronCareerRestPanelUI>(FindObjectsInactive.Include);
        if (!gameOverPanel) gameOverPanel = FindFirstObjectByType<IronCareerGameOverPanelUI>(FindObjectsInactive.Include);
        if (!rulesPopup) rulesPopup = FindFirstObjectByType<IronCareerRulesPopupUI>(FindObjectsInactive.Include);
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!IronCareerRuntime.IsActive) return;
        if (pauseStatus) Forfeit("Application paused");
    }

    private void OnEnable()
    {
        Application.focusChanged += OnAppFocusChanged;
    }

    private void OnDisable()
    {
        Application.focusChanged -= OnAppFocusChanged;
    }

    private void OnAppFocusChanged(bool hasFocus)
    {
        if (!IronCareerRuntime.IsActive) return;
        if (!hasFocus) Forfeit("Application lost focus");
    }

    // Phase 2 debug entry points preserved
    [ContextMenu("DEBUG/Start Standard Run")]
    public void DebugStartStandardRun() => StartNewRun(mode: Mode.Standard);

    [ContextMenu("DEBUG/Start Hardcore Run")]
    public void DebugStartHardcoreRun() => StartNewRun(mode: Mode.Hardcore);

    [ContextMenu("DEBUG/Begin Next Battle")]
    public void DebugBeginNextBattle() => BeginNextBattle();

    public void StartNewRun(Mode mode)
    {
        // If someone wires a button to this by mistake, we should still show Starter first.
        this.mode = mode;
        OpenStarterFromHome();
    }

    public void StartNewRunFromUI(IronCareerRunState.IronCareerMode m, List<MonsterDataSO> starterDefs)
    {
        mode = (m == IronCareerRunState.IronCareerMode.Hardcore) ? Mode.Hardcore : Mode.Standard;
        StartNewRun_Internal(m, starterDefs);
    }

    private void StartNewRun_Internal(IronCareerRunState.IronCareerMode m, List<MonsterDataSO> starterDefs)
    {
        IronCareerRuntime.Enter();
        _forfeited = false;

        if (seed == 0) seed = UnityEngine.Random.Range(1, int.MaxValue);

        _state.Reset(m, seed);
        _roster = new IronRoster(_state);
        _rng = new IronRngStream(seed);
        _titleRoller = new IronTitleRoller();
        _encounters = new IronEncounterService(_state, _rng, _titleRoller);

        // Top-level screen routing only
        UIManager.I?.Hide(PanelId.Encounter);
        UIManager.I?.Show(PanelId.IronCareerEncounter);

        BuildStarterParty(starterDefs);
        if (_roster.IsPartyEmpty)
        {
            Debug.LogError("[IronCareerManager] No starter party configured.");
            ShowGameOver(forfeit: false);
            return;
        }

        ironEncounterUI?.ShowBattleOnly(immediate: true);
        BeginNextBattle();
    }

    private void BuildStarterParty(List<MonsterDataSO> starterDefs)
    {
        _state.party.Clear();

        if (starterDefs != null && starterDefs.Count > 0)
        {
            for (int i = 0; i < starterDefs.Count && _state.party.Count < 3; i++)
            {
                var def = starterDefs[i];
                if (!def) continue;
                AddStarter(def, lvl: 1);
            }
        }

        if (_state.party.Count == 0 && debugStarterParty != null)
        {
            for (int i = 0; i < debugStarterParty.Count && _state.party.Count < 3; i++)
            {
                var def = debugStarterParty[i];
                if (!def) continue;

                int lvl = 1;
                if (debugStarterLevels != null && i < debugStarterLevels.Count)
                    lvl = Mathf.Max(1, debugStarterLevels[i]);

                AddStarter(def, lvl);
            }
        }
    }

    private void AddStarter(MonsterDataSO def, int lvl)
    {
        var title = _titleRoller != null ? _titleRoller.RollLockedTitle(def, lvl, _rng, isWild: false) : null;
        var m = new IronMonster(def, lvl, curHp: -1f, locked: title);
        _roster.EnsureHpInitialized(m);
        _state.party.Add(m);
    }

    private bool IsHardcore => _state.mode == IronCareerRunState.IronCareerMode.Hardcore;

    private void ShowHireOrReplace()
    {
        if (_pendingHire == null || _pendingHire.def == null)
        {
            Debug.LogWarning("[IronCareerManager] Missing pending hire offer. Continuing.");
            ShowPost();
            return;
        }

        ShowHire();
    }

    private void ShowHire()
    {
        ironEncounterUI?.ShowHire(immediate: true);
        if (hirePanel) hirePanel.Bind(_pendingHire, skipAllowed: !IsHardcore);
    }

    public void OnHireAccepted()
    {
        if (!_state.runActive) return;
        if (_pendingHire == null || _pendingHire.def == null) { ShowPost(); return; }

        if (_roster.IsFull)
        {
            ironEncounterUI?.ShowReplace(immediate: true);
            if (replacePanel) replacePanel.Bind(_roster.Party);
            return;
        }

        _roster.AddMember(_pendingHire);
        FinishHireStepAndContinue();
    }

    public void OnHireSkipped()
    {
        if (!_state.runActive) return;
        if (IsHardcore) return;
        FinishHireStepAndContinue();
    }

    public void OnReplaceChosen(int indexToReplace)
    {
        if (!_state.runActive) return;
        if (_pendingHire == null || _pendingHire.def == null) { FinishHireStepAndContinue(); return; }

        _roster.ReplaceMember(indexToReplace, _pendingHire);
        FinishHireStepAndContinue();
    }

    private void FinishHireStepAndContinue()
    {
        _encounters?.ClearWildCache();
        _pendingHire = null;

        ironEncounterUI?.HideAll(immediate: true);
        ShowPost();
    }

    private void ShowPost()
    {
        ironEncounterUI?.ShowPost(immediate: true);
        postPanel?.Bind(_roster.Party, _state.carryStatus);
    }

    public void OnPostContinue()
    {
        ironEncounterUI?.HideAll(immediate: true);
        ShowForcedEvolveStep();
    }

    private void ShowForcedEvolveStep()
    {
        string before = null;
        string after = null;
        bool evolved = false;

        if (_roster != null && !_roster.IsPartyEmpty)
        {
            int idx = _roster.ActiveIndex;
            if (idx >= 0 && idx < _state.party.Count)
                before = _state.party[idx].def ? _state.party[idx].def.displayName : null;

            evolved = _roster.TryForceEvolveActive();

            if (idx >= 0 && idx < _state.party.Count)
                after = _state.party[idx].def ? _state.party[idx].def.displayName : null;
        }

        ironEncounterUI?.ShowForcedEvolve(immediate: true);
        forcedEvolvePanel?.Bind(evolved, before ?? "", after ?? "");
    }

    public void OnForcedEvolveContinue()
    {
        if ((_state.wins % 3) == 0)
        {
            ironEncounterUI?.ShowRest(immediate: true);
            return;
        }

        ironEncounterUI?.ShowBattleOnly(immediate: true);
        BeginNextBattle();
    }

    public void OnRestHeal()
    {
        if (_roster == null) return;

        for (int i = 0; i < _state.party.Count; i++)
        {
            var m = _state.party[i];
            if (m == null || m.def == null || m.IsDead) continue;

            float add = 0.25f * Mathf.Max(1f, m.maxHp);
            m.hp = Mathf.Min(m.maxHp, m.hp + add);
            _state.party[i] = m;
        }

        ironEncounterUI?.ShowBattleOnly(immediate: true);
        BeginNextBattle();
    }

    public void OnRestBuff()
    {
        if (_roster == null || _roster.IsPartyEmpty) return;

        int tries = 0;
        while (tries++ < 8)
        {
            int idx = _rng != null ? _rng.NextInt(0, _state.party.Count) : UnityEngine.Random.Range(0, _state.party.Count);
            var m = _state.party[idx];
            if (m == null || m.def == null || m.IsDead) continue;

            m.level = Mathf.Min(m.def.maxLevel > 0 ? m.def.maxLevel : 99, Mathf.Max(1, m.level + 1));

            float hp01 = m.Hp01;
            m.maxHp = Mathf.Max(1f, BattleCalc.CalcHP(m.def, m.level));
            m.hp = Mathf.Clamp(m.maxHp * hp01, 0f, m.maxHp);

            _state.party[idx] = m;
            break;
        }

        ironEncounterUI?.ShowBattleOnly(immediate: true);
        BeginNextBattle();
    }

    private void BeginNextBattle()
    {
        if (!_state.runActive) return;
        if (_roster == null || _roster.IsPartyEmpty) { ShowGameOver(forfeit: false); return; }

        if (!battle || !bridge)
        {
            Debug.LogError("[IronCareerManager] Missing BattleManager or IronBattleBridge reference.");
            return;
        }

        UIManager.I?.Show(PanelId.IronCareerEncounter);

        ironEncounterUI?.ShowBattleOnly(immediate: true);
        ironBattleUI?.ApplyTo(battle);

        bridge.SetWins(_state.wins);
        bridge.BeginIronBattle(battle, null);
    }

    // Quit/Forfeit + GameOver UI

    public void RequestQuit()
    {
        if (!_state.runActive) return;
        ironEncounterUI?.ShowRules(immediate: true);
    }

    public void ConfirmQuitForfeit()
    {
        if (!_state.runActive) return;
        ironEncounterUI?.ShowRules(immediate: false); // if your panel UI uses ShowRules(false) to hide, change to HideRules()
        Forfeit("Quit confirmed");
    }

    public void CancelQuit()
    {
        // If your panel UI has HideRules(), call it instead.
        ironEncounterUI?.ShowRules(immediate: false);
    }

    private void Forfeit(string reason)
    {
        if (!_state.runActive) return;
        Debug.LogWarning($"[IronCareerManager] FORFEIT: {reason}");
        _forfeited = true;
        ShowGameOver(forfeit: true);
    }

    private void ShowGameOver(bool forfeit)
    {
        _state.runActive = false;

        ironEncounterUI?.ShowGameOver(immediate: true);
        gameOverPanel?.Bind(_state.wins, forfeited: forfeit);

        IronCareerRuntime.Exit();

        ironBattleUI?.RestoreBattleManagerDefaults();

        UIManager.I?.Hide(PanelId.IronCareerEncounter);
        // Do not force-show Encounter; regular flow opens it when needed.
    }

    public void ReturnToMenuFromGameOver()
    {
        UIManager.I?.Hide(PanelId.IronCareerEncounter);
        UIManager.I?.Show(PanelId.Home);
    }

    public void OpenStarterFromHome()
    {
        // Only route UI. Do NOT enter runtime; do NOT start battle.
        UIManager.I?.Hide(PanelId.Encounter);
        UIManager.I?.Hide(PanelId.PostBattleSummary);
        UIManager.I?.Show(PanelId.IronCareerEncounter);
        UIManager.I?.Hide(PanelId.Home);

        ironEncounterUI?.ShowStarter(immediate: true);
    }
}