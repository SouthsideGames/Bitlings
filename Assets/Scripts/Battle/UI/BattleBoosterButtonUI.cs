using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Reflection;

[RequireComponent(typeof(Button))]
public class BattleBoosterButtonUI : MonoBehaviour
{
    [Header("Config")]
    public BoosterType boosterType;

    [Header("UI")]
    public Button button;                 // auto-filled if null
    public TextMeshProUGUI titleText;     // optional label
    public TextMeshProUGUI cdText;        // "CD: 2"
    public TextMeshProUGUI hintText;      // reason when disabled (optional)
    public CanvasGroup canvasGroup;       // optional fade

    private float nextRefreshAt = 0f;
    private const float REFRESH_EVERY = 0.15f;

    void Reset() { button = GetComponent<Button>(); }

    void Awake()
    {
        if (!button) button = GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnClick);
        ApplyTitle();
    }

    void OnEnable() { RefreshImmediate(); }

    void Update()
    {
        if (Time.unscaledTime >= nextRefreshAt)
        {
            nextRefreshAt = Time.unscaledTime + REFRESH_EVERY;
            RefreshImmediate();
        }
    }

    private void ApplyTitle()
    {
        if (!titleText) return;
        titleText.text = boosterType switch
        {
            BoosterType.Attack     => "ATK Boost",
            BoosterType.Health     => "HP Boost",
            BoosterType.Speed      => "SPD Boost",
            BoosterType.TypeResist => "Resist",
            _ => "Boost"
        };
    }

    private void RefreshImmediate()
    {
        var ctrl = BattleBoosterController.I;
        bool interact = false; string reason = null; string cd = "";

        if (ctrl)
        {
            interact = ctrl.CanUse(boosterType, out reason);
            var (atk, hp, spd, res) = ctrl.Cooldowns();
            int c = boosterType switch
            {
                BoosterType.Attack     => atk,
                BoosterType.Health     => hp,
                BoosterType.Speed      => spd,
                BoosterType.TypeResist => res,
                _ => 0
            };
            cd = c > 0 ? $"CD: {c}" : "";
        }

        if (button) button.interactable = interact;
        if (cdText)  cdText.text = cd;

        if (canvasGroup)
        {
            canvasGroup.alpha = interact ? 1f : 0.55f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        if (hintText) hintText.text = (!interact && !string.IsNullOrEmpty(reason)) ? reason : "";
    }

    private void OnClick()
    {
        var ctrl = BattleBoosterController.I;
        if (!ctrl) return;

        // Try common method names on the controller to keep this UI decoupled
        bool used = false;
        var t = ctrl.GetType();

        // Prefer a UseFromUI method that might want a BattleManager reference
        var bm = FindFirstObjectByType<BattleManager>();

        used = TryInvokeBool(t, ctrl, "UseFromUI", new object[] { boosterType, bm }) ||
               TryInvokeBool(t, ctrl, "UseFromUI", new object[] { boosterType }) ||
               TryInvokeBool(t, ctrl, "Use",       new object[] { boosterType }) ||
               TryInvokeBool(t, ctrl, "TryUse",    new object[] { boosterType }) ||
               TryInvokeVoidThenTrue(t, ctrl, "Activate", new object[] { boosterType });

        if (used)
        {
            // Optional little feedback nudge if a BattleManager and icon exist
            if (bm && bm.isActiveAndEnabled)
            {
                // no hard dependency; UI feedback handled inside bm if desired
            }
        }

        RefreshImmediate();
    }

    private bool TryInvokeBool(System.Type t, object instance, string method, object[] args)
    {
        var m = t.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) return false;
        var ret = m.Invoke(instance, args);
        if (ret is bool b) return b;
        return true; // if it returned void/non-bool, assume success
    }

    private bool TryInvokeVoidThenTrue(System.Type t, object instance, string method, object[] args)
    {
        var m = t.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) return false;
        m.Invoke(instance, args);
        return true;
    }
}
