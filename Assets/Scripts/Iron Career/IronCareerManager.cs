// Assets/Scripts/Iron Career/IronCareerManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Iron Career run-loop orchestrator (Phase 3).
///
/// Hard rules (sealed mode):
/// - Runtime-only; no resume; quitting/app pause/focus loss = forfeit
/// - Do not read/write SaveManager.Data.team or .owned
/// - No rewards/resources; do not trigger global GameEvents battle end hooks
/// - No boosters/jobs/idle/world events/autobattle
/// - Titles are allowed but locked per monster instance (rolled from MonsterDataSO title track)
/// - Status carry-over: player-only, single primary field-wide status; shieldHP[] per slot
///
/// Flow order:
/// WIN  → Hire/Replace → Post → Forced Evolve → Rest (wins%3==0) → Next Battle
/// LOSS → Game Over
/// QUIT → Forfeit
/// Party empty after battle overrides everything → Game Over immediately
/// </summary>
public sealed class IronCareerManager : MonoBehaviour, IronBattleBridge.IIronBattleBridgeHost
{
    // Keep legacy Mode enum for inspector continuity.
    public enum Mode { Standard, Hardcore }

    [Header("Battle Refs")]
    [SerializeField] private BattleManager battle;
    [SerializeField] private IronBattleBridge bridge;

    [Header("Iron Systems (Phase 3)")]
    [SerializeField] private IronModeDisabler disabler;

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

    // ─────────────────────────────────────────────────────────────
    // KEEP Phase 2 inspector fields (do not delete)
    // ─────────────────────────────────────────────────────────────

    [Header("Debug Starter Party (Phase 2)")]
    [Tooltip("Starter party for debug runs (runtime-only). Max 3 used.")]
    [SerializeField] private List<MonsterDataSO> debugStarterParty = new List<MonsterDataSO>();

    [Tooltip("Starter levels (optional). If fewer than party count, missing entries default to 1.")]
    [SerializeField] private List<int> debugStarterLevels = new List<int>();

    [Header("Debug Wild Pool (Phase 2)")]
    [Tooltip("If empty, wild defaults to debugFallbackWild.")]
    [SerializeField] private List<MonsterDataSO> debugWildPool = new List<MonsterDataSO>();
    [SerializeField] private MonsterDataSO debugFallbackWild;
    [SerializeField] private int debugFallbackWildLevel = 1;

    // ─────────────────────────────────────────────────────────────
    // Runtime state (Phase 3)
    // ─────────────────────────────────────────────────────────────

    private readonly IronCareerRunState _state = new IronCareerRunState();
    private IronRoster _roster;
    private IronRngStream _rng;
    private IronTitleRoller _titleRoller;
    private IronEncounterService _encounters;

    // The hire offer is always the last rolled wild (shared by battle + hire).
    private IronMonster _pendingHire;

    private bool _forfeited;

    // ─────────────────────────────────────────────────────────────
    // Bridge host
    // ─────────────────────────────────────────────────────────────

    public int Wins => Mathf.Max(0, _state.wins);

    public IReadOnlyList<BattleCombatant> GetPartyForNextBattle()
    {
        if (!_state.runActive) return Array.Empty<BattleCombatant>();

        var party = _state.party;
        if (party == null || party.Count == 0) return Array.Empty<BattleCombatant>();

        // BattleManager treats slot 0 as active; reorder so ActiveIndex appears first.
        var list = new List<BattleCombatant>(Mathf.Min(3, party.Count));
        int active = _roster != null ? _roster.ActiveIndex : 0;

        // active first
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
                    combatantId = null, // bridge overwrites
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

        // Cache "last rolled wild" for hire step
        _state.lastRolledWild = wild;

        return new BattleCombatant
        {
            def = wild.def,
            level = Mathf.Max(1, wild.level),
            hp = Mathf.Max(0f, wild.hp),
            combatantId = null, // bridge overwrites
            lockedTitle = wild.lockedTitle
        };
    }

    public IronFieldStatusSnapshot GetCarryStatus() => _state.carryStatus;
    public float[] GetCarryShields() => _state.carryShields;

