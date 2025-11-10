using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
        var tbm = FindObjectOfType<TurnBattleManager>();
        if (!tbm) return;

        tbm.UseBoosterFromUI(boosterType); // consumes turn on success
        RefreshImmediate();
    }
}
