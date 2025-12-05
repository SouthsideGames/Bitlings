using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Reflection;

public class AttackBooster : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button boosterBtn;
    [SerializeField] private Image boosterRadial;             // shows ACTIVE duration fill
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
        int count = ResourceBank.Get(ResourceType.AttackBooster);
        if (boosterCountLabel) boosterCountLabel.text = $"{count}";
    }

    private void RefreshInteractability(bool hard)
    {
        var ctrl = BattleBoosterController.I;
        bool can = false;

        if (ctrl)
        {
            can = ctrl.CanUse(BoosterType.Attack, out _);

            // Active duration radial (remaining / max)
            var (rem, max) = ctrl.Active(BoosterType.Attack);
            if (boosterRadial)
                boosterRadial.fillAmount = (rem > 0 && max > 0) ? (float)rem / max : 0f;
        }

        bool haveItem = !consumeItem || ResourceBank.Get(ResourceType.AttackBooster) > 0;
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

        // Pre-check
        if (!ctrl.CanUse(BoosterType.Attack, out var why))
        {
            BattleLogger.Log(string.IsNullOrEmpty(why) ? "Cannot use Attack Booster right now." : why, LogScope.Battle);
            RefreshInteractability(true);
            return;
        }

        // Spend if required
        bool spent = true;
        if (consumeItem)
        {
            spent = ResourceBank.TrySpend(ResourceType.AttackBooster, 1);
            if (!spent)
            {
                RefreshCounts();
                RefreshInteractability(true);
                return;
            }
        }

        // Try to activate through controller (support multiple method names/signatures)
        bool used = false;
        var t = ctrl.GetType();
        var bm = Object.FindFirstObjectByType<BattleManager>(); // optional if controller accepts it

        used = TryInvokeBool(t, ctrl, "UseFromUI", new object[] { BoosterType.Attack, bm }) ||
               TryInvokeBool(t, ctrl, "UseFromUI", new object[] { BoosterType.Attack })     ||
               TryInvokeBool(t, ctrl, "Use",       new object[] { BoosterType.Attack })     ||
               TryInvokeBool(t, ctrl, "TryUse",    new object[] { BoosterType.Attack })     ||
               TryInvokeVoidThenTrue(t, ctrl, "Activate", new object[] { BoosterType.Attack });

        // Refund if failed after spending
        if (!used && consumeItem && spent)
        {
            ResourceBank.Add(ResourceType.AttackBooster, 1);
            BattleLogger.Log("Attack Booster failed to activate; item refunded.", LogScope.Battle);
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
        return true; // treat non-bool/void as success
    }

    private bool TryInvokeVoidThenTrue(System.Type t, object instance, string method, object[] args)
    {
        var m = t.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) return false;
        m.Invoke(instance, args);
        return true;
    }
}
