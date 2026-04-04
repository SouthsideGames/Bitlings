using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────────────────────
// DuplicateResolutionPanelUI — overlay shown when a duplicate
// Bitling is caught. Presents Train / Broker / Fulfill Request.
// ─────────────────────────────────────────────────────────────

public class DuplicateResolutionPanelUI : MonoBehaviour
{
    public static DuplicateResolutionPanelUI I;

    [Header("Species Display")]
    [SerializeField] private Image speciesIcon;
    [SerializeField] private TextMeshProUGUI speciesNameLabel;
    [SerializeField] private TextMeshProUGUI typeLabel;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private TextMeshProUGUI marketValueLabel;
    [SerializeField] private TextMeshProUGUI demandTrendLabel;

    [Header("Actions")]
    [SerializeField] private Button trainButton;
    [SerializeField] private TextMeshProUGUI trainLabel;
    [SerializeField] private Button brokerButton;
    [SerializeField] private TextMeshProUGUI brokerPayoutLabel;
    [SerializeField] private Button fulfillButton;
    [SerializeField] private TextMeshProUGUI fulfillRewardLabel;
    [SerializeField] private GameObject fulfillSection;

    [Header("Request List (optional)")]
    [SerializeField] private GameObject requestListRoot;
    [SerializeField] private Transform requestListParent;
    [SerializeField] private GameObject requestEntryPrefab;

    private const int DUPLICATE_LEVELUP_STAT_POINTS = 3;
    private const string TutDuplicate = "tut_exchange_duplicate_v1";
    private List<ActiveRequest> _matchingRequests = new List<ActiveRequest>();
    private ActiveRequest _selectedRequest;

    void Awake()
    {
        I = this;
    }

    void OnEnable()
    {
        Populate();
        TutorialOverlayPanel.RequestOpen(TutDuplicate);
    }

    void OnDisable()
    {
        _matchingRequests.Clear();
        _selectedRequest = null;
    }

    // ─────────── Population ───────────

