using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem; // NEW (Input System package)

public enum MonsterDetailMode
{
    StarterSelect,
    AssignToTeam,
    CodexView
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

    [Header("Personality")]
    [SerializeField] private TextMeshProUGUI personalityNameText;
    [SerializeField] private Button personalityInfoButton;

    [Header("Starter Buttons (Confirm/Cancel)")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    [Header("Visibility")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Titles (Tag Button hosts TitleButtonUI)")]
    [SerializeField] private TitleButtonUI titleButton;

    [Header("Type Matchups (Icons Only)")]
    [SerializeField] private Transform strongIconHolder;
    [SerializeField] private Transform weakIconHolder;
    [SerializeField] private GameObject typeIconPrefab;
    [SerializeField] private TypeIconLibrary typeIconLibrary;

    [Header("Mode Holders")]
    [SerializeField] private GameObject starterButtonsHolder;
    [SerializeField] private GameObject slotButtonsHolder;
    [SerializeField] private GameObject teamHolder;

    [Header("Slot Buttons")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button slot1Button;
    [SerializeField] private Button slot2Button;
    [SerializeField] private Button slot3Button;

    [Header("Team Holder Buttons")]
    [SerializeField] private Button removeButton;

    [Header("Evolution")]
    [Tooltip("Shown in AssignToTeam mode when this owned monster can evolve.")]
    [SerializeField] private Button evolveButton;
    [SerializeField] private EvolutionPanelUI evolutionPanel;

    [Header("Favorites")]
    [Tooltip("Shown when Codex_Favorites is unlocked. Toggles this monster as a favorite.")]
    [SerializeField] private Button favoriteButton;
    [SerializeField] private GameObject favoriteOnIcon;

    [Header("Shiny Variant Toggle (Codex)")]
    [Tooltip("Optional. If wired, shows a toggle in Codex detail view when you own BOTH the normal and shiny variant.")]
    [SerializeField] private GameObject shinyVariantRoot;

    [Tooltip("Optional. Clicking toggles between Normal and Shiny view (Codex only).")]
    [SerializeField] private Button shinyVariantToggleButton;

    [Tooltip("Optional. Label for the toggle button (e.g., 'View Shiny' / 'View Normal').")]
    [SerializeField] private TextMeshProUGUI shinyVariantToggleLabel;

    [Header("Stats View Toggle")]
    [Tooltip("Optional: button that toggles Base Stats vs Adjusted Stats.")]
    [SerializeField] private Button statsViewToggleButton;

    [Tooltip("Optional: label that shows current view (BASE / ADJ).")]
    [SerializeField] private TextMeshProUGUI statsViewToggleLabel;

    [Tooltip("If true, panel starts in Base stats view.")]
    [SerializeField] private bool startInBaseStatsView = false;

    [Header("Build Safe Mode (Isolation)")]
    [SerializeField] private bool safeSkipStats = true;
    [SerializeField] private bool safeSkipEvolution = false;
    [SerializeField] private bool safeSkipTypeIcons = true;
    [SerializeField] private bool safeSkipDescription = true;
    [SerializeField] private bool safeSkipMonsterIcon = true;
    [SerializeField] private bool buildVerboseLogging;

    [Header("Swipe Browse (Codex / Starter)")]
    [Tooltip("Enable swipe-to-browse in Codex and Starter detail views.")]
    [SerializeField] private bool enableSwipeBrowse = true;

    [Tooltip("Swipe distance in pixels before a browse triggers.")]
    [SerializeField] private float swipeMinPixels = 90f;

    [Tooltip("Max vertical deviation allowed for a horizontal swipe (pixels).")]
    [SerializeField] private float swipeMaxVerticalPixels = 120f;

    [Tooltip("Cooldown between browse actions (seconds).")]
    [SerializeField] private float swipeBrowseCooldown = 0.20f;

    private enum RenderStage { None, Header, StatsEvo, Description, TypeIcons, Done }
    private RenderStage _stage = RenderStage.None;
    private Coroutine _stageCR;

    private MonsterDetailMode _mode = MonsterDetailMode.StarterSelect;
    private MonsterDataSO current;
    private OwnedMonsterData _currentOwned;
    private Action<MonsterDataSO> onConfirm;
    private Action onCancel;

    private int _teamSlotIndex = -1;
    private Action _onRemoved;

    private bool _visible;
    private OwnedMonsterData _preferredOwned;
    private OwnedMonsterData _otherVariantOwned;

    

    // Stats source should remain stable; shiny is cosmetic-only.
    private OwnedMonsterData _statsOwned;

    // Cosmetic view flag (drives icon/name only)
    private bool _viewShinyCosmetic;
// Codex shiny/normal view state (variant toggle)
    private bool _codexHasNormal;
    private bool _codexHasShiny;
    private bool _codexViewingShiny;

    // Browse session (Codex/Starter swipe)
    private IReadOnlyList<MonsterDataSO> _browseDefs;
    private int _browseIndex = -1;
    private bool _browseWrap = true;

    private bool _swipeTracking;
    private Vector2 _swipeStartPos;
    private float _lastBrowseAt;

    private bool _showBaseStats;
    private const string TRAINING_GREEN = "#3CDE74";

    private static readonly Dictionary<MonsterType, Color> TYPE_COLORS = new Dictionary<MonsterType, Color>()
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

    private static readonly Dictionary<Rarity, Color> RARITY_COLORS = new Dictionary<Rarity, Color>()
    {
        { Rarity.Common,    new Color32(176,176,176,255) },
        { Rarity.Uncommon,  new Color32( 76,175, 80,255) },
        { Rarity.Rare,      new Color32( 33,150,243,255) },
        { Rarity.Epic,      new Color32(156, 39,176,255) },
        { Rarity.Legendary, new Color32(255,152,  0,255) },
        { Rarity.Mythic,    new Color32(255,235, 59,255) },
    };

    private void Awake()
    {
        _showBaseStats = startInBaseStatsView;

        if (confirmButton) { confirmButton.onClick.RemoveAllListeners(); confirmButton.onClick.AddListener(Confirm); }
        if (cancelButton) { cancelButton.onClick.RemoveAllListeners(); cancelButton.onClick.AddListener(Cancel); }
        if (closeButton) { closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(Hide); }
        if (slot1Button) { slot1Button.onClick.RemoveAllListeners(); slot1Button.onClick.AddListener(() => AssignToSlot(0)); }
        if (slot2Button) { slot2Button.onClick.RemoveAllListeners(); slot2Button.onClick.AddListener(() => AssignToSlot(1)); }
        if (slot3Button) { slot3Button.onClick.RemoveAllListeners(); slot3Button.onClick.AddListener(() => AssignToSlot(2)); }
        if (removeButton) { removeButton.onClick.RemoveAllListeners(); removeButton.onClick.AddListener(RemoveFromTeam); }

        if (evolveButton)
        {
            evolveButton.onClick.RemoveAllListeners();
            evolveButton.onClick.AddListener(OnClickEvolve);
        }

        if (favoriteButton)
        {
            favoriteButton.onClick.RemoveAllListeners();
            favoriteButton.onClick.AddListener(OnClickFavorite);
        }

        if (personalityInfoButton)
        {
            personalityInfoButton.onClick.RemoveAllListeners();
            personalityInfoButton.onClick.AddListener(OpenPersonalityInfo);
        }

        if (statsViewToggleButton)
        {
            statsViewToggleButton.onClick.RemoveAllListeners();
            statsViewToggleButton.onClick.AddListener(ToggleStatsView);
        }

        if (shinyVariantToggleButton)
        {
            shinyVariantToggleButton.onClick.RemoveAllListeners();
            shinyVariantToggleButton.onClick.AddListener(ToggleCodexShinyVariant);
        }

        RefreshStatsViewToggleLabel();
        ResolveTitleButton();

        TitleAssignPanelUI.OnTitlesChanged -= HandleTitlesChanged;
        TitleAssignPanelUI.OnTitlesChanged += HandleTitlesChanged;

        GameEvents.MonsterLeveled -= HandleMonsterLeveled;
        GameEvents.MonsterLeveled += HandleMonsterLeveled;
        GameEvents.MonsterEvolved -= HandleMonsterEvolved;
        GameEvents.MonsterEvolved += HandleMonsterEvolved;
    }

    private void OnDisable() => ResetVisualsImmediate();

    private void OnDestroy()
    {
        TitleAssignPanelUI.OnTitlesChanged -= HandleTitlesChanged;
        GameEvents.MonsterLeveled -= HandleMonsterLeveled;
        GameEvents.MonsterEvolved -= HandleMonsterEvolved;
    }

    private void Update()
    {
        if (!_visible) return;
        if (!enableSwipeBrowse) return;

        bool canSwipe =
            (_mode == MonsterDetailMode.CodexView) ||
            (_mode == MonsterDetailMode.StarterSelect && _browseDefs != null && _browseDefs.Count > 1);

        if (!canSwipe) return;

        HandleSwipeInput();
    }

    // ─────────────────────────────────────────────────────────────
    // Stats toggle
    // ─────────────────────────────────────────────────────────────

    private void ToggleStatsView()
    {
        _showBaseStats = !_showBaseStats;
        RefreshStatsViewToggleLabel();
        RenderStatsSection(); // instant refresh
        AudioManager.I?.PlayClick();
    }

    private void RefreshStatsViewToggleLabel()
    {
        if (!statsViewToggleLabel) return;
        statsViewToggleLabel.text = _showBaseStats ? "BASE" : "ADJ";
    }

    /// <summary>
    /// Re-renders only the stat labels (HP/ATK/DEF/SPD/EVO).
    /// </summary>
    private void RenderStatsSection()
    {
        if (!_visible) return;
        if (current == null) return;

        int dispLvl = GetDisplayLevel();
        if (lvlText) lvlText.text = $"LVL: {dispLvl}";

        // BASE view
        if (_showBaseStats)
        {
            int hpB = Mathf.RoundToInt(current.baseHP);
            int atkB = Mathf.RoundToInt(current.baseAttack);
            int defB = Mathf.RoundToInt(current.baseDefense);
            float spdB = current.baseSpeed;

            if (hpText) hpText.text = hpB > 0 ? $"HP: {hpB}" : "HP: —";
            if (atkText) atkText.text = atkB > 0 ? $"ATK: {atkB}" : "ATK: —";
            if (defText) defText.text = $"DEF: {defB}";
            if (spdText) spdText.text = $"SPD: {spdB:0.##}";

            if (evoText) evoText.text = (!safeSkipEvolution) ? BuildEvolutionLine(current) : "EVO: —";
            return;
        }

        // ADJ view
        int hpBaseAdj = 0;
        int atkBaseAdj = 0;
        int defBaseAdj = 0;
        int spdBaseAdj = 0;

        if (!safeSkipStats)
        {
            try { hpBaseAdj = Mathf.RoundToInt(BattleCalc.CalcHP(current, dispLvl)); }
            catch { hpBaseAdj = Mathf.RoundToInt(current.baseHP); }

            try { atkBaseAdj = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(current, dispLvl, 0, 0)); }
            catch { atkBaseAdj = Mathf.RoundToInt(current.baseAttack); }

            try { defBaseAdj = BattleCalc.CalcDefense(current, dispLvl); }
            catch { defBaseAdj = Mathf.RoundToInt(current.baseDefense); }

            try { spdBaseAdj = BattleCalc.CalcSpeed(current, dispLvl); }
            catch { spdBaseAdj = Mathf.Max(1, Mathf.RoundToInt(current.baseSpeed)); }
        }
        else
        {
            hpBaseAdj = Mathf.RoundToInt(current.baseHP);
            atkBaseAdj = Mathf.RoundToInt(current.baseAttack);
            defBaseAdj = Mathf.RoundToInt(current.baseDefense);
            spdBaseAdj = Mathf.RoundToInt(current.baseSpeed);
        }

        int trainHp = 0, trainAtk = 0, trainDef = 0, trainSpd = 0;
        int flatAtkBonus = 0;

        var srcOwned = _statsOwned ?? _currentOwned;
        bool hasOwnedInstance = (srcOwned != null) && !string.IsNullOrEmpty(srcOwned.monsterId);

        if (hasOwnedInstance)
        {
            trainHp = Mathf.Max(0, srcOwned.trainingBonus.hp);
            trainAtk = Mathf.Max(0, srcOwned.trainingBonus.atk);
            trainDef = Mathf.Max(0, srcOwned.trainingBonus.def);
            trainSpd = Mathf.Max(0, srcOwned.trainingBonus.spd);

            flatAtkBonus = Mathf.Max(0, srcOwned.flatAtkBonus);
        }

        int hpAdj = Mathf.Max(1, hpBaseAdj + (hasOwnedInstance ? trainHp : 0));
        int defAdj = Mathf.Max(0, defBaseAdj + (hasOwnedInstance ? trainDef : 0));
        int spdAdj = Mathf.Max(1, spdBaseAdj + (hasOwnedInstance ? trainSpd : 0));

        int atkTrainingPlusFlat = 0;
        if (hasOwnedInstance)
        {
            atkTrainingPlusFlat = trainAtk + flatAtkBonus;

            if (LooksLikeLegacyTrainingWasMirroredIntoFlat(flatAtkBonus, trainAtk))
                atkTrainingPlusFlat = Mathf.Max(0, flatAtkBonus);
        }

        int atkAdj = Mathf.Max(1, atkBaseAdj + atkTrainingPlusFlat);

        int curHP = hpAdj;
        if (hasOwnedInstance)
            curHP = Mathf.Clamp(srcOwned.currentHP, 0, hpAdj);

        if (hpText)
        {
            if (hasOwnedInstance && hpAdj > 0)
                hpText.text = curHP == 0 ? $"HP: 0 / {hpAdj}  (KO)" : $"HP: {curHP} / {hpAdj}";
            else
                hpText.text = hpAdj > 0 ? $"HP: {hpAdj}" : "HP: —";
        }

        if (hasOwnedInstance)
        {
            if (atkText) atkText.text = FormatAdjInt("ATK",
                atkBaseAdj + (LooksLikeLegacyTrainingWasMirroredIntoFlat(flatAtkBonus, trainAtk) ? flatAtkBonus : flatAtkBonus),
                LooksLikeLegacyTrainingWasMirroredIntoFlat(flatAtkBonus, trainAtk) ? 0 : trainAtk);

            if (defText) defText.text = FormatAdjInt("DEF", defBaseAdj, trainDef);
            if (spdText) spdText.text = FormatAdjInt("SPD", spdBaseAdj, trainSpd);
        }
        else
        {
            if (atkText) atkText.text = $"ATK: {atkAdj}";
            if (defText) defText.text = $"DEF: {defAdj}";
            if (spdText) spdText.text = $"SPD: {spdAdj}";
        }

        if (evoText) evoText.text = (!safeSkipEvolution) ? BuildEvolutionLine(current) : "EVO: —";
    }

    // ─────────────────────────────────────────────────────────────
    // Public Browse APIs
    // ─────────────────────────────────────────────────────────────

    public void SetBrowseSession(IReadOnlyList<MonsterDataSO> defs, MonsterDataSO currentDef, bool wrap = true)
    {
        _browseDefs = defs;
        _browseWrap = wrap;
        _browseIndex = ResolveBrowseIndex(defs, currentDef);
        _swipeTracking = false;
    }

    public void SetStarterBrowseContext(IReadOnlyList<MonsterDataSO> starterDefs, int selectedIndex, bool wrap = true)
    {
        if (starterDefs == null || starterDefs.Count == 0)
        {
            ClearStarterBrowseContext();
            return;
        }

        selectedIndex = Mathf.Clamp(selectedIndex, 0, starterDefs.Count - 1);
        SetBrowseSession(starterDefs, starterDefs[selectedIndex], wrap);
    }

    public void SetStarterBrowseContext(IReadOnlyList<MonsterDataSO> starterDefs, MonsterDataSO currentDef, bool wrap = true)
    {
        SetBrowseSession(starterDefs, currentDef, wrap);
    }

    public void ClearStarterBrowseContext()
    {
        _browseDefs = null;
        _browseIndex = -1;
        _browseWrap = true;
        _swipeTracking = false;
    }

    private int ResolveBrowseIndex(IReadOnlyList<MonsterDataSO> defs, MonsterDataSO currentDef)
    {
        if (defs == null || defs.Count == 0 || currentDef == null) return -1;

        string id = currentDef.id;
        if (!string.IsNullOrEmpty(id))
        {
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d && d.id == id) return i;
            }
        }

        for (int i = 0; i < defs.Count; i++)
            if (ReferenceEquals(defs[i], currentDef)) return i;

        return -1;
    }

