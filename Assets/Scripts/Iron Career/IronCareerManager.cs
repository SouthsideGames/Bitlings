// Assets/Scripts/Iron Career/IronCareerManager.cs
using System;
using System.Collections;
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

    [Header("Mode")]
    [SerializeField] private Mode mode = Mode.Standard;

    [Header("Runtime Safety")]
    [Tooltip("If true, backgrounding (pause/focus loss) will auto-forfeit the current Iron run. Defaults off to avoid accidental losses.")]
    [SerializeField] private bool forfeitOnBackground = false;

    [Tooltip("Grace period (seconds) before applying background forfeit. Gives players a moment to alt-tab without ending the run.")]
    [SerializeField] private float backgroundForfeitGraceSeconds = 0.5f;

    [Header("Hire Rules")]
    [Tooltip("Chance that a hire attempt succeeds when player chooses Yes. 1 = always succeeds.")]
    [Range(0f, 1f)]
    [SerializeField] private float hireSuccessChance = 1f;

    [Header("Seed (runtime-only)")]
    [Tooltip("If true, uses the inspector seed value for deterministic debug runs. Keep off for normal random Iron runs.")]
    [SerializeField] private bool useFixedSeed = false;
    [SerializeField] private int seed = 0;

    [Header("Debug Starter Party (Phase 2)")]
    [SerializeField] private List<MonsterDataSO> debugStarterParty = new List<MonsterDataSO>();
    [SerializeField] private List<int> debugStarterLevels = new List<int>();

#if UNITY_EDITOR
    [Header("DEV ONLY")]
    [SerializeField] private bool devForceUnlockIron = false;
