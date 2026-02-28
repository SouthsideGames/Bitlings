using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 3.A: Forced Evolution step (vertical mobile).
/// Blocks progress until all eligible evolutions are resolved.
/// HP carries forward by PERCENT (hp/maxHp) when evolving.
/// </summary>
public sealed class IronCareerForcedEvolutionUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleTMP;
    [SerializeField] private TextMeshProUGUI subtitleTMP;

    [Header("Eligible List")]
    [SerializeField] private TextMeshProUGUI eligibleHeaderTMP;
    [SerializeField] private Transform eligibleListParent;
    [SerializeField] private IronCareerEligibleEvolutionItemUI eligiblePrefab;

    [Header("Preview")]
    [SerializeField] private TextMeshProUGUI previewHeaderTMP;
    [SerializeField] private IronCareerEvolutionPreviewCardUI beforeCard;
    [SerializeField] private IronCareerEvolutionPreviewCardUI afterCard;
    [SerializeField] private TextMeshProUGUI statDeltaTMP;

    [Header("Warning")]
    [SerializeField] private TextMeshProUGUI warningTMP;

    [Header("Bottom Bar")]
    [SerializeField] private Button evolveButton;
    [SerializeField] private TextMeshProUGUI evolveButtonLabel;
    [SerializeField] private Button continueOrSkipButton;
    [SerializeField] private TextMeshProUGUI continueOrSkipLabel;

    [Header("Confirm Popup (Optional)")]
    [SerializeField] private GameObject evolveConfirmPopup;
    [SerializeField] private TextMeshProUGUI evolveConfirmLabel;
    [SerializeField] private Button evolveConfirmYesButton;
    [SerializeField] private Button evolveConfirmNoButton;

    [Header("Animation")]
    [SerializeField, Min(0f)] private float selectionPulseScale = 1.03f;
    [SerializeField, Min(0.01f)] private float selectionPulseTime = 0.10f;
    [SerializeField, Min(0f)] private float previewAnimDuration = 0.14f;

    private readonly List<IronCareerEligibleEvolutionItemUI> _spawned = new List<IronCareerEligibleEvolutionItemUI>(8);
    private readonly List<int> _eligibleIndices = new List<int>(8);

    private int _selectedPartyIndex = -1;

    private bool IsForcedEvolutionEligible(IronMonster monster)
    {
        if (monster == null || monster.def == null) return false;
        if (monster.IsDead) return false;

        var nextForm = monster.def.evolutionForm;
        if (nextForm == null) return false;
        if (ReferenceEquals(nextForm, monster.def)) return false;

        if (monster.def.evolutionLevel > 0 && monster.level < monster.def.evolutionLevel) return false;
        return true;
    }

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>(FindObjectsInactive.Include);
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();

        if (evolveButton)
        {
            evolveButton.onClick.RemoveAllListeners();
            evolveButton.onClick.AddListener(OnEvolveClicked);
        }

        if (continueOrSkipButton)
        {
            continueOrSkipButton.onClick.RemoveAllListeners();
            continueOrSkipButton.onClick.AddListener(OnContinueClicked);
        }

        if (evolveConfirmYesButton)
        {
            evolveConfirmYesButton.onClick.RemoveAllListeners();
            evolveConfirmYesButton.onClick.AddListener(OnConfirmEvolveYesClicked);
        }

        if (evolveConfirmNoButton)
        {
            evolveConfirmNoButton.onClick.RemoveAllListeners();
            evolveConfirmNoButton.onClick.AddListener(HideEvolveConfirmPopup);
        }

        EnsureRuntimeConfirmPopupIfNeeded();

        HideEvolveConfirmPopup();
    }

    private void OnDestroy()
    {
        if (evolveButton) evolveButton.onClick.RemoveAllListeners();
        if (continueOrSkipButton) continueOrSkipButton.onClick.RemoveAllListeners();
        if (evolveConfirmYesButton) evolveConfirmYesButton.onClick.RemoveAllListeners();
        if (evolveConfirmNoButton) evolveConfirmNoButton.onClick.RemoveAllListeners();
    }

    /// <summary>
    /// Bind and (re)build the UI from the current roster state.
    /// </summary>
    public void Bind(IReadOnlyList<IronMonster> party)
    {
        if (titleTMP) titleTMP.text = "FORCED EVOLUTION";
        if (subtitleTMP) subtitleTMP.text = "Evolution must be resolved before continuing.";

        if (eligibleHeaderTMP) eligibleHeaderTMP.text = "ELIGIBLE EVOLUTIONS";
        if (previewHeaderTMP) previewHeaderTMP.text = "SELECTED EVOLUTION (Preview)";

        if (warningTMP)
        {
            warningTMP.text = "⚠ Evolution is permanent in Iron Career. HP carries forward by percent. No healing is granted.";
        }

        _selectedPartyIndex = -1;
        RebuildEligibleList(party);
        RefreshButtonsAndPreview(party);
        SetInteractable(true);
    }

    private void SetInteractable(bool on)
    {
        if (!canvasGroup) return;
        canvasGroup.interactable = on;
        canvasGroup.blocksRaycasts = on;
    }

    private void RebuildEligibleList(IReadOnlyList<IronMonster> party)
    {
        _eligibleIndices.Clear();

        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i]) Destroy(_spawned[i].gameObject);
        }
        _spawned.Clear();

        if (party == null || party.Count == 0) return;
        if (!eligibleListParent || !eligiblePrefab) return;

        for (int i = 0; i < party.Count; i++)
        {
            var m = party[i];
            if (!IsForcedEvolutionEligible(m)) continue;

            _eligibleIndices.Add(i);

            var row = Instantiate(eligiblePrefab, eligibleListParent);
            _spawned.Add(row);

            int capturedIndex = i;
            row.Bind(capturedIndex, m, m.def.evolutionForm, () => OnSelectEligible(capturedIndex));
            row.SetSelected(false);
        }
    }

    private void OnSelectEligible(int partyIndex)
    {
        _selectedPartyIndex = partyIndex;

        for (int i = 0; i < _spawned.Count; i++)
        {
            var row = _spawned[i];
            if (!row) continue;
            bool selected = row.PartyIndex == partyIndex;
            row.SetSelected(selected);
            if (selected)
            {
                row.PlaySelectPulse(selectionPulseScale, selectionPulseTime);
            }
        }

        RefreshButtonsAndPreview(manager != null ? manager.GetIronPartyUnsafe() : null);
    }

    private void RefreshButtonsAndPreview(IReadOnlyList<IronMonster> party)
    {
        bool hasEligible = _eligibleIndices.Count > 0;

        // Continue is only available when no eligible evolutions remain.
        bool canContinue = !hasEligible;

        if (continueOrSkipButton)
        {
            continueOrSkipButton.gameObject.SetActive(canContinue);
            continueOrSkipButton.interactable = canContinue;
        }
        if (continueOrSkipLabel) continueOrSkipLabel.text = canContinue ? "CONTINUE" : string.Empty;

        if (eligibleHeaderTMP)
            eligibleHeaderTMP.text = hasEligible ? "ELIGIBLE EVOLUTIONS" : "ELIGIBLE EVOLUTIONS (None)";

        bool hasSelection = hasEligible && _selectedPartyIndex >= 0;
        if (evolveButton) evolveButton.interactable = hasSelection;
        if (evolveButtonLabel) evolveButtonLabel.text = "EVOLVE NOW";

        // Preview
        if (!hasSelection || party == null || _selectedPartyIndex < 0 || _selectedPartyIndex >= party.Count)
        {
            if (beforeCard) beforeCard.Clear();
            if (afterCard) afterCard.Clear();
            if (statDeltaTMP) statDeltaTMP.text = hasEligible ? "Select a monster to preview evolution." : "No evolutions available.";
            return;
        }

        var m = party[_selectedPartyIndex];
        if (m == null || m.def == null || m.def.evolutionForm == null)
        {
            if (beforeCard) beforeCard.Clear();
            if (afterCard) afterCard.Clear();
            if (statDeltaTMP) statDeltaTMP.text = "Select a monster to preview evolution.";
            return;
        }

        var beforeDef = m.def;
        var afterDef = m.def.evolutionForm;

        float beforeMaxHp = Mathf.Max(1f, m.maxHp > 0.01f ? m.maxHp : BattleCalc.CalcHP(beforeDef, Mathf.Max(1, m.level)));
        float hp01 = beforeMaxHp > 0.01f ? Mathf.Clamp01(m.hp / beforeMaxHp) : 0f;
        float afterMaxHp = Mathf.Max(1f, BattleCalc.CalcHP(afterDef, Mathf.Max(1, m.level)));

        if (beforeCard) beforeCard.Bind(beforeDef, m.level, m.lockedTitle);
        if (afterCard) afterCard.Bind(afterDef, m.level, m.lockedTitle);
        AnimatePreviewSwap();

        if (statDeltaTMP)
        {
            float atkBefore = BattleCalc.CalcBaseAttack(beforeDef, m.level, 0, 0);
            float atkAfter = BattleCalc.CalcBaseAttack(afterDef, m.level, 0, 0);
            int defBefore = BattleCalc.CalcDefense(beforeDef, m.level);
            int defAfter = BattleCalc.CalcDefense(afterDef, m.level);
            int spdBefore = BattleCalc.CalcSpeed(beforeDef, m.level);
            int spdAfter = BattleCalc.CalcSpeed(afterDef, m.level);

            string fmtDelta(float d) => (d >= 0f ? "+" : "-") + Mathf.Abs(d).ToString("0");
            string fmtDeltaInt(int d) => (d >= 0 ? "+" : "-") + Mathf.Abs(d).ToString();

            float dHp = afterMaxHp - beforeMaxHp;
            float dAtk = atkAfter - atkBefore;
            int dDef = defAfter - defBefore;
            int dSpd = spdAfter - spdBefore;

            statDeltaTMP.text =
                $"HP {fmtDelta(dHp)}   ATK {fmtDelta(dAtk)}   DEF {fmtDeltaInt(dDef)}   SPD {fmtDeltaInt(dSpd)}\n" +
                $"HP Carryover: {(hp01 * 100f).ToString("0")} %";
        }
    }

    private void OnEvolveClicked()
    {
        if (!CanEvolveSelected(out var evolveTargetName)) return;

        if (evolveConfirmPopup)
        {
            if (evolveConfirmLabel) evolveConfirmLabel.text = $"Evolve → {evolveTargetName}?";
            evolveConfirmPopup.SetActive(true);
            return;
        }

        ExecuteEvolveSelected();
    }

    private void OnConfirmEvolveYesClicked()
    {
        HideEvolveConfirmPopup();
        ExecuteEvolveSelected();
    }

    private bool CanEvolveSelected(out string evolveTargetName)
    {
        evolveTargetName = string.Empty;

        if (manager == null) return false;

        var party = manager.GetIronPartyUnsafe();
        if (party == null || party.Count == 0) return false;
        if (_selectedPartyIndex < 0 || _selectedPartyIndex >= party.Count) return false;

        var selected = party[_selectedPartyIndex];
        if (!IsForcedEvolutionEligible(selected)) return false;

        evolveTargetName = selected.def != null && selected.def.evolutionForm != null
            ? selected.def.evolutionForm.displayName
            : "—";
        return true;
    }

    private void ExecuteEvolveSelected()
    {
        if (manager == null) return;

        var party = manager.GetIronPartyUnsafe();
        if (party == null || party.Count == 0) return;
        if (_selectedPartyIndex < 0 || _selectedPartyIndex >= party.Count) return;
        if (!IsForcedEvolutionEligible(party[_selectedPartyIndex]))
        {
            Bind(party);
            return;
        }

        bool evolved = manager.TryForceEvolveAtIndex(_selectedPartyIndex);
        if (!evolved)
        {
            // Eligibility changed or invalid selection; refresh.
            Bind(party);
            return;
        }

        // Refresh and keep blocking until no eligible remain.
        Bind(manager.GetIronPartyUnsafe());
    }

    private void HideEvolveConfirmPopup()
    {
        if (evolveConfirmPopup) evolveConfirmPopup.SetActive(false);
    }

    private void EnsureRuntimeConfirmPopupIfNeeded()
    {
        if (evolveConfirmPopup && evolveConfirmLabel && evolveConfirmYesButton && evolveConfirmNoButton)
            return;

        Sprite uiSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");

        var popupGO = new GameObject("EvolveConfirmPopup", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        popupGO.transform.SetParent(transform, false);

        var popupRect = popupGO.GetComponent<RectTransform>();
        popupRect.anchorMin = Vector2.zero;
        popupRect.anchorMax = Vector2.one;
        popupRect.offsetMin = Vector2.zero;
        popupRect.offsetMax = Vector2.zero;

        var popupImage = popupGO.GetComponent<Image>();
        popupImage.sprite = uiSprite;
        popupImage.color = new Color(0f, 0f, 0f, 0.45f);

        var panelGO = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGO.transform.SetParent(popupGO.transform, false);
        var panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(620f, 300f);

        var panelImage = panelGO.GetComponent<Image>();
        panelImage.sprite = uiSprite;
        panelImage.color = new Color(0.1f, 0.14f, 0.18f, 0.97f);

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelGO.transform.SetParent(panelGO.transform, false);
        var labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 1f);
        labelRect.anchorMax = new Vector2(0.5f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -40f);
        labelRect.sizeDelta = new Vector2(560f, 120f);

        var label = labelGO.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 40f;
        label.enableAutoSizing = false;
        label.text = "Evolve?";
        if (titleTMP && titleTMP.font) label.font = titleTMP.font;

        var yesButton = CreatePopupButton(panelGO.transform, "YesButton", "YES", new Vector2(-120f, -105f), uiSprite);
        var noButton = CreatePopupButton(panelGO.transform, "NoButton", "NO", new Vector2(120f, -105f), uiSprite);

        evolveConfirmPopup = popupGO;
        evolveConfirmLabel = label;
        evolveConfirmYesButton = yesButton;
        evolveConfirmNoButton = noButton;

        evolveConfirmYesButton.onClick.RemoveAllListeners();
        evolveConfirmYesButton.onClick.AddListener(OnConfirmEvolveYesClicked);
        evolveConfirmNoButton.onClick.RemoveAllListeners();
        evolveConfirmNoButton.onClick.AddListener(HideEvolveConfirmPopup);
    }

    private Button CreatePopupButton(Transform parent, string name, string label, Vector2 anchoredPosition, Sprite uiSprite)
    {
        var buttonGO = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonGO.transform.SetParent(parent, false);

        var rect = buttonGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(200f, 90f);

        var image = buttonGO.GetComponent<Image>();
        image.sprite = uiSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.22f, 0.30f, 0.38f, 1f);

        var button = buttonGO.GetComponent<Button>();

        var textGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(buttonGO.transform, false);
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var text = textGO.GetComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.text = label;
        text.fontSize = 34f;
        text.enableAutoSizing = false;
        if (titleTMP && titleTMP.font) text.font = titleTMP.font;

        return button;
    }

    private void AnimatePreviewSwap()
    {
        AnimatePreviewCard(beforeCard != null ? beforeCard.gameObject : null, -10f);
        AnimatePreviewCard(afterCard != null ? afterCard.gameObject : null, 10f);
    }

    private void AnimatePreviewCard(GameObject cardGO, float fromOffsetX)
    {
        if (!cardGO) return;

        var rt = cardGO.transform as RectTransform;
        if (!rt) return;

        LeanTween.cancel(cardGO);

        Vector3 targetScale = Vector3.one;
        rt.localScale = Vector3.one * 0.97f;
        LeanTween.scale(cardGO, targetScale, previewAnimDuration).setEaseOutBack();

        Vector3 anchored = rt.anchoredPosition3D;
        rt.anchoredPosition3D = new Vector3(anchored.x + fromOffsetX, anchored.y, anchored.z);
        LeanTween.move(rt, new Vector3(anchored.x, anchored.y, anchored.z), previewAnimDuration).setEaseOutCubic();
    }

    private void OnContinueClicked()
    {
        manager?.OnForcedEvolveContinue();
    }
}
