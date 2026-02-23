using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class IronModeDisabler : MonoBehaviour
{
    [Header("Optional explicit bindings (recommended)")]
    [Tooltip("Disable these components on Iron enter; restore enabled state on exit.")]
    [SerializeField] private List<Behaviour> disableBehaviours = new List<Behaviour>();

    [Tooltip("Disable these GameObject roots on Iron enter; restore active state on exit.")]
    [SerializeField] private List<GameObject> disableRoots = new List<GameObject>();

    [Header("Panel switching (for your duplicated Panel_IronCareerEncounter approach)")]
    [Tooltip("Normal Canvas/Panel_Encounter root (turn OFF in Iron).")]
    [SerializeField] private GameObject normalEncounterPanelRoot;

    [Tooltip("Iron Canvas/Panel_IronCareerEncounter root (turn ON in Iron).")]
    [SerializeField] private GameObject ironEncounterPanelRoot;

    [Tooltip("Optional: the EncounterSystem root object (green). If set, will be disabled in Iron.")]
    [SerializeField] private GameObject encounterSystemRoot;

    [Header("Auto-find (fallback)")]
    [Tooltip("If true, tries to auto-find common systems by type and disable them.")]
    [SerializeField] private bool autoFindCommonSystems = true;

    private readonly Dictionary<Behaviour, bool> _prevEnabled = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<GameObject, bool> _prevActive = new Dictionary<GameObject, bool>();

    private bool _ironApplied;

    public void ApplyIron()
    {
        if (_ironApplied) return;
        _ironApplied = true;

        _prevEnabled.Clear();
        _prevActive.Clear();

        // Explicit lists first.
        for (int i = 0; i < disableBehaviours.Count; i++)
            Disable(disableBehaviours[i]);

        for (int i = 0; i < disableRoots.Count; i++)
            Disable(disableRoots[i]);

        // Explicit encounter root kill-switch (your green EncounterSystem)
        if (encounterSystemRoot) Disable(encounterSystemRoot);

        if (autoFindCommonSystems)
            AutoFindAndDisable();

        // Swap panels (this is the whole point of your current setup)
        if (normalEncounterPanelRoot) Disable(normalEncounterPanelRoot);
        if (ironEncounterPanelRoot) Enable(ironEncounterPanelRoot);

        // Also close normal panels that must not appear in Iron.
        if (UIManager.I)
        {
            UIManager.I.Hide(PanelId.Encounter);
            UIManager.I.Hide(PanelId.PostBattleSummary);
            UIManager.I.Hide(PanelId.IdleBattleRewards);

            // Show the Iron main panel if it exists in UIManager mapping.
            UIManager.I.Show(PanelId.IronCareerEncounter);
        }
    }

    public void Restore()
    {
        if (!_ironApplied) return;
        _ironApplied = false;

        foreach (var kv in _prevEnabled)
            if (kv.Key) kv.Key.enabled = kv.Value;

        foreach (var kv in _prevActive)
            if (kv.Key) kv.Key.SetActive(kv.Value);

        _prevEnabled.Clear();
        _prevActive.Clear();
    }

    private void Disable(Behaviour b)
    {
        if (!b) return;
        if (!_prevEnabled.ContainsKey(b))
            _prevEnabled.Add(b, b.enabled);
        b.enabled = false;
    }

    private void Disable(GameObject go)
    {
        if (!go) return;
        if (!_prevActive.ContainsKey(go))
            _prevActive.Add(go, go.activeSelf);
        go.SetActive(false);
    }

    private void Enable(GameObject go)
    {
        if (!go) return;
        if (!_prevActive.ContainsKey(go))
            _prevActive.Add(go, go.activeSelf);
        go.SetActive(true);
    }

    private void AutoFindAndDisable()
    {
        TryDisableFirst<HealthRegenSystem>();
        TryDisableFirst<JobManager>();
        TryDisableFirst<EncounterManager>();
        TryDisableFirst<IdleBattleManager>();
        TryDisableFirst<WorldEventSystem>();
        TryDisableFirst<WorldEventManager>();
        TryDisableFirst<PostBattleSummaryManager>();

        TryDisableFirst<BattleBoosterController>();
        TryDisableFirst<BattleTempBuffs>();

        // If the EncounterPanel root is present, disable the GameObject.
        var ep = FindFirstObjectByType<EncounterPanelUI>(FindObjectsInactive.Include);
        if (ep) Disable(ep.gameObject);

        // If the World Event ticker root exists, disable it.
        var ticker = FindFirstObjectByType<WorldEventTickerUI>(FindObjectsInactive.Include);
        if (ticker) Disable(ticker.gameObject);
    }

    private void TryDisableFirst<T>() where T : Behaviour
    {
        var c = FindFirstObjectByType<T>(FindObjectsInactive.Include);
        if (c) Disable(c);
    }
}