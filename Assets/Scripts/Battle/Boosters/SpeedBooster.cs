using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SpeedBooster : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button boosterBtn;
    [SerializeField] private Image boosterRadial;
    [SerializeField] private TextMeshProUGUI boosterCountLabel;

    [Header("Cost")]
    [SerializeField] private bool consumeItem = true;

    private Coroutine _activeLoop;

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
        EnsureActiveLoop();
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= HandleRefresh;
        GameEvents.OnBoostersChanged -= HandleRefresh;

        if (boosterBtn) boosterBtn.onClick.RemoveAllListeners();

        StopActiveLoop();
        if (boosterRadial) boosterRadial.fillAmount = 0f;
    }

    private void HandleRefresh()
    {
        RefreshAll(hard: false);
        EnsureActiveLoop();
    }

    private void RefreshAll(bool hard)
    {
        RefreshCounts();
        RefreshInteractability();
    }

    private void RefreshCounts()
    {
        int count = ResourceBank.Get(ResourceType.EfficiencyVoucher);
        if (boosterCountLabel) boosterCountLabel.text = $"{count}";
    }

    private void RefreshInteractability()
    {
        var ctrl = BattleBoosterController.I;

        bool can = ctrl && ctrl.CanUse(BoosterType.Speed, out _);
        bool haveItem = !consumeItem || ResourceBank.Get(ResourceType.EfficiencyVoucher) > 0;

        if (boosterBtn) boosterBtn.interactable = can && haveItem;

        if (boosterRadial && ctrl != null)
        {
            var (rem, max) = ctrl.Active(BoosterType.Speed);
            boosterRadial.fillAmount = (rem > 0 && max > 0) ? (float)rem / max : 0f;
        }
        else if (boosterRadial)
        {
            boosterRadial.fillAmount = 0f;
        }
    }

    private void EnsureActiveLoop()
    {
        var ctrl = BattleBoosterController.I;
        if (ctrl == null)
        {
            StopActiveLoop();
            return;
        }

        var (rem, max) = ctrl.Active(BoosterType.Speed);
        bool isActive = rem > 0 && max > 0;

        if (isActive && _activeLoop == null)
            _activeLoop = StartCoroutine(ActiveRadialLoop());
        else if (!isActive && _activeLoop != null)
            StopActiveLoop();
    }

    private System.Collections.IEnumerator ActiveRadialLoop()
    {
        while (true)
        {
            var ctrl = BattleBoosterController.I;
            if (ctrl == null) break;

            var (rem, max) = ctrl.Active(BoosterType.Speed);
            if (rem <= 0 || max <= 0) break;

            if (boosterRadial)
                boosterRadial.fillAmount = (float)rem / max;

            yield return new WaitForSecondsRealtime(0.10f);
        }

        _activeLoop = null;
        RefreshInteractability();
    }

    private void StopActiveLoop()
    {
        if (_activeLoop != null)
        {
            StopCoroutine(_activeLoop);
            _activeLoop = null;
        }
    }

    private void OnPress()
    {
        var ctrl = BattleBoosterController.I;
        if (!ctrl)
        {
            BattleLogger.Log("Booster controller not found.", LogScope.Battle);
            return;
        }

        if (!ctrl.CanUse(BoosterType.Speed, out var why))
        {
            BattleLogger.Log(string.IsNullOrEmpty(why) ? "Cannot use Speed Booster right now." : why, LogScope.Battle);
            RefreshAll(true);
            return;
        }

        bool spent = true;
        if (consumeItem)
        {
            spent = ResourceBank.TrySpend(ResourceType.EfficiencyVoucher, 1);
            if (!spent)
            {
                RefreshAll(true);
                return;
            }
        }

        bool used = ctrl.TryUseFromUI(BoosterType.Speed, out var msg);

        if (!used && consumeItem && spent)
        {
            ResourceBank.Add(ResourceType.EfficiencyVoucher, 1);
            BattleLogger.Log("Speed Booster failed to activate; item refunded.", LogScope.Battle);
        }
        else if (used)
        {
            if (!string.IsNullOrEmpty(msg))
                BattleLogger.Log(msg, LogScope.Battle);

            GameEvents.OnResourcesChanged?.Invoke();
        }

        RefreshAll(true);
        EnsureActiveLoop();
    }
}