    private void Populate()
    {
        if (!PendingDuplicateCapture.HasPending)
            TryRestorePendingDuplicateFromSave();

        if (!PendingDuplicateCapture.HasPending) return;

        var def = PendingDuplicateCapture.Def;
        var existing = PendingDuplicateCapture.Existing;
        bool isMax = PendingDuplicateCapture.IsMaxLevel;
        bool isPremium = PendingDuplicateCapture.IsPremium;

        // Species display
        if (speciesIcon != null)
        {
            Sprite icon = (isPremium && def.premiumIcon != null) ? def.premiumIcon : def.icon;
            speciesIcon.sprite = icon;
        }
        if (speciesNameLabel != null) speciesNameLabel.text = def.displayName;
        if (typeLabel != null)
        {
            typeLabel.text = def.type.ToString();
            typeLabel.color = TypeColorLibrary.Get(def.type);
        }

        // Level display
        if (levelLabel != null)
        {
            if (isMax)
                levelLabel.text = $"Lv {existing.level} (Max)";
            else
                levelLabel.text = $"Lv {existing.level} → {existing.level + 1}";
        }

        // Market value
        int currentValue = ExchangeManager.I != null ? ExchangeManager.I.GetCurrentValue(def.id) : def.baseMarketValue;
        if (marketValueLabel != null) marketValueLabel.text = $"Market Value: {currentValue} Credits";

        // Demand & Trend
        if (demandTrendLabel != null)
        {
            var state = ExchangeManager.I != null ? ExchangeManager.I.GetState(def.id) : null;
            if (state != null)
            {
                string demandText = state.demandLevel switch
                {
                    DemandLevel.Low    => "LOW",
                    DemandLevel.Medium => "STEADY",
                    DemandLevel.High   => "HIGH",
                    DemandLevel.Surge  => "SURGE",
                    _                  => "STEADY"
                };
                string trendText = state.trend switch
                {
                    TrendDirection.Rising  => "\u25B2 Rising",
                    TrendDirection.Falling => "\u25BC Falling",
                    _                      => "\u2192 Stable"
                };
                demandTrendLabel.text = $"Demand: {demandText}     Trend: {trendText}";
            }
            else
            {
                demandTrendLabel.text = "Demand: STEADY     Trend: \u2192 Stable";
            }
        }

        // Train button
        if (trainButton != null)
        {
            trainButton.onClick.RemoveAllListeners();
            if (isMax)
            {
                int cores = CalcConversionCores(def, PendingDuplicateCapture.EncounterLevel);
                if (trainLabel != null) trainLabel.text = $"Convert to Cores (+{cores})";
                trainButton.onClick.AddListener(() => OnTrainMaxLevel(def, cores));
            }
            else
            {
                if (trainLabel != null) trainLabel.text = "Train (Level Up)";
                trainButton.onClick.AddListener(() => OnTrain(existing, def));
            }
        }

        // Broker button
        int payout = ExchangeManager.I != null
            ? ExchangeManager.I.GetBrokerPayout(def.id, isPremium)
            : Mathf.Max(1, Mathf.RoundToInt(currentValue * 0.85f));

        if (brokerButton != null)
        {
            brokerButton.onClick.RemoveAllListeners();
            brokerButton.onClick.AddListener(() => OnBroker(def, payout));
        }
        if (brokerPayoutLabel != null) brokerPayoutLabel.text = $"+{payout} Credits";

        // Fulfill section
        _matchingRequests.Clear();
        if (ExchangeRequestManager.I != null)
            _matchingRequests = ExchangeRequestManager.I.GetMatchingRequests(def.id);

        bool hasRequests = _matchingRequests.Count > 0;
        if (fulfillSection != null) fulfillSection.SetActive(hasRequests);

        if (hasRequests)
        {
            _selectedRequest = _matchingRequests[0];
            UpdateFulfillDisplay();
            PopulateRequestList(def);
        }

        if (fulfillButton != null)
        {
            fulfillButton.onClick.RemoveAllListeners();
            fulfillButton.onClick.AddListener(() => OnFulfill(def));
        }
    }

    private void UpdateFulfillDisplay()
    {
        if (_selectedRequest == null || fulfillRewardLabel == null) return;
        string bonus = _selectedRequest.bonusResourceAmount > 0
            ? $" + {_selectedRequest.bonusResourceAmount} {_selectedRequest.bonusResourceType}"
            : "";
        fulfillRewardLabel.text = $"+{_selectedRequest.creditReward} Credits{bonus}";
    }

