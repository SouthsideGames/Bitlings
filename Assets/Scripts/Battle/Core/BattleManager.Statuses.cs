using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────
// BattleManager.Statuses
// Status application, ticking, and status-driven combat effects.
// ─────────────────────────────────────────────────────────────

public partial class BattleManager
{
    [Header("Statuses")]
    [SerializeField] private bool playerStatusesAreFieldWide = true;

    private int _lastPlayerStatusTickTurnIndex = int.MinValue;
    private int _lastWildStatusTickTurnIndex = int.MinValue;

    private bool TryProcessTurnStartStatus_PlayerActive_OncePerRound(out StatusType skippedBy)
    {
        skippedBy = StatusType.None;
        if (!inBattle) return false;
        if (_lastPlayerStatusTickTurnIndex == _turnIndex) return false;
        _lastPlayerStatusTickTurnIndex = _turnIndex;
        return TryProcessTurnStartStatus_PlayerActive(out skippedBy);
    }

    private bool TryProcessTurnStartStatus_Wild_OncePerRound(out StatusType skippedBy)
    {
        skippedBy = StatusType.None;
        if (!inBattle) return false;
        if (_lastWildStatusTickTurnIndex == _turnIndex) return false;
        _lastWildStatusTickTurnIndex = _turnIndex;
        return TryProcessTurnStartStatus_Wild(out skippedBy);
    }

    private bool PlayerTeamHasAnyActiveStatus()
    {
        if (teamStatus == null) return false;
        for (int i = 0; i < teamStatus.Length; i++)
        {
            if (teamDefs != null && i < teamDefs.Length && teamDefs[i] == null) continue;
            if (teamHP != null && i < teamHP.Length && teamHP[i] <= 0.01f) continue;
            if (teamStatus[i] != StatusType.None) return true;
        }
        return false;
    }

    private void SyncPlayerFieldStatusFromSlot(int sourceSlot)
    {
        if (!playerStatusesAreFieldWide) return;
        if (teamStatus == null || teamStatusTurns == null || teamStatusMagnitude == null || teamStatusPersistent == null) return;
        if (sourceSlot < 0 || sourceSlot >= teamStatus.Length) return;

        var st = teamStatus[sourceSlot];
        int turns = (sourceSlot < teamStatusTurns.Length) ? teamStatusTurns[sourceSlot] : 0;
        float mag = (sourceSlot < teamStatusMagnitude.Length) ? teamStatusMagnitude[sourceSlot] : 0f;
        bool persistent = (sourceSlot < teamStatusPersistent.Length) && teamStatusPersistent[sourceSlot];

        for (int i = 0; i < teamStatus.Length; i++)
        {
            if (teamDefs != null && i < teamDefs.Length && teamDefs[i] == null) continue;

            teamStatus[i] = st;
            if (i < teamStatusTurns.Length) teamStatusTurns[i] = turns;
            if (i < teamStatusMagnitude.Length) teamStatusMagnitude[i] = mag;
            if (i < teamStatusPersistent.Length) teamStatusPersistent[i] = persistent;
        }
    }
    private float[] _shieldedGrantTeam;
    private float _shieldedGrantWild;

    private bool[] _ironShieldCarrySlots;

    private void EnsureShieldGrantPools()
    {
        if (teamStatus != null)
        {
            if (_shieldedGrantTeam == null || _shieldedGrantTeam.Length != teamStatus.Length)
                _shieldedGrantTeam = new float[teamStatus.Length];

            if (_ironShieldCarrySlots == null || _ironShieldCarrySlots.Length != teamStatus.Length)
                _ironShieldCarrySlots = new bool[teamStatus.Length];
        }
        else
        {
            _shieldedGrantTeam = null;
            _ironShieldCarrySlots = null;
        }
    }

    private PlayerAction[] _foresightLastPlayerAction;
    private bool[] _foresightStunPendingTeam;

    private EnemyAction _foresightLastWildAction = EnemyAction.None;
    private bool _foresightStunPendingWild = false;

    private void EnsureForesightSidecars()
    {
        if (teamStatus != null)
        {
            if (_foresightLastPlayerAction == null || _foresightLastPlayerAction.Length != teamStatus.Length)
                _foresightLastPlayerAction = new PlayerAction[teamStatus.Length];

            if (_foresightStunPendingTeam == null || _foresightStunPendingTeam.Length != teamStatus.Length)
                _foresightStunPendingTeam = new bool[teamStatus.Length];
        }
        else
        {
            _foresightLastPlayerAction = null;
            _foresightStunPendingTeam = null;
        }
    }

    private void NotifyPlayerActionResolved_ForForesight(int slot, PlayerAction action)
    {
        EnsureForesightSidecars();

    if (!inBattle) return;
    if (teamStatus == null || slot < 0 || slot >= teamStatus.Length) return;
    if (teamStatus[slot] != StatusType.Foresight) return;

    if (action == PlayerAction.None) return;

    var last = (_foresightLastPlayerAction != null && slot < _foresightLastPlayerAction.Length)
        ? _foresightLastPlayerAction[slot]
        : PlayerAction.None;

    if (last == action)
    {
        if (_foresightStunPendingTeam != null && slot < _foresightStunPendingTeam.Length)
            _foresightStunPendingTeam[slot] = true;

        if (BattleLogger.Enabled)
            BattleLogger.Log($"[Status] Foresight backlash: {GetName(slot)} repeated {action} and will be stunned next turn.", LogScope.Battle);
    }

    if (_foresightLastPlayerAction != null && slot < _foresightLastPlayerAction.Length)
        _foresightLastPlayerAction[slot] = action;
}

