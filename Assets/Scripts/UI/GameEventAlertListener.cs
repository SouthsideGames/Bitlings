using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class GameEventAlertListener : MonoBehaviour, IPointerClickHandler
{
    [Flags]
    public enum AlertGameEvent
    {
        None = 0,

        TeamChanged = 1 << 0,
        ResourcesChanged = 1 << 1,
        JobsChanged = 1 << 2,
        SaveReloaded = 1 << 3,
        SettingsApplied = 1 << 4,

        MonsterLeveled = 1 << 5,
        EvolutionOffered = 1 << 6,
        MonsterEvolved = 1 << 7,
        MonsterCaptured = 1 << 8,
        OwnedMonstersChanged = 1 << 9,

        BattleFinished = 1 << 10,
        BattleStateChanged = 1 << 11,

        FeatureUnlocked = 1 << 12,
        PromotionRankChanged = 1 << 13,
        WorldEventsChanged = 1 << 14,
        FavoritesChanged = 1 << 15,
        BoostersChanged = 1 << 16,
        AutoApplyRequested = 1 << 17,
        ToastRequested = 1 << 18,
        PackSeasonChanged = 1 << 19,
        ExchangeMarketReset = 1 << 20,
        ArenaDataChanged = 1 << 21,
        MentorRetired = 1 << 22,
        HonorApplied = 1 << 23,
    }

    [Header("Alert")]
    [SerializeField] private GameObject alertTarget;
    [SerializeField] private bool hideOnAwake = true;

    [Header("Events")]
    [SerializeField] private AlertGameEvent listenFor = AlertGameEvent.FeatureUnlocked;
    [SerializeField] private bool arenaDataOnlyWhenNewRoundReady;

    [Header("Dismiss")]
    [SerializeField] private bool dismissOnPointerClick = true;
    [SerializeField] private Button dismissButton;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Header("Debug")]
    [SerializeField] private bool logEventTriggers;
#else
    private const bool logEventTriggers = false;
