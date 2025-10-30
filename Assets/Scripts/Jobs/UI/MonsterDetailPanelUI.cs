using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public enum MonsterDetailMode
{
    StarterSelect,
    AssignToTeam
}

public class MonsterDetailPanelUI : MonoBehaviour
{
    [Header("Panel Routing")]
    [SerializeField] private PanelId selfPanelId = PanelId.None;

    [Header("Theme")]
    [SerializeField, Range(0f, 1f)] private float rarityBackgroundAlpha = 0.18f;

    [Header("Refs")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI idText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI typeText;
    [SerializeField] private TextMeshProUGUI rarityText;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI atkText;
    [SerializeField] private TextMeshProUGUI spdText;
    [SerializeField] private TextMeshProUGUI defText;
    [SerializeField] private TextMeshProUGUI lvlText;
    [SerializeField] private TextMeshProUGUI evoText;
    [SerializeField] private TextMeshProUGUI descText;
    [SerializeField] private TextMeshProUGUI jobSiteText;

    [Header("Starter Buttons (Confirm/Cancel)")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    [Header("Visibility")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Titles (Tag Button hosts TitleButtonUI)")]
    [SerializeField] private TitleButtonUI titleButton; // Drag the Tag button's TitleButtonUI here

    [Header("Type Matchups (Icons Only)")]
    [SerializeField] private Transform strongIconHolder;
    [SerializeField] private Transform weakIconHolder;
    [SerializeField] private GameObject typeIconPrefab;
    [SerializeField] private TypeIconLibrary typeIconLibrary;

    // ===== Mode Holders =====
    [Header("Mode Holders")]
    [SerializeField] private GameObject starterButtonsHolder;
    [SerializeField] private GameObject slotButtonsHolder;
    [SerializeField] private GameObject teamHolder;

    [Header("Slot Buttons")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button slot1Button;
    [SerializeField] private Button slot2Button;
    [SerializeField] private Button slot3Button;

    // ===== Team Holder Buttons =====
    [Header("Team Holder Buttons")]
    [SerializeField] private Button removeButton;

    // ===== Build Safe Mode (Isolation) =====
    [Header("Build Safe Mode (Isolation)")]
    [SerializeField] private bool safeSkipStats = true;
    [SerializeField] private bool safeSkipEvolution = false;
    [SerializeField] private bool safeSkipTypeIcons = true;
    [SerializeField] private bool safeSkipDescription = true;
    [SerializeField] private bool safeSkipMonsterIcon = true;
    [SerializeField] private bool buildVerboseLogging;

    enum RenderStage { None, Header, StatsEvo, Description, TypeIcons, Done }
    RenderStage _stage = RenderStage.None;
    Coroutine _stageCR;

    private MonsterDetailMode _mode = MonsterDetailMode.StarterSelect;
    private MonsterDataSO current;
    private OwnedMonsterData _currentOwned;
    private Action<MonsterDataSO> onConfirm;
    private Action onCancel;

    // Team context
    private int _teamSlotIndex = -1;
    private Action _onRemoved;

    bool _visible;
    static bool _openingOrOpen;
    Coroutine _openRoutine;

    static readonly Dictionary<MonsterType, Color> TYPE_COLORS = new()
    {
        { MonsterType.Fire,     new Color32(230, 74,  25,255) },
        { MonsterType.Water,    new Color32( 30,136, 229,255) },
        { MonsterType.Grass,    new Color32( 56,142,  60,255) },
        { MonsterType.Electric, new Color32(255,193,   7,255) },
        { MonsterType.Ice,      new Color32( 79,195, 247,255) },
        { MonsterType.Clash,    new Color32(121, 85,  72,255) },
        { MonsterType.Corrupt,  new Color32(156, 39, 176,255) },
        { MonsterType.Ground,   new Color32(141,110,  99,255) },
        { MonsterType.Sky,      new Color32( 63, 81, 181,255) },
        { MonsterType.Oracle,   new Color32(  0,150, 136,255) },
        { MonsterType.Bug,      new Color32(104,159,  56,255) },
        { MonsterType.Rock,     new Color32(120,144, 156,255) },
        { MonsterType.Specter,  new Color32(103, 58, 183,255) },
        { MonsterType.Wyrm,     new Color32(255,112,  67,255) },
        { MonsterType.Umbral,   new Color32( 97, 97,  97,255) },
        { MonsterType.Alloy,    new Color32(158,158, 158,255) },
    };

    static readonly Dictionary<Rarity, Color> RARITY_COLORS = new()
    {
        { Rarity.Common,    new Color32(176,176,176,255) },
        { Rarity.Uncommon,  new Color32( 76,175, 80,255) },
        { Rarity.Rare,      new Color32( 33,150,243,255) },
        { Rarity.Epic,      new Color32(156, 39,176,255) },
        { Rarity.Legendary, new Color32(255,152,  0,255) },
        { Rarity.Mythic,    new Color32(255,235, 59,255) },
    };

    static readonly Dictionary<JobType, HashSet<MonsterType>> BEST_TYPES = new()
    {
        { JobType.Gym,          new() { MonsterType.Clash } },
        { JobType.Quarry,       new() { MonsterType.Ground, MonsterType.Rock } },
        { JobType.Mine,         new() { MonsterType.Rock } },
        { JobType.PowerPlant,   new() { MonsterType.Electric } },
        { JobType.Grove,        new() { MonsterType.Grass, MonsterType.Bug } },
        { JobType.Forge,        new() { MonsterType.Fire, MonsterType.Alloy } },
        { JobType.Workshop,     new() { MonsterType.Alloy } },
        { JobType.Harbor,       new() { MonsterType.Water, MonsterType.Sky } },
        { JobType.CryoLab,      new() { MonsterType.Ice } },
        { JobType.Observatory,  new() { MonsterType.Oracle } },
        { JobType.Containment,  new() { MonsterType.Corrupt } },
        { JobType.WyrmDen,      new() { MonsterType.Wyrm } },
        { JobType.ShadowMarket, new() { MonsterType.Umbral, MonsterType.Specter } },
        { JobType.Sanctum,      new() { MonsterType.Specter } },
        { JobType.Clinic,       new() { MonsterType.Sky } },
    };

    void Awake()
    {
        if (confirmButton) { confirmButton.onClick.RemoveAllListeners(); confirmButton.onClick.AddListener(Confirm); }
        if (cancelButton)  { cancelButton.onClick.RemoveAllListeners();  cancelButton.onClick.AddListener(Cancel);  }
        if (closeButton)   { closeButton.onClick.RemoveAllListeners();   closeButton.onClick.AddListener(Hide);     }
        if (slot1Button) { slot1Button.onClick.RemoveAllListeners(); slot1Button.onClick.AddListener(() => AssignToSlot(0)); }
        if (slot2Button) { slot2Button.onClick.RemoveAllListeners(); slot2Button.onClick.AddListener(() => AssignToSlot(1)); }
        if (slot3Button) { slot3Button.onClick.RemoveAllListeners(); slot3Button.onClick.AddListener(() => AssignToSlot(2)); }
        if (removeButton) { removeButton.onClick.RemoveAllListeners(); removeButton.onClick.AddListener(RemoveFromTeam); }

        ResolveTitleButton();

        // Keep the title button text hot while panel lives
        TitleAssignPanelUI.OnTitlesChanged -= HandleTitlesChanged;
        TitleAssignPanelUI.OnTitlesChanged += HandleTitlesChanged;
    }

    void OnDisable() => ResetVisualsImmediate();

    private void OnDestroy()
    {
        TitleAssignPanelUI.OnTitlesChanged -= HandleTitlesChanged;
    }

    // ─────────────────────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────────────────────
    public void Show(MonsterDataSO monster, Action<MonsterDataSO> onConfirmCallback, Action onCancelCallback = null)
    {
        _mode = MonsterDetailMode.StarterSelect;
        _currentOwned = default;
        _teamSlotIndex = -1;
        _onRemoved = null;

        current   = monster;
        onConfirm = onConfirmCallback;
        onCancel  = onCancelCallback;

        // Hide title button in starter flow
        if (titleButton) titleButton.gameObject.SetActive(false);

        SafeOpen(monster);
    }

    public void ShowStarter(MonsterDataSO monster, Action<MonsterDataSO> onConfirmCallback, Action onCancelCallback = null)
    {
        Show(monster, onConfirmCallback, onCancelCallback);
    }

    public void ShowAssign(OwnedMonsterData owned)
    {
        if (owned == null || string.IsNullOrEmpty(owned.monsterId)) return;

        _mode = MonsterDetailMode.AssignToTeam;
        _teamSlotIndex = -1;
        _onRemoved = null;

        _currentOwned = new OwnedMonsterData
        {
            monsterId = owned.monsterId,
            level     = owned.level,
            currentHP = owned.currentHP,
            currentXP = owned.currentXP
        };

        current = MonsterLibraryLocator.GetById(owned.monsterId);
        onConfirm = null;
        onCancel  = null;

        UpdateTitleButtonBinding(); // <— bind the Tag/Title button
        SafeOpen(current);
    }

    public void ShowTeamMember(int slotIndex, OwnedMonsterData member, Action onRemoved)
    {
        if (member == null || string.IsNullOrEmpty(member.monsterId)) return;

        _mode = MonsterDetailMode.AssignToTeam;
        _teamSlotIndex = Mathf.Clamp(slotIndex, 0, 2);
        _onRemoved = onRemoved;

        _currentOwned = new OwnedMonsterData
        {
            monsterId = member.monsterId,
            level     = member.level,
            currentHP = member.currentHP,
            currentXP = member.currentXP
        };

        current = MonsterLibraryLocator.GetById(member.monsterId);
        onConfirm = null;
        onCancel  = null;

        UpdateTitleButtonBinding(); // <— bind the Tag/Title button
        SafeOpen(current);
    }

    public void Hide()
    {
        // Clear reentrancy guard so a quick re-open works.
        _openingOrOpen = false;

        TryStep("Hide", () =>
        {
            if (_openRoutine != null)
            {
                StopCoroutine(_openRoutine);
                _openRoutine = null;
            }

            if (canvasGroup)
            {
                LeanTween.alphaCanvas(canvasGroup, 0f, 0.12f).setOnComplete(() =>
                {
                    CloseSelf();
                    ResetVisualsImmediate();
                });
            }
            else
            {
                CloseSelf();
                ResetVisualsImmediate();
            }
        });

        if (closeButton) closeButton.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // Open flow (guard + next frame)
    // ─────────────────────────────────────────────────────────────
    void SafeOpen(MonsterDataSO monster)
    {
        if (monster == null) return;

        // ensure object is active before coroutines
        if (!gameObject.activeInHierarchy)
        {
            if (canvasGroup) canvasGroup.alpha = 0f; // prevent flash
            OpenSelf();
        }

        if (_openRoutine != null) { StopCoroutine(_openRoutine); _openRoutine = null; }
        if (_stageCR != null)     { StopCoroutine(_stageCR);     _stageCR = null;     }

        _stage = RenderStage.Header;        // start staged flow
        _stageCR = StartCoroutine(CoRenderStaged(monster));
    }

    IEnumerator CoOpenNextFrame(MonsterDataSO monster)
    {
        yield return null; // let other UI events settle

        try
        {
            RenderAllAndOpen(monster);
        }
        catch (Exception e)
        {
            Debug.LogError($"[MonsterDetailPanelUI] Show failed for '{monster?.id}': {e}");
            _openingOrOpen = false;
        }
        finally
        {
            _openRoutine = null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Internal render + open
    // ─────────────────────────────────────────────────────────────
    void RenderAllAndOpen(MonsterDataSO monster)
    {
        LogStep("BEGIN Show");

        bool isAssign    = (_mode == MonsterDetailMode.AssignToTeam);
        bool isTeamView  = isAssign && _teamSlotIndex >= 0;
        bool isOwnedPick = isAssign && _teamSlotIndex < 0;

        if (starterButtonsHolder) starterButtonsHolder.SetActive(!isAssign);
        if (slotButtonsHolder)    slotButtonsHolder.SetActive(isOwnedPick);
        if (teamHolder)           teamHolder.SetActive(isTeamView);

        if (closeButton) closeButton.gameObject.SetActive(isAssign);

        TryStep("Header & Static Fields", () =>
        {
            if (!safeSkipMonsterIcon && icon) icon.sprite = monster ? monster.icon : null;
            if (idText)   idText.text = monster ? $"ID: {monster.id}" : "ID: -";
            if (nameText) nameText.text = monster ? monster.displayName : "-";
        });

        TryStep("Type & Rarity", () =>
        {
            if (typeText)
            {
                string labelHex = "FFFFFF";
                string typeName = monster ? monster.type.ToString() : "-";
                string typeHex  = "CCCCCC";
                if (monster && TYPE_COLORS.TryGetValue(monster.type, out var tc))
                    typeHex = ColorUtility.ToHtmlStringRGB(tc);

                typeText.color = Color.white;
                typeText.richText = true;
                typeText.text = $"<color=#{labelHex}>TYPE:</color> <color=#{typeHex}>{typeName}</color>";
            }

            if (rarityText)
            {
                rarityText.text = monster ? $"{monster.rarity}" : "-";
                if (monster && RARITY_COLORS.TryGetValue(monster.rarity, out var rc))
                {
                    rarityText.color = rc;
                    ApplyRarityBackground(rc);
                }
                else
                {
                    ApplyRarityBackground(Color.white);
                }
            }
        });

        TryStep("Stats & Evo", () =>
        {
            int dispLvl = GetDisplayLevel();

            int hpL   = current ? Mathf.RoundToInt(BattleCalc.CalcHP(current, dispLvl)) : 0;
            int atkL  = current ? Mathf.RoundToInt(BattleCalc.CalcBaseAttack(current, dispLvl, 0, 0)) : 0;
            int defL  = current ? Mathf.RoundToInt(current.baseDefense) : 0;
            float spd = current ? current.baseSpeed : 0f;

            if (lvlText) lvlText.text = $"LVL: {dispLvl}";
            if (hpText)  hpText.text  = $"HP: {hpL}";
            if (atkText) atkText.text = $"ATK: {atkL}";
            if (defText) defText.text = current ? $"DEF: {defL}" : "DEF: —";
            if (spdText) spdText.text = $"SPD: {spd:0.##}";

            if (evoText) evoText.text = BuildEvolutionLine(current);
        });

        TryStep("Description", () =>
        {
            if (!safeSkipDescription && descText)
                descText.text = monster ? monster.description : "";
            else if (descText) descText.text = "";
        });

        TryStep("Job Sites", () =>
        {
            if (jobSiteText)
            {
                var jobs = SitesForType(monster ? monster.type : default);
                jobSiteText.text = jobs.Count > 0
                    ? $"Job Site: {string.Join(", ", jobs.Select(NiceJobName))}"
                    : "Job Site: —";
            }
        });

        TryStep("Type Matchup Icons", () =>
        {
            if (safeSkipTypeIcons || monster == null)
            {
                ClearIcons(strongIconHolder);
                ClearIcons(weakIconHolder);
                return;
            }

            List<MonsterType> strong = null;
            List<MonsterType> weak   = null;

            try { strong = BattleTypeChart.GetStrongAgainst(monster.type); }
            catch (Exception ex)
            {
                Debug.LogError($"[DetailPanel] GetStrongAgainst({monster.type}) failed: {ex}");
            }

            try { weak = BattleTypeChart.GetWeakAgainst(monster.type); }
            catch (Exception ex)
            {
                Debug.LogError($"[DetailPanel] GetWeakAgainst({monster.type}) failed: {ex}");
            }

            ClearIcons(strongIconHolder);
            ClearIcons(weakIconHolder);

            if (strong != null)
                foreach (var t in strong) CreateTypeIcon(t, strongIconHolder, true);
            if (weak != null)
                foreach (var t in weak)   CreateTypeIcon(t,   weakIconHolder, true);
        });

        // ──────────────── Open & Fade ────────────────
        TryStep("Open & Fade", () =>
        {
            OpenSelf();

            if (canvasGroup)
            {
                if (canvasGroup.alpha < 1f)
                    LeanTween.alphaCanvas(canvasGroup, 1f, 0.15f);
            }
        });

        LogStep("END Show");
    }

    // ─────────────────────────────────────────────────────────────
    // Button handlers
    // ─────────────────────────────────────────────────────────────
    void Confirm()
    {
        TryStep("Confirm", () =>
        {
            if (_mode == MonsterDetailMode.AssignToTeam)
            {
                Hide();
                return;
            }

            if (current == null) { Hide(); return; }
            var cb = onConfirm;
            Hide();
            cb?.Invoke(current);
        });
    }

    void Cancel()
    {
        TryStep("Cancel", () =>
        {
            onCancel?.Invoke();
            Hide();
        });
    }

    void AssignToSlot(int slotIndex)
    {
        if (_mode != MonsterDetailMode.AssignToTeam || _currentOwned == null || string.IsNullOrEmpty(_currentOwned.monsterId))
        {
            Hide();
            return;
        }

        var team = SaveManager.Data.team ?? new List<OwnedMonsterData>();
        while (team.Count < 3) team.Add(new OwnedMonsterData());

        team[slotIndex] = new OwnedMonsterData
        {
            monsterId = _currentOwned.monsterId,
            level     = _currentOwned.level,
            currentHP = -1,
            currentXP = _currentOwned.currentXP
        };

        SaveManager.Data.team = team;
        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();

        Hide();
    }

    void RemoveFromTeam()
    {
        if (_teamSlotIndex < 0) { Hide(); return; }

        var team = SaveManager.Data.team ?? new List<OwnedMonsterData>();
        while (team.Count < 3) team.Add(new OwnedMonsterData());

        team[_teamSlotIndex] = new OwnedMonsterData();

        SaveManager.Data.team = team;
        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();

        _onRemoved?.Invoke();
        Hide();
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────
    List<JobType> SitesForType(MonsterType t)
    {
        var outList = new List<JobType>(2);
        foreach (var kv in BEST_TYPES)
            if (kv.Value.Contains(t)) outList.Add(kv.Key);
        return outList;
    }

    string NiceJobName(JobType jt)
    {
        var s = jt.ToString();
        for (int i = 1; i < s.Length; i++)
            if (char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) s = s.Insert(i, " ");
        return s;
    }

    void OpenSelf()
    {
        if (_visible) return;

        if (UIManager.I && selfPanelId != PanelId.None)
            UIManager.I.Show(selfPanelId);
        else
            gameObject.SetActive(true);

        _visible = true;
    }

    void CloseSelf()
    {
        if (!_visible) { _openingOrOpen = false; return; }

        if (UIManager.I && selfPanelId != PanelId.None)
            UIManager.I.Hide(selfPanelId);
        else
            gameObject.SetActive(false);

        _visible = false;
        _openingOrOpen = false; // clear reentrancy guard
    }

    void ApplyRarityBackground(Color baseColor)
    {
        if (!backgroundImage) return;
        var c = baseColor; c.a = rarityBackgroundAlpha;
        backgroundImage.color = c;
    }

    void ResetVisualsImmediate()
    {
        if (canvasGroup) canvasGroup.alpha = 0f;
        current   = null;
        onConfirm = null;
        onCancel  = null;
        _currentOwned = null;
        _mode = MonsterDetailMode.StarterSelect;

        _teamSlotIndex = -1;
        _onRemoved = null;

        if (icon) icon.sprite = null;
        if (jobSiteText) jobSiteText.text = string.Empty;

        ClearIcons(strongIconHolder);
        ClearIcons(weakIconHolder);

        if (starterButtonsHolder) starterButtonsHolder.SetActive(false);
        if (slotButtonsHolder)    slotButtonsHolder.SetActive(false);
        if (teamHolder)           teamHolder.SetActive(false);

        if (lvlText) lvlText.text = string.Empty;

        if (backgroundImage) backgroundImage.color = Color.white;

        if (!UIManager.I) gameObject.SetActive(false);

        // Hide the Tag/Title button on reset
        if (titleButton) titleButton.gameObject.SetActive(false);

        _visible = UIManager.I && selfPanelId != PanelId.None ? _visible : false;
    }

    void ShowTypeMatchups(MonsterType type)
    {
        if (!strongIconHolder || !weakIconHolder || !typeIconPrefab) return;

        ClearIcons(strongIconHolder);
        ClearIcons(weakIconHolder);

        if (type == default) return;

        var strong = BattleTypeChart.GetStrongAgainst(type);
        var weak   = BattleTypeChart.GetWeakAgainst(type);

        foreach (var t in strong) CreateTypeIcon(t, strongIconHolder, true);
        foreach (var t in weak)   CreateTypeIcon(t,   weakIconHolder, true);
    }

    void ClearIcons(Transform holder)
    {
        if (!holder) return;
        for (int i = holder.childCount - 1; i >= 0; i--)
            Destroy(holder.GetChild(i).gameObject);
    }

    void CreateTypeIcon(MonsterType type, Transform parent, bool animate)
    {
        var go = Instantiate(typeIconPrefab, parent);
        var img = go.GetComponent<Image>();
        if (img && typeIconLibrary) img.sprite = typeIconLibrary.GetIcon(type);

        if (animate)
        {
            go.transform.localScale = Vector3.zero;
            LeanTween.scale(go, Vector3.one, 0.15f).setEaseOutBack();
        }
    }

    string BuildEvolutionLine(MonsterDataSO m)
    {
        if (!m) return "EVO: —";

        if (!m.evolutionForm || m.evolutionLevel <= 0)
            return "EVO: —";

        string nextName = m.evolutionForm ? m.evolutionForm.displayName : "???";
        int lvl = Mathf.Max(1, m.evolutionLevel);

        int curHpAtEvo  = Mathf.RoundToInt(BattleCalc.CalcHP(m, lvl));
        int nxtHpAtEvo  = Mathf.RoundToInt(BattleCalc.CalcHP(m.evolutionForm, lvl));
        int curAtkAtEvo = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(m, lvl, 0, 0));
        int nxtAtkAtEvo = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(m.evolutionForm, lvl, 0, 0));

        int dHp  = nxtHpAtEvo  - curHpAtEvo;
        int dAtk = nxtAtkAtEvo - curAtkAtEvo;

        string deltas = $" (+{dHp} HP, +{dAtk} ATK)";
        return $"EVO: Lv {lvl} → {nextName}{(dHp > 0 || dAtk > 0 ? deltas : "")}";
    }

    void TryStep(string label, Action step)
    {
        try
        {
            LogStep(label + " START");
            step?.Invoke();
            LogStep(label + " END");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MonsterDetailPanelUI] Exception at step '{label}' (monster={(current ? current.id : "null")}): {ex}");
        }
    }

    void LogStep(string msg)
    {
        if (!buildVerboseLogging) return;
        Debug.Log($"[MonsterDetailPanelUI] {msg} (monster={(current ? current.id : "null")})");
    }

    int GetDisplayLevel()
    {
        if (_mode == MonsterDetailMode.AssignToTeam && _currentOwned != null && _currentOwned.level > 0)
            return _currentOwned.level;
        return 1;
    }

    IEnumerator CoRenderStaged(MonsterDataSO monster)
    {
        // FRAME 1: header only (zero risk)
        if (_stage == RenderStage.Header)
        {
            TryStep("Header & Static Fields", () =>
            {
                if (!safeSkipMonsterIcon && icon) icon.sprite = monster ? monster.icon : null;
                if (idText) idText.text = monster ? $"ID: {monster.id}" : "ID: -";
                if (nameText) nameText.text = monster ? monster.displayName : "-";
                if (typeText)
                {
                    string typeName = monster ? monster.type.ToString() : "-";
                    string typeHex = "CCCCCC";
                    if (monster && TYPE_COLORS.TryGetValue(monster.type, out var tc))
                        typeHex = ColorUtility.ToHtmlStringRGB(tc);
                    typeText.color = Color.white;
                    typeText.richText = true;
                    typeText.text = $"<color=#FFFFFF>TYPE:</color> <color=#{typeHex}>{typeName}</color>";
                }
                if (rarityText)
                {
                    rarityText.text = monster ? $"{monster.rarity}" : "-";
                    if (monster && RARITY_COLORS.TryGetValue(monster.rarity, out var rc))
                    {
                        rarityText.color = rc; ApplyRarityBackground(rc);
                    }
                    else ApplyRarityBackground(Color.white);
                }
                // holder visibility
                bool isAssign    = (_mode == MonsterDetailMode.AssignToTeam);
                bool isTeamView  = isAssign && _teamSlotIndex >= 0;
                bool isOwnedPick = isAssign && _teamSlotIndex < 0;
                if (starterButtonsHolder) starterButtonsHolder.SetActive(!isAssign);
                if (slotButtonsHolder)    slotButtonsHolder.SetActive(isOwnedPick);
                if (teamHolder)           teamHolder.SetActive(isTeamView);
                if (closeButton)          closeButton.gameObject.SetActive(isAssign);

                // Bind/mask Title button based on mode/ownership
                UpdateTitleButtonBinding();

                OpenSelf();
                if (canvasGroup) LeanTween.alphaCanvas(canvasGroup, 1f, 0.15f);
            });
            _stage = RenderStage.StatsEvo;
            yield return null;
        }

        // FRAME 2: stats/evo (guarded)
        if (_stage == RenderStage.StatsEvo)
        {
            TryStep("Stats & Evo", () =>
            {
                int dispLvl = GetDisplayLevel();
                if (lvlText) lvlText.text = $"LVL: {dispLvl}";

                if (!safeSkipStats && current)
                {
                    int hpL = 0, atkL = 0, defL = Mathf.RoundToInt(current.baseDefense); float spd = current.baseSpeed;
                    try { hpL = Mathf.RoundToInt(BattleCalc.CalcHP(current, dispLvl)); } catch (Exception e) { Debug.LogError($"HP calc {current?.id}: {e}"); }
                    try { atkL = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(current, dispLvl, 0, 0)); } catch (Exception e) { Debug.LogError($"ATK calc {current?.id}: {e}"); }
                    if (hpText)  hpText.text  = $"HP: {hpL}";
                    if (atkText) atkText.text = $"ATK: {atkL}";
                    if (defText) defText.text = $"DEF: {defL}";
                    if (spdText) spdText.text = $"SPD: {spd:0.##}";
                }
                else
                {
                    if (hpText)  hpText.text  = "HP: —";
                    if (atkText) atkText.text = "ATK: —";
                    if (defText) defText.text = current ? $"DEF: {Mathf.RoundToInt(current.baseDefense)}" : "DEF: —";
                    if (spdText) spdText.text = current ? $"SPD: {current.baseSpeed:0.##}" : "SPD: —";
                }

                if (evoText) evoText.text = (!safeSkipEvolution) ? BuildEvolutionLine(current) : "EVO: —";
            });
            _stage = RenderStage.Description;
            yield return null;
        }

        // FRAME 3: description (TMP worst-case)
        if (_stage == RenderStage.Description)
        {
            TryStep("Description", () =>
            {
                if (!safeSkipDescription && descText)
                    descText.text = current ? current.description : "";
                else if (descText) descText.text = "";
            });
            _stage = RenderStage.TypeIcons;
            yield return null;
        }

        // FRAME 4: type icons (sprite lookups)
        if (_stage == RenderStage.TypeIcons)
        {
            TryStep("Type Matchup Icons", () =>
            {
                ClearIcons(strongIconHolder);
                ClearIcons(weakIconHolder);
                if (safeSkipTypeIcons || current == null) return;

                List<MonsterType> strong = null, weak = null;
                try { strong = BattleTypeChart.GetStrongAgainst(current.type); } catch (Exception ex) { Debug.LogError($"Strong({current.type}) fail: {ex}"); }
                try { weak = BattleTypeChart.GetWeakAgainst(current.type); } catch (Exception ex) { Debug.LogError($"Weak({current.type}) fail: {ex}"); }

                if (strong != null) foreach (var t in strong) CreateTypeIcon(t, strongIconHolder, true);
                if (weak   != null) foreach (var t in weak)   CreateTypeIcon(t, weakIconHolder, true);
            });
            _stage = RenderStage.Done;
        }

        LogStep("END Show (staged)");
        _stageCR = null;
    }


    // ─────────────────────────────────────────────────────────────
    // Titles wiring
    // ─────────────────────────────────────────────────────────────
    private void ResolveTitleButton()
    {
        if (titleButton) return;
        titleButton = GetComponentInChildren<TitleButtonUI>(true);
    }

    private void UpdateTitleButtonBinding()
    {
        if (!titleButton) return;

        bool canBind = (_mode == MonsterDetailMode.AssignToTeam)
                       && _currentOwned != null
                       && !string.IsNullOrEmpty(_currentOwned.monsterId)
                       && current != null;

        titleButton.gameObject.SetActive(canBind);
        if (!canBind) return;

        int lvl = GetDisplayLevel();
        titleButton.Bind(_currentOwned.monsterId, current, lvl);
        titleButton.RefreshLabel(); // ensure label shows titleId or UNEMPLOYED immediately
    }

    private void HandleTitlesChanged(string ownedId)
    {
        // Only refresh if this panel is showing the same owned monster
        if (_currentOwned != null && !string.IsNullOrEmpty(_currentOwned.monsterId) && _currentOwned.monsterId == ownedId)
        {
            UpdateTitleButtonBinding();
        }
    }

    
}