    private void PopulateRequestList(MonsterDataSO def)
    {
        if (requestListRoot == null || requestListParent == null || requestEntryPrefab == null) return;

        // Clear existing entries
        for (int i = requestListParent.childCount - 1; i >= 0; i--)
            Destroy(requestListParent.GetChild(i).gameObject);

        if (_matchingRequests.Count <= 1)
        {
            requestListRoot.SetActive(false);
            return;
        }

        requestListRoot.SetActive(true);
        for (int i = 0; i < _matchingRequests.Count; i++)
        {
            var req = _matchingRequests[i];
            var go = Instantiate(requestEntryPrefab, requestListParent);
            go.SetActive(true);

            var label = go.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                string bonus = req.bonusResourceAmount > 0
                    ? $" + {req.bonusResourceAmount} {req.bonusResourceType}"
                    : "";
                label.text = $"{req.flavorText ?? "Request"}: +{req.creditReward} Credits{bonus}";
            }

            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                var captured = req;
                btn.onClick.AddListener(() =>
                {
                    _selectedRequest = captured;
                    UpdateFulfillDisplay();
                });
            }
        }
    }

    // ─────────── Actions ───────────

    private void OnTrain(OwnedMonsterData existing, MonsterDataSO def)
    {
        if (existing == null || def == null) return;
        if (trainButton != null) trainButton.interactable = false;

        int before = existing.level;
        ApplyDuplicateCaptureLevelUp(existing, def, DUPLICATE_LEVELUP_STAT_POINTS);
        SyncOwnedToTeam(existing);

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        GameEvents.MonsterCaptured?.Invoke(def.id, def.type);

        string key = !string.IsNullOrEmpty(existing.ownedUID) ? existing.ownedUID : existing.monsterId;
        GameEvents.MonsterLeveled?.Invoke(key, existing.level);
        GameEvents.RaiseToast($"{def.displayName} trained! Lv {before} → {existing.level}");

        Close();
    }

    private void OnTrainMaxLevel(MonsterDataSO def, int cores)
    {
        if (cores > 0)
            ResourceManager.I?.Add(ResourceType.GrowthCore, cores);

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        GameEvents.MonsterCaptured?.Invoke(def.id, def.type);
        GameEvents.RaiseToast($"{def.displayName} (max level) converted to +{cores} Growth Cores");

        Close();
    }

    private void OnBroker(MonsterDataSO def, int payout)
    {
        if (brokerButton != null) brokerButton.interactable = false;

        if (payout > 0)
            ResourceBank.Add(ResourceType.Credits, payout);

        // Track stats
        if (ExchangeManager.I != null)
        {
            var save = ExchangeManager.I.SaveData;
            if (save != null)
            {
                save.totalBrokered++;
                save.totalCreditsBrokered += payout;
            }
        }

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        GameEvents.MonsterCaptured?.Invoke(def.id, def.type);
        GameEvents.MonsterBrokered?.Invoke(def.id, payout);
        GameEvents.RaiseToast($"{def.displayName} brokered for +{payout} Credits");

        Close();
    }

    private void OnFulfill(MonsterDataSO def)
    {
        if (_selectedRequest == null || ExchangeRequestManager.I == null) return;
        if (fulfillButton != null) fulfillButton.interactable = false;

        int reward = ExchangeRequestManager.I.TryFulfillRequest(_selectedRequest.requestId, def.id);
        if (reward <= 0)
        {
            GameEvents.RaiseToast("Request could not be fulfilled.");
            return;
        }

        GameEvents.MonsterCaptured?.Invoke(def.id, def.type);
        GameEvents.RaiseToast($"{def.displayName} placed! Request fulfilled for +{reward} Credits");

        Close();
    }

    private void Close()
    {
        PendingDuplicateCapture.Clear();
        ClearPersistedPendingDuplicate();
        if (UIManager.I != null)
        {
            UIManager.I.Hide(PanelId.DuplicateResolution);

            // Reopen the Encounter panel so the player isn't left on a black screen.
            if (!UIManager.I.IsOpen(PanelId.Encounter))
                UIManager.I.Show(PanelId.Encounter);
        }
    }

    private static void TryRestorePendingDuplicateFromSave()
    {
        try
        {
            var save = SaveManager.GetExchangeBlob();
            var p = save?.pendingDuplicate;
            if (p == null || string.IsNullOrEmpty(p.speciesId)) return;

            var def = MonsterCatalog.GetById(p.speciesId);
            if (def == null)
            {
                ClearPersistedPendingDuplicate();
                return;
            }

            OwnedMonsterData existing = null;
            var owned = SaveManager.Data?.owned;
            if (owned != null)
            {
                for (int i = 0; i < owned.Count; i++)
                {
                    var entry = owned[i];
                    if (entry == null) continue;

                    if (!string.IsNullOrEmpty(p.ownedUID) && !string.IsNullOrEmpty(entry.ownedUID) &&
                        string.Equals(entry.ownedUID, p.ownedUID, StringComparison.Ordinal))
                    {
                        existing = entry;
                        break;
                    }

                    if (existing == null && string.Equals(entry.monsterId, p.speciesId, StringComparison.Ordinal))
                        existing = entry;
                }
            }

            if (existing == null)
            {
                ClearPersistedPendingDuplicate();
                return;
            }

            PendingDuplicateCapture.Set(existing, def, Mathf.Max(1, p.encounterLevel), p.isPremium, p.isMaxLevel);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DuplicateResolution] TryRestorePendingDuplicate failed: {ex.Message}");
        }
    }

    private static void ClearPersistedPendingDuplicate()
    {
        try
        {
            var save = SaveManager.GetExchangeBlob();
            if (save == null || save.pendingDuplicate == null) return;
            save.pendingDuplicate = null;
            SaveManager.SetExchangeBlob(save);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DuplicateResolution] ClearPersistedPendingDuplicate failed: {ex.Message}");
        }
    }

    // ─────────── Duplicate level-up (mirrors EncounterManager logic) ───────────

    private static void ApplyDuplicateCaptureLevelUp(OwnedMonsterData target, MonsterDataSO def, int pointsPerLevel)
    {
        if (target == null) return;

        target.level = Mathf.Max(1, target.level + 1);
        target.unspentStatPoints += Mathf.Max(0, pointsPerLevel);

        if (target.isPremium)
            target.premiumTier = Mathf.Max(1, target.premiumTier);
        else
            target.premiumTier = 0;

        if (def != null)
        {
            int totalMaxHP = HealingService.CalcMaxHP(def, target.level, includeTraining: true, includeTitles: false);
            if (target.currentHP > totalMaxHP)
                target.currentHP = totalMaxHP;
        }
    }

    private static void SyncOwnedToTeam(OwnedMonsterData owned)
    {
        var data = SaveManager.Data;
        if (data?.team == null || owned == null) return;

        for (int i = 0; i < data.team.Count; i++)
        {
            var t = data.team[i];
            if (t == null) continue;

            bool match = false;
            if (!string.IsNullOrEmpty(owned.ownedUID) && !string.IsNullOrEmpty(t.ownedUID))
                match = string.Equals(t.ownedUID, owned.ownedUID, System.StringComparison.Ordinal);
            else if (!string.IsNullOrEmpty(owned.monsterId))
                match = string.Equals(t.monsterId, owned.monsterId, System.StringComparison.Ordinal)
                        && t.isPremium == owned.isPremium;

            if (!match) continue;

            t.level = owned.level;
            t.currentXP = owned.currentXP;
            SaveManager.SetTeamSlotHPExact(i, owned.currentHP, owned.lastHPUnix, save: false, fireEvents: false);
            t.flatAtkBonus = owned.flatAtkBonus;
            t.isTraining = owned.isTraining;
            t.trainingLastUnix = owned.trainingLastUnix;
            t.pendingLevels = owned.pendingLevels;
            t.lastLevelClaimDay = owned.lastLevelClaimDay;
            t.isPremium = owned.isPremium;
            t.premiumTier = owned.premiumTier;
            t.trainingBonus = owned.trainingBonus;
            t.autoApply = owned.autoApply;
            t.autoApplyTargetLevel = owned.autoApplyTargetLevel;
            t.lastBucketId = owned.lastBucketId;
            t.unspentStatPoints = owned.unspentStatPoints;
        }
    }

    private static int CalcConversionCores(MonsterDataSO def, int encounterLevel)
    {
        if (def == null) return 0;
        int baseCores = Mathf.Max(1, 2 + Mathf.Max(1, encounterLevel));
        float rarityMul;
        switch (def.rarity)
        {
            case Rarity.Common:    rarityMul = 1.00f; break;
            case Rarity.Uncommon:  rarityMul = 1.10f; break;
            case Rarity.Rare:      rarityMul = 1.25f; break;
            case Rarity.Epic:      rarityMul = 1.40f; break;
            case Rarity.Legendary: rarityMul = 1.60f; break;
            case Rarity.Mythic:    rarityMul = 1.80f; break;
            default:               rarityMul = 1.00f; break;
        }
        return Mathf.Clamp(Mathf.RoundToInt(baseCores * rarityMul), 1, 250);
    }
}
