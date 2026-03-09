using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealButtonController : MonoBehaviour
{
    [SerializeField] private int teamIndex = -1;

    [SerializeField] private Button healButton;
    [SerializeField] private TextMeshProUGUI costLabel;
    [SerializeField] private TextMeshProUGUI hpLabel;

    [SerializeField] private int hpPerMedkit = 50;

    [Header("Optional Wiring (avoids Resources lookups)")]
    [SerializeField] private HealingConfigSO config;
    [SerializeField] private MonsterLibrarySO library;

    // If you still want Resources fallback, keep these paths consistent.
    [Header("Resources Fallback Paths")]
    [SerializeField] private string monsterLibraryResourcePath = "MonsterLibrary";
    [SerializeField] private string healingConfigResourcePath = "HealingConfig";

    private bool _ready;

    /// <summary>
    /// Optional hook for the owning UI to run logic before healing (e.g., selecting the card).
    /// </summary>
    public System.Action OnBeforeHeal;

    /// <summary>
    /// Bind this heal button to a specific team slot.
    /// Call this immediately after instantiating the prefab.
    /// </summary>
    public void BindTeamIndex(int index)
    {
        teamIndex = index;
        Refresh();
    }

    void Awake()
    {
        // Prefer inspector references; fall back to Resources for older scenes.
        if (library == null)
            library = Resources.Load<MonsterLibrarySO>(monsterLibraryResourcePath);
        if (config == null)
            config = Resources.Load<HealingConfigSO>(healingConfigResourcePath);

        _ready = library != null && config != null;

        // Ensure this button instance is wired to THIS component, not an inspector-stale target.
        if (healButton)
        {
            healButton.onClick.RemoveAllListeners();
            healButton.onClick.AddListener(OnClickHeal);
        }

        if (!_ready)
        {
            // This is almost always a wiring/content issue, not a logic failure.
            // Keep it as a warning so release builds aren't polluted with false "errors".
            Debug.LogWarning(
                "[HealButtonController] Missing required assets. " +
                $"MonsterLibrary loaded={library != null}, HealingConfig loaded={config != null}. " +
                "Heal UI will be disabled until references are fixed.",
                this
            );

            if (hpLabel != null)   hpLabel.text = "Heal Unavailable";
            if (costLabel != null) costLabel.text = "Missing Config";
            if (healButton != null) healButton.interactable = false;
        }
    }

    void OnEnable() { Refresh(); }

    public void Refresh()
    {
        if (!_ready)
            return;

        // Ensure save state exists (menu-first boot can call UI before SaveManager loads).
        if (SaveManager.Data == null)
            SaveManager.LoadOrCreate();

        // If not bound yet, keep the UI safely disabled.
        if (teamIndex < 0)
        {
            if (hpLabel != null)   hpLabel.text = "";
            if (costLabel != null) costLabel.text = "";
            if (healButton != null) healButton.interactable = false;
            return;
        }

        var team = SaveManager.Data?.team;
        if (team == null || teamIndex < 0 || teamIndex >= team.Count) return;

        var owned = team[teamIndex];
        if (owned == null || string.IsNullOrEmpty(owned.monsterId)) return;

        var def = (library != null) ? library.GetById(owned.monsterId) : null;
        if (def == null)
        {
            if (hpLabel != null) hpLabel.text = "HP: ?";
            if (costLabel != null) costLabel.text = "Missing Monster";
            if (healButton != null) healButton.interactable = false;
            return;
        }
        int maxHP = HealingService.CalcMaxHP(def, owned.level);
        // Enforce HP invariant.
        int curHP = Mathf.Clamp(owned.currentHP, 0, maxHP);
        int missing = HealingService.MissingHP(curHP, maxHP);

        int kitsNeeded = HealingService.MedkitsToHealFull(missing, hpPerMedkit);
        int creditsNeeded = HealingService.CreditsToHealFull(config, owned.level, missing);

        int haveKits = ResourceBank.Get(ResourceType.Medkit);
        bool useKitsOnly = haveKits >= kitsNeeded && kitsNeeded > 0;
        bool needFallback = kitsNeeded > 0 && haveKits < kitsNeeded;

        if (missing <= 0)
        {
            hpLabel.text = "HP: Full";
            costLabel.text = "Heal";
            healButton.interactable = false;
            return;
        }

        hpLabel.text = $"HP: {curHP}/{maxHP}";

        if (useKitsOnly)
        {
            costLabel.text = $"{kitsNeeded} Medkits";
            healButton.interactable = true;
        }
        else if (needFallback)
        {
            int credits = ResourceManager.I != null
                ? ResourceManager.I.Get(ResourceType.Credits)
                : ResourceBank.Get(ResourceType.Credits);

            costLabel.text = $"{haveKits} Medkits + {creditsNeeded} credits";
            healButton.interactable = (credits >= creditsNeeded) || (haveKits > 0);
        }
        else
        {
            int credits = ResourceManager.I != null
                ? ResourceManager.I.Get(ResourceType.Credits)
                : ResourceBank.Get(ResourceType.Credits);

            costLabel.text = $"{creditsNeeded} credits";
            healButton.interactable = credits >= creditsNeeded;
        }
    }

    public void OnClickHeal()
    {
        OnBeforeHeal?.Invoke();

        if (!_ready)
            return;

        if (teamIndex < 0)
        {
            Refresh();
            return;
        }

        var team = SaveManager.Data?.team;
        if (team == null || teamIndex < 0 || teamIndex >= team.Count) return;

        var owned = team[teamIndex];
        if (owned == null || string.IsNullOrEmpty(owned.monsterId)) return;

        var def = (library != null) ? library.GetById(owned.monsterId) : null;
        if (def == null) { Refresh(); return; }
        int maxHP = HealingService.CalcMaxHP(def, owned.level);
        // Enforce HP invariant.
        int curHP = Mathf.Clamp(owned.currentHP, 0, maxHP);
        int missing = HealingService.MissingHP(curHP, maxHP);
        if (missing <= 0) { Refresh(); return; }
        if (!config.allowHealingIfKO && curHP <= 0) { Refresh(); return; }

        int kitsNeeded = HealingService.MedkitsToHealFull(missing, hpPerMedkit);
        int haveKits = ResourceBank.Get(ResourceType.Medkit);

        if (haveKits >= kitsNeeded && kitsNeeded > 0)
        {
            if (!ResourceBank.TrySpend(ResourceType.Medkit, kitsNeeded)) { Refresh(); return; }
        }
        else
        {
            if (haveKits > 0)
            {
                if (!ResourceBank.TrySpend(ResourceType.Medkit, haveKits)) { Refresh(); return; }
            }
            int creditsNeeded = HealingService.CreditsToHealFull(config, owned.level, missing);
            bool spent = ResourceManager.I != null
                ? ResourceManager.I.TrySpend(ResourceType.Credits, creditsNeeded)
                : ResourceBank.TrySpend(ResourceType.Credits, creditsNeeded);
            if (!spent) { Refresh(); return; }
        }

        // One authoritative write path.
        long now = SaveManager.NowUnix();
        OwnedMonsterHP.SetFull(ref owned, now, OwnedMonsterHP.Reason.HealButton);

        SaveManager.Data.team[teamIndex] = owned;
        SaveManager.Save();

        GameEvents.OnTeamChanged?.Invoke();
        GameEvents.OnTeamHealthChanged?.Invoke();
        GameEvents.OnResourcesChanged?.Invoke();

        Refresh();
    }
}
