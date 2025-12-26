using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealButtonController : MonoBehaviour
{
    [SerializeField] private int teamIndex = 0;

    [SerializeField] private Button healButton;
    [SerializeField] private TextMeshProUGUI costLabel;
    [SerializeField] private TextMeshProUGUI hpLabel;

    [SerializeField] private int hpPerMedkit = 50;

    private HealingConfigSO config;
    private MonsterLibrarySO library;

    void Awake()
    {
        if (library == null)
            library = Resources.Load<MonsterLibrarySO>("MonsterLibrary");
        if (config == null)
            config = Resources.Load<HealingConfigSO>("HealingConfig");
    }

    void OnEnable() { Refresh(); }

    public void Refresh()
    {
        var team = SaveManager.Data?.team;
        if (team == null || teamIndex < 0 || teamIndex >= team.Count) return;

        var owned = team[teamIndex];
        if (string.IsNullOrEmpty(owned.monsterId)) return;

        var def = library.GetById(owned.monsterId);
        int maxHP = HealingService.CalcMaxHP(def, owned.level);
        int curHP = owned.currentHP >= 0 ? Mathf.Min(owned.currentHP, maxHP) : maxHP;
        int missing = HealingService.MissingHP(curHP, maxHP);

        int kitsNeeded = HealingService.MedkitsToHealFull(missing, hpPerMedkit);
        int creditsNeeded = HealingService.creditsToHealFull(config, owned.level, missing);

        int haveKits = ResourceBank.Get(ResourceType.Medkit);
        bool useKitsOnly = haveKits >= kitsNeeded && kitsNeeded > 0;
        bool needFallback = (kitsNeeded > 0 && haveKits < kitsNeeded);

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
            int kitsShort = kitsNeeded - haveKits; // you already computed kitsNeeded & haveKits
            int credits     = ResourceManager.I.Get(ResourceType.Credits);

            costLabel.text = $"{haveKits} Medkits + {creditsNeeded} credits";
            healButton.interactable = (credits >= creditsNeeded) || (haveKits > 0);
        }
        else
        {
            int credits = ResourceManager.I.Get(ResourceType.Credits);

            costLabel.text = $"{creditsNeeded} credits";
            healButton.interactable = (credits >= creditsNeeded);
        }
    }

    public void OnClickHeal()
    {
        var team = SaveManager.Data?.team;
        if (team == null || teamIndex < 0 || teamIndex >= team.Count) return;

        var owned = team[teamIndex];
        if (string.IsNullOrEmpty(owned.monsterId)) return;

        var def = library.GetById(owned.monsterId);
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
                // spend what you have, then cover rest with credits
                if (!ResourceBank.TrySpend(ResourceType.Medkit, haveKits)) { Refresh(); return; }
            }
            int creditsNeeded = HealingService.creditsToHealFull(config, owned.level, missing);
            if (!ResourceManager.I.TrySpend(ResourceType.Credits, creditsNeeded)) { Refresh(); return; }
        }

        owned.currentHP = maxHP;
        SaveManager.Data.team[teamIndex] = owned;
        SaveManager.Save();

        GameEvents.OnTeamChanged?.Invoke();
        GameEvents.OnResourcesChanged?.Invoke();

        Refresh();
    }
}
