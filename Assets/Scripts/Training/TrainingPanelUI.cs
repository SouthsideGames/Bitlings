using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class TrainingPanelUI : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private TextMeshProUGUI headerText;

    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private GameObject itemPrefab;

    [Header("Actions")]
    [SerializeField] private Button claimOneBtn;
    [SerializeField] private Button claimAllBtn;

    private readonly List<MonsterTrainingItemUI> _items = new();
    private OwnedMonsterData _current;

    void Awake()
    {
        if (claimOneBtn)
        {
            claimOneBtn.onClick.RemoveAllListeners();
            claimOneBtn.onClick.AddListener(OnClickClaimOne);
        }

        if (claimAllBtn)
        {
            claimAllBtn.onClick.RemoveAllListeners();
            claimAllBtn.onClick.AddListener(OnClickClaimAll);
        }
    }

    void OnEnable()
    {
        // Ensure offline training has just been applied if coming from a resume
        TrainingManager.I?.ProcessOfflineTrainingAll();

        BuildList();
        RefreshHeader();
        RefreshActionButtons();

        // Listen to changes that affect XP/levels/list
        GameEvents.OnResourcesChanged += OnAnyTrainingRelatedChange;
        GameEvents.MonsterLeveled += OnMonsterLeveled;
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= OnAnyTrainingRelatedChange;
        GameEvents.MonsterLeveled -= OnMonsterLeveled;
    }

    void OnAnyTrainingRelatedChange()
    {
        // Refresh rows quickly; avoids reconstructing the list unless necessary
        RefreshAllRows();
        RefreshHeader();
        RefreshActionButtons();
    }

    void OnMonsterLeveled(string monsterId, int newLevel)
    {
        RefreshAllRows();
        if (_current != null && _current.monsterId == monsterId)
        {
            RefreshHeader();
            RefreshActionButtons();
        }
    }

    public void BuildList()
    {
        // Clear old
        for (int i = listRoot.childCount - 1; i >= 0; i--)
            Destroy(listRoot.GetChild(i).gameObject);

        _items.Clear();
        _current = null;

        var data = SaveManager.Data;
        if (data?.owned == null || data.owned.Count == 0) return;

        foreach (var om in data.owned)
        {
            if (om == null) continue;
            var go = Instantiate(itemPrefab, listRoot);
            var ui = go.GetComponent<MonsterTrainingItemUI>();
            if (!ui) continue;

            ui.Setup(om, OnSelected);
            _items.Add(ui);

            if (_current == null) _current = om;
        }
    }

    void OnSelected(OwnedMonsterData om)
    {
        _current = om;
        RefreshHeader();
        RefreshActionButtons();
    }

    void RefreshAllRows()
    {
        foreach (var ui in _items)
            if (ui) ui.RefreshUI();
    }

    void OnClickClaimOne()
    {
        if (!TrainingManager.I || _current == null) return;
        if (_current.level >= LevelRules.MaxLevel) return;
        if (!TrainingManager.I.CanClaimLevel(_current)) return;

        TrainingManager.I.ClaimOneLevel(_current);

        RefreshHeader();
        RefreshAllRows();
        RefreshActionButtons();
    }

    void OnClickClaimAll()
    {
        if (!TrainingManager.I) return;

        var data = SaveManager.Data;
        if (data?.owned == null) return;

        bool any = false;

        // Respect daily limit: one claim per eligible monster.
        foreach (var om in data.owned)
        {
            if (om == null) continue;
            if (om.level >= LevelRules.MaxLevel) continue;
            if (!TrainingManager.I.CanClaimLevel(om)) continue;

            if (TrainingManager.I.ClaimOneLevel(om))
                any = true;
        }

        if (any) SaveManager.Save();

        RefreshHeader();
        RefreshAllRows();
        RefreshActionButtons();
    }

    void RefreshHeader()
    {
        if (!headerText) return;

        if (_current == null)
        {
            headerText.text = "No monster selected";
            return;
        }

        var def = MonsterLibraryLocator.GetById(_current.monsterId);
        string name = def ? def.displayName : _current.monsterId;

        string xpPart = (_current.level >= LevelRules.MaxLevel)
            ? "MAX"
            : $"{_current.currentXP}/{LevelRules.XPToNext(_current.level)} XP";

        headerText.text = $"{name} • L{_current.level} • {xpPart}";
    }

    void RefreshActionButtons()
    {
        bool canOne = TrainingManager.I && _current != null &&
                      _current.level < LevelRules.MaxLevel &&
                      TrainingManager.I.CanClaimLevel(_current);

        if (claimOneBtn) claimOneBtn.interactable = canOne;

        bool canAny = false;
        var data = SaveManager.Data;
        if (TrainingManager.I && data?.owned != null)
        {
            foreach (var om in data.owned)
            {
                if (om == null) continue;
                if (om.level < LevelRules.MaxLevel && TrainingManager.I.CanClaimLevel(om))
                {
                    canAny = true;
                    break;
                }
            }
        }

        if (claimAllBtn) claimAllBtn.interactable = canAny;
    }
}
