using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────────────────────
// ExchangeRequestRowUI — a single row in the Exchange Requests
// tab, showing a wanted species request and its reward.
// ─────────────────────────────────────────────────────────────

public class ExchangeRequestRowUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI requirementLabel;
    [SerializeField] private TextMeshProUGUI rewardLabel;
    [SerializeField] private TextMeshProUGUI timeRemainingLabel;
    [SerializeField] private Button fulfillButton;
    [SerializeField] private TextMeshProUGUI fulfillButtonLabel;
    [SerializeField] private TextMeshProUGUI flavorLabel;

    private ActiveRequest _request;

    public void Populate(ActiveRequest request)
    {
        _request = request;
        if (request == null) return;

        // Species icon & requirement text
        if (!string.IsNullOrEmpty(request.requiredSpeciesId))
        {
            var def = MonsterCatalog.GetById(request.requiredSpeciesId);
            if (def != null)
            {
                if (requirementLabel != null) requirementLabel.text = $"Wanted: {def.displayName}";
            }
            else
            {
                if (requirementLabel != null) requirementLabel.text = $"Wanted: {request.requiredSpeciesId}";
            }
        }
        else
        {
            // Generic request
            string typeStr = request.requiredType != MonsterType.None ? request.requiredType.ToString() : "Any";
            string rarityStr = request.requiredMinRarity != Rarity.Common ? $"{request.requiredMinRarity}+" : "";
            if (requirementLabel != null) requirementLabel.text = $"Wanted: {rarityStr} {typeStr} Bitling";
        }

        // Reward
        if (rewardLabel != null)
        {
            string bonus = request.bonusResourceAmount > 0
                ? $" + {request.bonusResourceAmount} {request.bonusResourceType}"
                : "";
            rewardLabel.text = $"+{request.creditReward} Credits{bonus}";
        }

        // Time remaining
        if (timeRemainingLabel != null)
        {
            if (request.expiresUnix > 0)
            {
                long remaining = request.expiresUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (remaining > 0)
                {
                    int hours = (int)(remaining / 3600);
                    int minutes = (int)((remaining % 3600) / 60);
                    timeRemainingLabel.text = hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
                }
                else
                {
                    timeRemainingLabel.text = "Expired";
                }
            }
            else
            {
                timeRemainingLabel.text = "";
            }
        }

        // Flavor text
        if (flavorLabel != null)
        {
            flavorLabel.text = request.flavorText ?? "";
            flavorLabel.gameObject.SetActive(!string.IsNullOrEmpty(request.flavorText));
        }

        // Fulfill button state — check if player owns a matching species
        bool canFulfill = CanPlayerFulfill(request);
        if (fulfillButton != null) fulfillButton.interactable = canFulfill;
        if (fulfillButtonLabel != null)
            fulfillButtonLabel.text = canFulfill ? "Place Bitling" : "None Available";

        if (fulfillButton != null)
        {
            fulfillButton.onClick.RemoveAllListeners();
            if (canFulfill)
                fulfillButton.onClick.AddListener(OnFulfillClicked);
        }
    }

    private void OnFulfillClicked()
    {
        if (_request == null || ExchangeRequestManager.I == null) return;

        // Find a matching species the player owns
        OwnedMonsterData ownedMatch = FindOwnedMatchingOwned(_request);
        if (ownedMatch == null)
        {
            GameEvents.RaiseToast("No matching Bitling available.");
            return;
        }

        // Show confirmation — the monster is permanently removed on accept
        if (ExchangePanelUI.I != null)
            ExchangePanelUI.I.ShowFulfillConfirmation(_request, ownedMatch);
    }

    private bool CanPlayerFulfill(ActiveRequest request)
    {
        if (request.expiresUnix > 0 && request.expiresUnix <= SaveManager.NowUnix())
            return false;
        return FindOwnedMatchingOwned(request) != null;
    }

    private OwnedMonsterData FindOwnedMatchingOwned(ActiveRequest request)
    {
        var data = SaveManager.Data;
        if (data?.owned == null || request == null) return null;

        for (int i = 0; i < data.owned.Count; i++)
        {
            var o = data.owned[i];
            if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;

            if (!string.IsNullOrEmpty(request.requiredSpeciesId))
            {
                if (string.Equals(o.monsterId, request.requiredSpeciesId, StringComparison.Ordinal))
                    return o;
            }
            else
            {
                var def = MonsterCatalog.GetById(o.monsterId);
                if (def == null) continue;
                bool typeOk = request.requiredType == MonsterType.None || request.requiredType == def.type;
                bool rarityOk = (int)def.rarity >= (int)request.requiredMinRarity;
                if (typeOk && rarityOk) return o;
            }
        }
        return null;
    }

}
