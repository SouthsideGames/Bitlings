using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealButtonController : MonoBehaviour
{
    // IMPORTANT:
    // This controller MUST be bound to a specific team slot index.
    // Leaving it at a default like 0 causes every heal button to target the first team member.
    [SerializeField] private int teamIndex = -1;

    [SerializeField] private Button healButton;
    [SerializeField] private TextMeshProUGUI costLabel;
    [SerializeField] private TextMeshProUGUI hpLabel;

    [SerializeField] private int hpPerMedkit = 50;

    private HealingConfigSO config;
    private MonsterLibrarySO library;

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
        if (library == null)
            library = Resources.Load<MonsterLibrarySO>("MonsterLibrary");
        if (config == null)
            config = Resources.Load<HealingConfigSO>("HealingConfig");

        _ready = library != null && config != null;

        // Ensure this button instance is wired to THIS component, not an inspector-stale target.
        if (healButton)
        {
            healButton.onClick.RemoveAllListeners();
            healButton.onClick.AddListener(OnClickHeal);
        }

        if (!_ready)
        {
            Debug.LogError(
                "[HealButtonController] Missing required Resources assets. " +
                $"MonsterLibrary loaded={library != null}, HealingConfig loaded={config != null}. " +
                "Heal UI will be disabled.",
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
        if (string.IsNullOrEmpty(owned.monsterId)) return;

        var def = (library != null) ? library.GetById(owned.monsterId) : null;
        if (def == null)
        {
            if (hpLabel != null) hpLabel.text = "HP: ?";
            if (costLabel != null) costLabel.text = "Missing Monster";
            if (healButton != null) healButton.interactable = false;
            return;
        }
        int maxHP = HealingService.CalcMaxHP(def, owned.level);
        int curHP = owned.currentHP >= 0 ? Mathf.Min(owned.currentHP, maxHP) : maxHP;
        int missing = HealingService.MissingHP(curHP, maxHP);

        int kitsNeeded = HealingService.MedkitsToHealFull(missing, hpPerMedkit);
        int creditsNeeded = HealingService.creditsToHealFull(config, owned.level, missing);

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
            int kitsShort = kitsNeeded - haveKits; 
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
        if (string.IsNullOrEmpty(owned.monsterId)) return;

        var def = (library != null) ? library.GetById(owned.monsterId) : null;
        if (def == null) { Refresh(); return; }
        int maxHP = HealingService.CalcMaxHP(def, owned.level);
        int curHP = owned.currentHP >= 0 ? Mathf.Min(owned.currentHP, maxHP) : maxHP;
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
            int creditsNeeded = HealingService.creditsToHealFull(config, owned.level, missing);
            bool spent = ResourceManager.I != null
                ? ResourceManager.I.TrySpend(ResourceType.Credits, creditsNeeded)
                : ResourceBank.TrySpend(ResourceType.Credits, creditsNeeded);
            if (!spent) { Refresh(); return; }
        }

        owned.currentHP = maxHP;
        SaveManager.Data.team[teamIndex] = owned;
        SaveManager.Save();

        GameEvents.OnTeamChanged?.Invoke();
        GameEvents.OnTeamHealthChanged?.Invoke();
        GameEvents.OnResourcesChanged?.Invoke();

        Refresh();
    }
}