    private void NotifyWildActionResolved_ForForesight(EnemyAction action)
{
    if (!inBattle) return;
    if (wildStatus != StatusType.Foresight) return;

    if (action == EnemyAction.None) return;

    if (_foresightLastWildAction == action)
    {
        _foresightStunPendingWild = true;

        if (BattleLogger.Enabled)
            BattleLogger.Log($"[Status] Foresight backlash: Wild repeated {action} and will be stunned next turn.", LogScope.Battle);
    }

    _foresightLastWildAction = action;
}

private bool TryConsumeForesightStun_Player(int slot)
{
    EnsureForesightSidecars();

    if (!inBattle) return false;
    if (_foresightStunPendingTeam == null || slot < 0 || slot >= _foresightStunPendingTeam.Length) return false;
    if (!_foresightStunPendingTeam[slot]) return false;

    _foresightStunPendingTeam[slot] = false;
    return true;
}

private bool TryConsumeForesightStun_Wild()
{
    if (!inBattle) return false;
    if (!_foresightStunPendingWild) return false;

    _foresightStunPendingWild = false;
    return true;
}

private float GetActivePlayerTailwindBonusPct()
{
    if (!inBattle) return 0f;
    if (teamStatus == null || activeIndex < 0 || activeIndex >= teamStatus.Length) return 0f;
    if (teamStatus[activeIndex] != StatusType.Tailwind) return 0f;

    float mag = (teamStatusMagnitude != null && activeIndex < teamStatusMagnitude.Length) ? teamStatusMagnitude[activeIndex] : 0f;
    return (mag > 0f) ? mag : 0.25f;
}

private float GetWildTailwindBonusPct()
{
    if (!inBattle) return 0f;
    if (wildStatus != StatusType.Tailwind) return 0f;

    float mag = wildStatusMagnitude;
    return (mag > 0f) ? mag : 0.25f;
}

private float GetActivePlayerPhantasmalSelfDmgPct()
{
    if (!inBattle) return 0f;
    if (teamStatus == null || activeIndex < 0 || activeIndex >= teamStatus.Length) return 0f;
    if (teamStatus[activeIndex] != StatusType.Phantasmal) return 0f;

    float mag = (teamStatusMagnitude != null && activeIndex < teamStatusMagnitude.Length) ? teamStatusMagnitude[activeIndex] : 0f;
    return (mag > 0f) ? mag : 0.05f;
}

private float GetWildPhantasmalSelfDmgPct()
{
    if (!inBattle) return 0f;
    if (wildStatus != StatusType.Phantasmal) return 0f;

    float mag = wildStatusMagnitude;
    return (mag > 0f) ? mag : 0.05f;
}
// ─────────────────────────────────────────────────────────────
    // Synergy → Status (battle start only)
    // Phase 3: Apply-only + UI + logging. Phase 4 will add ticking.
    // ─────────────────────────────────────────────────────────────
    private void ApplyBattleStartSynergies()
    {
        // Feature gate: player synergies unlock at Promotion Rank 10.
        // Wild synergies: Normal none, Hard T1, Insane T2 (Difficulty unlock at Rank 15).
        if (statusLibrary == null || synergyLibrary == null)
        {
            if (debugSynergyLogs)
                Debug.LogWarning($"[Battle][Synergy] Missing libraries on BattleManager '{name}'. statusLibrary={(statusLibrary ? statusLibrary.name : "NULL")}, synergyLibrary={(synergyLibrary ? synergyLibrary.name : "NULL")}. No battle-start statuses will apply.");
            return;
        }

        if (teamCount <= 0 || teamDefs == null) return;
        if (wildDef == null) return;

        // Clear any prior status UI (guard/charge handled elsewhere).
        if (feedback != null)
        {
            feedback.ClearPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Player);
            feedback.ClearPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Wild);
        }

        // Build team type list (only slots that can actually act).
        var teamTypes = new MonsterType[teamCount];
        int teamTypesCount = 0;
        for (int i = 0; i < teamCount; i++)
        {
            if (teamDefs[i] == null) continue;
            if (teamHP != null && i < teamHP.Length && teamHP[i] <= 0.01f) continue;
            teamTypes[teamTypesCount++] = teamDefs[i].type;
        }

        // Player synergies
        bool playerUnlocked = SaveManager.Data != null && SaveManager.Data.HasSynergyUnlocked;
        if (debugForceSynergyApply) playerUnlocked = true;

        if (playerUnlocked && teamTypesCount > 0)
        {
            var tmp = new List<SynergyResolver.ApplyCommand>(4);

            // Trim array if some slots were KO.
            if (teamTypesCount != teamTypes.Length)
            {
                var trimmed = new MonsterType[teamTypesCount];
                Array.Copy(teamTypes, trimmed, teamTypesCount);
                teamTypes = trimmed;
            }

            if (!debugForceSynergyApply)
            {
                SynergyResolver.ResolvePlayer(teamTypes, maxSynergies: 2, synergyLibrary, statusLibrary, tmp);
            }
            else
            {
                // Force at least one command so UI + logging can be verified even with a mixed team.
                // Uses the active monster's type as the synergy source.
                var srcType = (activeIndex >= 0 && activeIndex < teamCount && teamDefs != null && teamDefs[activeIndex] != null)
                    ? teamDefs[activeIndex].type
                    : (teamDefs != null && teamDefs.Length > 0 && teamDefs[0] != null ? teamDefs[0].type : wildDef.type);

                if (SynergyResolver.ResolveWild(srcType, debugForcePlayerSynergyTier, synergyLibrary, statusLibrary, out var forced))
                    tmp.Add(forced);
                else if (debugSynergyLogs)
                    Debug.LogWarning($"[Battle][Synergy] debugForcePlayerSynergyTier enabled but no synergy mapping found for {srcType} {debugForcePlayerSynergyTier}.");
            }

            if (debugSynergyLogs)
                Debug.Log($"[Battle][Synergy] Player resolved {tmp.Count} command(s). Unlocked={playerUnlocked} Forced={debugForceSynergyApply} TeamTypesCount={teamTypesCount}.");

            for (int i = 0; i < tmp.Count; i++)
            {
                ApplySynergyCommand(sourceSide: BattleSide.Player, tmp[i]);
            }
        }
        else if (debugSynergyLogs)
        {
            Debug.Log($"[Battle][Synergy] Player synergies not applied. Unlocked={playerUnlocked} TeamTypesCount={teamTypesCount} PromotionRank={(SaveManager.Data != null ? SaveManager.Data.promotionRank : -1)}");
        }

        // Wild synergies (driven by difficulty)
        SynergyTier wildTier;
        if (debugForceWildSynergyTier)
        {
            wildTier = debugWildSynergyTier;
            if (SynergyResolver.ResolveWild(wildDef.type, wildTier, synergyLibrary, statusLibrary, out var forcedWild))
            {
                if (debugSynergyLogs)
                    Debug.Log($"[Battle][Synergy] Wild forced synergy {wildDef.type} {wildTier}.");
                ApplySynergyCommand(sourceSide: BattleSide.Wild, forcedWild);
            }
            else if (debugSynergyLogs)
                Debug.LogWarning($"[Battle][Synergy] debugForceWildSynergyTier enabled but no synergy mapping found for {wildDef.type} {wildTier}.");
        }
        else if (TryGetWildSynergyTier(out wildTier))
        {
            if (SynergyResolver.ResolveWild(wildDef.type, wildTier, synergyLibrary, statusLibrary, out var cmd))
                ApplySynergyCommand(sourceSide: BattleSide.Wild, cmd);
            else if (debugSynergyLogs)
                Debug.LogWarning($"[Battle][Synergy] Wild tier {wildTier} but no mapping found for {wildDef.type}. Check SynergyLibrarySO.");
        }
        else if (debugSynergyLogs)
        {
            int mode = (SaveManager.Data != null && SaveManager.Data.settings != null) ? SaveManager.Data.settings.difficultyMode : 0;
            Debug.Log($"[Battle][Synergy] Wild synergies not applied. DifficultyMode={mode} HasDifficultyUnlocked={(SaveManager.Data != null && SaveManager.Data.HasDifficultyUnlocked)}");
        }

        // After applying, push the active player + wild status to the UI (Phase 2 UI).
        RefreshPrimaryStatusUI();
    }

    private bool TryGetWildSynergyTier(out SynergyTier tier)
    {
        tier = default;
        if (SaveManager.Data == null) return false;

        int mode = 0;
        if (SaveManager.Data.HasDifficultyUnlocked && SaveManager.Data.settings != null)
            mode = SaveManager.Data.settings.difficultyMode;

        // 0 Normal => none
        if (mode == 1) { tier = SynergyTier.Tier1; return true; }
        if (mode == 2) { tier = SynergyTier.Tier2; return true; }
        return false;
    }

    private void ApplySynergyCommand(BattleSide sourceSide, SynergyResolver.ApplyCommand cmd)
    {
        EnsureShieldGrantPools();

        if (cmd.status == StatusType.None) return;

        // New: Self scope always targets the source unit.
        // This keeps mappings intuitive for self-buffs (e.g., Reinforce) and prevents
        // "player gets it when wild uses it" confusion.
        if (cmd.scope == SynergyTargetScope.Self)
        {
            if (sourceSide == BattleSide.Player)
                TryApplyStatusToActivePlayer(cmd.status, cmd.turns, cmd.persistent, cmd.magnitude, sourceSide, cmd);
            else
                TryApplyStatusToWild(cmd.status, cmd.turns, cmd.persistent, cmd.magnitude, sourceSide, cmd);
            return;
        }

        // Resolve target based on scope and source side.
        // Player synergies generally target Wild (EnemySingle) or Player team (AllyTeam).
        // Wild synergies generally target Player (EnemySingle) or Wild self (AllySingle), depending on your library.
        if (cmd.scope == SynergyTargetScope.EnemySingle)
        {
            var targetSide = (sourceSide == BattleSide.Player) ? BattleSide.Wild : BattleSide.Player;
            if (targetSide == BattleSide.Wild)
                TryApplyStatusToWild(cmd.status, cmd.turns, cmd.persistent, cmd.magnitude, sourceSide, cmd);
            else
                TryApplyStatusToActivePlayer(cmd.status, cmd.turns, cmd.persistent, cmd.magnitude, sourceSide, cmd);
            return;
        }

        if (cmd.scope == SynergyTargetScope.AllySingle)
        {
            if (sourceSide == BattleSide.Player)
                TryApplyStatusToActivePlayer(cmd.status, cmd.turns, cmd.persistent, cmd.magnitude, sourceSide, cmd);
            else
                TryApplyStatusToWild(cmd.status, cmd.turns, cmd.persistent, cmd.magnitude, sourceSide, cmd);
            return;
        }

        if (cmd.scope == SynergyTargetScope.AllyTeam)
        {
            if (sourceSide == BattleSide.Player)
            {
                for (int i = 0; i < teamCount; i++)
                {
                    if (teamDefs[i] == null) continue;
                    if (teamHP != null && i < teamHP.Length && teamHP[i] <= 0.01f) continue;
                    TryApplyStatusToTeamSlot(i, cmd.status, cmd.turns, cmd.persistent, cmd.magnitude, sourceSide, cmd);
                }
            }
            else
            {
                // Wild has no team. Treat as ally-single.
                TryApplyStatusToWild(cmd.status, cmd.turns, cmd.persistent, cmd.magnitude, sourceSide, cmd);
            }
        }
    }

    private void TryApplyStatusToActivePlayer(StatusType type, int turns, bool persistent, float magnitude, BattleSide sourceSide, SynergyResolver.ApplyCommand cmd)
    {
        if (activeIndex < 0 || activeIndex >= teamCount) return;
        TryApplyStatusToTeamSlot(activeIndex, type, turns, persistent, magnitude, sourceSide, cmd);
    }

    private void TryApplyStatusToTeamSlot(int slot, StatusType type, int turns, bool persistent, float magnitude, BattleSide sourceSide, SynergyResolver.ApplyCommand cmd)
    {
        if (teamStatus == null || slot < 0 || slot >= teamStatus.Length) return;
        if (type == StatusType.None) return;

        // Phase 4 rule: Player side uses a single "field" status (one status total across the team).
        // No refresh, no overwrite.
        if (playerStatusesAreFieldWide && PlayerTeamHasAnyActiveStatus())
        {
            // Special case: Reinforce blocks new statuses.
            // Otherwise just state that the field already has a status.
            var existing = teamStatus != null && activeIndex >= 0 && activeIndex < teamStatus.Length ? teamStatus[activeIndex] : StatusType.None;
            if (existing == StatusType.Reinforce)
                BattleLogger.Log($"[Status] Apply blocked (Reinforce): Player field is Reinforced and immune to {type}.", LogScope.Battle);
            else
                BattleLogger.Log($"[Status] Apply blocked (field already has {existing}): Player cannot receive {type}.", LogScope.Battle);
            return;
        }

        // One status per unit. No overwrite.
        if (teamStatus[slot] != StatusType.None)
        {
            // Special case: Reinforce explicitly blocks new statuses (future-proof if overwrite rules change).
            if (teamStatus[slot] == StatusType.Reinforce)
                BattleLogger.Log($"[Status] Apply blocked (Reinforce): {GetName(slot)} is Reinforced and immune to {type}.", LogScope.Battle);
            else
                BattleLogger.Log($"[Status] Apply blocked (already has {teamStatus[slot]}): {GetName(slot)} cannot receive {type}.", LogScope.Battle);

            return;
        }

        teamStatus[slot] = type;
        teamStatusTurns[slot] = Mathf.Max(0, turns);
        teamStatusMagnitude[slot] = magnitude;
        teamStatusPersistent[slot] = persistent;

        // If we are treating player statuses as field-wide, mirror the status onto every slot.
        // This ensures swapping monsters does not change the status or its remaining turns.
        SyncPlayerFieldStatusFromSlot(slot);

        // Apply-once statuses
        if (type == StatusType.Shielded)
        {
            EnsureShieldGrantPools();

            if (playerStatusesAreFieldWide)
            {
                // Field-wide Shielded: grant shields to all living slots once.
                for (int i = 0; i < teamCount; i++)
                {
                    if (teamDefs == null || i < 0 || i >= teamCount || teamDefs[i] == null) continue;
                    if (teamHP != null && i < teamHP.Length && teamHP[i] <= 0.01f) continue;

                    float maxHp = GetFinalMaxHPForIndex(i);
                    int shieldAdd = Mathf.Max(1, Mathf.RoundToInt(maxHp * Mathf.Max(0f, magnitude)));
                    if (shieldHP != null && i >= 0 && i < shieldHP.Length)
                        shieldHP[i] = Mathf.Max(0f, shieldHP[i]) + shieldAdd;
                    if (_shieldedGrantTeam != null && i >= 0 && i < _shieldedGrantTeam.Length)
                        _shieldedGrantTeam[i] += shieldAdd;

                    BattleLogger.Log($"[Status] {GetName(i)} gains {shieldAdd} Shield from Shielded.", LogScope.Battle);
                }
                PushHPBars();
            }
            else
            {
                float maxHp = GetFinalMaxHPForIndex(slot);
                int shieldAdd = Mathf.Max(1, Mathf.RoundToInt(maxHp * Mathf.Max(0f, magnitude)));
                if (shieldHP != null && slot >= 0 && slot < shieldHP.Length)
                    shieldHP[slot] = Mathf.Max(0f, shieldHP[slot]) + shieldAdd;
                if (_shieldedGrantTeam != null && slot >= 0 && slot < _shieldedGrantTeam.Length)
                    _shieldedGrantTeam[slot] += shieldAdd;
                BattleLogger.Log($"[Status] {GetName(slot)} gains {shieldAdd} Shield from Shielded.", LogScope.Battle);
                PushHPBars();
            }
        }

        Emit(BattleEvent.StatusApplied(sourceSide, BattleSide.Player, type.ToString(), stacks: persistent ? 0 : teamStatusTurns[slot], seconds: magnitude));
        BattleLogger.Log($"[Status] {GetName(slot)} gains {type}{FormatStatusDetail(persistent, teamStatusTurns[slot], magnitude)} (Synergy: {cmd.sourceType} {cmd.tier}).", LogScope.Battle);

        RefreshPrimaryStatusUI();
        GameEvents.OnBattleStateChanged?.Invoke();
    }

    private void TryApplyStatusToWild(StatusType type, int turns, bool persistent, float magnitude, BattleSide sourceSide, SynergyResolver.ApplyCommand cmd)
    {
        if (type == StatusType.None) return;

        // One status per unit. No overwrite.
        if (wildStatus != StatusType.None)
        {
            // Special case: Reinforce explicitly blocks new statuses (future-proof if overwrite rules change).
            if (wildStatus == StatusType.Reinforce)
                BattleLogger.Log($"[Status] Apply blocked (Reinforce): Wild is Reinforced and immune to {type}.", LogScope.Battle);
            else
                BattleLogger.Log($"[Status] Apply blocked (already has {wildStatus}): Wild cannot receive {type}.", LogScope.Battle);

            return;
        }

        wildStatus = type;
        wildStatusTurns = Mathf.Max(0, turns);
        wildStatusMagnitude = magnitude;
        wildStatusPersistent = persistent;

        // Apply-once statuses
        if (type == StatusType.Shielded)
        {
            float maxHp = Mathf.Max(1f, wildMaxHP);
            int shieldAdd = Mathf.Max(1, Mathf.RoundToInt(maxHp * Mathf.Max(0f, magnitude)));
            wildShieldHP = Mathf.Max(0f, wildShieldHP) + shieldAdd;
            _shieldedGrantWild += shieldAdd;
            BattleLogger.Log($"[Status] Wild gains {shieldAdd} Shield from Shielded.", LogScope.Battle);
            PushHPBars();
        }

        Emit(BattleEvent.StatusApplied(sourceSide, BattleSide.Wild, type.ToString(), stacks: persistent ? 0 : wildStatusTurns, seconds: magnitude));
        BattleLogger.Log($"[Status] Wild gains {type}{FormatStatusDetail(persistent, wildStatusTurns, magnitude)} (Synergy: {cmd.sourceType} {cmd.tier}).", LogScope.Battle);

        RefreshPrimaryStatusUI();
        GameEvents.OnBattleStateChanged?.Invoke();
    }

    private void RefreshPrimaryStatusUI()
    {
        if (feedback == null) return;

        // Active player slot
        if (activeIndex >= 0 && teamStatus != null && activeIndex < teamStatus.Length)
        {
            var st = teamStatus[activeIndex];
            if (st != StatusType.None)
            {
                var icon = statusLibrary != null ? statusLibrary.GetIcon(st) : null;
                string ttTitle = statusLibrary != null ? statusLibrary.GetDisplayName(st) : st.ToString();
                string ttDesc  = statusLibrary != null ? statusLibrary.GetDescription(st) : string.Empty;
                int turns = (teamStatusPersistent != null && activeIndex < teamStatusPersistent.Length && teamStatusPersistent[activeIndex])
                    ? 0
                    : (teamStatusTurns != null && activeIndex < teamStatusTurns.Length ? Mathf.Max(0, teamStatusTurns[activeIndex]) : 0);
                bool persistent = (teamStatusPersistent != null && activeIndex < teamStatusPersistent.Length) && teamStatusPersistent[activeIndex];
                feedback.SetPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Player, icon, turns, persistent, ttTitle, ttDesc);
            }
            else
            {
                feedback.ClearPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Player);
            }
        }
        else
        {
            feedback.ClearPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Player);
        }

        // Wild
        if (wildStatus != StatusType.None)
        {
            var icon = statusLibrary != null ? statusLibrary.GetIcon(wildStatus) : null;
            int turns = wildStatusPersistent ? 0 : Mathf.Max(0, wildStatusTurns);
            string ttTitle = statusLibrary != null ? statusLibrary.GetDisplayName(wildStatus) : wildStatus.ToString();
            string ttDesc  = statusLibrary != null ? statusLibrary.GetDescription(wildStatus) : string.Empty;
            feedback.SetPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Wild, icon, turns, wildStatusPersistent, ttTitle, ttDesc);
        }
        else
        {
            feedback.ClearPrimaryStatus(BattleFeedbackManager.BattleFeedbackSide.Wild);
        }
    }


    private string FormatStatusDetail(bool persistent, int turns, float magnitude)
    {
        if (persistent) return " (persistent)";

        // Keep magnitude optional so logs don't look noisy if not configured yet.
        if (Mathf.Abs(magnitude) > 0.0001f)
            return $" ({turns} turns, {magnitude:0.##})";

        return $" ({turns} turns)";
    }


    // ─────────────────────────────────────────────────────────────
    // Phase 4: Turn-start status ticking + combat hooks
    // - Ticks at the start of the unit's turn (before the action resolves).
    // - Decrements duration AFTER the tick/skip.
    // - Clears status when turns reach 0 (non-persistent only).
    // ─────────────────────────────────────────────────────────────

    private bool TryProcessTurnStartStatus_PlayerActive() => TryProcessTurnStartStatus_PlayerActive(out _);

    // Overload: returns which status (if any) caused an action skip.
    // Keep the original signature intact for existing callers.
    private bool TryProcessTurnStartStatus_PlayerActive(out StatusType skippedBy)
    {
        skippedBy = StatusType.None;
        if (activeIndex < 0 || teamStatus == null || activeIndex >= teamStatus.Length) return false;
        return TryProcessTurnStartStatus_TeamSlot(activeIndex, out skippedBy);
    }

    private bool TryProcessTurnStartStatus_TeamSlot(int slot) => TryProcessTurnStartStatus_TeamSlot(slot, out _);

    // Overload: returns which status (if any) caused an action skip.
    // Keep the original signature intact for existing callers.
    private bool TryProcessTurnStartStatus_TeamSlot(int slot, out StatusType skippedBy)
    {
        skippedBy = StatusType.None;
        if (teamStatus == null || slot < 0 || slot >= teamStatus.Length) return false;

        var st = teamStatus[slot];
        if (st == StatusType.None) return false;

        // Persistent statuses do not tick down and do not have turn-start effects by default.
        if (teamStatusPersistent != null && slot < teamStatusPersistent.Length && teamStatusPersistent[slot])
            return false;

        int turns = (teamStatusTurns != null && slot < teamStatusTurns.Length) ? Mathf.Max(0, teamStatusTurns[slot]) : 0;
        float mag = (teamStatusMagnitude != null && slot < teamStatusMagnitude.Length) ? teamStatusMagnitude[slot] : 0f;

        if (turns <= 0)
        {
            ClearTeamStatus(slot, reason: "expired");
            return false;
        }

        bool skipAction = false;

        // Foresight: if a repeat was detected last turn, stun (skip) this turn.
        if (st == StatusType.Foresight && TryConsumeForesightStun_Player(slot))
        {
            skipAction = true;
            skippedBy = StatusType.Foresight;
            BattleLogger.Log($"[Status] {GetName(slot)} is stunned by Foresight backlash and skips its action.", LogScope.Battle);
        }

        // Apply turn-start effect
        switch (st)
        {
            case StatusType.Burn:
            case StatusType.Corrupt:
                {
                    // DOT: % of max HP, bypasses DEF (and shields by design).
                    float maxHp = GetFinalMaxHPForIndex(slot);
                    int dmg = Mathf.Max(1, Mathf.RoundToInt(maxHp * Mathf.Max(0f, mag)));
                    float pre = teamHP != null && slot < teamHP.Length ? teamHP[slot] : 0f;
                    if (teamHP != null && slot < teamHP.Length)
                        teamHP[slot] = Mathf.Max(0f, teamHP[slot] - dmg);
                    float post = teamHP != null && slot < teamHP.Length ? teamHP[slot] : 0f;

                    BattleLogger.Log($"[Status] {GetName(slot)} suffers {dmg} damage from {st}. ({Mathf.CeilToInt(pre)}→{Mathf.CeilToInt(post)})", LogScope.Battle);
                    if (slot == activeIndex) ClampAndPushActiveHP();
                    else PushHPBars();
                }
                break;

            case StatusType.Freeze:
                {
                    // Skip the unit's action this turn.
                    skipAction = true;
                    skippedBy = StatusType.Freeze;
                    BattleLogger.Log($"[Status] {GetName(slot)} is Frozen and skips its action.", LogScope.Battle);
                }
                break;

            case StatusType.Shock:
                {
                    // 25% chance the unit's action fails this turn.
                    // Caller is responsible for actually consuming the turn when skipAction is true.
                    if (UnityEngine.Random.value < 0.25f)
                    {
                        skipAction = true;
                        skippedBy = StatusType.Shock;
                    }
                }
                break;

            default:
                break;
        }

        // Decrement after effect
        turns = Mathf.Max(0, turns - 1);
        if (teamStatusTurns != null && slot < teamStatusTurns.Length) teamStatusTurns[slot] = turns;

        // Field-wide: keep all slots on the same remaining duration.
        if (playerStatusesAreFieldWide && turns > 0)
            SyncPlayerFieldStatusFromSlot(slot);

        if (turns <= 0)
            ClearTeamStatus(slot, reason: "ended");

        // Keep UI in sync (only primary is shown right now).
        RefreshPrimaryStatusUI();

        return skipAction;
    }

    private bool TryProcessTurnStartStatus_Wild() => TryProcessTurnStartStatus_Wild(out _);

    // Overload: returns which status (if any) caused an action skip.
    // Keep the original signature intact for existing callers.
    private bool TryProcessTurnStartStatus_Wild(out StatusType skippedBy)
    {
        skippedBy = StatusType.None;
        if (wildStatus == StatusType.None) return false;
        if (wildStatusPersistent) return false;

        int turns = Mathf.Max(0, wildStatusTurns);
        float mag = wildStatusMagnitude;

        if (turns <= 0)
        {
            ClearWildStatus(reason: "expired");
            return false;
        }

        bool skipAction = false;

        // Foresight: if a repeat was detected last turn, stun (skip) this turn.
        if (wildStatus == StatusType.Foresight && TryConsumeForesightStun_Wild())
        {
            skipAction = true;
            skippedBy = StatusType.Foresight;
            BattleLogger.Log("[Status] Wild is stunned by Foresight backlash and skips its action.", LogScope.Battle);
        }

        switch (wildStatus)
        {
            case StatusType.Burn:
            case StatusType.Corrupt:
                {
                    float maxHp = Mathf.Max(1f, wildMaxHP);
                    int dmg = Mathf.Max(1, Mathf.RoundToInt(maxHp * Mathf.Max(0f, mag)));
                    float pre = wildHP;
                    wildHP = Mathf.Max(0f, wildHP - dmg);
                    float post = wildHP;

                    BattleLogger.Log($"[Status] Wild suffers {dmg} damage from {wildStatus}. ({Mathf.CeilToInt(pre)}→{Mathf.CeilToInt(post)})", LogScope.Battle);
                    PushHPBars();
                }
                break;

            case StatusType.Freeze:
                skipAction = true;
                skippedBy = StatusType.Freeze;
                BattleLogger.Log("[Status] Wild is Frozen and skips its action.", LogScope.Battle);
                break;

            case StatusType.Shock:
                // 25% chance the wild's action fails this turn.
                if (UnityEngine.Random.value < 0.25f)
                {
                    skipAction = true;
                    skippedBy = StatusType.Shock;
                }
                break;

            default:
                break;
        }

        turns = Mathf.Max(0, turns - 1);
        wildStatusTurns = turns;

        if (turns <= 0)
            ClearWildStatus(reason: "ended");

        RefreshPrimaryStatusUI();
        return skipAction;
    }

    private void ClearTeamStatus(int slot, string reason)
    {
        if (teamStatus == null || slot < 0 || slot >= teamStatus.Length) return;
        var prev = teamStatus[slot];
        if (prev == StatusType.None) return;

        // Field-wide: clearing any slot clears the entire player field status.
        if (playerStatusesAreFieldWide)
        {
            // If Shielded is expiring/clearing, remove the remaining portion of shield granted by this status for ALL slots.
            if (prev == StatusType.Shielded)
            {
                EnsureShieldGrantPools();
                for (int i = 0; i < teamStatus.Length; i++)
                {
                    // Iron carry safety: never subtract imported shields on Shielded expiry.
                    if (IronCareerRuntime.IsActive && _ironShieldCarrySlots != null && i >= 0 && i < _ironShieldCarrySlots.Length && _ironShieldCarrySlots[i])
                    {
                        if (_shieldedGrantTeam != null && i >= 0 && i < _shieldedGrantTeam.Length)
                            _shieldedGrantTeam[i] = 0f;
                        _ironShieldCarrySlots[i] = false;
                        continue;
                    }

                    float remove = (_shieldedGrantTeam != null && i >= 0 && i < _shieldedGrantTeam.Length) ? _shieldedGrantTeam[i] : 0f;
                    if (remove > 0f)
                    {
                        float curShield = (shieldHP != null && i >= 0 && i < shieldHP.Length) ? Mathf.Max(0f, shieldHP[i]) : 0f;
                        float used = Mathf.Min(curShield, remove);

                        if (shieldHP != null && i >= 0 && i < shieldHP.Length)
                            shieldHP[i] = Mathf.Max(0f, curShield - used);

                        if (_shieldedGrantTeam != null && i >= 0 && i < _shieldedGrantTeam.Length)
                            _shieldedGrantTeam[i] = 0f;
                    }
                    else
                    {
                        if (_shieldedGrantTeam != null && i >= 0 && i < _shieldedGrantTeam.Length)
                            _shieldedGrantTeam[i] = 0f;
                    }
                }
                PushHPBars();
            }

            for (int i = 0; i < teamStatus.Length; i++)
            {
                teamStatus[i] = StatusType.None;
                if (teamStatusTurns != null && i < teamStatusTurns.Length) teamStatusTurns[i] = 0;
                if (teamStatusMagnitude != null && i < teamStatusMagnitude.Length) teamStatusMagnitude[i] = 0f;
                if (teamStatusPersistent != null && i < teamStatusPersistent.Length) teamStatusPersistent[i] = false;
            }

            BattleLogger.Log($"[Status] Player field {reason}: {prev}.", LogScope.Battle);
            RefreshPrimaryStatusUI();
            GameEvents.OnBattleStateChanged?.Invoke();
            return;
        }

        // If Shielded is expiring/clearing, remove only the remaining portion of shield granted by this status.
        if (prev == StatusType.Shielded)
        {
            EnsureShieldGrantPools();

            // Iron carry safety: never subtract imported shields on Shielded expiry.
            if (IronCareerRuntime.IsActive && _ironShieldCarrySlots != null && slot >= 0 && slot < _ironShieldCarrySlots.Length && _ironShieldCarrySlots[slot])
            {
                if (_shieldedGrantTeam != null && slot >= 0 && slot < _shieldedGrantTeam.Length)
                    _shieldedGrantTeam[slot] = 0f;
                _ironShieldCarrySlots[slot] = false;
            }

            float remove = (_shieldedGrantTeam != null && slot >= 0 && slot < _shieldedGrantTeam.Length) ? _shieldedGrantTeam[slot] : 0f;
            if (remove > 0f)
            {
                float curShield = (shieldHP != null && slot >= 0 && slot < shieldHP.Length) ? Mathf.Max(0f, shieldHP[slot]) : 0f;
                float used = Mathf.Min(curShield, remove);

                if (shieldHP != null && slot >= 0 && slot < shieldHP.Length)
                    shieldHP[slot] = Mathf.Max(0f, curShield - used);

                if (_shieldedGrantTeam != null && slot >= 0 && slot < _shieldedGrantTeam.Length)
                    _shieldedGrantTeam[slot] = 0f;

                PushHPBars();
            }
            else
            {
                if (_shieldedGrantTeam != null && slot >= 0 && slot < _shieldedGrantTeam.Length)
                    _shieldedGrantTeam[slot] = 0f;
            }
        }

        teamStatus[slot] = StatusType.None;
        if (teamStatusTurns != null && slot < teamStatusTurns.Length) teamStatusTurns[slot] = 0;
        if (teamStatusMagnitude != null && slot < teamStatusMagnitude.Length) teamStatusMagnitude[slot] = 0f;
        if (teamStatusPersistent != null && slot < teamStatusPersistent.Length) teamStatusPersistent[slot] = false;

        BattleLogger.Log($"[Status] {GetName(slot)} {reason}: {prev}.", LogScope.Battle);

        // Keep UI + action gating in sync (Freeze can re-enable input).
        RefreshPrimaryStatusUI();
        GameEvents.OnBattleStateChanged?.Invoke();
    }


    private void ClearWildStatus(string reason)
    {
        var prev = wildStatus;
        if (prev == StatusType.None) return;
        // If Shielded is expiring/clearing, remove any remaining shield granted by this status.
        if (prev == StatusType.Shielded)
        {
            float remove = _shieldedGrantWild;
            if (remove > 0f)
            {
                float used = Mathf.Min(wildShieldHP, remove);
                wildShieldHP = Mathf.Max(0f, wildShieldHP - used);
            }
            _shieldedGrantWild = 0f;

            PushHPBars();
        }


        wildStatus = StatusType.None;
        wildStatusTurns = 0;
        wildStatusMagnitude = 0f;
        wildStatusPersistent = false;

        BattleLogger.Log($"[Status] Wild {reason}: {prev}.", LogScope.Battle);

        RefreshPrimaryStatusUI();
        GameEvents.OnBattleStateChanged?.Invoke();
    }

    // private float GetOutgoingDamageMultiplier(BattleSide side)
    // {
    //     // Weakened: -X% outgoing damage
    //     if (side == BattleSide.Player)
    //     {
    //         if (activeIndex < 0 || teamStatus == null || activeIndex >= teamStatus.Length) return 1f;
    //         if (teamStatus[activeIndex] != StatusType.Weakened) return 1f;
    //         float mag = (teamStatusMagnitude != null && activeIndex < teamStatusMagnitude.Length) ? teamStatusMagnitude[activeIndex] : 0f;
    //         return Mathf.Clamp01(1f - Mathf.Max(0f, mag));
    //     }

    //     if (side == BattleSide.Wild)
    //     {
    //         if (wildStatus != StatusType.Weakened) return 1f;
    //         return Mathf.Clamp01(1f - Mathf.Max(0f, wildStatusMagnitude));
    //     }

    //     return 1f;
    // }

    private float GetLeechingPct(BattleSide side)
    {
        if (side == BattleSide.Player)
        {
            if (activeIndex < 0 || teamStatus == null || activeIndex >= teamStatus.Length) return 0f;
            if (teamStatus[activeIndex] != StatusType.Leeching) return 0f;
            float mag = (teamStatusMagnitude != null && activeIndex < teamStatusMagnitude.Length) ? teamStatusMagnitude[activeIndex] : 0f;
            return Mathf.Max(0f, mag);
        }

        if (side == BattleSide.Wild)
        {
            if (wildStatus != StatusType.Leeching) return 0f;
            return Mathf.Max(0f, wildStatusMagnitude);
        }

        return 0f;
    }

    private void ApplyLeechHeal(BattleSide healer, int damageDealt)
    {
        if (damageDealt <= 0) return;

        float pct = GetLeechingPct(healer);
        if (pct <= 0f) return;

        int heal = Mathf.Max(1, Mathf.RoundToInt(damageDealt * pct));

        if (healer == BattleSide.Player)
        {
            float before = GetActivePlayerCurHP();
            TryAddHPToActive(heal);
            float after = GetActivePlayerCurHP();
            int actual = Mathf.Max(0, Mathf.RoundToInt(after - before));
            if (actual > 0)
                BattleLogger.Log($"[Status] {GetName(activeIndex)} leeches {actual} HP.", LogScope.Battle);
        }
        else if (healer == BattleSide.Wild)
        {
            float before = wildHP;
            wildHP = Mathf.Clamp(wildHP + heal, 0f, Mathf.Max(1f, wildMaxHP));
            int actual = Mathf.Max(0, Mathf.RoundToInt(wildHP - before));
            if (actual > 0)
            {
                BattleLogger.Log($"[Status] Wild leeches {actual} HP.", LogScope.Battle);
                PushHPBars();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Status helpers (UI / decision gating)
    // ─────────────────────────────────────────────────────────────

    public bool IsActivePlayerFrozen()
    {
        // Use the BattleManager's canonical "inBattle" flag (no "battleActive" field exists).
        if (!inBattle) return false;
        if (teamStatus == null) return false;
        if (activeIndex < 0 || activeIndex >= teamStatus.Length) return false;

        if (teamStatus[activeIndex] != StatusType.Freeze) return false;

        // Persistent Freeze lasts the entire battle.
        if (teamStatusPersistent != null && activeIndex < teamStatusPersistent.Length && teamStatusPersistent[activeIndex])
            return true;

        return teamStatusTurns != null && activeIndex < teamStatusTurns.Length && teamStatusTurns[activeIndex] > 0;
    }

    public bool IsWildFrozen()
    {
        if (!inBattle) return false;
        if (wildStatus != StatusType.Freeze) return false;

        // Persistent Freeze lasts the entire battle.
        if (wildStatusPersistent) return true;

        return wildStatusTurns > 0;
    }

    public bool IsActivePlayerSundered()
    {
        if (!inBattle) return false;
        if (teamStatus == null) return false;
        if (activeIndex < 0 || activeIndex >= teamStatus.Length) return false;

        if (teamStatus[activeIndex] != StatusType.Sundering) return false;

        if (teamStatusPersistent != null && activeIndex < teamStatusPersistent.Length && teamStatusPersistent[activeIndex])
            return true;

        return teamStatusTurns != null && activeIndex < teamStatusTurns.Length && teamStatusTurns[activeIndex] > 0;
    }

    public bool IsWildSundered()
    {
        if (!inBattle) return false;
        if (wildStatus != StatusType.Sundering) return false;
        if (wildStatusPersistent) return true;
        return wildStatusTurns > 0;
    }

    public bool IsActivePlayerWyrmFury()
    {
        if (!inBattle) return false;
        if (teamStatus == null) return false;
        if (activeIndex < 0 || activeIndex >= teamStatus.Length) return false;

        if (teamStatus[activeIndex] != StatusType.WyrmFury) return false;

        if (teamStatusPersistent != null && activeIndex < teamStatusPersistent.Length && teamStatusPersistent[activeIndex])
            return true;

        return teamStatusTurns != null && activeIndex < teamStatusTurns.Length && teamStatusTurns[activeIndex] > 0;
    }

    public bool IsWildWyrmFury()
    {
        if (!inBattle) return false;
        if (wildStatus != StatusType.WyrmFury) return false;
        if (wildStatusPersistent) return true;
        return wildStatusTurns > 0;
    }


    public bool IsActivePlayerShadowVeiled()
    {
        if (!inBattle) return false;
        if (teamStatus == null) return false;
        if (activeIndex < 0 || activeIndex >= teamStatus.Length) return false;

        if (teamStatus[activeIndex] != StatusType.ShadowVeil) return false;

        if (teamStatusPersistent != null && activeIndex < teamStatusPersistent.Length && teamStatusPersistent[activeIndex])
            return true;

        return teamStatusTurns != null && activeIndex < teamStatusTurns.Length && teamStatusTurns[activeIndex] > 0;
    }

    public bool IsWildShadowVeiled()
    {
        if (!inBattle) return false;
        if (wildStatus != StatusType.ShadowVeil) return false;
        if (wildStatusPersistent) return true;
        return wildStatusTurns > 0;
    }

    public bool IsActivePlayerReinforced()
    {
        if (!inBattle) return false;
        if (teamStatus == null) return false;
        if (activeIndex < 0 || activeIndex >= teamStatus.Length) return false;

        if (teamStatus[activeIndex] != StatusType.Reinforce) return false;

        if (teamStatusPersistent != null && activeIndex < teamStatusPersistent.Length && teamStatusPersistent[activeIndex])
            return true;

        return teamStatusTurns != null && activeIndex < teamStatusTurns.Length && teamStatusTurns[activeIndex] > 0;
    }

    public bool IsWildReinforced()
    {
        if (!inBattle) return false;
        if (wildStatus != StatusType.Reinforce) return false;
        if (wildStatusPersistent) return true;
        return wildStatusTurns > 0;
    }

    // ─────────────────────────────────────────────────────────────
    // Safe status accessors (used by BattleStatsSystem/UI)
    // ─────────────────────────────────────────────────────────────

    public StatusType GetTeamStatusTypeSafe(int idx)
    {
        if (!inBattle) return StatusType.None;
        if (teamStatus == null) return StatusType.None;
        if (idx < 0 || idx >= teamStatus.Length) return StatusType.None;
        return teamStatus[idx];
    }

    public float GetTeamStatusMagnitudeSafe(int idx)
    {
        if (!inBattle) return 0f;
        if (teamStatusMagnitude == null) return 0f;
        if (idx < 0 || idx >= teamStatusMagnitude.Length) return 0f;
        return teamStatusMagnitude[idx];
    }

    public StatusType GetWildStatusTypeSafe()
    {
        if (!inBattle) return StatusType.None;
        return wildStatus;
    }

    public float GetWildStatusMagnitudeSafe()
    {
        if (!inBattle) return 0f;
        return wildStatusMagnitude;
    }

    public float GetPlayerTeamRallyBonusPctSafe()
    {
        if (!inBattle) return 0f;
        if (teamStatus == null) return 0f;

        // Aura rule: if any living ally has Rally active, return its magnitude (or default).
        for (int i = 0; i < teamStatus.Length; i++)
        {
            if (teamHP != null && i < teamHP.Length && teamHP[i] <= 0f) continue;
            if (teamStatus[i] != StatusType.Rally) continue;

            bool persistent = (teamStatusPersistent != null && i < teamStatusPersistent.Length) && teamStatusPersistent[i];
            int turns = (teamStatusTurns != null && i < teamStatusTurns.Length) ? Mathf.Max(0, teamStatusTurns[i]) : 0;
            if (!persistent && turns <= 0) continue;

            float mag = (teamStatusMagnitude != null && i < teamStatusMagnitude.Length) ? teamStatusMagnitude[i] : 0f;
            return (mag > 0f) ? mag : 0.10f;
        }

        return 0f;
    }

    public float GetWildRallyBonusPctSafe()
    {
        if (!inBattle) return 0f;
        if (wildStatus != StatusType.Rally) return 0f;
        if (!wildStatusPersistent && wildStatusTurns <= 0) return 0f;
        return (wildStatusMagnitude > 0f) ? wildStatusMagnitude : 0.10f;
    }

    // ─────────────────────────────────────────────────────────────
// Status: Soaked (speed reduction) helpers
// Magnitude represents reduction pct (e.g., 0.25 => -25% speed). Fallback 0.25 if unset.
// These helpers are referenced by TurnLoop + Mechanics.
// ─────────────────────────────────────────────────────────────
private float GetActivePlayerSoakedSpeedMultiplier()
{
    if (!inBattle) return 1f;
    if (activeIndex < 0 || teamStatus == null || activeIndex >= teamStatus.Length) return 1f;
    if (teamStatus[activeIndex] != StatusType.Soaked) return 1f;

    float red = (teamStatusMagnitude != null && activeIndex < teamStatusMagnitude.Length)
        ? teamStatusMagnitude[activeIndex]
        : 0f;

    if (red <= 0f) red = 0.25f;
    red = Mathf.Clamp(red, 0f, 0.9f);
    return 1f - red;
}

private float GetWildSoakedSpeedMultiplier()
{
    if (!inBattle) return 1f;
    if (wildStatus != StatusType.Soaked) return 1f;

    float red = wildStatusMagnitude;
    if (red <= 0f) red = 0.25f;
    red = Mathf.Clamp(red, 0f, 0.9f);
    return 1f - red;
}

// ─────────────────────────────────────────────────────────────
// Status: Rally (team aura) helpers
// Magnitude represents bonus pct (e.g., 0.10 => +10% outgoing dmg). Fallback 0.10 if unset.
// These helpers are referenced by TurnLoop.
// ─────────────────────────────────────────────────────────────
private float GetPlayerTeamRallyBonusPct()
{
    // You already implemented the logic in the Safe version.
    return GetPlayerTeamRallyBonusPctSafe();
}

private float GetWildRallyBonusPct()
{
    // You already implemented the logic in the Safe version.
    return GetWildRallyBonusPctSafe();
}

}