#endif

    private void Reset()
    {
        dismissButton = GetComponent<Button>();

        if (!alertTarget && transform.childCount > 0)
            alertTarget = transform.GetChild(0).gameObject;
    }

    private void Awake()
    {
        if (hideOnAwake)
            SetAlertVisible(false);
    }

    private void OnEnable()
    {
        ApplySubscriptions(true);

        if ((listenFor & AlertGameEvent.ArenaDataChanged) != 0 && arenaDataOnlyWhenNewRoundReady)
            SetAlertVisible(ArenaSaveHelper.ShouldShowArenaRoundAlert());

        if (dismissButton)
        {
            dismissButton.onClick.RemoveListener(DismissAlert);
            dismissButton.onClick.AddListener(DismissAlert);
        }
    }

    private void OnDisable()
    {
        ApplySubscriptions(false);

        if (dismissButton)
            dismissButton.onClick.RemoveListener(DismissAlert);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!dismissOnPointerClick) return;
        DismissAlert();
    }

    public void DismissAlert()
    {
        SetAlertVisible(false);
    }

    private void TriggerAlert(string source)
    {
        if (logEventTriggers)
            DevLog.Log($"[GameEventAlertListener] Triggered by {source} on {name}", this);

        SetAlertVisible(true);
    }

    private void SetAlertVisible(bool visible)
    {
        if (!alertTarget) return;

        if (alertTarget.activeSelf != visible)
            alertTarget.SetActive(visible);
    }

    private void ApplySubscriptions(bool subscribe)
    {
        Toggle(AlertGameEvent.TeamChanged, subscribe, AddTeamChanged, RemoveTeamChanged);
        Toggle(AlertGameEvent.ResourcesChanged, subscribe, AddResourcesChanged, RemoveResourcesChanged);
        Toggle(AlertGameEvent.JobsChanged, subscribe, AddJobsChanged, RemoveJobsChanged);
        Toggle(AlertGameEvent.SaveReloaded, subscribe, AddSaveReloaded, RemoveSaveReloaded);
        Toggle(AlertGameEvent.SettingsApplied, subscribe, AddSettingsApplied, RemoveSettingsApplied);

        Toggle(AlertGameEvent.MonsterLeveled, subscribe, AddMonsterLeveled, RemoveMonsterLeveled);
        Toggle(AlertGameEvent.EvolutionOffered, subscribe, AddEvolutionOffered, RemoveEvolutionOffered);
        Toggle(AlertGameEvent.MonsterEvolved, subscribe, AddMonsterEvolved, RemoveMonsterEvolved);
        Toggle(AlertGameEvent.MonsterCaptured, subscribe, AddMonsterCaptured, RemoveMonsterCaptured);
        Toggle(AlertGameEvent.OwnedMonstersChanged, subscribe, AddOwnedMonstersChanged, RemoveOwnedMonstersChanged);

        Toggle(AlertGameEvent.BattleFinished, subscribe, AddBattleFinished, RemoveBattleFinished);
        Toggle(AlertGameEvent.BattleStateChanged, subscribe, AddBattleStateChanged, RemoveBattleStateChanged);

        Toggle(AlertGameEvent.FeatureUnlocked, subscribe, AddFeatureUnlocked, RemoveFeatureUnlocked);
        Toggle(AlertGameEvent.PromotionRankChanged, subscribe, AddPromotionRankChanged, RemovePromotionRankChanged);
        Toggle(AlertGameEvent.WorldEventsChanged, subscribe, AddWorldEventsChanged, RemoveWorldEventsChanged);
        Toggle(AlertGameEvent.FavoritesChanged, subscribe, AddFavoritesChanged, RemoveFavoritesChanged);
        Toggle(AlertGameEvent.BoostersChanged, subscribe, AddBoostersChanged, RemoveBoostersChanged);
        Toggle(AlertGameEvent.AutoApplyRequested, subscribe, AddAutoApplyRequested, RemoveAutoApplyRequested);
        Toggle(AlertGameEvent.ToastRequested, subscribe, AddToastRequested, RemoveToastRequested);
        Toggle(AlertGameEvent.PackSeasonChanged, subscribe, AddPackSeasonChanged, RemovePackSeasonChanged);
        Toggle(AlertGameEvent.ExchangeMarketReset, subscribe, AddExchangeMarketReset, RemoveExchangeMarketReset);
        Toggle(AlertGameEvent.ArenaDataChanged, subscribe, AddArenaDataChanged, RemoveArenaDataChanged);
        Toggle(AlertGameEvent.MentorRetired, subscribe, AddMentorRetired, RemoveMentorRetired);
        Toggle(AlertGameEvent.HonorApplied, subscribe, AddHonorApplied, RemoveHonorApplied);
    }

    private void Toggle(AlertGameEvent flag, bool subscribe, Action add, Action remove)
    {
        if ((listenFor & flag) == 0) return;
        if (subscribe) add();
        else remove();
    }

    private void AddTeamChanged() => GameEvents.OnTeamChanged += HandleTeamChanged;
    private void RemoveTeamChanged() => GameEvents.OnTeamChanged -= HandleTeamChanged;
    private void HandleTeamChanged() => TriggerAlert(nameof(GameEvents.OnTeamChanged));

    private void AddResourcesChanged() => GameEvents.OnResourcesChanged += HandleResourcesChanged;
    private void RemoveResourcesChanged() => GameEvents.OnResourcesChanged -= HandleResourcesChanged;
    private void HandleResourcesChanged() => TriggerAlert(nameof(GameEvents.OnResourcesChanged));

    private void AddJobsChanged() => GameEvents.OnJobsChanged += HandleJobsChanged;
    private void RemoveJobsChanged() => GameEvents.OnJobsChanged -= HandleJobsChanged;
    private void HandleJobsChanged() => TriggerAlert(nameof(GameEvents.OnJobsChanged));

    private void AddSaveReloaded() => GameEvents.OnSaveReloaded += HandleSaveReloaded;
    private void RemoveSaveReloaded() => GameEvents.OnSaveReloaded -= HandleSaveReloaded;
    private void HandleSaveReloaded() => TriggerAlert(nameof(GameEvents.OnSaveReloaded));

    private void AddSettingsApplied() => GameEvents.OnSettingsApplied += HandleSettingsApplied;
    private void RemoveSettingsApplied() => GameEvents.OnSettingsApplied -= HandleSettingsApplied;
    private void HandleSettingsApplied() => TriggerAlert(nameof(GameEvents.OnSettingsApplied));

    private void AddMonsterLeveled() => GameEvents.MonsterLeveled += HandleMonsterLeveled;
    private void RemoveMonsterLeveled() => GameEvents.MonsterLeveled -= HandleMonsterLeveled;
    private void HandleMonsterLeveled(string monsterId, int level) => TriggerAlert(nameof(GameEvents.MonsterLeveled));

    private void AddEvolutionOffered() => GameEvents.EvolutionOffered += HandleEvolutionOffered;
    private void RemoveEvolutionOffered() => GameEvents.EvolutionOffered -= HandleEvolutionOffered;
    private void HandleEvolutionOffered(string monsterId) => TriggerAlert(nameof(GameEvents.EvolutionOffered));

    private void AddMonsterEvolved() => GameEvents.MonsterEvolved += HandleMonsterEvolved;
    private void RemoveMonsterEvolved() => GameEvents.MonsterEvolved -= HandleMonsterEvolved;
    private void HandleMonsterEvolved(string monsterId) => TriggerAlert(nameof(GameEvents.MonsterEvolved));

    private void AddMonsterCaptured() => GameEvents.MonsterCaptured += HandleMonsterCaptured;
    private void RemoveMonsterCaptured() => GameEvents.MonsterCaptured -= HandleMonsterCaptured;
    private void HandleMonsterCaptured(string monsterId, MonsterType type) => TriggerAlert(nameof(GameEvents.MonsterCaptured));

    private void AddOwnedMonstersChanged() => GameEvents.OnOwnedMonstersChanged += HandleOwnedMonstersChanged;
    private void RemoveOwnedMonstersChanged() => GameEvents.OnOwnedMonstersChanged -= HandleOwnedMonstersChanged;
    private void HandleOwnedMonstersChanged() => TriggerAlert(nameof(GameEvents.OnOwnedMonstersChanged));

    private void AddBattleFinished() => GameEvents.BattleFinished += HandleBattleFinished;
    private void RemoveBattleFinished() => GameEvents.BattleFinished -= HandleBattleFinished;
    private void HandleBattleFinished(BattleResult result) => TriggerAlert(nameof(GameEvents.BattleFinished));

    private void AddBattleStateChanged() => GameEvents.OnBattleStateChanged += HandleBattleStateChanged;
    private void RemoveBattleStateChanged() => GameEvents.OnBattleStateChanged -= HandleBattleStateChanged;
    private void HandleBattleStateChanged() => TriggerAlert(nameof(GameEvents.OnBattleStateChanged));

    private void AddFeatureUnlocked() => GameEvents.FeatureUnlocked += HandleFeatureUnlocked;
    private void RemoveFeatureUnlocked() => GameEvents.FeatureUnlocked -= HandleFeatureUnlocked;
    private void HandleFeatureUnlocked(FeatureId featureId) => TriggerAlert(nameof(GameEvents.FeatureUnlocked));

    private void AddPromotionRankChanged() => GameEvents.PromotionRankChanged += HandlePromotionRankChanged;
    private void RemovePromotionRankChanged() => GameEvents.PromotionRankChanged -= HandlePromotionRankChanged;
    private void HandlePromotionRankChanged(int oldRank, int newRank) => TriggerAlert(nameof(GameEvents.PromotionRankChanged));

    private void AddWorldEventsChanged() => GameEvents.WorldEventsChanged += HandleWorldEventsChanged;
    private void RemoveWorldEventsChanged() => GameEvents.WorldEventsChanged -= HandleWorldEventsChanged;
    private void HandleWorldEventsChanged() => TriggerAlert(nameof(GameEvents.WorldEventsChanged));

    private void AddFavoritesChanged() => GameEvents.FavoritesChanged += HandleFavoritesChanged;
    private void RemoveFavoritesChanged() => GameEvents.FavoritesChanged -= HandleFavoritesChanged;
    private void HandleFavoritesChanged() => TriggerAlert(nameof(GameEvents.FavoritesChanged));

    private void AddBoostersChanged() => GameEvents.OnBoostersChanged += HandleBoostersChanged;
    private void RemoveBoostersChanged() => GameEvents.OnBoostersChanged -= HandleBoostersChanged;
    private void HandleBoostersChanged() => TriggerAlert(nameof(GameEvents.OnBoostersChanged));

    private void AddAutoApplyRequested() => GameEvents.AutoApplyRequested += HandleAutoApplyRequested;
    private void RemoveAutoApplyRequested() => GameEvents.AutoApplyRequested -= HandleAutoApplyRequested;
    private void HandleAutoApplyRequested() => TriggerAlert(nameof(GameEvents.AutoApplyRequested));

    private void AddToastRequested() => GameEvents.ToastRequested += HandleToastRequested;
    private void RemoveToastRequested() => GameEvents.ToastRequested -= HandleToastRequested;
    private void HandleToastRequested(string message) => TriggerAlert(nameof(GameEvents.ToastRequested));

    private void AddPackSeasonChanged() => GameEvents.PackSeasonChanged += HandlePackSeasonChanged;
    private void RemovePackSeasonChanged() => GameEvents.PackSeasonChanged -= HandlePackSeasonChanged;
    private void HandlePackSeasonChanged() => TriggerAlert(nameof(GameEvents.PackSeasonChanged));

    private void AddExchangeMarketReset() => GameEvents.ExchangeMarketReset += HandleExchangeMarketReset;
    private void RemoveExchangeMarketReset() => GameEvents.ExchangeMarketReset -= HandleExchangeMarketReset;
    private void HandleExchangeMarketReset() => TriggerAlert(nameof(GameEvents.ExchangeMarketReset));

    private void AddArenaDataChanged() => GameEvents.ArenaDataChanged += HandleArenaDataChanged;
    private void RemoveArenaDataChanged() => GameEvents.ArenaDataChanged -= HandleArenaDataChanged;
    private void HandleArenaDataChanged()
    {
        if (arenaDataOnlyWhenNewRoundReady)
        {
            SetAlertVisible(ArenaSaveHelper.ShouldShowArenaRoundAlert());
            return;
        }

        TriggerAlert(nameof(GameEvents.ArenaDataChanged));
    }

    private void AddMentorRetired() => GameEvents.MentorRetired += HandleMentorRetired;
    private void RemoveMentorRetired() => GameEvents.MentorRetired -= HandleMentorRetired;
    private void HandleMentorRetired(string _) => TriggerAlert(nameof(GameEvents.MentorRetired));

    private void AddHonorApplied() => GameEvents.HonorApplied += HandleHonorApplied;
    private void RemoveHonorApplied() => GameEvents.HonorApplied -= HandleHonorApplied;
    private void HandleHonorApplied(string _) => TriggerAlert(nameof(GameEvents.HonorApplied));
}