    // ─────────────────────────────────────────────────────────────
    // Public Show APIs
    // ─────────────────────────────────────────────────────────────

    public void Show(MonsterDataSO monster, Action<MonsterDataSO> onConfirmCallback, Action onCancelCallback = null)
    {
        ClearVariantState();

        _mode = MonsterDetailMode.StarterSelect;
        _currentOwned = null;
        _teamSlotIndex = -1;
        _onRemoved = null;

        current = monster;
        onConfirm = onConfirmCallback;
        onCancel = onCancelCallback;

        RefreshEvolveButton();
        SetupFavoriteButton();

        ResolveVariantState(monster ? monster.id : null);
        _viewShinyCosmetic = (_preferredOwned != null)
            ? (_preferredOwned.isShiny || _preferredOwned.shinyTier > 0)
            : (_statsOwned != null && (_statsOwned.isShiny || _statsOwned.shinyTier > 0));
        SetupShinyVariantUI();
        UpdateShinyVariantToggleLabel(); // ✅ ensure label correct at open
        SafeOpen(monster);
    }

    public void ShowStarter(MonsterDataSO monster, Action<MonsterDataSO> onConfirmCallback, Action onCancelCallback = null)
    {
        Show(monster, onConfirmCallback, onCancelCallback);
    }

    public void ShowAssign(OwnedMonsterData owned)
    {
        if (owned == null || string.IsNullOrEmpty(owned.monsterId)) return;

        ClearVariantState();

        _mode = MonsterDetailMode.AssignToTeam;
        _teamSlotIndex = -1;
        _onRemoved = null;

        _currentOwned = owned;

        
        _statsOwned = _currentOwned;

        // Cosmetic-only: use global preferred variant if set, otherwise fall back to this instance.
        var prefCos = MonsterVariantPreference.GetPreferredOwned(_currentOwned.monsterId);
        _viewShinyCosmetic = (prefCos != null)
            ? (prefCos.isShiny || prefCos.shinyTier > 0)
            : (_currentOwned.isShiny || _currentOwned.shinyTier > 0);
current = MonsterLibraryLocator.GetById(_currentOwned.monsterId);
        onConfirm = null;
        onCancel = null;

        UpdateTitleButtonBinding();
        RefreshEvolveButton();
        SetupFavoriteButton();

        ResolveVariantState(current ? current.id : null, _currentOwned);
        // Sync cosmetic view to preferred after resolving.
        if (_preferredOwned != null)
            _viewShinyCosmetic = (_preferredOwned.isShiny || _preferredOwned.shinyTier > 0);

        SetupShinyVariantUI();
        UpdateShinyVariantToggleLabel(); // ✅
        ApplyVariantCosmeticImmediate();
        SafeOpen(current);
    }

