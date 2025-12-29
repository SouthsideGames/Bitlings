using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class BattleBoosterButtonUI : MonoBehaviour
{
    [Header("Config")]
    public BoosterType boosterType;

    [Header("UI")]
    public Button button;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI cdText;
    public TextMeshProUGUI hintText;
    public CanvasGroup canvasGroup;

    void Reset() { button = GetComponent<Button>(); }

    void Awake()
    {
        if (!button) button = GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnClick);
        ApplyTitle();
    }

    void OnEnable()
    {
        GameEvents.OnResourcesChanged += RefreshImmediate;
        GameEvents.OnBoostersChanged += RefreshImmediate;

        RefreshImmediate();
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= RefreshImmediate;
        GameEvents.OnBoostersChanged -= RefreshImmediate;
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

        bool interact = false;
        string reason = null;
        string cd = "";

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
        if (cdText) cdText.text = cd;

        if (canvasGroup)
        {
            canvasGroup.alpha = interact ? 1f : 0.55f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        if (hintText)
            hintText.text = (!interact && !string.IsNullOrEmpty(reason)) ? reason : "";
    }

    private void OnClick()
    {
        var ctrl = BattleBoosterController.I;
        if (!ctrl) return;

        bool used = ctrl.TryUseFromUI(boosterType, out var msg);

        if (used)
        {
            if (!string.IsNullOrEmpty(msg))
                BattleLogger.Log(msg, LogScope.Battle);
        }

        RefreshImmediate();
    }
}
