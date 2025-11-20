using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TeamMonsterCardUI : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Image img;
    [SerializeField] private TextMeshProUGUI hpText;

    [Header("Buttons")]
    [SerializeField] private Button rootButton;
    [SerializeField] private Button healBtn;

    [Header("Heal Settings (Coins Fallback)")]
    [SerializeField, Range(0f, 1f)] private float partialHealPct = 0.25f;
    [SerializeField] private ResourceType healCostType = ResourceType.Coins;
    [SerializeField] private int partialHealCost = 1;
    [SerializeField] private int fullHealCost = 3;

    [Header("Heal Settings (Medkits First)")]
    [SerializeField] private ResourceType medkitResourceType = ResourceType.Medkits;
    [SerializeField] private int partialHealMedkitCost = 1;
    [SerializeField] private int fullHealMedkitCost = 1;

    [Header("Evolution Alert")]
    [Tooltip("Shown when this monster is eligible to evolve.")]
    [SerializeField] private GameObject evolveAlert;   // assign a small icon GameObject in the prefab

    private OwnedMonsterData _data;
    private MonsterDataSO _def;
    private Action<OwnedMonsterData> _onClick;      // open detail
    private Action _onAnyChanged;

    public void Setup(
        OwnedMonsterData data,
        MonsterDataSO def,
        Action<OwnedMonsterData> onClick,
        Action onAnyChanged)
    {
        _data = data;
        _def = def;
        _onClick = onClick;
        _onAnyChanged = onAnyChanged;

        WireButtons();
        RefreshVisuals();
    }

    private void WireButtons()
    {
        if (rootButton)
        {
            rootButton.onClick.RemoveAllListeners();
            rootButton.onClick.AddListener(OnClickRoot);
        }

        if (healBtn)
        {
            healBtn.onClick.RemoveAllListeners();
            healBtn.onClick.AddListener(OnClickHealPartial); // simple one-click heal
        }
    }

    private void OnClickRoot()
    {
        _onClick?.Invoke(_data);
        AudioManager.I.PlayClick();
    }

    private void OnClickHealPartial()
    {
        TryHeal(partial: true);
        AudioManager.I.PlayClick();
    }

    // If you ever hook up a separate full-heal button, call this.
    private void OnClickHealFull()
    {
        TryHeal(partial: false);
        AudioManager.I.PlayClick();
    }

    private void OnEnable()
    {
        GameEvents.OnResourcesChanged += HandleResourcesChanged;
        GameEvents.OnTeamChanged += HandleResourcesChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnResourcesChanged -= HandleResourcesChanged;
        GameEvents.OnTeamChanged -= HandleResourcesChanged;
    }

    private void HandleResourcesChanged()
    {
        // Team changed or resources changed: HP, heal button, and evo alert might need updating.
        UpdateHpText();
        UpdateHealInteractable();
        RefreshEvolutionAlert();
    }

    public void RefreshVisuals()
    {
        if (img)
        {
            if (_def && _def.icon)
            {
                img.enabled = true;
                img.sprite = _def.icon;
            }
            else
            {
                img.enabled = false;
                img.sprite = null;
            }
        }

        UpdateHpText();
        UpdateHealInteractable();
        RefreshEvolutionAlert();
    }

    private void UpdateHpText()
    {
        if (!hpText || _def == null || _data == null) return;

        int maxHP = HealingService.CalcMaxHP(_def, _data.level);
        int curHP = _data.currentHP >= 0 ? Mathf.Min(_data.currentHP, maxHP) : maxHP;

        hpText.text = (maxHP > 0) ? $"HP: {Mathf.Max(0, curHP)}/{maxHP}" : string.Empty;
    }

    private void UpdateHealInteractable()
    {
        if (!healBtn) return;

        GameObject healGO = healBtn.gameObject;
        bool shouldBeActive = false;

        if (_def != null && _data != null)
        {
            int maxHP = HealingService.CalcMaxHP(_def, _data.level);
            int curHP = _data.currentHP >= 0 ? Mathf.Min(_data.currentHP, maxHP) : maxHP;
            bool needsHeal = curHP < maxHP;

            int medkits = GetResource(medkitResourceType);
            int coins = GetResource(healCostType);

            // For this card we assume the button uses partial heal costs
            bool canHealWithMedkits = medkits >= partialHealMedkitCost;
            bool canHealWithCoins = coins >= partialHealCost;
            bool canHeal = canHealWithMedkits || canHealWithCoins;

            shouldBeActive = needsHeal && canHeal;
        }

        healGO.SetActive(shouldBeActive);
        healBtn.interactable = shouldBeActive;
    }

    private void RefreshEvolutionAlert()
    {
        if (!evolveAlert) return;

        bool show = false;
        if (_data != null && _def != null)
        {
            show = EvolutionHelper.CanEvolve(_data, _def);
        }

        evolveAlert.SetActive(show);
    }

    private void TryHeal(bool partial)
    {
        if (_def == null || _data == null) return;

        int maxHP = HealingService.CalcMaxHP(_def, _data.level);
        int curHP = _data.currentHP >= 0 ? Mathf.Min(_data.currentHP, maxHP) : maxHP;
        if (curHP >= maxHP)
        {
            UpdateHealInteractable();
            return;
        }

        // ----- RESOURCE CHOICE: use medkits first, then coins -----
        int medkitCost = partial ? partialHealMedkitCost : fullHealMedkitCost;
        int coinCost = partial ? partialHealCost : fullHealCost;

        bool paid = false;

        int medkits = GetResource(medkitResourceType);
        if (medkits >= medkitCost && medkitCost > 0)
        {
            paid = SpendResource(medkitResourceType, medkitCost);
        }
        else if (coinCost > 0)
        {
            paid = SpendResource(healCostType, coinCost);
        }

        if (!paid)
        {
            UpdateHealInteractable();
            return;
        }

        int restore = partial
            ? Mathf.CeilToInt(maxHP * Mathf.Clamp01(partialHealPct))
            : (maxHP - curHP);

        _data.currentHP = Mathf.Clamp(curHP + restore, 0, maxHP);

        SaveManager.Save();
        UpdateHpText();
        UpdateHealInteractable();
        _onAnyChanged?.Invoke();
    }

    private int GetResource(ResourceType type) =>
        ResourceManager.I ? ResourceManager.I.Get(type) : 0;

    private bool SpendResource(ResourceType type, int amount) =>
        ResourceManager.I && ResourceManager.I.TrySpend(type, amount);
}