    public void ShowTeamMember(int slotIndex, OwnedMonsterData member, Action onRemoved)
    {
        if (member == null || string.IsNullOrEmpty(member.monsterId)) return;

        ClearVariantState();

        _mode = MonsterDetailMode.AssignToTeam;
        _teamSlotIndex = Mathf.Clamp(slotIndex, 0, 2);
        _onRemoved = onRemoved;

        _currentOwned = member;

        _statsOwned = _currentOwned;

        // Cosmetic-only: use global preferred variant if set, otherwise fall back to this instance.
        var prefCos = MonsterVariantPreference.GetPreferredOwned(_currentOwned.monsterId);
        _viewShinyCosmetic = (prefCos != null)
            ? (prefCos.isShiny || prefCos.shinyTier > 0)
            : (_currentOwned.isShiny || _currentOwned.shinyTier > 0);
current = MonsterLibraryLocator.GetById(_currentOwned.monsterId);
        onConfirm = null;
        onCancel = null;

        UpdateTitleButtonBinding();
        RefreshEvolveButton();
        SetupFavoriteButton();

        ResolveVariantState(current ? current.id : null, _currentOwned);
        // Sync cosmetic view to preferred after resolving.
        if (_preferredOwned != null)
            _viewShinyCosmetic = (_preferredOwned.isShiny || _preferredOwned.shinyTier > 0);

        SetupShinyVariantUI();
        UpdateShinyVariantToggleLabel();
        ApplyVariantCosmeticImmediate();
        SafeOpen(current);
    }