    public void OnIronBattleResolved(IronBattleOutcome outcome)
    {
        if (!_state.runActive) return;

        // Apply end-of-battle snapshot to party HP
        if (outcome.teamHP != null)
        {
            for (int i = 0; i < _state.party.Count; i++)
            {
                if (i >= outcome.teamHP.Length) break;
                _state.party[i].hp = Mathf.Max(0f, outcome.teamHP[i]);
            }
        }

        // Remove dead and clamp active
        _roster?.RemoveDead();

        // Carry-over export (player-only) for next battle
        _state.carryStatus = outcome.playerFieldStatus;
        _state.carryShields = outcome.shieldHP ?? new float[3];

        // Party empty overrides everything.
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

        // WIN path
        _state.wins++;
        _roster.RotateActiveAfterWin();

        // Cache wild from encounter service for the hire step.
        _pendingHire = _state.lastRolledWild;

        // Enter Hire/Replace.
        ShowHireOrReplace();
    }

    // ─────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (!battle) battle = FindFirstObjectByType<BattleManager>();
        if (!bridge) bridge = FindFirstObjectByType<IronBattleBridge>();
        if (!disabler) disabler = FindFirstObjectByType<IronModeDisabler>(FindObjectsInactive.Include);

        if (!starterPanel) starterPanel = FindFirstObjectByType<IronCareerStarterPanelUI>(FindObjectsInactive.Include);
        if (!hirePanel) hirePanel = FindFirstObjectByType<IronCareerHirePanelUI>(FindObjectsInactive.Include);
        if (!replacePanel) replacePanel = FindFirstObjectByType<IronCareerReplacePanelUI>(FindObjectsInactive.Include);
        if (!postPanel) postPanel = FindFirstObjectByType<IronCareerPostScreenUI>(FindObjectsInactive.Include);
        if (!forcedEvolvePanel) forcedEvolvePanel = FindFirstObjectByType<IronCareerForcedEvolutionUI>(FindObjectsInactive.Include);
        if (!restPanel) restPanel = FindFirstObjectByType<IronCareerRestPanelUI>(FindObjectsInactive.Include);
        if (!gameOverPanel) gameOverPanel = FindFirstObjectByType<IronCareerGameOverPanelUI>(FindObjectsInactive.Include);
        if (!rulesPopup) rulesPopup = FindFirstObjectByType<IronCareerRulesPopupUI>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        Application.focusChanged += OnAppFocusChanged;
    }

    private void OnDisable()
    {
        Application.focusChanged -= OnAppFocusChanged;
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!IronCareerRuntime.IsActive) return;
        if (pauseStatus) Forfeit("Application paused");
    }

    private void OnAppFocusChanged(bool hasFocus)
    {
        if (!IronCareerRuntime.IsActive) return;
        if (!hasFocus) Forfeit("Application lost focus");
    }

    // ─────────────────────────────────────────────────────────────
    // Phase 2 debug entry points (PRESERVED)
    // ─────────────────────────────────────────────────────────────

    [ContextMenu("DEBUG/Start Standard Run")]
    public void DebugStartStandardRun() => StartNewRun(mode: Mode.Standard);

    [ContextMenu("DEBUG/Start Hardcore Run")]
    public void DebugStartHardcoreRun() => StartNewRun(mode: Mode.Hardcore);

    [ContextMenu("DEBUG/Begin Next Battle")]
    public void DebugBeginNextBattle() => BeginNextBattle();

    /// <summary>
    /// Preserved Phase 2 API: starts a run using current debug starter party.
    /// </summary>
    public void StartNewRun(Mode mode)
    {
        var m = (mode == Mode.Hardcore) ? IronCareerRunState.IronCareerMode.Hardcore : IronCareerRunState.IronCareerMode.Standard;
        StartNewRun_Internal(m, starterDefs: null);
    }

    // ─────────────────────────────────────────────────────────────
    // Phase 3.A: Starter + UI entry (METHODS UI EXPECTS)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by IronCareerStarterPanelUI.
    /// </summary>
    public void StartNewRunFromUI(IronCareerRunState.IronCareerMode m, List<MonsterDataSO> starterDefs)
    {
        mode = (m == IronCareerRunState.IronCareerMode.Hardcore) ? Mode.Hardcore : Mode.Standard;
        StartNewRun_Internal(m, starterDefs);
    }

    private void StartNewRun_Internal(IronCareerRunState.IronCareerMode m, List<MonsterDataSO> starterDefs)
    {
        // No resume: always fresh.
        IronCareerRuntime.Enter();
        _forfeited = false;

        if (seed == 0) seed = UnityEngine.Random.Range(1, int.MaxValue);

        _state.Reset(m, seed);
        _roster = new IronRoster(_state);
        _rng = new IronRngStream(seed);
        _titleRoller = new IronTitleRoller();
        _encounters = new IronEncounterService(_state, _rng, _titleRoller);

        disabler?.ApplyIron();

        BuildStarterParty(starterDefs);
        if (_roster.IsPartyEmpty)
        {
            Debug.LogError("[IronCareerManager] No starter party configured.");
            ShowGameOver(forfeit: false);
            return;
        }

        // Close starter panel
        UIManager.I?.Hide(PanelId.IronCareerStarter);

        BeginNextBattle();
    }

    private void BuildStarterParty(List<MonsterDataSO> starterDefs)
    {
        _state.party.Clear();

        // Use provided roulette list first.
        if (starterDefs != null && starterDefs.Count > 0)
        {
            for (int i = 0; i < starterDefs.Count && _state.party.Count < 3; i++)
            {
                var def = starterDefs[i];
                if (!def) continue;
                AddStarter(def, lvl: 1);
            }
        }

        // Fallback to debug list.
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

    /// <summary>
    /// Used by Starter panel to preview a roulette party.
    /// </summary>
    public List<MonsterDataSO> RollStarterPartyRoulette()
    {
        var all = MonsterLibraryLocator.AllMonsters;
        var result = new List<MonsterDataSO>(3);
        if (all == null || all.Count == 0) return result;

        // Build weighted list for canBeStarter.
        int total = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var d = all[i];
            if (!d) continue;
            if (!d.canBeStarter) continue;
            if (d.uncatchable) continue;
            if (d.isBoss) continue;
            total += Mathf.Max(0, d.starterWeight);
        }

        if (total <= 0) return result;

        // UI preview uses Unity random; seed isn't committed until run starts.
        var used = new HashSet<MonsterDataSO>();
        for (int pick = 0; pick < 3; pick++)
        {
            int roll = UnityEngine.Random.Range(0, total);
            MonsterDataSO chosen = null;

            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (!d) continue;
                if (!d.canBeStarter) continue;
                if (d.uncatchable) continue;
                if (d.isBoss) continue;
                int w = Mathf.Max(0, d.starterWeight);
                if (w <= 0) continue;
                roll -= w;
                if (roll < 0)
                {
                    chosen = d;
                    break;
                }
            }

            if (!chosen) break;
            if (used.Contains(chosen)) { pick--; continue; }
            used.Add(chosen);
            result.Add(chosen);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────
    // Phase 3.B: Flow steps (METHODS UI EXPECTS)
    // ─────────────────────────────────────────────────────────────

    private bool IsHardcore => _state.mode == IronCareerRunState.IronCareerMode.Hardcore;

    private void ShowHireOrReplace()
    {
        // If missing hire offer, fail-safe: proceed to post.
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
        UIManager.I?.Show(PanelId.IronCareerHire);
        if (hirePanel)
            hirePanel.Bind(_pendingHire, skipAllowed: !IsHardcore);
    }

    public void OnHireAccepted()
    {
        if (!_state.runActive) return;
        if (_pendingHire == null || _pendingHire.def == null) { ShowPost(); return; }

        // If party full, go to Replace.
        if (_roster.IsFull)
        {
            UIManager.I?.Show(PanelId.IronCareerReplace);
            if (replacePanel) replacePanel.Bind(_roster.Party);
            return;
        }

        // Add directly.
        _roster.AddMember(_pendingHire);
        FinishHireStepAndContinue();
    }

    public void OnHireSkipped()
    {
        if (!_state.runActive) return;
        if (IsHardcore) return; // should be disabled by UI
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
        // Consume wild cache so next battle rolls a fresh wild.
        _encounters?.ClearWildCache();
        _pendingHire = null;

        UIManager.I?.Hide(PanelId.IronCareerHire);
        UIManager.I?.Hide(PanelId.IronCareerReplace);

        ShowPost();
    }

    private void ShowPost()
    {
        UIManager.I?.Show(PanelId.IronCareerPost);
        if (postPanel)
            postPanel.Bind(_roster.Party, _state.carryStatus);
    }

    public void OnPostContinue()
    {
        UIManager.I?.Hide(PanelId.IronCareerPost);
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

        UIManager.I?.Show(PanelId.IronCareerForcedEvolve);
        forcedEvolvePanel?.Bind(evolved, before ?? "", after ?? "");
    }

    public void OnForcedEvolveContinue()
    {
        UIManager.I?.Hide(PanelId.IronCareerForcedEvolve);

        if ((_state.wins % 3) == 0)
        {
            UIManager.I?.Show(PanelId.IronCareerRest);
            return;
        }

        BeginNextBattle();
    }

    public void OnRestHeal()
    {
        if (_roster == null) return;

        // Heal party 25% (no revive).
        for (int i = 0; i < _state.party.Count; i++)
        {
            var m = _state.party[i];
            if (m == null || m.def == null || m.IsDead) continue;

            float add = 0.25f * Mathf.Max(1f, m.maxHp);
            m.hp = Mathf.Min(m.maxHp, m.hp + add);
            _state.party[i] = m;
        }

        UIManager.I?.Hide(PanelId.IronCareerRest);
        BeginNextBattle();
    }

    public void OnRestBuff()
    {
        if (_roster == null || _roster.IsPartyEmpty) return;

        // +1 level to a random alive party member.
        int tries = 0;
        while (tries++ < 8)
        {
            int idx = _rng != null ? _rng.NextInt(0, _state.party.Count) : UnityEngine.Random.Range(0, _state.party.Count);
            var m = _state.party[idx];
            if (m == null || m.def == null || m.IsDead) continue;

            m.level = Mathf.Min(m.def.maxLevel > 0 ? m.def.maxLevel : 99, Mathf.Max(1, m.level + 1));

            // Recalc HP preserving %
            float hp01 = m.Hp01;
            m.maxHp = Mathf.Max(1f, BattleCalc.CalcHP(m.def, m.level));
            m.hp = Mathf.Clamp(m.maxHp * hp01, 0f, m.maxHp);

            _state.party[idx] = m;
            break;
        }

        UIManager.I?.Hide(PanelId.IronCareerRest);
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

        bridge.SetWins(_state.wins);
        bridge.BeginIronBattle(battle, null);
    }

    // ─────────────────────────────────────────────────────────────
    // Quit/Forfeit + GameOver UI (METHODS UI EXPECTS)
    // ─────────────────────────────────────────────────────────────

    public void RequestQuit()
    {
        if (!_state.runActive) return;
        UIManager.I?.Show(PanelId.IronCareerRules);
    }

    public void ConfirmQuitForfeit()
    {
        if (!_state.runActive) return;
        UIManager.I?.Hide(PanelId.IronCareerRules);
        Forfeit("Quit confirmed");
    }

    public void CancelQuit()
    {
        UIManager.I?.Hide(PanelId.IronCareerRules);
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
        // Lock run state immediately.
        _state.runActive = false;

        // Close any iron panels that might be open.
        UIManager.I?.Hide(PanelId.IronCareerHire);
        UIManager.I?.Hide(PanelId.IronCareerReplace);
        UIManager.I?.Hide(PanelId.IronCareerPost);
        UIManager.I?.Hide(PanelId.IronCareerForcedEvolve);
        UIManager.I?.Hide(PanelId.IronCareerRest);
        UIManager.I?.Hide(PanelId.IronCareerRules);

        UIManager.I?.Show(PanelId.IronCareerGameOver);
        gameOverPanel?.Bind(_state.wins, forfeited: forfeit);

        // End sealed mode runtime.
        IronCareerRuntime.Exit();
        disabler?.Restore();
    }

    public void ReturnToMenuFromGameOver()
    {
        UIManager.I?.Hide(PanelId.IronCareerGameOver);
        UIManager.I?.Show(PanelId.Home);
    }
}