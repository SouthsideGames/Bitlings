// Assets/Scripts/Executive Trial/UI/ExecutiveTrialStarterSlotUI.cs

using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ExecutiveTrialStarterSlotUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private Image typeIcon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statsText;

    private static TypeIconLibrary _typeIconLibrary;

    private MonsterDataSO _boundDef;
    public MonsterDataSO BoundDef => _boundDef;
    private int _iconBreatheTweenId = -1;

    private void OnEnable()
    {
        StartIconBreathing();
    }

    private void OnDisable()
    {
        StopIconBreathing();
    }

    private void EnsureLibraryLoaded()
    {
        if (_typeIconLibrary == null)
        {
            _typeIconLibrary = Resources.Load<TypeIconLibrary>("TypeIconLibrary");

            if (_typeIconLibrary == null)
                Debug.LogError("[ExecutiveTrialStarterSlotUI] Could not load Resources/TypeIconLibrary");
        }
    }

    public void Bind(MonsterDataSO def)
    {
        _boundDef = def;

        if (def == null)
        {
            Clear();
            return;
        }

        EnsureLibraryLoaded();

        // Main icon
        if (icon)
        {
            icon.enabled = def.icon != null;
            icon.sprite = def.icon;
        }

        StartIconBreathing();

        // Name
        if (nameText)
            nameText.text = string.IsNullOrEmpty(def.displayName)
                ? def.id
                : def.displayName;

        // Type icon
        if (typeIcon && _typeIconLibrary != null)
        {
            var sprite = _typeIconLibrary.GetIcon(def.type);
            typeIcon.enabled = sprite != null;
            typeIcon.sprite = sprite;
        }

        // Stats block (single formatted text)
        if (statsText)
        {
            statsText.text =
                $"HP: {def.baseHP}\n" +
                $"ATK: {def.baseAttack}\n" +
                $"DEF: {def.baseDefense}\n" +
                $"SPD: {def.baseSpeed}";
        }
    }

    private void Clear()
    {
        StopIconBreathing();

        if (icon)
        {
            icon.enabled = false;
            icon.sprite = null;
        }

        if (typeIcon)
        {
            typeIcon.enabled = false;
            typeIcon.sprite = null;
        }

        if (nameText) nameText.text = "—";
        if (statsText) statsText.text = "";
    }

    private void StartIconBreathing()
    {
        if (!isActiveAndEnabled || !icon || !icon.enabled)
            return;

        StopIconBreathing();

        var rt = icon.rectTransform;
        if (!rt)
            return;

        rt.localScale = Vector3.one;
        _iconBreatheTweenId = LeanTween.value(gameObject, 1f, 1.08f, 2.1f)
            .setEase(LeanTweenType.easeInOutSine)
            .setLoopPingPong()
            .setOnUpdate((float v) =>
            {
                if (icon) icon.rectTransform.localScale = new Vector3(v, v, 1f);
            })
            .id;
    }

    private void StopIconBreathing()
    {
        if (_iconBreatheTweenId != -1)
        {
            LeanTween.cancel(_iconBreatheTweenId);
            _iconBreatheTweenId = -1;
        }

        if (icon)
            icon.rectTransform.localScale = Vector3.one;
    }
}