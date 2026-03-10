using System;
using UnityEngine;


public sealed class IronEventGuard : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Header("Guard")]
    [SerializeField] private bool enabledGuard = true;
    [SerializeField] private bool logStackTrace = false;
    [SerializeField] private bool pauseEditorOnViolation = true;

    [Header("Forbidden during Iron")]
    [SerializeField] private bool forbidBattleFinished = true;
    [SerializeField] private bool forbidTeamChanged = true;
    [SerializeField] private bool forbidWinStreakChanged = true;
    [SerializeField] private bool forbidPromotions = true;
    [SerializeField] private bool forbidBoosters = true;
    [SerializeField] private bool forbidEnergy = true;
    [SerializeField] private bool forbidOwnedDiscovery = true;
    [SerializeField] private bool forbidAutoBattle = true;
    [SerializeField] private bool forbidRewardPopup = true;
    [SerializeField] private bool forbidJobs = true;
    [SerializeField] private bool forbidWorldEvents = true;

    private IronCareerManager _ironManager;

    private void OnEnable()
    {
        // NOTE: We intentionally subscribe broadly; checks are gated by IronCareerRuntime.IsActive.
        GameEvents.BattleFinished += OnBattleFinished;
        GameEvents.OnTeamChanged += OnTeamChanged;
        GameEvents.OnResourcesChanged += OnResourcesChanged;
        GameEvents.WinStreakChanged += OnWinStreakChanged;
        GameEvents.PromotionRankChanged += OnPromotionRankChanged;
        GameEvents.PromotionProgressChanged += OnPromotionProgressChanged;
        GameEvents.OnBoostersChanged += OnBoostersChanged;
        GameEvents.EnergyChanged += OnEnergyChanged;
        GameEvents.OnOwnedMonstersChanged += OnOwnedMonstersChanged;
        GameEvents.MonsterCaptured += OnMonsterCaptured;
        GameEvents.MonsterLeveled += OnMonsterLeveled;
        GameEvents.MonsterEvolved += OnMonsterEvolved;
        GameEvents.EvolutionOffered += OnEvolutionOffered;
        GameEvents.StarterChosen += OnStarterChosen;
        GameEvents.AutoBattleModeChanged += OnAutoBattleModeChanged;
        GameEvents.OnEncounterAutoModeChanged += OnEncounterAutoModeChanged;
        GameEvents.ShowRewardPopup += OnShowRewardPopup;
        GameEvents.OnJobsChanged += OnJobsChanged;
        GameEvents.JobGlobalModsChanged += OnJobGlobalModsChanged;
        GameEvents.WorldEventsChanged += OnWorldEventsChanged;
    }

    private void OnDisable()
    {
        GameEvents.BattleFinished -= OnBattleFinished;
        GameEvents.OnTeamChanged -= OnTeamChanged;
        GameEvents.OnResourcesChanged -= OnResourcesChanged;
        GameEvents.WinStreakChanged -= OnWinStreakChanged;
        GameEvents.PromotionRankChanged -= OnPromotionRankChanged;
        GameEvents.PromotionProgressChanged -= OnPromotionProgressChanged;
        GameEvents.OnBoostersChanged -= OnBoostersChanged;
        GameEvents.EnergyChanged -= OnEnergyChanged;
        GameEvents.OnOwnedMonstersChanged -= OnOwnedMonstersChanged;
        GameEvents.MonsterCaptured -= OnMonsterCaptured;
        GameEvents.MonsterLeveled -= OnMonsterLeveled;
        GameEvents.MonsterEvolved -= OnMonsterEvolved;
        GameEvents.EvolutionOffered -= OnEvolutionOffered;
        GameEvents.StarterChosen -= OnStarterChosen;
        GameEvents.AutoBattleModeChanged -= OnAutoBattleModeChanged;
        GameEvents.OnEncounterAutoModeChanged -= OnEncounterAutoModeChanged;
        GameEvents.ShowRewardPopup -= OnShowRewardPopup;
        GameEvents.OnJobsChanged -= OnJobsChanged;
        GameEvents.JobGlobalModsChanged -= OnJobGlobalModsChanged;
        GameEvents.WorldEventsChanged -= OnWorldEventsChanged;
    }

    private void Violation(string what)
    {
        if (!enabledGuard) return;
        if (!IsGuardActive()) return;

        string msg = $"[IronEventGuard] FORBIDDEN event fired during Iron: {what}";
        if (logStackTrace)
            Debug.LogError(msg + "\n" + Environment.StackTrace);
        else
            Debug.LogError(msg);

#if UNITY_EDITOR
        if (pauseEditorOnViolation)
            Debug.Break();
#endif
    }

    private bool IsGuardActive()
    {
        if (!IronCareerRuntime.IsActive) return false;

        if (!_ironManager)
            _ironManager = FindFirstObjectByType<IronCareerManager>(FindObjectsInactive.Include);

        return _ironManager != null && _ironManager.IsRunActive;
    }

    private void OnBattleFinished(BattleResult r)
    {
        if (forbidBattleFinished) Violation(nameof(GameEvents.BattleFinished));
    }

    private void OnTeamChanged()
    {
        if (forbidTeamChanged) Violation(nameof(GameEvents.OnTeamChanged));
    }

    private void OnResourcesChanged()
    {
        // Iron now intentionally banks run rewards (credits/growth cores) on battle resolution.
        // That flow emits OnResourcesChanged and is valid during the game-over transition.
        // Keep this event allowed to avoid false-positive guard breaks.
    }

    private void OnWinStreakChanged(int _)
    {
        if (forbidWinStreakChanged) Violation(nameof(GameEvents.WinStreakChanged));
    }

    private void OnPromotionRankChanged(int oldRank, int newRank)
    {
        if (forbidPromotions) Violation($"{nameof(GameEvents.PromotionRankChanged)} ({oldRank}->{newRank})");
    }

    private void OnPromotionProgressChanged(int rank, int xp, int xpThisRank, int xpToNext)
    {
        if (forbidPromotions) Violation(nameof(GameEvents.PromotionProgressChanged));
    }

    private void OnBoostersChanged()
    {
        // NOTE: Battle turn loop broadcasts this for HUD refresh.
        // It does not represent a persistent/meta booster change, so we allow it in Iron.
        // (We still guard real persistence via Save writes and other forbidden events.)
    }

    private void OnEnergyChanged()
    {
        if (forbidEnergy) Violation(nameof(GameEvents.EnergyChanged));
    }

    private void OnOwnedMonstersChanged()
    {
        if (forbidOwnedDiscovery) Violation(nameof(GameEvents.OnOwnedMonstersChanged));
    }

    private void OnMonsterCaptured(string id, MonsterType type)
    {
        if (forbidOwnedDiscovery) Violation(nameof(GameEvents.MonsterCaptured));
    }

    private void OnMonsterLeveled(string id, int lvl)
    {
        if (forbidOwnedDiscovery) Violation(nameof(GameEvents.MonsterLeveled));
    }

    private void OnMonsterEvolved(string id)
    {
        if (forbidOwnedDiscovery) Violation(nameof(GameEvents.MonsterEvolved));
    }

    private void OnEvolutionOffered(string id)
    {
        if (forbidOwnedDiscovery) Violation(nameof(GameEvents.EvolutionOffered));
    }

    private void OnStarterChosen(MonsterType _)
    {
        if (forbidOwnedDiscovery) Violation(nameof(GameEvents.StarterChosen));
    }

    private void OnAutoBattleModeChanged(bool _)
    {
        if (forbidAutoBattle) Violation(nameof(GameEvents.AutoBattleModeChanged));
    }

    private void OnEncounterAutoModeChanged()
    {
        if (forbidAutoBattle) Violation(nameof(GameEvents.OnEncounterAutoModeChanged));
    }

    private void OnShowRewardPopup(string a, string b, int c, int d)
    {
        if (forbidRewardPopup) Violation(nameof(GameEvents.ShowRewardPopup));
    }

    private void OnJobsChanged()
    {
        if (forbidJobs) Violation(nameof(GameEvents.OnJobsChanged));
    }

    private void OnJobGlobalModsChanged()
    {
        if (forbidJobs) Violation(nameof(GameEvents.JobGlobalModsChanged));
    }

    private void OnWorldEventsChanged()
    {
        if (forbidWorldEvents) Violation(nameof(GameEvents.WorldEventsChanged));
    }
#else
    private void OnEnable() { }
#endif
}
