using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Reflection;

public class SpeedBooster : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button boosterBtn;
    [SerializeField] private Image boosterRadial;             // shows ACTIVE duration
    [SerializeField] private TextMeshProUGUI boosterCountLabel;

    [Header("Cost")]
    [SerializeField] private bool consumeItem = true;

    private const float UI_REFRESH = 0.15f;
    private float _nextRefresh;

    void OnEnable()
    {
        if (!boosterBtn) boosterBtn = GetComponent<Button>();
        boosterBtn.onClick.RemoveAllListeners();
        boosterBtn.onClick.AddListener(OnPress);

        if (boosterRadial) boosterRadial.fillAmount = 0f;

        RefreshCounts();
        RefreshInteractability(true);
    }

    void OnDisable()
    {
        if (boosterBtn) boosterBtn.onClick.RemoveAllListeners();
        if (boosterRadial) boosterRadial.fillAmount = 0f;
    }

    void Update()
    {
        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + UI_REFRESH;
            RefreshInteractability(false);
        }
    }

    private void RefreshCounts()
    {
        int count = ResourceBank.Get(ResourceType.SpeedBoosters);
        if (boosterCountLabel) boosterCountLabel.text = $"{count}";
    }

    private void RefreshInteractability(bool hard)
    {
        var ctrl = BattleBoosterController.I;

        if (ctrl)
        {
            // Interactability
            bool can = ctrl.CanUse(BoosterType.Speed, out _);

            // Active duration on radial
            var (rem, max) = ctrl.Active(BoosterType.Speed);
            if (boosterRadial)
                boosterRadial.fillAmount = (rem > 0 && max > 0) ? (float)rem / max : 0f;

            bool haveItem = !consumeItem || ResourceBank.Get(ResourceType.SpeedBoosters) > 0;
            if (boosterBtn) boosterBtn.interactable = can && haveItem;
        }
        else
        {
            if (boosterBtn) boosterBtn.interactable = false;
            if (boosterRadial) boosterRadial.fillAmount = 0f;
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

        // Pre-check
        if (!ctrl.CanUse(BoosterType.Speed, out var why))
        {
            BattleLogger.Log(string.IsNullOrEmpty(why) ? "Cannot use Speed Booster right now." : why, LogScope.Battle);
            RefreshInteractability(true);
            return;
        }

        // Spend item if required
        bool spent = true;
        if (consumeItem)
        {
            spent = ResourceBank.TrySpend(ResourceType.SpeedBoosters, 1);
            if (!spent)
            {
                RefreshCounts();
                RefreshInteractability(true);
                return;
            }
        }

        // Try to activate via controller (supports multiple method names)
        bool used = false;
        var t = ctrl.GetType();
        var bm = Object.FindFirstObjectByType<BattleManager>(); // optional, passed if method accepts it

        used = TryInvokeBool(t, ctrl, "UseFromUI", new object[] { BoosterType.Speed, bm }) ||
               TryInvokeBool(t, ctrl, "UseFromUI", new object[] { BoosterType.Speed })     ||
               TryInvokeBool(t, ctrl, "Use",       new object[] { BoosterType.Speed })     ||
               TryInvokeBool(t, ctrl, "TryUse",    new object[] { BoosterType.Speed })     ||
               TryInvokeVoidThenTrue(t, ctrl, "Activate", new object[] { BoosterType.Speed });

        // Refund if failed after spending
        if (!used && consumeItem && spent)
        {
            ResourceBank.Add(ResourceType.SpeedBoosters, 1);
            BattleLogger.Log("Speed Booster failed to activate; item refunded.", LogScope.Battle);
        }
        else if (used)
        {
            GameEvents.OnResourcesChanged?.Invoke();
        }

        RefreshCounts();
        RefreshInteractability(true);
    }

    private bool TryInvokeBool(System.Type t, object instance, string method, object[] args)
    {
        var m = t.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) return false;
        var ret = m.Invoke(instance, args);
        if (ret is bool b) return b;
        return true; // assume success if non-bool/void
    }

    private bool TryInvokeVoidThenTrue(System.Type t, object instance, string method, object[] args)
    {
        var m = t.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) return false;
        m.Invoke(instance, args);
        return true;
    }
}