    public void ShowCodex(MonsterDataSO monster)
    {
        ClearVariantState();

        _mode = MonsterDetailMode.CodexView;
        _currentOwned = null;
        _teamSlotIndex = -1;
        _onRemoved = null;

        current = monster;
        onConfirm = null;
        onCancel = null;

        RefreshEvolveButton();
        SetupFavoriteButton();

        ResolveVariantState(monster ? monster.id : null);
        _statsOwned = _preferredOwned;
        _viewShinyCosmetic = _codexViewingShiny;
        SetupShinyVariantUI();
        UpdateShinyVariantToggleLabel(); 
        SafeOpen(monster);
    }

    public void ShowCodexOwned(MonsterDataSO monster, OwnedMonsterData owned)
    {
        if (monster == null)
            return;

        ClearVariantState();

        _mode = MonsterDetailMode.CodexView;
        _teamSlotIndex = -1;
        _onRemoved = null;

        current = monster;
        _currentOwned = owned;
        _statsOwned = _currentOwned;

        onConfirm = null;
        onCancel = null;

        UpdateTitleButtonBinding();
        RefreshEvolveButton();
        SetupFavoriteButton();

        ResolveVariantState(monster ? monster.id : null, owned);

        // Ensure the cosmetic view matches the instance we opened from.
        // Without this, if the panel was previously opened from a shiny team member,
        // clicking a non-shiny owned row could keep the "Shiny" tab selected.
        _viewShinyCosmetic = _codexViewingShiny;

        // In Codex mode, stats are driven by the focused owned instance, but shiny is cosmetic-only.
        // Apply cosmetic immediately so icon + name are correct even if the panel was already open.
        ApplyVariantCosmeticImmediate();
        SetupShinyVariantUI();
        UpdateShinyVariantToggleLabel();
        SafeOpen(monster);
    }

