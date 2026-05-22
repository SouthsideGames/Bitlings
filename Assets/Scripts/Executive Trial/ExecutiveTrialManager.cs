// Assets/Scripts/Executive Trial/ExecutiveTrialManager.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class ExecutiveTrialManager : MonoBehaviour, ExecutiveTrailBattleBridge.IExecutiveTrailBattleBridgeHost
{
    public enum Mode { Standard, Hardcore }

    [Header("Battle Refs")]
    [SerializeField] private BattleManager battle;
    [SerializeField] private ExecutiveTrailBattleBridge bridge;

    [Header("Executive Trial Systems (Phase 3)")]
    [Tooltip("Reference to the Executive Trial rift panel controller on Panel_ExecutiveTrialRift.")]
    [SerializeField] private ExecutiveTrialRiftPanelUI executiveTrialRiftUI;

    [Tooltip("Reference to the Executive Trial battle UI root (Panel_ExecutiveTrialRift/ExecutiveTrialBattle).")]
    [SerializeField] private ExecutiveTrialBattleUIRoot executiveTrialBattleUI;

    [Header("Executive Trial Panels (Phase 3)")]
    [SerializeField] private ExecutiveTrialStarterPanelUI starterPanel;
    [SerializeField] private ExecutiveTrialHirePanelUI hirePanel;
    [SerializeField] private ExecutiveTrialReplacePanelUI replacePanel;
    [SerializeField] private ExecutiveTrialPostScreenUI postPanel;
    [SerializeField] private ExecutiveTrialForcedEvolutionUI forcedEvolvePanel;
    [SerializeField] private ExecutiveTrialRestPanelUI restPanel;
    [SerializeField] private ExecutiveTrialGameOverPanelUI gameOverPanel;

    [Header("Mode")]
    [SerializeField] private Mode mode = Mode.Standard;

    [Header("Runtime Safety")]
    [Tooltip("If true, backgrounding (pause/focus loss) will auto-forfeit the current Iron run. Defaults off to avoid accidental losses.")]
    [SerializeField] private bool forfeitOnBackground = false;

    [Tooltip("Grace period (seconds) before applying background forfeit. Gives players a moment to alt-tab without ending the run.")]
    [SerializeField] private float backgroundForfeitGraceSeconds = 0.5f;

    // Hire success is rarity-gated: rarer monsters are harder to recruit.
    // The hireDenyPrefab animation is shown to the player on failure.
    private static float GetHireChanceForRarity(MonsterRarity rarity)
    {
        return rarity switch
        {
            MonsterRarity.Common    => 1.00f,
            MonsterRarity.Uncommon  => 0.90f,
            MonsterRarity.Rare      => 0.80f,
            MonsterRarity.Epic      => 0.70f,
            MonsterRarity.Legendary => 0.55f,
            MonsterRarity.Mythic    => 0.40f,
            MonsterRarity.Boss      => 0.30f,
            _                       => 0.85f,
        };
    }

    public float GetHireSuccessChance(ExecutiveTrailMonster offer)
    {
        if (offer == null || offer.def == null) return 0f;
        if (offer.def.uncatchable) return 0f;
        return GetHireChanceForRarity(offer.def.rarity);
    }

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

    [Tooltip("Testing only. Converts normal Iron battle losses into wins so panel flow can be exercised end-to-end.")]
    [SerializeField] private bool devAlwaysWinBattles = false;
#elif DEVELOPMENT_BUILD
    [Header("DEV ONLY")]
    [Tooltip("Testing only. Converts normal Iron battle losses into wins so panel flow can be exercised end-to-end.")]
    [SerializeField] private bool devAlwaysWinBattles = false;
#endif

    private readonly ExecutiveTrialRunState _state = new ExecutiveTrialRunState();
    private ExecutiveTrailRoster _roster;
    private ExecutiveTrailRngStream _rng;
    private ExecutiveTrailTitleRoller _titleRoller;
    private ExecutiveTrailRiftService _rifts;
    private IronBattleOutcome _lastOutcome;
    private ExecutiveTrailMonster _pendingHire;
    private bool _ironWildIsPremium;
    private bool _hasLastOutcome;
    private bool _finalizedRunStats;
    private readonly List<int> _tmpLivingIndices = new List<int>(4);
    private Coroutine _backgroundForfeitCo;
    private Coroutine _beginNextBattleCo;

    private void ResolveBattleRefsIfNeeded()
    {
        if (!battle) battle = FindFirstObjectByType<BattleManager>(FindObjectsInactive.Include);
        if (!bridge) bridge = FindFirstObjectByType<ExecutiveTrailBattleBridge>(FindObjectsInactive.Include);
    }

    public int Wins => Mathf.Max(0, _state.wins);
    public bool IsRunActive => _state.runActive;

    // Back-compat for UI panels that read mode directly.
    public bool IsHardcoreMode => _state.mode == ExecutiveTrialRunState.ExecutiveTrialMode.Hardcore;

    public bool IronWildIsPremium => _ironWildIsPremium;

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
    public IReadOnlyList<ExecutiveTrailMonster> GetIronPartyUnsafe()
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

        return SaveManager.Data != null && SaveManager.Data.HasExecutiveTrialUnlocked;
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

        var wild = _rifts != null ? _rifts.RollNextWild() : null;
        if (wild == null || wild.def == null) return null;

        _state.lastRolledWild = wild;

        _ironWildIsPremium = wild.isPremium;

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
        int winsBeforeBattle = _state.wins;

        if (!_state.runActive) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        NormalizeOutcomeForDevTesting(ref outcome);
#endif

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
        int deadCount = 0;
        for (int i = 0; i < _state.party.Count; i++)
        {
            var m = _state.party[i];
            if (m != null && m.IsDead) deadCount++;
        }
        _state.runSummary.totalDeaths += deadCount;
        _roster?.RemoveDead();

        _state.carryStatus = outcome.playerFieldStatus;
        _state.carryShields = remappedShields;

        _state.battleLog.Add(new IronBattleLogEntry
        {
            victory          = outcome.victory,
            wildEscaped      = outcome.wildEscaped,
            playerEscaped    = outcome.escaped,
            wildId           = outcome.wildDef != null ? outcome.wildDef.id : string.Empty,
            wildLevel        = Mathf.Max(1, outcome.wildLevel),
            damageDealt      = Mathf.Max(0, outcome.damageDealt),
            damageTaken      = Mathf.Max(0, outcome.damageTaken),
            turnsSurvived    = Mathf.Max(0, outcome.turnsSurvived),
            deathsThisBattle = deadCount,
            winsBeforeBattle = winsBeforeBattle,
            isForfeit        = false,
        });

        _state.battleInProgress = false; // FIXED: cleared after clean outcome — crash between these two = recoverable loss
        ExecutiveTrialMetaSave.Save(LoadMetaData());

        if (_roster == null || _roster.IsPartyEmpty)
        {
            ShowGameOver(forfeit: false, defeatCauseOverride: outcome.wildEscaped ? "Enemy Fled" : null);
            return;
        }

        // If the wild fled, keep the run alive and route to post-battle (no rewards, no win increment).
        if (outcome.wildEscaped)
        {
            Debug.LogWarning("[ExecutiveTrialManager] Wild fled. Showing post-battle without win/rewards.");
            _pendingHire = null;
            _rifts?.ClearWildCache();

            executiveTrialRiftUI?.HideAll(immediate: true);
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
        TryRecordExecutiveTrialWinForActiveSpecies();
        ApplyPartyAutoLevelOnWin();

        GameEvents.ExecutiveTrialBattleWon?.Invoke();

        // Rotate the active slot immediately after a win, before any hire/replace flow.
        // This keeps newly hired monsters from becoming the default starter next battle.
        _roster?.RotateActiveAfterWin();

        var wildSnapshot = _state.lastRolledWild;
        if (wildSnapshot != null && wildSnapshot.def != null)
        {
            var hireTitle = _titleRoller != null
                ? _titleRoller.RollExecutiveTrailTitle(wildSnapshot.def, wildSnapshot.level, _rng, isWild: false)
                : wildSnapshot.lockedTitle;

            _pendingHire = new ExecutiveTrailMonster(wildSnapshot.def, wildSnapshot.level, curHp: -1f, locked: hireTitle, premium: wildSnapshot.isPremium);
            _pendingHire.hp = _pendingHire.maxHp;
        }
        else
        {
            _pendingHire = null;
        }

        ShowHireOrReplace();
    }

    private void TryRecordExecutiveTrialWinForActiveSpecies()
    {
        if (_roster == null) return;

        int idx = _roster.ActiveIndex;
        var party = _state.party;
        if (party == null || idx < 0 || idx >= party.Count) return;

        var active = party[idx];
        if (active == null || active.def == null || string.IsNullOrEmpty(active.def.id)) return;

        var owned = SaveManager.Data?.owned;
        if (owned == null || owned.Count == 0) return;

        for (int i = 0; i < owned.Count; i++)
        {
            var o = owned[i];
            if (o == null || string.IsNullOrEmpty(o.ownedUID) || string.IsNullOrEmpty(o.monsterId)) continue;
            if (!string.Equals(o.monsterId, active.def.id, StringComparison.Ordinal)) continue;

            SaveManager.RecordExecutiveTrialWinForOwnedUid(o.ownedUID);
            break;
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void NormalizeOutcomeForDevTesting(ref IronBattleOutcome outcome)
    {
        if (!devAlwaysWinBattles) return;
        if (outcome.victory || outcome.escaped || outcome.wildEscaped) return;
        if (_state.party == null || _state.party.Count == 0) return;

        int count = _state.party.Count;

        if (outcome.teamHP == null || outcome.teamHP.Length < count)
        {
            var expandedTeamHp = new float[count];
            if (outcome.teamHP != null)
                Array.Copy(outcome.teamHP, expandedTeamHp, Mathf.Min(outcome.teamHP.Length, expandedTeamHp.Length));
            outcome.teamHP = expandedTeamHp;
        }

        if (outcome.teamMaxHP == null || outcome.teamMaxHP.Length < count)
        {
            var expandedMaxHp = new float[count];
            if (outcome.teamMaxHP != null)
                Array.Copy(outcome.teamMaxHP, expandedMaxHp, Mathf.Min(outcome.teamMaxHP.Length, expandedMaxHp.Length));
            outcome.teamMaxHP = expandedMaxHp;
        }

        for (int i = 0; i < count; i++)
        {
            var monster = _state.party[i];
            if (monster == null || monster.def == null) continue;

            float maxHp = Mathf.Max(1f, monster.maxHp);
            outcome.teamMaxHP[i] = maxHp;

            float resolvedHp = outcome.teamHP[i] > 0f ? outcome.teamHP[i] : monster.hp;
            if (resolvedHp <= 0f)
                resolvedHp = Mathf.Max(1f, maxHp * 0.25f);

            outcome.teamHP[i] = Mathf.Clamp(resolvedHp, 1f, maxHp);
        }

        outcome.victory = true;
        outcome.escaped = false;

        DevLog.Log("[ExecutiveTrialManager] DEV override active: converted Iron battle loss into a win for panel-flow testing.");
    }
#endif

    private void Awake()
    {
        ResolveBattleRefsIfNeeded();
        if (!executiveTrialRiftUI) executiveTrialRiftUI = FindFirstObjectByType<ExecutiveTrialRiftPanelUI>(FindObjectsInactive.Include);
        if (!executiveTrialBattleUI) executiveTrialBattleUI = FindFirstObjectByType<ExecutiveTrialBattleUIRoot>(FindObjectsInactive.Include);

        if (!starterPanel) starterPanel = FindFirstObjectByType<ExecutiveTrialStarterPanelUI>(FindObjectsInactive.Include);
        if (!hirePanel) hirePanel = FindFirstObjectByType<ExecutiveTrialHirePanelUI>(FindObjectsInactive.Include);
        if (!replacePanel) replacePanel = FindFirstObjectByType<ExecutiveTrialReplacePanelUI>(FindObjectsInactive.Include);
        if (!postPanel) postPanel = FindFirstObjectByType<ExecutiveTrialPostScreenUI>(FindObjectsInactive.Include);
        if (!forcedEvolvePanel) forcedEvolvePanel = FindFirstObjectByType<ExecutiveTrialForcedEvolutionUI>(FindObjectsInactive.Include);
        if (!restPanel) restPanel = FindFirstObjectByType<ExecutiveTrialRestPanelUI>(FindObjectsInactive.Include);
        if (!gameOverPanel) gameOverPanel = FindFirstObjectByType<ExecutiveTrialGameOverPanelUI>(FindObjectsInactive.Include);
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!ExecutiveTrialRuntime.IsActive) return;
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
        if (!ExecutiveTrialRuntime.IsActive) return;
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
            DevLog.Log($"[ExecutiveTrialManager] Ignoring background event: {reason}");
            return;
        }

        if (_backgroundForfeitCo != null)
            StopCoroutine(_backgroundForfeitCo);

        DevLog.Log($"[ExecutiveTrialManager] Arming background forfeit (grace {Mathf.Max(0f, backgroundForfeitGraceSeconds):0.00}s): {reason}");
        _backgroundForfeitCo = StartCoroutine(Co_ForfeitAfterBackground(reason));
    }

    private void CancelBackgroundForfeit(string reason)
    {
        if (_backgroundForfeitCo == null) return;

        StopCoroutine(_backgroundForfeitCo);
        _backgroundForfeitCo = null;
        DevLog.Log($"[ExecutiveTrialManager] Cleared pending background forfeit: {reason}");
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

    [ContextMenu("DEBUG/Show Forced Evolution")]
    public void DebugShowForcedEvolution()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_state.runActive)
        {
            Debug.LogWarning("[ExecutiveTrialManager] Cannot open forced evolution debug panel without an active Iron run.");
            return;
        }

        if (_roster == null || _roster.IsPartyEmpty)
        {
            Debug.LogWarning("[ExecutiveTrialManager] Cannot open forced evolution debug panel with an empty roster.");
            return;
        }

        if (!PrepareForcedEvolutionCandidateForDebug(out int preparedIndex))
        {
            Debug.LogWarning("[ExecutiveTrialManager] No party member has an evolution chain available for forced evolution panel testing.");
            return;
        }

        DevLog.Log($"[ExecutiveTrialManager] Debug forced evolution prepared for slot {preparedIndex}.");
        _pendingHire = null;
        ShowForcedEvolveStep();
#endif
    }

    public void StartNewRun(Mode mode)
    {
        // If someone wires a button to this by mistake, we should still show Starter first.
        this.mode = mode;
        OpenStarterFromHome();
    }

    public void StartNewRunFromUI(ExecutiveTrialRunState.ExecutiveTrialMode m, List<MonsterDataSO> starterDefs)
    {
        mode = (m == ExecutiveTrialRunState.ExecutiveTrialMode.Hardcore) ? Mode.Hardcore : Mode.Standard;
        StartNewRun_Internal(m, starterDefs);
    }

    private void StartNewRun_Internal(ExecutiveTrialRunState.ExecutiveTrialMode m, List<MonsterDataSO> starterDefs)
    {
        // IRON GUARD: hard-stop any active regular rift flows so they cannot resume post-Iron.
        RiftManager.I?.ForceStopForIron();

        ExecutiveTrialRuntime.Enter();
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
        DevLog.Log($"[ExecutiveTrialManager] StartNewRun_Internal: mode={m} starterDefs={(starterDefs != null ? starterDefs.Count : -1)} seed={runSeed} fixedSeed={useFixedSeed}");
    #endif

        _state.Reset(m, runSeed);
        _roster = new ExecutiveTrailRoster(_state);
        _rng = new ExecutiveTrailRngStream(runSeed);
        _titleRoller = new ExecutiveTrailTitleRoller();
        _rifts = new ExecutiveTrailRiftService(_state, _rng, _titleRoller);

        // Top-level screen routing only
        UIManager.I?.Hide(PanelId.Rift);
        
        // IRON GUARD: explicitly disable Rift panel GameObject to prevent overlap
        var riftPanelUI = FindFirstObjectByType<RiftPanelUI>(FindObjectsInactive.Include);
        if (riftPanelUI && riftPanelUI.gameObject.activeSelf)
            riftPanelUI.gameObject.SetActive(false);
        
        UIManager.I?.Show(PanelId.ExecutiveTrialRift);

        BuildStarterParty(starterDefs);

        // DEBUG: dump starter party after build so we can see why the roster might be empty/invalid.
#if UNITY_EDITOR
        try
        {
            DevLog.Log($"[ExecutiveTrialManager] Starter party built: count={_state.party?.Count ?? -1}");
            if (_state.party != null)
            {
                for (int i = 0; i < _state.party.Count; i++)
                {
                    var pm = _state.party[i];
                    DevLog.Log($"[ExecutiveTrialManager] Party[{i}] def={(pm != null && pm.def != null ? pm.def.name : "NULL")} lvl={(pm != null ? pm.level : -1)} hp={(pm != null ? pm.hp : -1f)} maxHp={(pm != null ? pm.maxHp : -1f)} dead={(pm != null && pm.IsDead)}");
                }
            }
        }
        catch (Exception ex)
        {
            DevLog.Log($"[ExecutiveTrialManager] Starter party dump failed: {ex.Message}");
        }
#endif
        if (_roster.IsPartyEmpty)
        {
            Debug.LogError("[ExecutiveTrialManager] No starter party configured. Returning to starter selection.");
            _state.runActive = false;
            ExecutiveTrialRuntime.Exit();
            executiveTrialRiftUI?.ShowStarter(immediate: true);
            return;
        }

        QueueBeginNextBattleAfterTransition();

        GameEvents.ExecutiveTrialStarted?.Invoke();
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
        var title = _titleRoller != null ? _titleRoller.RollExecutiveTrailTitle(def, lvl, _rng, isWild: false) : null;
        var m = new ExecutiveTrailMonster(def, lvl, curHp: -1f, locked: title);
        _roster.EnsureHpInitialized(m);

        // FORCE full HP for starters
        m.hp = m.maxHp;

        _state.party.Add(m);
    }

    private bool IsHardcore => _state.mode == ExecutiveTrialRunState.ExecutiveTrialMode.Hardcore;

    private void ShowHireOrReplace()
    {
        if (_pendingHire == null || _pendingHire.def == null)
        {
            Debug.LogWarning("[ExecutiveTrialManager] Missing pending hire offer. Continuing.");
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
            
        executiveTrialRiftUI?.ShowHire(immediate: true);
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
            DevLog.Log("[ExecutiveTrialManager] Hire attempt failed. Recruit did not join the team.");
            FinishHireStepAndContinue();
            return false;
        }

        if (_roster.IsFull)
        {
            executiveTrialRiftUI?.ShowReplace(immediate: true);
            if (replacePanel)
            {
                // Pass the offered hire so the replace screen can show the incoming recruit.
                replacePanel.Bind(_roster.Party, _pendingHire, hardcoreMode: IsHardcore);
            }
            else
            {
                Debug.LogWarning("[ExecutiveTrialManager] Replace panel missing; defaulting replace target to active slot.");
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
        _rifts?.ClearWildCache();
        _pendingHire = null;

        executiveTrialRiftUI?.HideAll(immediate: true);
        ShowPost();
    }

    private bool RollHireSuccess(ExecutiveTrailMonster offer)
    {
        if (offer == null || offer.def == null) return false;
        if (offer.def.uncatchable) return false;

        float chance = Mathf.Clamp01(GetHireChanceForRarity(offer.def.rarity));
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
            executiveTrialRiftUI?.ShowRest(immediate: true);
            return;
        }

        QueueBeginNextBattleAfterTransition();
    }

    private void ShowPost()
    {
        executiveTrialRiftUI?.ShowPost(immediate: true);
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

        executiveTrialRiftUI?.HideAll(immediate: true);
        ContinueAfterHireAndReplacement();
    }

    private void ShowForcedEvolveStep()
    {
        executiveTrialRiftUI?.ShowForcedEvolve(immediate: true);


        forcedEvolvePanel?.Bind(_roster != null ? _roster.Party : null);
    }

    private bool PrepareForcedEvolutionCandidateForDebug(out int preparedIndex)
    {
        preparedIndex = -1;

        if (_state.party == null || _state.party.Count == 0)
            return false;

        for (int i = 0; i < _state.party.Count; i++)
        {
            var monster = _state.party[i];
            if (monster == null || monster.def == null || monster.IsDead)
                continue;

            var nextForm = monster.def.evolutionForm;
            if (nextForm == null || ReferenceEquals(nextForm, monster.def))
                continue;

            int requiredLevel = monster.def.evolutionLevel > 0 ? monster.def.evolutionLevel : 5;
            if (monster.level < requiredLevel)
            {
                monster.level = requiredLevel;
                monster.RecomputeMaxHpPreservePct();
                _state.party[i] = monster;
            }

            preparedIndex = i;
            return true;
        }

        return false;
    }

    public void OnForcedEvolveContinue()
    {
        ContinueAfterHireAndReplacement();
    }

    public struct RestLevelUpResult
    {
        public string monsterName;
        public int levelsGained;
    }

    private int RollIntInclusive(int minInclusive, int maxInclusive)
    {
        if (maxInclusive < minInclusive)
        {
            int tmp = minInclusive;
            minInclusive = maxInclusive;
            maxInclusive = tmp;
        }

        if (_rng != null)
            return _rng.NextInt(minInclusive, maxInclusive + 1);

        return UnityEngine.Random.Range(minInclusive, maxInclusive + 1);
    }

    public int OnRestHealRandomPercent(int minPercentInclusive, int maxPercentInclusive)
    {
        int rolledPercent = RollIntInclusive(minPercentInclusive, maxPercentInclusive);
        float healPct = Mathf.Clamp01(rolledPercent * 0.01f);

        if (_roster == null) return rolledPercent;

        for (int i = 0; i < _state.party.Count; i++)
        {
            var m = _state.party[i];
            if (m == null || m.def == null || m.IsDead) continue;

            float add = healPct * Mathf.Max(1f, m.maxHp);
            m.hp = Mathf.Min(m.maxHp, m.hp + add);
            _state.party[i] = m;
        }

        return rolledPercent;
    }

    public void OnRestHeal()
    {
        OnRestHealRandomPercent(25, 25);
    }

    /// <summary>
    /// Rest Option B: random level gain to a random living party member.
    /// "Fair" randomness: chooses uniformly among living members only.
    /// HP% is preserved when max HP changes due to level.
    /// </summary>
    public RestLevelUpResult OnRestRandomLevelUpRandom(int minLevelsInclusive, int maxLevelsInclusive)
    {
        var result = new RestLevelUpResult
        {
            monsterName = string.Empty,
            levelsGained = 0
        };

        if (_roster == null || _roster.IsPartyEmpty) return result;

        // Gather living indices
        _tmpLivingIndices.Clear();
        for (int i = 0; i < _state.party.Count; i++)
        {
            var m = _state.party[i];
            if (m == null || m.def == null || m.IsDead) continue;
            _tmpLivingIndices.Add(i);
        }

        if (_tmpLivingIndices.Count == 0) return result;

        int pick = (_rng != null) ? _rng.NextInt(0, _tmpLivingIndices.Count) : UnityEngine.Random.Range(0, _tmpLivingIndices.Count);
        int targetIndex = _tmpLivingIndices[pick];

        var target = _state.party[targetIndex];
        if (target == null || target.def == null) return result;

        int levelGain = Mathf.Max(1, RollIntInclusive(minLevelsInclusive, maxLevelsInclusive));
        int oldLevel = target.level;
        int cap = target.def.maxLevel > 0 ? target.def.maxLevel : 99;
        target.level = Mathf.Min(cap, Mathf.Max(1, target.level + levelGain));
        target.RecomputeMaxHpPreservePct();

        _state.party[targetIndex] = target;
        result.monsterName = target.def != null ? target.def.name : string.Empty;
        result.levelsGained = Mathf.Max(0, target.level - oldLevel);
        return result;
    }

    public string OnRestRandomLevelUp()
    {
        return OnRestRandomLevelUpRandom(1, 1).monsterName;
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
        if (executiveTrialRiftUI)
            yield return executiveTrialRiftUI.Co_ShowBattleOnlyThenReady();
        else
            yield return null;

        _beginNextBattleCo = null;
        BeginNextBattle();
    }

    private void BeginNextBattle()
    {
        if (!_state.runActive) return;
        if (_roster == null || _roster.IsPartyEmpty) { ShowGameOver(forfeit: false); return; }

        _state.battleInProgress = true; // FIXED: checkpoint written before battle — detectable on crash recovery
        ExecutiveTrialMetaSave.Save(LoadMetaData()); // persist state immediately

        if (!ExecutiveTrialRuntime.IsActive)
        {
            Debug.LogError("[ExecutiveTrialManager] Iron runtime inactive before battle start. Ending run gracefully.");
            ShowGameOver(forfeit: false);
            return;
        }

        var wildPreview = GetWildForNextBattle();
        if (wildPreview == null || wildPreview.def == null)
        {
            Debug.LogError("[ExecutiveTrialManager] Rift generation failed (wild null). Ending run gracefully.");
            ShowGameOver(forfeit: false);
            return;
        }

        ResolveBattleRefsIfNeeded();

        if (!executiveTrialBattleUI)
            executiveTrialBattleUI = FindFirstObjectByType<ExecutiveTrialBattleUIRoot>(FindObjectsInactive.Include);

        if (!battle || !bridge)
        {
            Debug.LogError("[ExecutiveTrialManager] Missing BattleManager or ExecutiveTrailBattleBridge reference.");
            return;
        }

        if (!executiveTrialBattleUI)
        {
            Debug.LogError("[ExecutiveTrialManager] Missing ExecutiveTrialBattleUIRoot. Executive Trial battle textbox/UI bindings will not work.");
            return;
        }

        UIManager.I?.Show(PanelId.ExecutiveTrialRift);

        executiveTrialRiftUI?.ShowBattleOnly(immediate: true);

    #if UNITY_EDITOR
        DevLog.Log($"[ExecutiveTrialManager] BeginNextBattle: battle={(battle ? battle.name : "NULL")} bridge={(bridge ? bridge.name : "NULL")} executiveTrialBattleUI={(executiveTrialBattleUI ? executiveTrialBattleUI.name : "NULL")}");
    #endif

        executiveTrialBattleUI.ApplyTo(battle);

        // DEBUG: prove what we are about to inject.
#if UNITY_EDITOR
        try
        {
            var p = GetPartyForNextBattle();
            var cachedWild = _state.lastRolledWild;
            DevLog.Log($"[ExecutiveTrialManager] BeginNextBattle inject preview: party={p.Count} cachedWild={(cachedWild != null && cachedWild.def != null ? cachedWild.def.name : "NULL")}");
        }
        catch (Exception ex)
        {
            DevLog.Log($"[ExecutiveTrialManager] Inject preview failed: {ex.Message}");
        }
#endif

        // Ensure Executive Trial battles use the user's preferred battle speed
        if (battle != null && SaveManager.Data != null && SaveManager.Data.settings != null)
        {
            float preferredSpeed = Mathf.Clamp(SaveManager.Data.settings.battleSpeed, 0.25f, 5f);
            battle.SetBattleSpeed(preferredSpeed);
        }

        bridge.SetWins(_state.wins);
        bridge.BeginIronBattle(battle, null);
    }

    private ExecutiveTrialMetaData LoadMetaData()
    {
        var meta = ExecutiveTrialMetaSave.Load() ?? new ExecutiveTrialMetaData();
        meta.battleInProgress = _state.battleInProgress;
        return meta;
    }

    // Quit/Forfeit + GameOver UI


    // Rules popup runtime flags (session-only)
    private bool _quitPromptActive;
    private bool _suppressRulesThisRun;

    public bool IsQuitPromptActive => _quitPromptActive;
    public bool SuppressRulesThisRun => _suppressRulesThisRun;

    /// <summary>
    /// Called by ExecutiveTrialRulesPopupUI when the user toggles "Don't show again".
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
        executiveTrialRiftUI?.HideRules(immediate: true);
    }

    /// <summary>
    /// Close informational rules popup.
    /// </summary>
    public void CloseRules()
    {
        _quitPromptActive = false;
        executiveTrialRiftUI?.HideRules(immediate: true);
    }

    public void RequestQuit()
    {
        if (!_state.runActive) return;
        _quitPromptActive = true;
        executiveTrialRiftUI?.ShowRules(immediate: true);
    }

    public void ConfirmQuitForfeit()
    {
        if (!_state.runActive) return;
        _quitPromptActive = false;
        executiveTrialRiftUI?.HideRules(immediate: true);
        Forfeit("Quit confirmed");
    }

    public void CancelQuit()
    {
        _quitPromptActive = false;
        executiveTrialRiftUI?.HideRules(immediate: true);
    }

    private void Forfeit(string reason)
    {
        if (!_state.runActive) return;
        Debug.LogWarning($"[ExecutiveTrialManager] FORFEIT: {reason}");
        ShowGameOver(forfeit: true);
    }

    private void ShowGameOver(bool forfeit, string defeatCauseOverride = null)
    {
        // Ensure no pending background forfeit coroutine survives into game-over.
        CancelBackgroundForfeit("Game over");

        FinalizeRunStats(forfeit);

        if (forfeit)
        {
            _state.battleLog.Add(new IronBattleLogEntry
            {
                isForfeit        = true,
                winsBeforeBattle = _state.wins,
            });
        }

        _state.runActive = false;

        GameEvents.ExecutiveTrialCompleted?.Invoke(_state.wins, forfeit, _state.runSummary.totalDeaths);

        UIManager.I?.Show(PanelId.ExecutiveTrialRift);

        executiveTrialRiftUI?.ShowGameOver(immediate: true);
        gameOverPanel?.Bind(
            _state.mode,
            _state.wins,
            _state.runSummary,
            forfeited: forfeit,
            defeatCauseOverride: defeatCauseOverride,
            battleLog: _state.battleLog
        );

        // EXECUTIVE TRIAL GUARD: Explicitly disable regular Rift panel BEFORE exiting Executive Trial runtime.
        // This prevents race condition where RiftPanelUI.OnEnable() check fails after Exit() is called.
        var riftPanelUI = FindFirstObjectByType<RiftPanelUI>(FindObjectsInactive.Include);
        if (riftPanelUI && riftPanelUI.gameObject.activeSelf)
            riftPanelUI.gameObject.SetActive(false);

        ExecutiveTrialRuntime.Exit();

        executiveTrialBattleUI?.RestoreBattleManagerDefaults();
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

        ExecutiveTrialStats.RecordRunEnd(_state.mode, _state.wins, forfeited, _state.runSummary);
    }

    public void ReturnToMenuFromGameOver()
    {
        // IRON GUARD: Force disable regular Rift panel to prevent it from auto-starting battles
        var riftPanelUI = FindFirstObjectByType<RiftPanelUI>(FindObjectsInactive.Include);
        if (riftPanelUI && riftPanelUI.gameObject.activeSelf)
            riftPanelUI.gameObject.SetActive(false);

        UIManager.I?.Hide(PanelId.ExecutiveTrialRift);
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
        UIManager.I?.Hide(PanelId.Rift);
        UIManager.I?.Hide(PanelId.PostBattleSummary);
        UIManager.I?.Show(PanelId.ExecutiveTrialRift);
        UIManager.I?.Hide(PanelId.Home);

        executiveTrialRiftUI?.ShowStarter(immediate: true);
    }
}