#endif

    private readonly IronCareerRunState _state = new IronCareerRunState();
    private IronRoster _roster;
    private IronRngStream _rng;
    private IronTitleRoller _titleRoller;
    private IronEncounterService _encounters;
    private IronBattleOutcome _lastOutcome;
    private IronMonster _pendingHire;
    private bool _hasLastOutcome;
    private bool _finalizedRunStats;
    private readonly List<int> _tmpLivingIndices = new List<int>(4);
    private Coroutine _backgroundForfeitCo;
    private Coroutine _beginNextBattleCo;

    private void ResolveBattleRefsIfNeeded()
    {
        if (!battle) battle = FindFirstObjectByType<BattleManager>(FindObjectsInactive.Include);
        if (!bridge) bridge = FindFirstObjectByType<IronBattleBridge>(FindObjectsInactive.Include);
    }

    public int Wins => Mathf.Max(0, _state.wins);
    public bool IsRunActive => _state.runActive;

    // Back-compat for UI panels that read mode directly.
    public bool IsHardcoreMode => _state.mode == IronCareerRunState.IronCareerMode.Hardcore;

    // Back-compat for post/check panels that preview forced evolution availability.
    public bool HasForcedEvolutionAvailable()
    {
        if (!_state.runActive || _roster == null) return false;
        return _roster.CanEvolveAny();
    }

    /// <summary>
    /// UI helper: expose the current Iron party list (runtime-only).
    /// This is intentionally read-only from the caller perspective.
    /// </summary>
    public IReadOnlyList<IronMonster> GetIronPartyUnsafe()
    {
        return _state.party;
    }

    /// <summary>
    /// UI action: attempt to force-evolve a party member at index.
    /// Evolution uses HP PERCENT carryover (hp/maxHp) per Iron rules.
    /// </summary>
    public bool TryForceEvolveAtIndex(int idx)
    {
        if (!_state.runActive || _roster == null || _roster.IsPartyEmpty) return false;
        return _roster.TryForceEvolveAtIndex(idx);
    }

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

        _lastOutcome = outcome;
        _hasLastOutcome = true;

        _state.runSummary.totalBattles += 1;
        _state.runSummary.totalDamageDealt += Mathf.Max(0, outcome.damageDealt);
        _state.runSummary.totalDamageTaken += Mathf.Max(0, outcome.damageTaken);
        _state.runSummary.totalCrits += Mathf.Max(0, outcome.critCount);
        _state.runSummary.totalGrowthCores += Mathf.Max(0, outcome.growthCoresGained);
        _state.runSummary.totalCredits += Mathf.Max(0, outcome.creditsGained);
        _state.runSummary.totalSecondsSurvived += Mathf.Max(0f, outcome.secondsSurvived);

        // Nuzlocke rule: any monster that hits 0 HP is removed from the roster.
        // IMPORTANT: keep shield values aligned to the surviving party order.

        // 1) Apply HP results.
        if (outcome.teamHP != null)
        {
            for (int i = 0; i < _state.party.Count; i++)
            {
                if (i >= outcome.teamHP.Length) break;
                _state.party[i].hp = Mathf.Max(0f, outcome.teamHP[i]);
            }
        }

        // 2) Remap shields to match survivors (pre-cleanup arrays are by old slot index).
        float[] remappedShields = new float[3];
        if (outcome.shieldHP != null && outcome.shieldHP.Length > 0)
        {
            int dst = 0;
            for (int src = 0; src < _state.party.Count && dst < remappedShields.Length; src++)
            {
                var m = _state.party[src];
                if (m == null || m.def == null) continue;
                if (m.IsDead) continue;

                if (src < outcome.shieldHP.Length)
                    remappedShields[dst] = Mathf.Max(0f, outcome.shieldHP[src]);

                dst++;
            }
        }

        // 3) Remove dead from roster (permadeath).
        _roster?.RemoveDead();

        _state.carryStatus = outcome.playerFieldStatus;
        _state.carryShields = remappedShields;

        if (_roster == null || _roster.IsPartyEmpty)
        {
            ShowGameOver(forfeit: false);
            return;
        }

        // If the wild fled, keep the run alive and route to post-battle (no rewards, no win increment).
        if (outcome.wildEscaped)
        {
            Debug.LogWarning("[IronCareerManager] Wild fled. Showing post-battle without win/rewards.");
            _pendingHire = null;
            _encounters?.ClearWildCache();

            ironEncounterUI?.HideAll(immediate: true);
            ShowPost();
            return;
        }

        if (!outcome.victory)
        {
            ShowGameOver(forfeit: false);
            return;
        }

        // Only grant rewards on victories to prevent loss farming.
        GrantIronRunRewards(outcome);

        _state.wins++;
        ApplyPartyAutoLevelOnWin();

        // Rotate the active slot immediately after a win, before any hire/replace flow.
        // This keeps newly hired monsters from becoming the default starter next battle.
        _roster?.RotateActiveAfterWin();

        var wildSnapshot = _state.lastRolledWild;
        if (wildSnapshot != null && wildSnapshot.def != null)
        {
            var hireTitle = _titleRoller != null
                ? _titleRoller.RollIronTitle(wildSnapshot.def, wildSnapshot.level, _rng, isWild: false)
                : wildSnapshot.lockedTitle;

            _pendingHire = new IronMonster(wildSnapshot.def, wildSnapshot.level, curHp: -1f, locked: hireTitle);
            _pendingHire.hp = _pendingHire.maxHp;
        }
        else
        {
            _pendingHire = null;
        }

        ShowHireOrReplace();
    }

    private void Awake()
    {
        ResolveBattleRefsIfNeeded();
        if (!ironEncounterUI) ironEncounterUI = FindFirstObjectByType<IronCareerEncounterPanelUI>(FindObjectsInactive.Include);
        if (!ironBattleUI) ironBattleUI = FindFirstObjectByType<IronBattleUIRoot>(FindObjectsInactive.Include);

        if (!starterPanel) starterPanel = FindFirstObjectByType<IronCareerStarterPanelUI>(FindObjectsInactive.Include);
        if (!hirePanel) hirePanel = FindFirstObjectByType<IronCareerHirePanelUI>(FindObjectsInactive.Include);
        if (!replacePanel) replacePanel = FindFirstObjectByType<IronCareerReplacePanelUI>(FindObjectsInactive.Include);
        if (!postPanel) postPanel = FindFirstObjectByType<IronCareerPostScreenUI>(FindObjectsInactive.Include);
        if (!forcedEvolvePanel) forcedEvolvePanel = FindFirstObjectByType<IronCareerForcedEvolutionUI>(FindObjectsInactive.Include);
        if (!restPanel) restPanel = FindFirstObjectByType<IronCareerRestPanelUI>(FindObjectsInactive.Include);
        if (!gameOverPanel) gameOverPanel = FindFirstObjectByType<IronCareerGameOverPanelUI>(FindObjectsInactive.Include);
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!IronCareerRuntime.IsActive) return;
        if (pauseStatus)
        {
            HandleBackgroundLoss("Application paused");
        }
        else
        {
            CancelBackgroundForfeit("Application resumed (pause cleared)");
        }
    }

    private void OnEnable()
    {
        Application.focusChanged += OnAppFocusChanged;
    }

    private void OnDisable()
    {
        Application.focusChanged -= OnAppFocusChanged;

        if (_backgroundForfeitCo != null)
        {
            StopCoroutine(_backgroundForfeitCo);
            _backgroundForfeitCo = null;
        }
    }

    private void OnAppFocusChanged(bool hasFocus)
    {
        if (!IronCareerRuntime.IsActive) return;
        if (hasFocus)
        {
            CancelBackgroundForfeit("Application regained focus");
            return;
        }

        HandleBackgroundLoss("Application lost focus");
    }

    private bool ShouldForfeitOnBackground()
    {
        return _state.runActive && forfeitOnBackground;
    }

    private void HandleBackgroundLoss(string reason)
    {
        if (!ShouldForfeitOnBackground())
        {
            DevLog.Log($"[IronCareerManager] Ignoring background event: {reason}");
            return;
        }

        if (_backgroundForfeitCo != null)
            StopCoroutine(_backgroundForfeitCo);

        DevLog.Log($"[IronCareerManager] Arming background forfeit (grace {Mathf.Max(0f, backgroundForfeitGraceSeconds):0.00}s): {reason}");
        _backgroundForfeitCo = StartCoroutine(Co_ForfeitAfterBackground(reason));
    }

    private void CancelBackgroundForfeit(string reason)
    {
        if (_backgroundForfeitCo == null) return;

        StopCoroutine(_backgroundForfeitCo);
        _backgroundForfeitCo = null;
        DevLog.Log($"[IronCareerManager] Cleared pending background forfeit: {reason}");
    }

    private IEnumerator Co_ForfeitAfterBackground(string reason)
    {
        float delay = Mathf.Max(0f, backgroundForfeitGraceSeconds);
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        _backgroundForfeitCo = null;
        Forfeit(reason);
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
        // IRON GUARD: hard-stop any active regular encounter flows so they cannot resume post-Iron.
        EncounterManager.I?.ForceStopForIron();

        IronCareerRuntime.Enter();
        _finalizedRunStats = false;
        _quitPromptActive = false;
        _suppressRulesThisRun = false;
        _hasLastOutcome = false;
        _lastOutcome = default;

        int runSeed = useFixedSeed && seed > 0
            ? seed
            : UnityEngine.Random.Range(1, int.MaxValue);

        seed = runSeed;

    #if UNITY_EDITOR
        DevLog.Log($"[IronCareerManager] StartNewRun_Internal: mode={m} starterDefs={(starterDefs != null ? starterDefs.Count : -1)} seed={runSeed} fixedSeed={useFixedSeed}");
    #endif

        _state.Reset(m, runSeed);
        _roster = new IronRoster(_state);
        _rng = new IronRngStream(runSeed);
        _titleRoller = new IronTitleRoller();
        _encounters = new IronEncounterService(_state, _rng, _titleRoller);

        // Top-level screen routing only
        UIManager.I?.Hide(PanelId.Encounter);
        
        // IRON GUARD: explicitly disable Encounter panel GameObject to prevent overlap
        var encounterPanelUI = FindFirstObjectByType<EncounterPanelUI>(FindObjectsInactive.Include);
        if (encounterPanelUI && encounterPanelUI.gameObject.activeSelf)
            encounterPanelUI.gameObject.SetActive(false);
        
        UIManager.I?.Show(PanelId.IronCareerEncounter);

        BuildStarterParty(starterDefs);

        // DEBUG: dump starter party after build so we can see why the roster might be empty/invalid.
#if UNITY_EDITOR
        try
        {
            DevLog.Log($"[IronCareerManager] Starter party built: count={_state.party?.Count ?? -1}");
            if (_state.party != null)
            {
                for (int i = 0; i < _state.party.Count; i++)
                {
                    var pm = _state.party[i];
                    DevLog.Log($"[IronCareerManager] Party[{i}] def={(pm != null && pm.def != null ? pm.def.name : "NULL")} lvl={(pm != null ? pm.level : -1)} hp={(pm != null ? pm.hp : -1f)} maxHp={(pm != null ? pm.maxHp : -1f)} dead={(pm != null && pm.IsDead)}");
                }
            }
        }
        catch (Exception ex)
        {
            DevLog.Log($"[IronCareerManager] Starter party dump failed: {ex.Message}");
        }
#endif
        if (_roster.IsPartyEmpty)
        {
            Debug.LogError("[IronCareerManager] No starter party configured. Returning to starter selection.");
            _state.runActive = false;
            IronCareerRuntime.Exit();
            ironEncounterUI?.ShowStarter(immediate: true);
            return;
        }

        QueueBeginNextBattleAfterTransition();
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
        var title = _titleRoller != null ? _titleRoller.RollIronTitle(def, lvl, _rng, isWild: false) : null;
        var m = new IronMonster(def, lvl, curHp: -1f, locked: title);
        _roster.EnsureHpInitialized(m);

        // FORCE full HP for starters
        m.hp = m.maxHp;

        _state.party.Add(m);
    }

    private bool IsHardcore => _state.mode == IronCareerRunState.IronCareerMode.Hardcore;

    private void ShowHireOrReplace()
    {
        if (_pendingHire == null || _pendingHire.def == null)
        {
            Debug.LogWarning("[IronCareerManager] Missing pending hire offer. Continuing.");
            ContinueAfterHireAndReplacement();
            return;
        }

        // Guard: uncatchable monsters can never be hired — skip the hire panel entirely.
        if (_pendingHire.def.uncatchable)
        {
            _pendingHire = null;
            ContinueAfterHireAndReplacement();
            return;
        }

        ShowHire();
    }

    private void ShowHire()
    {
            
        ironEncounterUI?.ShowHire(immediate: true);
        if (hirePanel) hirePanel.Bind(_pendingHire, skipAllowed: !IsHardcore);
    }

    public bool OnHireAccepted()
    {
        if (!_state.runActive) return false;
        if (_pendingHire == null || _pendingHire.def == null)
        {
            ContinueAfterHireAndReplacement();
            return false;
        }

        bool hireSucceeded = RollHireSuccess(_pendingHire);
        if (!hireSucceeded)
        {
            DevLog.Log("[IronCareerManager] Hire attempt failed. Recruit did not join the team.");
            FinishHireStepAndContinue();
            return false;
        }

        if (_roster.IsFull)
        {
            ironEncounterUI?.ShowReplace(immediate: true);
            if (replacePanel)
            {
                // Pass the offered hire so the replace screen can show the incoming recruit.
                replacePanel.Bind(_roster.Party, _pendingHire, hardcoreMode: IsHardcore);
            }
            else
            {
                Debug.LogWarning("[IronCareerManager] Replace panel missing; defaulting replace target to active slot.");
                _roster.ReplaceMember(_roster.ActiveIndex, _pendingHire);
                FinishHireStepAndContinue();
            }
            return true;
        }

        _roster.AddMember(_pendingHire);
        FinishHireStepAndContinue();
        return true;
    }

    public bool OnHireSkipped()
    {
        if (!_state.runActive) return false;
        if (IsHardcore)
        {
            ShowHire();
            return false;
        }
        FinishHireStepAndContinue();
        return false;
    }

    public void OnReplaceChosen(int indexToReplace)
    {
        if (!_state.runActive) return;
        if (_pendingHire == null || _pendingHire.def == null) { FinishHireStepAndContinue(); return; }

        int activeBefore = _roster != null ? _roster.ActiveIndex : -1;

        _roster.ReplaceMember(indexToReplace, _pendingHire);

        if (_roster != null && activeBefore >= 0 && activeBefore == Mathf.Clamp(indexToReplace, 0, _roster.Party.Count - 1) && _roster.Party.Count > 1)
            _roster.RotateActiveAfterWin();

        FinishHireStepAndContinue();
    }

    /// <summary>
    /// Standard-mode only: return to the Hire panel without making a replacement.
    /// Hardcore mode should never allow cancel.
    /// </summary>
    public void OnReplaceCancelled()
    {
        if (!_state.runActive) return;
        if (IsHardcore) return;
        FinishHireStepAndContinue();
    }

    private void FinishHireStepAndContinue()
    {
        _encounters?.ClearWildCache();
        _pendingHire = null;

        ironEncounterUI?.HideAll(immediate: true);
        ShowPost();
    }

    private bool RollHireSuccess(IronMonster offer)
    {
        if (offer == null || offer.def == null) return false;
        if (offer.def.uncatchable) return false;

        float chance = Mathf.Clamp01(hireSuccessChance);
        if (_rng == null) return chance >= 1f;
        return _rng.Chance(chance);
    }

    private bool IsRestFloor(int wins)
    {
        return wins > 0 && (wins % 3) == 0;
    }

    private void ContinueAfterHireAndReplacement()
    {
        if (!_state.runActive) return;

        if (_roster == null || _roster.IsPartyEmpty)
        {
            ShowGameOver(forfeit: false);
            return;
        }

        if (_roster.CanEvolveAny())
        {
            ShowForcedEvolveStep();
            return;
        }

        if (IsRestFloor(_state.wins))
        {
            restPanel?.Bind(_roster.Party, _state.wins, mode == Mode.Hardcore);
            ironEncounterUI?.ShowRest(immediate: true);
            return;
        }

        QueueBeginNextBattleAfterTransition();
    }

    private void ShowPost()
    {
        ironEncounterUI?.ShowPost(immediate: true);
        if (_hasLastOutcome)
            postPanel?.Bind(_roster.Party, _state.carryStatus, _state.wins, _lastOutcome);
        else
            postPanel?.Bind(_roster.Party, _state.carryStatus, _state.wins);
    }

    public void OnPostContinue()
    {
        if (_pendingHire != null && _pendingHire.def != null)
        {
            ShowHireOrReplace();
            return;
        }

        ironEncounterUI?.HideAll(immediate: true);
        ContinueAfterHireAndReplacement();
    }

    private void ShowForcedEvolveStep()
    {
        ironEncounterUI?.ShowForcedEvolve(immediate: true);


        forcedEvolvePanel?.Bind(_roster != null ? _roster.Party : null);
    }

    public void OnForcedEvolveContinue()
    {
        ContinueAfterHireAndReplacement();
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
    }

    /// <summary>
    /// Rest Option B: +1 level to a random living party member. Returns the monster name (for UI feedback).
    /// "Fair" randomness: chooses uniformly among living members only.
    /// HP% is preserved when max HP changes due to level.
    /// </summary>
    public string OnRestRandomLevelUp()
    {
        if (_roster == null || _roster.IsPartyEmpty) return string.Empty;

        // Gather living indices
        _tmpLivingIndices.Clear();
        for (int i = 0; i < _state.party.Count; i++)
        {
            var m = _state.party[i];
            if (m == null || m.def == null || m.IsDead) continue;
            _tmpLivingIndices.Add(i);
        }

        if (_tmpLivingIndices.Count == 0) return string.Empty;

        int pick = (_rng != null) ? _rng.NextInt(0, _tmpLivingIndices.Count) : UnityEngine.Random.Range(0, _tmpLivingIndices.Count);
        int targetIndex = _tmpLivingIndices[pick];

        var target = _state.party[targetIndex];
        if (target == null || target.def == null) return string.Empty;

        int cap = target.def.maxLevel > 0 ? target.def.maxLevel : 99;
        target.level = Mathf.Min(cap, Mathf.Max(1, target.level + 1));
        target.RecomputeMaxHpPreservePct();

        _state.party[targetIndex] = target;
        return target.def != null ? target.def.name : string.Empty;
    }

    /// <summary>
    /// Called by the Rest UI after an option is applied. Intentionally does not advance the run.
    /// The UI is responsible for showing a Continue button.
    /// </summary>
    public void OnRestAppliedOnly()
    {
        // no-op by design (kept as a clear integration point)
    }

    /// <summary>
    /// Called by the Rest UI "Continue" button to resume the run after the rest benefit is applied.
    /// </summary>
    public void OnRestContinue()
    {
        if (!_state.runActive) return;

        QueueBeginNextBattleAfterTransition();
    }

    public void OnRestBuffAt(int targetIndex)

    {
        if (_roster == null || _roster.IsPartyEmpty) return;

        targetIndex = Mathf.Clamp(targetIndex, 0, _state.party.Count - 1);
        var m = _state.party[targetIndex];
        if (m == null || m.def == null || m.IsDead)
        {
            QueueBeginNextBattleAfterTransition();
            return;
        }

        m.level = Mathf.Min(m.def.maxLevel > 0 ? m.def.maxLevel : 99, Mathf.Max(1, m.level + 1));

        float hp01 = m.Hp01;
        m.maxHp = Mathf.Max(1f, BattleCalc.CalcHP(m.def, m.level));
        m.hp = Mathf.Clamp(m.maxHp * hp01, 0f, m.maxHp);
        _state.party[targetIndex] = m;

        QueueBeginNextBattleAfterTransition();
    }

    private void QueueBeginNextBattleAfterTransition()
    {
        if (_beginNextBattleCo != null)
            StopCoroutine(_beginNextBattleCo);

        _beginNextBattleCo = StartCoroutine(Co_BeginNextBattleAfterTransition());
    }

    private IEnumerator Co_BeginNextBattleAfterTransition()
    {
        if (ironEncounterUI)
            yield return ironEncounterUI.Co_ShowBattleOnlyThenReady();
        else
            yield return null;

        _beginNextBattleCo = null;
        BeginNextBattle();
    }

    private void BeginNextBattle()
    {
        if (!_state.runActive) return;
        if (_roster == null || _roster.IsPartyEmpty) { ShowGameOver(forfeit: false); return; }

        if (!IronCareerRuntime.IsActive)
        {
            Debug.LogError("[IronCareerManager] Iron runtime inactive before battle start. Ending run gracefully.");
            ShowGameOver(forfeit: false);
            return;
        }

        var wildPreview = GetWildForNextBattle();
        if (wildPreview == null || wildPreview.def == null)
        {
            Debug.LogError("[IronCareerManager] Encounter generation failed (wild null). Ending run gracefully.");
            ShowGameOver(forfeit: false);
            return;
        }

        ResolveBattleRefsIfNeeded();

        if (!ironBattleUI)
            ironBattleUI = FindFirstObjectByType<IronBattleUIRoot>(FindObjectsInactive.Include);

        if (!battle || !bridge)
        {
            Debug.LogError("[IronCareerManager] Missing BattleManager or IronBattleBridge reference.");
            return;
        }

        if (!ironBattleUI)
        {
            Debug.LogError("[IronCareerManager] Missing IronBattleUIRoot. Iron battle textbox/UI bindings will not work.");
            return;
        }

        UIManager.I?.Show(PanelId.IronCareerEncounter);

        ironEncounterUI?.ShowBattleOnly(immediate: true);

    #if UNITY_EDITOR
        DevLog.Log($"[IronTextTrace] BeginNextBattle: battle={(battle ? battle.name : "NULL")} bridge={(bridge ? bridge.name : "NULL")} ironBattleUI={(ironBattleUI ? ironBattleUI.name : "NULL")}");
    #endif

        ironBattleUI.ApplyTo(battle);

        // DEBUG: prove what we are about to inject.
#if UNITY_EDITOR
        try
        {
            var p = GetPartyForNextBattle();
            var cachedWild = _state.lastRolledWild;
            DevLog.Log($"[IronCareerManager] BeginNextBattle inject preview: party={p.Count} cachedWild={(cachedWild != null && cachedWild.def != null ? cachedWild.def.name : "NULL")}");
        }
        catch (Exception ex)
        {
            DevLog.Log($"[IronCareerManager] Inject preview failed: {ex.Message}");
        }
#endif

        bridge.SetWins(_state.wins);
        bridge.BeginIronBattle(battle, null);
    }

    // Quit/Forfeit + GameOver UI


    // Rules popup runtime flags (session-only)
    private bool _quitPromptActive;
    private bool _suppressRulesThisRun;

    public bool IsQuitPromptActive => _quitPromptActive;
    public bool SuppressRulesThisRun => _suppressRulesThisRun;

    /// <summary>
    /// Called by IronCareerRulesPopupUI when the user toggles "Don't show again".
    /// This is intentionally session-only (does not persist).
    /// </summary>
    public void SetSuppressRulesThisRun(bool suppress)
    {
        _suppressRulesThisRun = suppress;
    }

    /// <summary>
    /// Informational rules popup acknowledged (not a forfeit confirm).
    /// </summary>
    public void AcknowledgeRules()
    {
        _quitPromptActive = false;
        ironEncounterUI?.HideRules(immediate: true);
    }

    /// <summary>
    /// Close informational rules popup.
    /// </summary>
    public void CloseRules()
    {
        _quitPromptActive = false;
        ironEncounterUI?.HideRules(immediate: true);
    }

    public void RequestQuit()
    {
        if (!_state.runActive) return;
        _quitPromptActive = true;
        ironEncounterUI?.ShowRules(immediate: true);
    }

    public void ConfirmQuitForfeit()
    {
        if (!_state.runActive) return;
        _quitPromptActive = false;
        ironEncounterUI?.HideRules(immediate: true);
        Forfeit("Quit confirmed");
    }

    public void CancelQuit()
    {
        _quitPromptActive = false;
        ironEncounterUI?.HideRules(immediate: true);
    }

    private void Forfeit(string reason)
    {
        if (!_state.runActive) return;
        Debug.LogWarning($"[IronCareerManager] FORFEIT: {reason}");
        ShowGameOver(forfeit: true);
    }

    private void ShowGameOver(bool forfeit)
    {
        // Ensure no pending background forfeit coroutine survives into game-over.
        CancelBackgroundForfeit("Game over");

        FinalizeRunStats(forfeit);

        _state.runActive = false;

        UIManager.I?.Show(PanelId.IronCareerEncounter);

        ironEncounterUI?.ShowGameOver(immediate: true);
        gameOverPanel?.Bind(_state.mode, _state.wins, _state.runSummary, forfeited: forfeit);

        // IRON GUARD: Explicitly disable regular Encounter panel BEFORE exiting Iron runtime.
        // This prevents race condition where EncounterPanelUI.OnEnable() check fails after Exit() is called.
        var encounterPanelUI = FindFirstObjectByType<EncounterPanelUI>(FindObjectsInactive.Include);
        if (encounterPanelUI && encounterPanelUI.gameObject.activeSelf)
            encounterPanelUI.gameObject.SetActive(false);

        IronCareerRuntime.Exit();

        ironBattleUI?.RestoreBattleManagerDefaults();
    }

    private static void GrantIronRunRewards(IronBattleOutcome outcome)
    {
        int credits = Mathf.Max(0, outcome.creditsGained);
        int growthCores = Mathf.Max(0, outcome.growthCoresGained);

        if (credits <= 0 && growthCores <= 0) return;

        if (ResourceManager.I != null)
        {
            if (credits > 0) ResourceManager.I.Add(ResourceType.Credits, credits);
            if (growthCores > 0) ResourceManager.I.Add(ResourceType.GrowthCore, growthCores);
            return;
        }

        if (credits > 0) ResourceBank.Add(ResourceType.Credits, credits);
        if (growthCores > 0) ResourceBank.Add(ResourceType.GrowthCore, growthCores);
    }

    private void ApplyPartyAutoLevelOnWin()
    {
        if (_state.party == null) return;

        for (int i = 0; i < _state.party.Count; i++)
        {
            var m = _state.party[i];
            if (m == null || m.def == null || m.IsDead) continue;

            int nextLevel = Mathf.Max(1, m.level + 1);
            int maxLevel = (m.def.maxLevel > 0) ? m.def.maxLevel : 99;
            m.level = Mathf.Min(maxLevel, nextLevel);
            m.RecomputeMaxHpPreservePct();
            _state.party[i] = m;
        }
    }

    private void FinalizeRunStats(bool forfeited)
    {
        if (_finalizedRunStats) return;
        _finalizedRunStats = true;

        IronCareerStats.RecordRunEnd(_state.mode, _state.wins, forfeited, _state.runSummary);
    }

    public void ReturnToMenuFromGameOver()
    {
        // IRON GUARD: Force disable regular Encounter panel to prevent it from auto-starting battles
        var encounterPanelUI = FindFirstObjectByType<EncounterPanelUI>(FindObjectsInactive.Include);
        if (encounterPanelUI && encounterPanelUI.gameObject.activeSelf)
            encounterPanelUI.gameObject.SetActive(false);

        UIManager.I?.Hide(PanelId.IronCareerEncounter);
        UIManager.I?.Show(PanelId.Home);
    }

    public void RestartIronFromGameOver()
    {
        // Route back to starter selection. Runtime is already exited in ShowGameOver().
        OpenStarterFromHome();
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