    public void Hide()
    {
        if (_stageCR != null)
        {
            StopCoroutine(_stageCR);
            _stageCR = null;
        }
        _stage = RenderStage.None;

        ClearVariantState();

        TryStep("Hide", () =>
        {
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
    // Favorite handling
    // ─────────────────────────────────────────────────────────────

    private void SetupFavoriteButton()
    {
        if (!favoriteButton)
            return;

        bool featureOn = FeatureUnlockManager.I &&
                         FeatureUnlockManager.I.IsUnlocked(FeatureId.Codex_Favorites) &&
                         current != null;

        favoriteButton.gameObject.SetActive(featureOn);
        favoriteButton.onClick.RemoveAllListeners();

        if (featureOn)
        {
            favoriteButton.onClick.AddListener(OnClickFavorite);
            RefreshFavoriteVisual();
        }
        else if (favoriteOnIcon)
        {
            favoriteOnIcon.SetActive(false);
        }
    }

    private void OnClickFavorite()
    {
        if (current == null) return;

        FavoriteService.ToggleFavorite(current.id);
        RefreshFavoriteVisual();
    }

    private void RefreshFavoriteVisual()
    {
        if (!favoriteOnIcon || current == null) return;

        bool isFav = FavoriteService.IsFavorite(current.id);
        favoriteOnIcon.SetActive(isFav);
    }

    // ─────────────────────────────────────────────────────────────
    // Shiny variant toggle UI + label
    // ─────────────────────────────────────────────────────────────

    private bool IsViewingShinyNow()
    {
        // Shiny is cosmetic-only. The active cosmetic flag drives icon/name.
        if (_mode == MonsterDetailMode.AssignToTeam || _mode == MonsterDetailMode.CodexView)
            return _viewShinyCosmetic;

        return false;
    }

    private void UpdateShinyVariantToggleLabel()
    {
        if (!shinyVariantToggleLabel) return;
        if (!shinyVariantRoot || !shinyVariantRoot.activeSelf) return;

        bool viewingShiny = IsViewingShinyNow();
        shinyVariantToggleLabel.text = viewingShiny ? "Normal" : "Shiny";
    }

    private void SetupShinyVariantUI()
    {
        if (!shinyVariantRoot)
            return;

        // Show in Codex + Assign/Team. Never in StarterSelect.
        if (_mode != MonsterDetailMode.CodexView && _mode != MonsterDetailMode.AssignToTeam)
        {
            shinyVariantRoot.SetActive(false);
            return;
        }

        bool show = current != null
                    && _codexHasNormal
                    && _codexHasShiny
                    && _preferredOwned != null
                    && _otherVariantOwned != null;

        shinyVariantRoot.SetActive(show);

        if (!show)
            return;

        UpdateShinyVariantToggleLabel(); 
    }

    private void ToggleCodexShinyVariant()
    {
        if (current == null || string.IsNullOrEmpty(current.id))
            return;

        if (!MonsterVariantPreference.PlayerHasBothVariants(current.id, out var shiny, out var non))
            return;

        if (shiny == null || non == null)
            return;

        bool viewingShiny = IsViewingShinyNow();

        var next = viewingShiny ? non : shiny;
        var other = viewingShiny ? shiny : non;

        _preferredOwned = next;
        _otherVariantOwned = other;

        _codexHasShiny = shiny != null;
        _codexHasNormal = non != null;
        _codexViewingShiny = next != null && (next.isShiny || next.shinyTier > 0);

        _viewShinyCosmetic = (next != null && (next.isShiny || next.shinyTier > 0));

        if (next != null && !string.IsNullOrEmpty(next.ownedUID))
            MonsterVariantPreference.SetPreferred(current.id, next.ownedUID);

        SetupShinyVariantUI();
        UpdateShinyVariantToggleLabel();
        ApplyVariantCosmeticImmediate();
        SafeOpen(current);

        GameEvents.OnTeamChanged?.Invoke();
        GameEvents.OnJobsChanged?.Invoke();
        GameEvents.FavoritesChanged?.Invoke();

        AudioManager.I?.PlayClick();
    }

    
private void ApplyVariantCosmeticImmediate()
{
    if (current == null) return;

    // Force-refresh the cosmetic pieces immediately (independent of staged rendering / safe flags).
    try
    {
        if (icon)
        {
            var spr = GetVariantIcon(current);
            if (spr != null)
            {
                icon.sprite = spr;
                icon.enabled = true;
            }
        }

        if (nameText)
            nameText.text = BuildVariantDisplayName(current);
    }
    catch { /* cosmetic-only; never break panel */ }
}

private Sprite GetVariantIcon(MonsterDataSO monster)
    {
        if (monster == null) return null;

        bool shiny = (_mode == MonsterDetailMode.AssignToTeam || _mode == MonsterDetailMode.CodexView) ? _viewShinyCosmetic : false;

        if (shiny && monster.shinyIcon != null)
            return monster.shinyIcon;

        return monster.icon;
    }

    private string BuildVariantDisplayName(MonsterDataSO monster)
    {
        if (monster == null) return "-";

        string baseName = string.IsNullOrEmpty(monster.displayName) ? monster.name : monster.displayName;

        bool shiny = (_mode == MonsterDetailMode.AssignToTeam || _mode == MonsterDetailMode.CodexView) ? _viewShinyCosmetic : false;

        if (!shiny)
            return baseName;

        return $"{baseName} <color=#FFD54F>(Shiny)</color>";
    }

    // ─────────────────────────────────────────────────────────────
    // Staged render
    // ─────────────────────────────────────────────────────────────

    private void SafeOpen(MonsterDataSO monster)
    {
        if (monster == null) return;

        if (!gameObject.activeInHierarchy)
        {
            if (canvasGroup) canvasGroup.alpha = 0f;
            OpenSelf();
        }

        if (_stageCR != null)
        {
            StopCoroutine(_stageCR);
            _stageCR = null;
        }

        _stage = RenderStage.Header;
        _stageCR = StartCoroutine(CoRenderStaged(monster));
    }

    private IEnumerator CoRenderStaged(MonsterDataSO monster)
    {
        if (_stage == RenderStage.Header)
        {
            TryStep("Header & Static Fields", () =>
            {
                if (!safeSkipMonsterIcon && icon) icon.sprite = GetVariantIcon(monster);
                if (idText) idText.text = monster ? $"ID: {monster.id}" : "ID: -";
                if (nameText) nameText.text = BuildVariantDisplayName(monster);

                if (typeText)
                {
                    string typeName = monster ? monster.type.ToString() : "-";
                    string typeHex = "CCCCCC";
                    if (monster != null && TYPE_COLORS.TryGetValue(monster.type, out var tc))
                        typeHex = ColorUtility.ToHtmlStringRGB(tc);

                    typeText.color = Color.white;
                    typeText.richText = true;
                    typeText.text = $"<color=#{typeHex}>{typeName}</color>";
                }

                if (rarityText)
                {
                    rarityText.text = monster ? $"{monster.rarity}" : "-";
                    if (monster != null && RARITY_COLORS.TryGetValue(monster.rarity, out var rc))
                    {
                        rarityText.color = rc;
                        ApplyRarityBackground(rc);
                    }
                    else
                    {
                        ApplyRarityBackground(Color.white);
                    }
                }

                bool isStarter = _mode == MonsterDetailMode.StarterSelect;
                bool isCodex = _mode == MonsterDetailMode.CodexView;
                bool isAssign = _mode == MonsterDetailMode.AssignToTeam;

                if (starterButtonsHolder) starterButtonsHolder.SetActive(isStarter);
                if (slotButtonsHolder) slotButtonsHolder.SetActive(isCodex);
                if (teamHolder) teamHolder.SetActive(isAssign && _teamSlotIndex >= 0);

                if (closeButton) closeButton.gameObject.SetActive(!isStarter);

                RenderJobSites(monster);
                UpdateTitleButtonBinding();
                RefreshPersonalityUI(monster);

                RefreshStatsViewToggleLabel();

                // ✅ CRITICAL FIX:
                // Preserve focused owned instance during staged refresh so Team/Assign doesn’t
                // accidentally revert to global codex preference mid-render.
                ResolveVariantState(monster ? monster.id : null);
                SetupShinyVariantUI();
                UpdateShinyVariantToggleLabel();

                RenderStatsSection();

                if (canvasGroup) LeanTween.alphaCanvas(canvasGroup, 1f, 0.12f);
            });

            _stage = RenderStage.StatsEvo;
            yield return null;
        }

        if (_stage == RenderStage.StatsEvo)
        {
            TryStep("Stats & Evo", RenderStatsSection);

            _stage = RenderStage.Description;
            yield return null;
        }

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

        if (_stage == RenderStage.TypeIcons)
        {
            TryStep("Type Matchup Icons", () =>
            {
                ClearIcons(strongIconHolder);
                ClearIcons(weakIconHolder);
                if (safeSkipTypeIcons || current == null) return;

                List<MonsterType> strong = null, weak = null;
                try { strong = BattleTypeChart.GetStrongAgainst(current.type); } catch { strong = null; }
                try { weak = BattleTypeChart.GetWeakAgainst(current.type); } catch { weak = null; }

                if (strong != null) foreach (var t in strong) CreateTypeIcon(t, strongIconHolder, true);
                if (weak != null) foreach (var t in weak) CreateTypeIcon(t, weakIconHolder, true);
            });

            _stage = RenderStage.Done;
        }

        LogStep("END Show (staged)");
        _stageCR = null;
    }

    // ─────────────────────────────────────────────────────────────
    // Swipe input (Input System)
    // ─────────────────────────────────────────────────────────────

    private void HandleSwipeInput()
    {
        if (_browseDefs == null || _browseDefs.Count <= 1) return;

        bool pressed = false;
        bool released = false;
        Vector2 pos = Vector2.zero;

        var ts = Touchscreen.current;
        if (ts != null && ts.primaryTouch != null)
        {
            var touch = ts.primaryTouch;
            pressed = touch.press.wasPressedThisFrame;
            released = touch.press.wasReleasedThisFrame;
            pos = touch.position.ReadValue();
        }
        else
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            pressed = mouse.leftButton.wasPressedThisFrame;
            released = mouse.leftButton.wasReleasedThisFrame;
            pos = mouse.position.ReadValue();
        }

        if (pressed)
        {
            _swipeTracking = true;
            _swipeStartPos = pos;
            return;
        }

        if (!_swipeTracking) return;

        if (released)
        {
            _swipeTracking = false;

            if (Time.unscaledTime - _lastBrowseAt < swipeBrowseCooldown)
                return;

            Vector2 delta = pos - _swipeStartPos;

            if (Mathf.Abs(delta.y) > swipeMaxVerticalPixels)
                return;

            if (Mathf.Abs(delta.x) < swipeMinPixels)
                return;

            _lastBrowseAt = Time.unscaledTime;

            if (delta.x < 0f) BrowseNext();
            else BrowsePrev();
        }
    }

    private void BrowseNext()
    {
        if (_browseDefs == null || _browseDefs.Count == 0) return;

        int idx = _browseIndex;
        if (idx < 0) idx = ResolveBrowseIndex(_browseDefs, current);

        int next = idx + 1;
        if (next >= _browseDefs.Count)
        {
            if (!_browseWrap) return;
            next = 0;
        }

        OpenBrowseIndex(next);
    }

    private void BrowsePrev()
    {
        if (_browseDefs == null || _browseDefs.Count == 0) return;

        int idx = _browseIndex;
        if (idx < 0) idx = ResolveBrowseIndex(_browseDefs, current);

        int prev = idx - 1;
        if (prev < 0)
        {
            if (!_browseWrap) return;
            prev = _browseDefs.Count - 1;
        }

        OpenBrowseIndex(prev);
    }

    private void OpenBrowseIndex(int index)
    {
        if (_browseDefs == null || _browseDefs.Count == 0) return;

        index = Mathf.Clamp(index, 0, _browseDefs.Count - 1);
        var def = _browseDefs[index];
        if (def == null) return;

        _browseIndex = index;
        current = def;

        RefreshEvolveButton();
        SetupFavoriteButton();
        SafeOpen(def);
    }

    // ─────────────────────────────────────────────────────────────
    // Personality (InfoRouter pattern)
    // ─────────────────────────────────────────────────────────────

    private void RefreshPersonalityUI(MonsterDataSO monster)
    {
        var p = monster ? monster.Personality : null;

        if (personalityNameText)
            personalityNameText.text = p ? p.name : "—";

        if (personalityInfoButton)
        {
            personalityInfoButton.interactable = p != null;
            personalityInfoButton.gameObject.SetActive(true);
        }
    }

    private void OpenPersonalityInfo()
    {
        if (current == null) return;

        var p = current.Personality;
        if (p == null)
        {
            AudioManager.I?.PlayClick();
            return;
        }

        string id = $"per.{p.name}".ToLowerInvariant();
        string title = p.name;
        string subtitle = "Personality";
        string body = !string.IsNullOrWhiteSpace(p.description) ? p.description : "No description available.";

        InfoRouter.Open(id, title, subtitle, body);
        AudioManager.I?.PlayClick();
    }

    // ─────────────────────────────────────────────────────────────
    // Confirm / Cancel / Assign / Remove
    // ─────────────────────────────────────────────────────────────

    private void Confirm()
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

    private void Cancel()
    {
        TryStep("Cancel", () =>
        {
            onCancel?.Invoke();
            Hide();
        });
    }

    private void AssignToSlot(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 0, 2);

        if (_mode == MonsterDetailMode.CodexView)
        {
            if (current == null || string.IsNullOrEmpty(current.id)) { Hide(); return; }

            var data = SaveManager.Data;
            if (data == null)
            {
                Debug.LogWarning("[MonsterDetailPanel] SaveManager.Data is null in AssignToSlot (CodexView). Attempting LoadOrCreate.");
                SaveManager.LoadOrCreate();
                data = SaveManager.Data;
                if (data == null) { Hide(); return; }
            }

            var team = data.team ?? new List<OwnedMonsterData>();
            TeamUtils.EnsureTeamSize(team, 3);

            var preferred = MonsterVariantPreference.GetPreferredOwned(current.id);
            if (preferred == null)
            {
                Debug.LogWarning("[MonsterDetailPanel] No owned instance found for this monster; cannot assign from Codex.");
                Hide();
                return;
            }

            var clone = XPManager.Resolve(preferred) ?? preferred;

            // Enforce: one owned monster instance per team slot.
            TeamUtils.RemoveDuplicatesForAssignment(team, clone, slotIndex);
            team[slotIndex] = clone;

            data.team = team;
            SaveManager.Save();
            GameEvents.OnTeamChanged?.Invoke();

            Hide();
            return;
        }

        if (_mode != MonsterDetailMode.AssignToTeam
            || _currentOwned == null
            || string.IsNullOrEmpty(_currentOwned.monsterId))
        {
            Hide();
            return;
        }

        // Allow assigning KO'd monsters back onto the team so the player can use team healing.
        // Battle eligibility is enforced elsewhere (EligibilityRules / EncounterManager).

        var data2 = SaveManager.Data;
        if (data2 == null)
        {
            Debug.LogWarning("[MonsterDetailPanel] SaveManager.Data is null in AssignToSlot. Attempting LoadOrCreate.");
            SaveManager.LoadOrCreate();
            data2 = SaveManager.Data;
            if (data2 == null)
            {
                Hide();
                return;
            }
        }

        var team2 = data2.team ?? new List<OwnedMonsterData>();
        TeamUtils.EnsureTeamSize(team2, 3);

        var canonical = XPManager.Resolve(_currentOwned) ?? _currentOwned;

        // Enforce: one owned monster instance per team slot.
        TeamUtils.RemoveDuplicatesForAssignment(team2, canonical, slotIndex);
        team2[slotIndex] = canonical;

        data2.team = team2;
        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();

        Hide();
    }

    private void RemoveFromTeam()
    {
        if (_teamSlotIndex < 0) { Hide(); return; }

        var data = SaveManager.Data;
        if (data == null)
        {
            SaveManager.LoadOrCreate();
            data = SaveManager.Data;
            if (data == null)
            {
                Hide();
                return;
            }
        }

        var team = data.team ?? new List<OwnedMonsterData>();
        while (team.Count < 3) team.Add(new OwnedMonsterData());

        team[_teamSlotIndex] = new OwnedMonsterData();

        data.team = team;
        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();

        _onRemoved?.Invoke();
        Hide();
    }

    // ─────────────────────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────────────────────

    private void OpenSelf()
    {
        if (_visible) return;

        if (UIManager.I && selfPanelId != PanelId.None)
            UIManager.I.Show(selfPanelId);
        else
            gameObject.SetActive(true);

        _visible = true;
    }

    private void CloseSelf()
    {
        if (!_visible) return;

        if (UIManager.I && selfPanelId != PanelId.None)
            UIManager.I.Hide(selfPanelId);
        else
            gameObject.SetActive(false);

        _visible = false;
    }

    private void ApplyRarityBackground(Color baseColor)
    {
        if (!backgroundImage) return;
        var c = baseColor; c.a = rarityBackgroundAlpha;
        backgroundImage.color = c;
    }

    private void ClearVariantState()
    {
        _codexHasNormal = false;
        _codexHasShiny = false;
        _codexViewingShiny = false;

        _preferredOwned = null;
        _otherVariantOwned = null;

        
        _statsOwned = null;
        _viewShinyCosmetic = false;
if (shinyVariantRoot) shinyVariantRoot.SetActive(false);
        if (shinyVariantToggleLabel) shinyVariantToggleLabel.text = string.Empty;
    }

    private void ResetVisualsImmediate()
    {
        if (_stageCR != null)
        {
            StopCoroutine(_stageCR);
            _stageCR = null;
        }
        _stage = RenderStage.None;

        if (canvasGroup) canvasGroup.alpha = 0f;

        current = null;
        onConfirm = null;
        onCancel = null;
        _currentOwned = null;
        _mode = MonsterDetailMode.StarterSelect;

        _teamSlotIndex = -1;
        _onRemoved = null;

        if (icon) icon.sprite = null;
        if (jobSiteText) jobSiteText.text = string.Empty;

        if (personalityNameText) personalityNameText.text = string.Empty;
        if (personalityInfoButton) personalityInfoButton.interactable = false;

        ClearIcons(strongIconHolder);
        ClearIcons(weakIconHolder);

        if (starterButtonsHolder) starterButtonsHolder.SetActive(false);
        if (slotButtonsHolder) slotButtonsHolder.SetActive(false);
        if (teamHolder) teamHolder.SetActive(false);

        if (lvlText) lvlText.text = string.Empty;

        if (backgroundImage) backgroundImage.color = Color.white;

        if (!UIManager.I) gameObject.SetActive(false);

        if (titleButton) titleButton.gameObject.SetActive(false);

        if (evolveButton) evolveButton.gameObject.SetActive(false);

        if (favoriteButton) favoriteButton.gameObject.SetActive(false);
        if (favoriteOnIcon) favoriteOnIcon.SetActive(false);

        ClearVariantState();

        _swipeTracking = false;
    }

    private void ClearIcons(Transform holder)
    {
        if (!holder) return;
        for (int i = holder.childCount - 1; i >= 0; i--)
            Destroy(holder.GetChild(i).gameObject);
    }

    private void CreateTypeIcon(MonsterType type, Transform parent, bool animate)
    {
        if (!typeIconPrefab || !parent) return;

        var go = Instantiate(typeIconPrefab, parent);
        var img = go.GetComponent<Image>();
        if (img && typeIconLibrary) img.sprite = typeIconLibrary.GetIcon(type);

        if (animate)
        {
            go.transform.localScale = Vector3.zero;
            LeanTween.scale(go, Vector3.one, 0.15f).setEaseOutBack();
        }
    }

    private string BuildEvolutionLine(MonsterDataSO m)
    {
        if (!m) return "EVO: —";

        if (!m.evolutionForm || m.evolutionLevel <= 0)
            return "EVO: —";

        var nextDef = m.evolutionForm;
        string nextName = nextDef
            ? (string.IsNullOrEmpty(nextDef.displayName) ? nextDef.name : nextDef.displayName)
            : "???";

        int evoLvl = Mathf.Max(1, m.evolutionLevel);

        var srcOwned = _statsOwned ?? _currentOwned;
        bool hasOwnedInstance = (srcOwned != null) && !string.IsNullOrEmpty(srcOwned.monsterId);

        int trainHP = 0, trainATK = 0, trainDEF = 0, trainSPD = 0;
        int flatAtkBonus = 0;

        if (hasOwnedInstance)
        {
            trainHP = Mathf.Max(0, srcOwned.trainingBonus.hp);
            trainATK = Mathf.Max(0, srcOwned.trainingBonus.atk);
            trainDEF = Mathf.Max(0, srcOwned.trainingBonus.def);
            trainSPD = Mathf.Max(0, srcOwned.trainingBonus.spd);
            flatAtkBonus = Mathf.Max(0, srcOwned.flatAtkBonus);

            if (LooksLikeLegacyTrainingWasMirroredIntoFlat(flatAtkBonus, trainATK))
                trainATK = 0;
        }

        int curHP = Mathf.RoundToInt(BattleCalc.CalcHP(m, evoLvl));
        int nxtHP = Mathf.RoundToInt(BattleCalc.CalcHP(nextDef, evoLvl));

        int curATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(m, evoLvl, 0, 0));
        int nxtATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(nextDef, evoLvl, 0, 0));

