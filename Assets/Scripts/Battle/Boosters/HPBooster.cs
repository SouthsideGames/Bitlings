using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Reflection;

public class HPBooster : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button boosterBtn;
    [SerializeField] private Image boosterRadial;             // HP has no duration → stays 0
    [SerializeField] private TextMeshProUGUI boosterCountLabel;

    [Header("Cost")]
    [SerializeField] private bool consumeItem = true;

    private const float UI_REFRESH = 0.15f;
    private float _nextRefresh;

    void OnEnable()
    {
        if (!boosterBtn) boosterBtn = GetComponent<Button>();
        if (boosterBtn)
        {
            boosterBtn.onClick.RemoveAllListeners();
            boosterBtn.onClick.AddListener(OnPress);
        }

        if (boosterRadial) boosterRadial.fillAmount = 0f;

        GameEvents.OnResourcesChanged += RefreshCounts;
        RefreshCounts();
        RefreshInteractability(true);
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= RefreshCounts;
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
        int have = ResourceBank.Get(ResourceType.HPBooster);
        if (boosterCountLabel) boosterCountLabel.text = $"x{have}";
    }

    private void RefreshInteractability(bool hard)
    {
        var ctrl = BattleBoosterController.I;
        bool can = false;

        if (ctrl)
        {
            can = ctrl.CanUse(BoosterType.Health, out _);

            // HP booster has no active duration; keep ring empty
            if (boosterRadial) boosterRadial.fillAmount = 0f;
        }

        bool haveItem = !consumeItem || ResourceBank.Get(ResourceType.HPBooster) > 0;
        if (boosterBtn) boosterBtn.interactable = can && haveItem;
    }

    private void OnPress()
    {
        var ctrl = BattleBoosterController.I;
        if (!ctrl)
        {
            BattleLogger.Log("Booster controller not found.", LogScope.Battle);
            return;
        }

        // Pre-check ability to use
        if (!ctrl.CanUse(BoosterType.Health, out var why))
        {
            if (!string.IsNullOrEmpty(why))
                BattleLogger.Log(why, LogScope.Battle);
            RefreshInteractability(true);
            return;
        }

        // Spend item if required
        bool spent = true;
        if (consumeItem)
        {
            spent = ResourceBank.TrySpend(ResourceType.HPBooster, 1);
            if (!spent)
            {
                RefreshCounts();
                RefreshInteractability(true);
                return;
            }
        }

        // Attempt activation via controller (supports multiple method names)
        bool used = false;
        var t = ctrl.GetType();
        var bm = Object.FindFirstObjectByType<BattleManager>(); // optional if controller accepts it

        used = TryInvokeBool(t, ctrl, "UseFromUI", new object[] { BoosterType.Health, bm }) ||
               TryInvokeBool(t, ctrl, "UseFromUI", new object[] { BoosterType.Health })     ||
               TryInvokeBool(t, ctrl, "Use",       new object[] { BoosterType.Health })     ||
               TryInvokeBool(t, ctrl, "TryUse",    new object[] { BoosterType.Health })     ||
               TryInvokeVoidThenTrue(t, ctrl, "Activate", new object[] { BoosterType.Health });

        // Refund if it failed after spending
        if (!used && consumeItem && spent)
        {
            ResourceBank.Add(ResourceType.HPBooster, 1);
            BattleLogger.Log("HP Boost failed to activate; item refunded.", LogScope.Battle);
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
        return true; // treat void/other returns as success
    }

    private bool TryInvokeVoidThenTrue(System.Type t, object instance, string method, object[] args)
    {
        var m = t.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) return false;
        m.Invoke(instance, args);
        return true;
    }
}
