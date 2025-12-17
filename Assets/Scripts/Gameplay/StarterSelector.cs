using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class StarterSelector : MonoBehaviour
{
    [Header("Routing")]
    [SerializeField] private PanelId selfPanelId = PanelId.StarterPicker;
    [SerializeField] private PanelId introPanelId = PanelId.Intro;
    [SerializeField] private PanelId homePanelId  = PanelId.Home;

    [Tooltip("If true, this class routes to Home after choosing. If false, leave routing to caller.")]
    [SerializeField] private bool routeToHomeOnChoose = true;

    [Header("UI")]
    [SerializeField] private Button[] starterButtons;
    [SerializeField] private MonsterDetailPanelUI detailPanel; // optional when bypassing
    [SerializeField, Min(1)] private int maxNumberOfStarters = 4;

    [Header("Starter Filters")]
    [SerializeField] private int minHpAtLv1  = 22;
    [SerializeField] private int minAtkAtLv1 = 6;

    [Header("Debug")]
    [Tooltip("Bypass the MonsterDetail panel entirely. Click chooses immediately.")]
    [SerializeField] private bool bypassDetailPanelForDebug = true;

    private MonsterDataSO[] _starters;
    private bool _locked;
    private Coroutine _openCR;

    void Awake()
    {
        if (starterButtons == null) return;
        for (int i = 0; i < starterButtons.Length; i++)
        {
            if (!starterButtons[i]) continue;
            var nav = starterButtons[i].navigation;
            nav.mode = Navigation.Mode.None;
            starterButtons[i].navigation = nav;
        }
    }

    void OnEnable()
    {
        _locked = false;
        if (SaveManager.Data == null || !SaveManager.Data.hasChosenStarter)
            Show();
    }

    void OnDisable()
    {
        if (_openCR != null)
        {
            StopCoroutine(_openCR);
            _openCR = null;
        }
        _locked = false;
        SetButtonsInteractable(true);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
    }

    public void Show()
    {
        if (_locked) return;
        if (SaveManager.Data != null && SaveManager.Data.hasChosenStarter) return;

        var lib = MonsterLibraryLocator.Lib;
        if (!lib || starterButtons == null || starterButtons.Length == 0)
        {
            Debug.LogWarning("[StarterSelector] Missing library or no buttons wired.");
            return;
        }

        int count = Mathf.Min(maxNumberOfStarters, starterButtons.Length);
        if (count <= 0)
        {
            Debug.LogWarning("[StarterSelector] No starter buttons available.");
            return;
        }

        var pool = BuildStarterPool(lib);
        _starters = PickDailyDiverse(pool, count);

        for (int i = 0; i < starterButtons.Length; i++)
        {
            var btn = starterButtons[i];
            if (!btn) continue;

            btn.onClick.RemoveAllListeners();

            if (i < count)
            {
                var defLocal = _starters[i];

                if (defLocal == null || string.IsNullOrEmpty(defLocal.id))
                {
                    Debug.LogError($"[StarterSelector] Null or missing-id monster at index {i}");
                    btn.gameObject.SetActive(false);
                    continue;
                }

                var label = btn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label) label.text = defLocal.displayName ?? "???";

                var icon = FindIconChild(btn);
                if (icon)
                {
                    if (defLocal.icon)
                    {
                        icon.enabled = true;
                        icon.sprite = defLocal.icon;
                    }
                    else
                    {
                        icon.enabled = false;
                        icon.sprite = null;
                    }
                }

                var capturedDef = defLocal;

                if (bypassDetailPanelForDebug)
                {
                    btn.onClick.AddListener(() =>
                    {
                        if (_locked) return;
                        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
                        _locked = true;
                        SetButtonsInteractable(false);
                        Debug.Log($"[StarterSelector][BYPASS] Choosing starter: {capturedDef.id}");
                        Choose(capturedDef);
                    });
                }
                else
                {
                    btn.onClick.AddListener(() =>
                    {
                        if (_locked) return;
                        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
                        if (_openCR != null) StopCoroutine(_openCR);
                        _openCR = StartCoroutine(OpenDetailNextFrame(capturedDef));
                    });
                }

                btn.interactable = !_locked;
                btn.gameObject.SetActive(true);
            }
            else
            {
                btn.gameObject.SetActive(false);
            }
        }

        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
    }

    IEnumerator OpenDetailNextFrame(MonsterDataSO defLocal)
    {
        if (_locked || defLocal == null)
        {
            _locked = false;
            SetButtonsInteractable(true);
            yield break;
        }

        if (!detailPanel)
        {
            Debug.LogError("[StarterSelector] Detail panel reference is missing (bypass is off).");
            _locked = false;
            SetButtonsInteractable(true);
            yield break;
        }

        _locked = true;
        SetButtonsInteractable(false);

        yield return null; // let UI settle

        try
        {
            detailPanel.ShowStarter(
                defLocal,
                onConfirmCallback: OnConfirmDetail,
                onCancelCallback: OnCancelDetail
            );
        }
        catch (Exception ex)
        {
            Debug.LogError($"[StarterSelector] Exception opening detail panel for {defLocal?.id}: {ex}");
            _locked = false;
            SetButtonsInteractable(true);
        }
        finally
        {
            _openCR = null;
        }
    }

    void OnCancelDetail()
    {
        _locked = false;
        SetButtonsInteractable(true);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
    }

    void OnConfirmDetail(MonsterDataSO chosen) => Choose(chosen);

    // =========================================================================
    // IMPORTANT UPDATE:
    // - After selecting a starter, we ensure SaveManager transient sets are rebuilt
    // - We apply starter unlocks
    // - We force Save() AND broadcast OnJobsChanged (and OnTeamChanged) AFTER save
    // - We also force an immediate UI refresh on the next frame
    // =========================================================================
    void Choose(MonsterDataSO pick)
    {
        if (pick == null || string.IsNullOrEmpty(pick.id))
        {
            Debug.LogError("[StarterSelector] Invalid monster in Choose");
            _locked = false;
            SetButtonsInteractable(true);
            return;
        }

        try
        {
            Debug.Log($"[StarterSelector] GrantStarter -> {pick.id}");

            // This already saves, and may raise StarterChosen (via SaveManager.GrantStarter).
            SaveManager.GrantStarter(pick.id, 1);

            // Ensure runtime sets are present NOW (fixes “works after relaunch” symptoms)
            if (SaveManager.Data != null)
                SaveManager.Data.EnsureTransientSets();

            // Apply unlocks (may Save internally). We still broadcast after our final Save().
            if (JobManager.I != null)
                JobManager.I.ApplyStarterUnlocksNow(pick.type);

            if (SaveManager.Data != null)
            {
                SaveManager.Data.hasChosenStarter = true;

                // Ensure new team member(s) have valid HP
                var lib  = MonsterLibraryLocator.Lib;
                var team = SaveManager.Data.team;
                if (lib && team != null && team.Count > 0)
                {
                    for (int i = 0; i < team.Count; i++)
                    {
                        var om = team[i];
                        if (om == null || string.IsNullOrEmpty(om.monsterId)) continue;

                        if (om.currentHP <= 0)
                        {
                            var def = lib.GetById(om.monsterId);
                            if (!def) continue;
                            int maxHP = Mathf.RoundToInt(BattleCalc.CalcHP(def, Mathf.Max(1, om.level)));
                            om.currentHP = Mathf.Max(1, maxHP);
                            team[i] = om;
                        }
                    }
                }

                // FINAL authoritative save for everything we changed above.
                SaveManager.Save();

                // Rebuild transient sets again after Save (defensive; avoids stale in-memory state)
                SaveManager.Data.EnsureTransientSets();
            }

            // Broadcast AFTER Save so listeners read consistent state.
            try { GameEvents.OnJobsChanged?.Invoke(); } catch { }
            try { GameEvents.OnTeamChanged?.Invoke(); } catch { }

            // Force views to refresh both immediately and next frame (covers enable-order issues)
            if (JobManager.I != null)
            {
                try { JobManager.I.RefreshAllJobSiteViewsInScene(); } catch { }
                StartCoroutine(RefreshJobsNextFrame());
            }

            StartCoroutine(RouteNextFrame());
        }
        catch (Exception ex)
        {
            Debug.LogError($"[StarterSelector] Exception during Choose: {ex}");
            _locked = false;
            SetButtonsInteractable(true);
        }
    }

    private IEnumerator RefreshJobsNextFrame()
    {
        yield return null;

        // Ensure sets are present before refresh reads them
        SaveManager.Data?.EnsureTransientSets();

        if (JobManager.I != null)
        {
            try { JobManager.I.RefreshAllJobSiteViewsInScene(); } catch { }
        }

        try { GameEvents.OnJobsChanged?.Invoke(); } catch { }
    }

    IEnumerator RouteNextFrame()
    {
        yield return null;

        if (UIManager.I != null)
        {
            if (selfPanelId  != PanelId.None) UIManager.I.Hide(selfPanelId);
            if (introPanelId != PanelId.None) UIManager.I.Hide(introPanelId);
            if (routeToHomeOnChoose && homePanelId != PanelId.None)
                UIManager.I.Show(homePanelId);
        }

        _locked = false;
        SetButtonsInteractable(true);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
    }

    void SetButtonsInteractable(bool on)
    {
        if (starterButtons == null) return;
        for (int i = 0; i < starterButtons.Length; i++)
            if (starterButtons[i]) starterButtons[i].interactable = on;
    }

    Image FindIconChild(Button btn)
    {
        var t = btn.transform.Find("Icon");
        if (t) return t.GetComponent<Image>();
        return btn.GetComponentInChildren<Image>(true);
    }

    // =========================
    // Pool & Picking
    // =========================
    MonsterDataSO[] BuildStarterPool(MonsterLibrarySO lib)
    {
        if (lib?.monsters == null) return Array.Empty<MonsterDataSO>();

        var allowedRarities = new HashSet<Rarity>(new[] { Rarity.Common, Rarity.Uncommon });

        return lib.monsters
            .Where(m => m && !string.IsNullOrEmpty(m.id))
            .Where(m => allowedRarities.Contains(m.rarity))
            .Where(m => m.canBeStarter)
            .Where(m =>
            {
                int hp1  = Mathf.RoundToInt(BattleCalc.CalcHP(m, 1));
                int atk1 = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(m, 1, 0, 0));
                return hp1 >= minHpAtLv1 && atk1 >= minAtkAtLv1;
            })
            .ToArray();
    }

    MonsterDataSO[] PickDailyDiverse(MonsterDataSO[] pool, int needed)
    {
        if (pool == null || pool.Length == 0)
        {
            var empty = new MonsterDataSO[needed];
            for (int i = 0; i < needed; i++) empty[i] = null;
            return empty;
        }

        int seed = SaveManager.TodayYMD() ^ SafeHash(GetStablePlayerKey());
        var rng = new System.Random(seed);

        var bag = pool
            .Where(m => m && !string.IsNullOrEmpty(m.id))
            .GroupBy(m => m.id)
            .Select(g => g.First())
            .ToList();

        var picks = new List<MonsterDataSO>(needed);
        var usedTypes = new HashSet<MonsterType>();

        MonsterDataSO WeightedPick(List<MonsterDataSO> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;

            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
                total += Math.Max(0, candidates[i].starterWeight);

            double roll = rng.NextDouble() * Math.Max(0.0001, total);
            float acc = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                acc += Math.Max(0, candidates[i].starterWeight);
                if (roll <= acc) return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        for (int i = 0; i < needed && bag.Count > 0; i++)
        {
            var typeFiltered = bag.Where(m => !usedTypes.Contains(m.type)).ToList();
            var pick = WeightedPick(typeFiltered.Count > 0 ? typeFiltered : bag);
            if (pick == null) break;

            picks.Add(pick);
            usedTypes.Add(pick.type);
            bag.RemoveAll(m => ReferenceEquals(m, pick) || m.id == pick.id);
        }

        while (picks.Count < needed) picks.Add(null);
        return picks.ToArray();
    }

    string GetStablePlayerKey()
    {
        var key = (SaveManager.Data != null && !string.IsNullOrEmpty(SaveManager.Data.playerId))
                  ? SaveManager.Data.playerId
                  : SystemInfo.deviceUniqueIdentifier;
        return string.IsNullOrEmpty(key) ? "fallback" : key;
    }

    int SafeHash(string s)
    {
        unchecked
        {
            int h = 23;
            for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
            return h;
        }
    }
}
