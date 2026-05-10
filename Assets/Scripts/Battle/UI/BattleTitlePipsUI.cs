using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BattleTitlePipsUI : MonoBehaviour
{
    private enum TargetKind { Player, Wild }

    [Header("Refs")]
    [SerializeField] private BattleManager battle;

    [Header("Target")]
    [SerializeField] private TargetKind target = TargetKind.Player;

    [Header("UI")]
    [Tooltip("Optional: child container to hide/show without disabling this component.")]
    [SerializeField] private GameObject visualsRoot;

    [SerializeField] private Transform pipsRoot;
    [SerializeField] private GameObject pipPrefab;

    [Header("Rules")]
    [Tooltip("Maximum number of pips to show (extra titles are ignored).")]
    [SerializeField, Min(1)] private int maxPips = 4;

    [Tooltip("If true, hides the entire bar when there are no titles to show.")]
    [SerializeField] private bool hideIfNone = true;

    [Tooltip("Alpha applied to inactive pips.")]
    [SerializeField, Range(0f, 1f)] private float inactiveAlpha = 0.35f;

    [Tooltip("Optional: punch scale when a pip becomes active.")]
    [SerializeField] private bool punchOnActivate = true;

    [SerializeField, Range(1.01f, 1.35f)] private float punchScale = 1.15f;
    [SerializeField, Min(0.01f)] private float punchTime = 0.10f; // TODO: confirm this 0.01f is intentional

    private readonly List<Pip> _pips = new List<Pip>(8);
    private string _lastKey;
    private int _lastHash;
    private BattleManager _battle;
    private bool _warnedMissingRefs;

    private sealed class Pip
    {
        public GameObject go;
        public Image icon;
        public CanvasGroup cg;
        public TMP_Text stacks;
        public Button btn;

        public string titleId;
        public bool active;
        public int stackCount;
    }

    private void Awake()
    {
        if (!pipsRoot) pipsRoot = transform;
        if (!visualsRoot) visualsRoot = gameObject;
        BootstrapExistingPips();
    }

    private void OnEnable()
    {
        if (_battle == null)
            _battle = battle != null
                ? battle
                : (GetComponentInParent<BattleManager>() ?? FindFirstObjectByType<BattleManager>(FindObjectsInactive.Include));

        // Defensive removal ensures no duplicate handlers on repeated OnEnable calls
        GameEvents.OnBattleStateChanged -= Refresh;
        GameEvents.OnBattleStateChanged += Refresh;
        GameEvents.OnTeamChanged -= Refresh;
        GameEvents.OnTeamChanged += Refresh;

        if (_battle != null)
        {
            _battle.OnBattleEvent -= OnBattleEvent;
            _battle.OnBattleEvent += OnBattleEvent;
        }

        Refresh();
    }

    private void OnDisable()
    {
        GameEvents.OnBattleStateChanged -= Refresh;
        GameEvents.OnTeamChanged -= Refresh;

        if (_battle != null)
            _battle.OnBattleEvent -= OnBattleEvent;

        ResetTweenState();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            ResetTweenState();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            ResetTweenState();
    }

    private void OnBattleEvent(BattleEvent e)
    {
        // Refresh on events likely to change title state (HP thresholds, stacks, etc.)
        switch (e.kind)
        {
            case BattleEvent.Kind.Swap:
            case BattleEvent.Kind.Damage:
            case BattleEvent.Kind.StatusApplied:
            case BattleEvent.Kind.KO:
            case BattleEvent.Kind.UIRefreshHP:
            case BattleEvent.Kind.GuardChanged:
            case BattleEvent.Kind.ChargeChanged:
            case BattleEvent.Kind.DefendResult:
            case BattleEvent.Kind.ActionWindup:
            case BattleEvent.Kind.ActionQueued:
                Refresh();
                break;
        }
    }

    public void Refresh()
    {
        if (_battle == null || !_battle.InBattle || TitleManager.I == null)
        {
            ApplyNone();
            return;
        }

        string combatId = GetCombatantId();
        if (string.IsNullOrEmpty(combatId))
        {
            ApplyNone();
            return;
        }

        var states = TitleManager.I.GetActiveTitleUIStates(combatId);
        if (states == null || states.Count == 0)
        {
            ApplyNone();
            return;
        }

        int count = Mathf.Min(maxPips, states.Count);
        int h = 17;
        for (int i = 0; i < count; i++)
        {
            var s = states[i];
            h = (h * 31) ^ (s.titleId != null ? s.titleId.GetHashCode() : 0);
            h = (h * 31) ^ (s.isActive ? 1 : 0);
            h = (h * 31) ^ s.stacks;
        }

        if (_lastKey == combatId && _lastHash == h)
            return;

        _lastKey = combatId;
        _lastHash = h;

        EnsurePipCount(count);
        int renderCount = Mathf.Min(count, _pips.Count);
        if (renderCount <= 0)
        {
            ApplyNone();
            return;
        }

        if (visualsRoot) visualsRoot.SetActive(true);

        for (int i = 0; i < renderCount; i++)
        {
            var s = states[i];
            var p = _pips[i];

            p.go.SetActive(true);

            bool wasActive = p.active;
            p.titleId = s.titleId;
            p.active = s.isActive;
            p.stackCount = s.stacks;

            if (p.icon)
                p.icon.sprite = s.icon;

            float a = p.active ? 1f : inactiveAlpha;
            if (p.cg)
                p.cg.alpha = a;
            else if (p.icon)
            {
                var c = p.icon.color;
                c.a = a;
                p.icon.color = c;
            }

            if (p.stacks)
            {
                if (p.stackCount > 0)
                {
                    p.stacks.gameObject.SetActive(true);
                    p.stacks.text = p.stackCount.ToString();
                }
                else
                {
                    p.stacks.gameObject.SetActive(false);
                    p.stacks.text = "";
                }
            }

            if (p.btn)
            {
                p.btn.onClick.RemoveAllListeners();
                string tid = p.titleId;
                p.btn.onClick.AddListener(() => OpenTitleInfo(tid));
            }

            if (punchOnActivate && p.active && !wasActive)
                Punch(p.go);
        }

        for (int i = renderCount; i < _pips.Count; i++)
            _pips[i].go.SetActive(false);
    }

    private void ApplyNone()
    {
        _lastKey = null;
        _lastHash = 0;

        for (int i = 0; i < _pips.Count; i++)
            if (_pips[i] != null && _pips[i].go) _pips[i].go.SetActive(false);

        if (visualsRoot)
            visualsRoot.SetActive(!hideIfNone);
    }

    private string GetCombatantId()
    {
        if (_battle == null) return "";
        if (target == TargetKind.Wild)
            return _battle.WildCombatIdForTitles;

        string ownedId = _battle.ActivePlayerTitleOwnerId;
        if (!string.IsNullOrEmpty(ownedId))
            return ownedId;

        return _battle.ActivePlayerMonsterId;
    }

    private void EnsurePipCount(int needed)
    {
        BootstrapExistingPips();

        if (pipsRoot == null)
        {
            if (!_warnedMissingRefs && needed > 0)
            {
                _warnedMissingRefs = true;
                Debug.LogWarning($"[{nameof(BattleTitlePipsUI)}] Missing pipsRoot on '{name}'.", this);
            }
            return;
        }

        if (pipPrefab == null)
        {
            if (!_warnedMissingRefs && needed > 0)
            {
                _warnedMissingRefs = true;
                Debug.LogWarning($"[{nameof(BattleTitlePipsUI)}] Missing pipPrefab on '{name}'. Assign pipPrefab or add pre-placed pip children under pipsRoot.", this);
            }
            return;
        }

        while (_pips.Count < needed)
        {
            var go = Instantiate(pipPrefab, pipsRoot);
            go.name = $"TitlePip_{_pips.Count}";

            var p = new Pip
            {
                go = go,
                icon = go.GetComponentInChildren<Image>(true),
                cg = go.GetComponentInChildren<CanvasGroup>(true),
                btn = go.GetComponentInChildren<Button>(true),
                titleId = null,
                active = false,
                stackCount = 0
            };

            // Prefer a TMP named "Stacks" if present.
            var labels = go.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] != null && string.Equals(labels[i].gameObject.name, "Stacks", StringComparison.OrdinalIgnoreCase))
                {
                    p.stacks = labels[i];
                    break;
                }
            }
            if (p.stacks == null && labels != null && labels.Length == 1)
                p.stacks = labels[0];

            _pips.Add(p);
        }
    }

    private void BootstrapExistingPips()
    {
        if (_pips.Count > 0 || pipsRoot == null) return;

        for (int i = 0; i < pipsRoot.childCount; i++)
        {
            var child = pipsRoot.GetChild(i);
            if (!child) continue;

            var p = BuildPip(child.gameObject);
            if (p != null)
                _pips.Add(p);
        }
    }

    private static Pip BuildPip(GameObject go)
    {
        if (!go) return null;

        var p = new Pip
        {
            go = go,
            icon = go.GetComponentInChildren<Image>(true),
            cg = go.GetComponentInChildren<CanvasGroup>(true),
            btn = go.GetComponentInChildren<Button>(true),
            titleId = null,
            active = false,
            stackCount = 0
        };

        var labels = go.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] != null && string.Equals(labels[i].gameObject.name, "Stacks", StringComparison.OrdinalIgnoreCase))
            {
                p.stacks = labels[i];
                break;
            }
        }
        if (p.stacks == null && labels != null && labels.Length == 1)
            p.stacks = labels[0];

        return p;
    }

    private void OpenTitleInfo(string titleId)
    {
        if (string.IsNullOrEmpty(titleId)) return;
        if (TitleManager.I == null) return;

        var title = TitleManager.I.GetTitleById(titleId);
        if (!title) return;

        InfoRouter.Open(
            $"title.{title.titleId}",
            title.displayName,
            target == TargetKind.Wild ? "Wild Title" : "Active Title",
            title.description
        );

        AudioManager.I?.PlayClick();
    }

    private void Punch(GameObject go)
    {
        if (!go) return;
        LeanTween.cancel(go);

        var t = go.transform;
        t.localScale = Vector3.one;

        LeanTween.scale(go, Vector3.one * punchScale, punchTime)
            .setEaseOutBack()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (!go) return;
                LeanTween.scale(go, Vector3.one, punchTime * 0.9f)
                    .setEaseOutQuad()
                    .setIgnoreTimeScale(true);
            });
    }

    private void ResetTweenState()
    {
        for (int i = 0; i < _pips.Count; i++)
        {
            var p = _pips[i];
            if (p == null || !p.go) continue;

            LeanTween.cancel(p.go);
            p.go.transform.localScale = Vector3.one;
        }
    }
}
