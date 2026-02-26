using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class IronBattleBridge : MonoBehaviour, IBattleRosterProvider, IBattleContext
{
    public interface IIronBattleBridgeHost
    {
        /// <summary>Current wins/floor index for the run (used for wild combatant id).</summary>
        int Wins { get; }

        /// <summary>Return the party for the next battle (<=3). hp can be -1 to auto-fill from BattleCalc.</summary>
        IReadOnlyList<BattleCombatant> GetPartyForNextBattle();

        /// <summary>Return the wild combatant for the next battle. Must not be null.</summary>
        BattleCombatant GetWildForNextBattle();

        /// <summary>Carry-over status to apply before battle start (player-only).</summary>
        IronFieldStatusSnapshot GetCarryStatus();

        /// <summary>Carry-over shields to apply before battle start (player-only). Length <= 3.</summary>
        float[] GetCarryShields();

        /// <summary>Receive end-of-battle outcome and update runtime state (HP/status/shields).</summary>
        void OnIronBattleResolved(IronBattleOutcome outcome);
    }

    [Header("Host (Optional)")]
    [Tooltip("If set, this component must implement IronBattleBridge.IIronBattleBridgeHost.")]
    [SerializeField] private MonoBehaviour hostBehaviour;

    [Header("Debug Fallback (Optional)")]
    [Tooltip("If no host is provided, use these combatants for manual testing.")]
    [SerializeField] private bool useDebugFallbackIfNoHost = false;

    [SerializeField] private List<BattleCombatant> debugParty = new List<BattleCombatant>();
    [SerializeField] private BattleCombatant debugWild;

    [Header("Runtime")]
    [SerializeField] private int winsForId = 0;

    private IIronBattleBridgeHost Host => ResolveHost();

    public BattleRules Rules => BattleRules.Iron;

    private void Awake()
    {
        ResolveHost();
    }

    private IIronBattleBridgeHost ResolveHost()
    {
        if (hostBehaviour is IIronBattleBridgeHost typedHost)
            return typedHost;

        if (hostBehaviour != null)
            Debug.LogWarning($"[IronBattleBridge] Assigned host '{hostBehaviour.name}' does not implement IIronBattleBridgeHost.");

        var localBehaviours = GetComponents<MonoBehaviour>();
        for (int i = 0; i < localBehaviours.Length; i++)
        {
            if (localBehaviours[i] is IIronBattleBridgeHost localHost)
            {
                hostBehaviour = localBehaviours[i];
                return localHost;
            }
        }

        var managerHost = FindFirstObjectByType<IronCareerManager>(FindObjectsInactive.Include);
        if (managerHost != null)
        {
            hostBehaviour = managerHost;
            return managerHost;
        }

        return null;
    }

    /// <summary>Update wins used for the wild combatant id (IRON::W::<RunGuid>::F&lt;wins&gt;).</summary>
    public void SetWins(int wins)
    {
        winsForId = Mathf.Max(0, wins);
    }

    /// <summary>
    /// Convenience API for Phase 1 verification: starts a battle using injected roster + applies carry.
    /// </summary>
    public void BeginIronBattle(BattleManager battle, Action<BattleResult> onEnded)
    {
        if (battle == null) return;

        Debug.Log($"[IronBattleBridge] BeginIronBattle: ironActive={IronCareerRuntime.IsActive} host={(hostBehaviour ? hostBehaviour.name : "NULL")} useDebugFallback={useDebugFallbackIfNoHost}");

        if (!IronCareerRuntime.IsActive)
        {
            Debug.LogError("[IronBattleBridge] BeginIronBattle called but IronCareerRuntime is not active. Forfeiting.");
            battle.ForceEndBattleEarly(false);
            return;
        }

        // Build and cache ids + title injection BEFORE Begin.
        var party = GetPlayerTeam();
        var wild = GetWild();

        Debug.Log($"[IronBattleBridge] Pre-Begin snapshot: partyCount={party.Count} wild={(wild != null && wild.def != null ? wild.def.name : "NULL")}");
        for (int i = 0; i < party.Count; i++)
        {
            var c = party[i];
            Debug.Log($"[IronBattleBridge] Party[{i}] def={(c != null && c.def != null ? c.def.name : "NULL")} lvl={(c != null ? c.level : -1)} hp={(c != null ? c.hp : -1f)} cid={(c != null ? c.combatantId : "NULL")}");
        }
        if (wild == null || wild.def == null)
        {
            Debug.LogError("[IronBattleBridge] Wild combatant is null/invalid. Forfeiting.");
            battle.ForceEndBattleEarly(false);
            return;
        }

        if (Host != null) winsForId = Mathf.Max(0, Host.Wins);

        // Start the battle using injected roster.
        battle.Begin(wild.def, Mathf.Max(1, wild.level), onEnded, this, this);

        Debug.Log("[IronBattleBridge] battle.Begin(...) invoked with injected roster provider + context.");

        // Apply carry after arrays are initialized.
        var carryStatus = (Host != null) ? Host.GetCarryStatus() : IronFieldStatusSnapshot.None;
        var carryShield = (Host != null) ? Host.GetCarryShields() : null;
        battle.ApplyIronCarryToPlayerField(carryStatus, carryShield);

        Debug.Log($"[IronBattleBridge] carryStatus snapshot = {carryStatus}");    
        
    }

    // ─────────────────────────────────────────────────────────────
    // IBattleRosterProvider
    // ─────────────────────────────────────────────────────────────

    public IReadOnlyList<BattleCombatant> GetPlayerTeam()
    {
        var host = Host;
        IReadOnlyList<BattleCombatant> src = null;
        if (host != null)
            src = host.GetPartyForNextBattle();
        else if (useDebugFallbackIfNoHost)
            src = debugParty;

        Debug.Log($"[IronBattleBridge] GetPlayerTeam: host={(host != null ? host.GetType().Name : "NULL")} srcCount={(src != null ? src.Count : -1)}");

        if (src == null) return Array.Empty<BattleCombatant>();

        // Ensure stable combatant IDs and Title injection for each member.
        var list = new List<BattleCombatant>(Mathf.Min(3, src.Count));
        for (int i = 0; i < src.Count && i < 3; i++)
        {
            var c = src[i];
            if (c == null || c.def == null) continue;

            c.combatantId = BuildPartyCombatantId(i);
            InjectTitlesFor(c);
            list.Add(c);
        }

        Debug.Log($"[IronBattleBridge] GetPlayerTeam resultCount={list.Count}");

        return list;
    }

    public BattleCombatant GetWild()
    {
        var host = Host;
        BattleCombatant c = null;
        if (host != null)
            c = host.GetWildForNextBattle();
        else if (useDebugFallbackIfNoHost)
            c = debugWild;

        Debug.Log($"[IronBattleBridge] GetWild: host={(host != null ? host.GetType().Name : "NULL")} def={(c != null && c.def != null ? c.def.name : "NULL")}");

        if (c == null || c.def == null) return null;

        c.combatantId = BuildWildCombatantId();
        InjectTitlesFor(c);
        return c;
    }

    // ─────────────────────────────────────────────────────────────
    // IBattleContext
    // ─────────────────────────────────────────────────────────────

    public void OnBattleResolved(IronBattleOutcome outcome)
    {
        Debug.Log($"[IronBattleBridge] OnBattleResolved: victory={outcome.victory} escaped={outcome.escaped} turns={outcome.turnsSurvived}");
        // Forward to run host (IronCareerManager) and allow it to update runtime state.
        var host = Host;
        if (host != null)
            host.OnIronBattleResolved(outcome);
    }

    // ─────────────────────────────────────────────────────────────
    // IDs + Titles injection
    // ─────────────────────────────────────────────────────────────

    private string BuildPartyCombatantId(int slot)
    {
        string g = string.IsNullOrEmpty(IronCareerRuntime.RunGuid) ? "noguid" : IronCareerRuntime.RunGuid;
        return $"IRON::P::{g}::S{slot}";
    }

    private string BuildWildCombatantId()
    {
        string g = string.IsNullOrEmpty(IronCareerRuntime.RunGuid) ? "noguid" : IronCareerRuntime.RunGuid;
        int w = (Host != null) ? Mathf.Max(0, Host.Wins) : winsForId;

        // IMPORTANT: must start with "WILD::" so BattleManager will not replace it.
        return $"WILD::IRON::{g}::F{w}";
    }

    private void InjectTitlesFor(BattleCombatant c)
    {
        if (c == null || c.def == null) return;
        if (string.IsNullOrEmpty(c.combatantId)) return;

        // Register synthetic context so TitleManager can evaluate title rules against the correct monster.
        TitlesAdapter.RegisterBattleContext(c.combatantId, c.def, Mathf.Max(1, c.level));

        // Locked titles (Iron rules) injected per-instance.
        if (c.lockedTitle != null)
            TitlesAdapter.SetLocalTitles(c.combatantId, new TitleSO[] { c.lockedTitle });
    }
}
