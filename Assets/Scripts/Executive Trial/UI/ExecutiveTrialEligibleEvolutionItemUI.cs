using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI row prefab for the Forced Evolution screen.
/// Shows the current monster, evolution target, and requirement.
/// </summary>
public sealed class ExecutiveTrialEligibleEvolutionItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private Image typeIcon;
    [SerializeField] private TextMeshProUGUI nameTMP;
    [SerializeField] private TextMeshProUGUI levelTMP;
    [SerializeField] private TextMeshProUGUI titleTMP;
    [SerializeField] private Slider hpSlider;
    [SerializeField] private TextMeshProUGUI hpTMP;

    [Header("Evolution")]
    [SerializeField] private TextMeshProUGUI evolvesToTMP;
    [SerializeField] private TextMeshProUGUI requirementTMP;

    [Header("Selection")]
    [SerializeField] private GameObject selectedFrame;

    private RectTransform _rectTransform;

    public int PartyIndex { get; private set; } = -1;

    private void Awake()
    {
        _rectTransform = transform as RectTransform;
    }

    public void Bind(int partyIndex, ExecutiveTrailMonster monster, MonsterDataSO evolveTo, Action onClick)
    {
        PartyIndex = partyIndex;

        if (icon) icon.sprite = monster != null && monster.def ? monster.def.icon : null;
        if (typeIcon)
        {
            typeIcon.sprite = monster != null && monster.def ? monster.def.typeIcon : null;
            typeIcon.gameObject.SetActive(typeIcon.sprite != null);
        }

        if (nameTMP) nameTMP.text = monster != null && monster.def ? MonsterNameFormatter.Format(monster.def, monster.isPremium) : "?";
        if (levelTMP) levelTMP.text = monster != null ? $"Lv {Mathf.Max(1, monster.level)}" : "Lv ?";
        if (titleTMP) titleTMP.text = monster != null && monster.lockedTitle ? $"Title: {monster.lockedTitle.displayName}" : "Title: —";

        float maxHp = monster != null ? Mathf.Max(1f, monster.maxHp) : 1f;
        float hp = monster != null ? Mathf.Clamp(monster.hp, 0f, maxHp) : 0f;
        if (hpSlider)
        {
            hpSlider.minValue = 0f;
            hpSlider.maxValue = maxHp;
            hpSlider.value = hp;
        }
        if (hpTMP) hpTMP.text = $"{Mathf.RoundToInt(hp)} / {Mathf.RoundToInt(maxHp)}";

        if (evolvesToTMP)
        {
            evolvesToTMP.text = evolveTo ? $"Evolves → {MonsterNameFormatter.Format(evolveTo, monster?.isPremium ?? false)}" : "Evolves → —";
        }

        if (requirementTMP)
        {
            int req = (monster != null && monster.def) ? Mathf.Max(0, monster.def.evolutionLevel) : 0;
            bool ok = (req <= 0) || (monster != null && monster.level >= req);
            requirementTMP.text = req > 0 ? $"Requirement: Level {req} {(ok ? "✅" : "❌")}" : "Requirement: —";
        }

        if (button)
        {
            button.onClick.RemoveAllListeners();
            if (onClick != null) button.onClick.AddListener(() => onClick());
        }
    }

    public void SetPartyIndex(int idx) => PartyIndex = idx;

    public void SetSelected(bool selected)
    {
        if (selectedFrame) selectedFrame.SetActive(selected);
    }

    public void PlaySelectPulse(float targetScale = 1.03f, float duration = 0.10f)
    {
        if (_rectTransform == null) _rectTransform = transform as RectTransform;
        if (_rectTransform == null) return;

        targetScale = Mathf.Max(1f, targetScale);
        duration = Mathf.Max(0.05f, duration);

        LeanTween.cancel(gameObject);
        _rectTransform.localScale = Vector3.one;
        LeanTween.scale(gameObject, Vector3.one * targetScale, duration)
            .setEaseOutBack()
            .setLoopPingPong(1);
    }
}
