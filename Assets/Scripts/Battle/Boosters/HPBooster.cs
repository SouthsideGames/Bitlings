using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HPBooster : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button boosterBtn;
    [SerializeField] private Image boosterRadial; // HP has no duration -> stays 0
    [SerializeField] private TextMeshProUGUI boosterCountLabel;

    [Header("Cost")]
    [SerializeField] private bool consumeItem = true;

    void OnEnable()
    {
        if (!boosterBtn) boosterBtn = GetComponent<Button>();
        if (boosterBtn)
        {
            boosterBtn.onClick.RemoveAllListeners();
            boosterBtn.onClick.AddListener(OnPress);
        }

        if (boosterRadial) boosterRadial.fillAmount = 0f;

        GameEvents.OnResourcesChanged += HandleRefresh;
        GameEvents.OnBoostersChanged += HandleRefresh;

        RefreshAll(hard: true);
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= HandleRefresh;
        GameEvents.OnBoostersChanged -= HandleRefresh;

        if (boosterBtn) boosterBtn.onClick.RemoveAllListeners();
        if (boosterRadial) boosterRadial.fillAmount = 0f;
    }

    private void HandleRefresh() => RefreshAll(hard: false);

    private void RefreshAll(bool hard)
    {
        RefreshCounts();
        RefreshInteractability();
    }

    private void RefreshCounts()
    {
        int have = ResourceBank.Get(ResourceType.WellnessVoucher);
        if (boosterCountLabel) boosterCountLabel.text = $"x{have}";
    }

    private void RefreshInteractability()
    {
        var ctrl = BattleBoosterController.I;

        bool can = ctrl && ctrl.CanUse(BoosterType.Health, out _);
        bool haveItem = !consumeItem || ResourceBank.Get(ResourceType.WellnessVoucher) > 0;

        if (boosterBtn) boosterBtn.interactable = can && haveItem;
        if (boosterRadial) boosterRadial.fillAmount = 0f; // instant
    }

    private void OnPress()
    {
        var ctrl = BattleBoosterController.I;
        if (!ctrl)
        {
            BattleLogger.Log("Booster controller not found.", LogScope.Battle);
            return;
        }

        if (!ctrl.CanUse(BoosterType.Health, out var why))
        {
            if (!string.IsNullOrEmpty(why))
                BattleLogger.Log(why, LogScope.Battle);

            RefreshAll(true);
            return;
        }

        bool spent = true;
        if (consumeItem)
        {
            spent = ResourceBank.TrySpend(ResourceType.WellnessVoucher, 1);
            if (!spent)
            {
                RefreshAll(true);
                return;
            }
        }

        bool used = ctrl.TryUseFromUI(BoosterType.Health, out var msg);

        if (!used && consumeItem && spent)
        {
            ResourceBank.Add(ResourceType.WellnessVoucher, 1);
            BattleLogger.Log("HP Boost failed to activate; item refunded.", LogScope.Battle);
        }
        else if (used)
        {
            if (!string.IsNullOrEmpty(msg))
                BattleLogger.Log(msg, LogScope.Battle);

            GameEvents.OnResourcesChanged?.Invoke();
        }

        RefreshAll(true);
    }
}