        int curDEF = BattleCalc.CalcDefense(m, evoLvl);
        int nxtDEF = BattleCalc.CalcDefense(nextDef, evoLvl);

        int curSPD = BattleCalc.CalcSpeed(m, evoLvl);
        int nxtSPD = BattleCalc.CalcSpeed(nextDef, evoLvl);

        if (hasOwnedInstance)
        {
            curHP += trainHP; nxtHP += trainHP;
            curATK += (trainATK + flatAtkBonus); nxtATK += (trainATK + flatAtkBonus);
            curDEF += trainDEF; nxtDEF += trainDEF;
            curSPD += trainSPD; nxtSPD += trainSPD;
        }

        int dHp = nxtHP - curHP;
        int dAtk = nxtATK - curATK;
        int dDef = nxtDEF - curDEF;
        int dSpd = nxtSPD - curSPD;

        List<string> parts = new List<string>(4);
        if (dHp != 0) parts.Add($"{(dHp > 0 ? "+" : "")}{dHp} HP");
        if (dAtk != 0) parts.Add($"{(dAtk > 0 ? "+" : "")}{dAtk} ATK");
        if (dDef != 0) parts.Add($"{(dDef > 0 ? "+" : "")}{dDef} DEF");
        if (dSpd != 0) parts.Add($"{(dSpd > 0 ? "+" : "")}{dSpd} SPD");

