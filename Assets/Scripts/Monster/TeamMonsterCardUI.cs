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

    [Header("Heal Settings")]
    [SerializeField, Range(0f, 1f)] private float partialHealPct = 0.25f;
    [SerializeField] private ResourceType healCostType = ResourceType.Coins;
    [SerializeField] private int partialHealCost = 1;
    [SerializeField] private int fullHealCost = 3;

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

    // OPTIONAL: if you ever add a “Full Heal” button
    private void OnClickHealFull()
    {
        TryHeal(partial: false);
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
        hpText.text = (maxHP > 0) ? $"HP: {Mathf.Max(0, curHP)}/{maxHP}" : "";
    }

    private void UpdateHealInteractable()
    {
        if (!healBtn) return;

        bool interactable = false;

        if (_def != null && _data != null)
        {
            int maxHP = HealingService.CalcMaxHP(_def, _data.level);
            int curHP = _data.currentHP >= 0 ? Mathf.Min(_data.currentHP, maxHP) : maxHP;
            bool needsHeal = curHP < maxHP;

            int have = GetResource(healCostType);
            bool canHeal = have >= partialHealCost;

            interactable = needsHeal && canHeal;
        }

        healBtn.interactable = interactable;
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
        if (curHP >= maxHP) { UpdateHealInteractable(); return; }

        int cost = partial ? partialHealCost : fullHealCost;
        if (!SpendResource(healCostType, cost)) { UpdateHealInteractable(); return; }

        int restore = partial
            ? Mathf.CeilToInt(maxHP * Mathf.Clamp01(partialHealPct))
            : (maxHP - curHP);

        _data.currentHP = Mathf.Clamp(curHP + restore, 0, maxHP);

        SaveManager.Save();
        UpdateHpText();
        UpdateHealInteractable();
        _onAnyChanged?.Invoke();
    }

    private int GetResource(ResourceType type) =>ResourceManager.I ? ResourceManager.I.Get(type) : 0;
    private bool SpendResource(ResourceType type, int amount) => ResourceManager.I && ResourceManager.I.TrySpend(type, amount);
}