        string deltas = parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "";

        return $"EVO: Lv {evoLvl} → {nextName}{deltas}";
    }

    private void TryStep(string label, Action step)
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

    private void LogStep(string msg)
    {
        if (!buildVerboseLogging) return;
        Debug.Log($"[MonsterDetailPanelUI] {msg} (monster={(current ? current.id : "null")})");
    }

    private int GetDisplayLevel()
    {
        // Stats source is stable; shiny is cosmetic-only.
        var src = _statsOwned ?? _currentOwned;
        if (src != null && src.level > 0)
            return src.level;

        return 1;
    }

    private void RefreshEvolveButton()
    {
        if (!evolveButton)
            return;

        var srcOwned = _statsOwned ?? _currentOwned;
        bool hasOwned = (srcOwned != null) && !string.IsNullOrEmpty(srcOwned.monsterId);

        if (!hasOwned || current == null)
        {
            evolveButton.gameObject.SetActive(false);
            return;
        }

        bool hasEvolution = current.evolutionForm != null && current.evolutionLevel > 0;

        int curLevel = GetDisplayLevel();
        bool meetsLevel = hasEvolution && curLevel >= current.evolutionLevel;

        evolveButton.gameObject.SetActive(meetsLevel);

        bool canActuallyEvolve = false;
        if (meetsLevel)
            canActuallyEvolve = EvolutionHelper.CanEvolve(srcOwned, current);

        evolveButton.interactable = canActuallyEvolve;
    }

    private void ResolveTitleButton()
    {
        if (titleButton) return;
        titleButton = GetComponentInChildren<TitleButtonUI>(true);
    }

    private void UpdateTitleButtonBinding()
    {
        if (!titleButton) return;

        string key = null;

        var srcOwned = _statsOwned ?? _currentOwned;

        if (srcOwned != null && !string.IsNullOrEmpty(srcOwned.monsterId))
            key = srcOwned.monsterId;
        else if (current != null && !string.IsNullOrEmpty(current.id))
            key = current.id;

        bool canBind = current != null && !string.IsNullOrEmpty(key);

        titleButton.gameObject.SetActive(canBind);
        if (!canBind) return;

        int lvl = GetDisplayLevel();
        titleButton.Bind(key, current, lvl);
        titleButton.RefreshLabel();
    }

    private void HandleTitlesChanged(string ownedId)
    {
        var srcOwned = _statsOwned ?? _currentOwned;
        if (srcOwned != null
            && !string.IsNullOrEmpty(srcOwned.monsterId)
            && srcOwned.monsterId == ownedId)
        {
            UpdateTitleButtonBinding();
        }
    }

    private void RenderJobSites(MonsterDataSO monster)
    {
        if (!jobSiteText) return;

        var jobs = (monster != null)
            ? JobBalance.JobsUnlockedByType(monster.type).ToList()
            : new List<JobType>();

        jobSiteText.text = (jobs.Count > 0)
            ? string.Join(", ", jobs.Select(JobStrings.SiteName))
            : "—";
    }

    private void OnClickEvolve()
    {
        if (_mode != MonsterDetailMode.AssignToTeam)
            return;

        if (_currentOwned == null || string.IsNullOrEmpty(_currentOwned.monsterId))
            return;

        if (!evolutionPanel)
        {
            Debug.LogWarning("[MonsterDetailPanelUI] EvolutionPanelUI reference is missing.");
            return;
        }

        evolutionPanel.Open(_currentOwned);
    }

    private void HandleMonsterLeveled(string ownedIdOrDefId, int newLevel)
    {
        var srcOwned = _statsOwned ?? _currentOwned;
        if (srcOwned == null || string.IsNullOrEmpty(srcOwned.monsterId))
            return;

        string myKey = !string.IsNullOrEmpty(srcOwned.ownedUID)
            ? srcOwned.ownedUID
            : srcOwned.monsterId;

        if (myKey != ownedIdOrDefId)
            return;

        srcOwned.level = newLevel;

        if (lvlText)
            lvlText.text = $"LVL: {GetDisplayLevel()}";

        RefreshEvolveButton();

        if (!_showBaseStats)
            RenderStatsSection();
    }

    private void HandleMonsterEvolved(string newDefId)
    {
        if (!_visible) return;
        if (_mode != MonsterDetailMode.AssignToTeam) return;
        if (_currentOwned == null) return;

        var resolved = XPManager.Resolve(_currentOwned) ?? _currentOwned;
        _currentOwned = resolved;

        if (string.IsNullOrEmpty(_currentOwned.monsterId))
            return;

        current = MonsterLibraryLocator.GetById(_currentOwned.monsterId);
        if (!current) return;

        UpdateTitleButtonBinding();
        RefreshEvolveButton();
        SetupFavoriteButton();

        SafeOpen(current);
    }

    private string FormatAdjInt(string label, int baseVal, int trainingVal)
    {
        int total = baseVal + trainingVal;
        return $"{label}: {total} ({baseVal} + <color={TRAINING_GREEN}>{trainingVal}</color>)";
    }

    private bool LooksLikeLegacyTrainingWasMirroredIntoFlat(int flatAtkBonus, int trainingAtk)
    {
        if (flatAtkBonus <= 0 || trainingAtk <= 0) return false;
        return flatAtkBonus >= trainingAtk && flatAtkBonus >= 10;
    }

    // ─────────────────────────────────────────────────────────────
    // Variant state resolution (unchanged API, improved usage)
    // ─────────────────────────────────────────────────────────────

    private void ResolveVariantState(string monsterId)
    {
        ResolveVariantState(monsterId, focusOwned: null);
    }

    // Overload: when opening from a specific owned instance (team card / owned list),
    // we want the detail panel to start from THAT instance (not a saved global preference).
    void ResolveVariantState(string monsterId, OwnedMonsterData focusOwned)
    {
        _codexHasNormal = false;
        _codexHasShiny = false;
        _codexViewingShiny = false;

        _preferredOwned = null;
        _otherVariantOwned = null;

        if (string.IsNullOrEmpty(monsterId) || SaveManager.Data == null)
            return;

        if (MonsterVariantPreference.PlayerHasBothVariants(monsterId, out var shiny, out var non))
        {
            _codexHasShiny = shiny != null;
            _codexHasNormal = non != null;

            if (focusOwned != null && focusOwned.monsterId == monsterId)
                _preferredOwned = focusOwned;
            else
                _preferredOwned = MonsterVariantPreference.GetPreferredOwned(monsterId);

            if (_preferredOwned == null)
                _preferredOwned = non ?? shiny;

            _otherVariantOwned = MonsterVariantPreference.GetOtherVariant(monsterId, _preferredOwned);

            _codexViewingShiny = _preferredOwned != null && (_preferredOwned.isShiny || _preferredOwned.shinyTier > 0);
            return;
        }

        var pref = (focusOwned != null && focusOwned.monsterId == monsterId)
            ? focusOwned
            : MonsterVariantPreference.GetPreferredOwned(monsterId);

        if (pref != null)
        {
            bool s = pref.isShiny || pref.shinyTier > 0;
            _codexHasShiny = s;
            _codexHasNormal = !s;
            _preferredOwned = pref;
            _codexViewingShiny = s;
        }
    }
}
