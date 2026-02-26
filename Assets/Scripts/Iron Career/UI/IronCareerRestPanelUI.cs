using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System;

/// <summary>
/// Phase 3.A: IronCareerRestPanelUI
/// Appears only on wins % 3 == 0.
/// Choices:
/// - Heal party 25%
/// - Training: +1 level to a chosen party member
/// </summary>
public sealed class IronCareerRestPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI headerLabel;
    [SerializeField] private Button healButton;
    [SerializeField] private Button buffButton;
    [SerializeField] private List<Button> trainingTargetButtons = new List<Button>(3);
    [SerializeField] private List<TextMeshProUGUI> trainingTargetLabels = new List<TextMeshProUGUI>(3);
    [SerializeField] private List<Image> trainingTargetIcons = new List<Image>(3);
    [SerializeField] private List<TextMeshProUGUI> trainingTargetHpTexts = new List<TextMeshProUGUI>(3);

    [Header("Selection Visuals (Optional)")]
    [SerializeField] private List<GameObject> trainingTargetSelectedFX = new List<GameObject>(3);
    [SerializeField] private Color selectedButtonTint = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color unselectedButtonTint = new Color(0.85f, 0.85f, 0.85f, 1f);

    private bool _wired;
    private int _selectedTargetIndex = -1;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();

        AutoWireTrainingTargetsIfNeeded();
        WireButtons();
    }

    private void OnEnable()
    {
        if (headerLabel) headerLabel.text = "Rest";
    }

    private void OnDestroy()
    {
        UnwireButtons();
    }

    public void Bind(IReadOnlyList<IronMonster> party, int defaultIndex)
    {
        AutoWireTrainingTargetsIfNeeded();
        AutoWireTargetVisualsIfNeeded();

        if (trainingTargetButtons == null || trainingTargetButtons.Count == 0)
        {
            if (buffButton) buffButton.interactable = true;
            return;
        }

        int safeDefault = Mathf.Clamp(defaultIndex, 0, Mathf.Max(0, trainingTargetButtons.Count - 1));
        _selectedTargetIndex = safeDefault;

        for (int i = 0; i < trainingTargetButtons.Count; i++)
        {
            var btn = trainingTargetButtons[i];
            if (!btn) continue;

            var m = (party != null && i < party.Count) ? party[i] : null;
            bool valid = m != null && m.def != null && !m.IsDead;

            btn.interactable = valid;

            if (trainingTargetIcons != null && i < trainingTargetIcons.Count && trainingTargetIcons[i])
                trainingTargetIcons[i].sprite = (m != null && m.def != null) ? m.def.icon : null;

            if (trainingTargetLabels != null && i < trainingTargetLabels.Count && trainingTargetLabels[i])
            {
                if (m != null && m.def != null)
                    trainingTargetLabels[i].text = m.def.displayName;
                else
                    trainingTargetLabels[i].text = "-";
            }

            if (trainingTargetHpTexts != null && i < trainingTargetHpTexts.Count && trainingTargetHpTexts[i])
            {
                if (m != null && m.def != null)
                    trainingTargetHpTexts[i].text = $"{Mathf.CeilToInt(m.hp)}/{Mathf.CeilToInt(m.maxHp)}";
                else
                    trainingTargetHpTexts[i].text = string.Empty;
            }
        }

        if (buffButton)
        {
            int idx = Mathf.Clamp(defaultIndex, 0, (party != null ? Mathf.Max(0, party.Count - 1) : 0));
            bool fallbackValid = party != null && idx < party.Count && party[idx] != null && party[idx].def != null && !party[idx].IsDead;
            buffButton.interactable = fallbackValid;
        }

        RefreshSelectionVisuals();
    }

    private void WireButtons()
    {
        if (_wired) return;
        if (healButton) healButton.onClick.AddListener(() => manager?.OnRestHeal());
        if (buffButton) buffButton.onClick.AddListener(OnBuffButtonClicked);

        for (int i = 0; i < trainingTargetButtons.Count; i++)
        {
            int idx = i;
            if (trainingTargetButtons[i])
                trainingTargetButtons[i].onClick.AddListener(() => OnTrainingTargetClicked(idx));
        }
        _wired = true;
    }

    private void OnBuffButtonClicked()
    {
        int idx = _selectedTargetIndex;
        if (idx < 0) idx = 0;
        manager?.OnRestBuffAt(idx);
    }

    private void OnTrainingTargetClicked(int idx)
    {
        _selectedTargetIndex = idx;
        RefreshSelectionVisuals();
        manager?.OnRestBuffAt(idx);
    }

    private void RefreshSelectionVisuals()
    {
        EnsureListSize(trainingTargetSelectedFX, trainingTargetButtons != null ? trainingTargetButtons.Count : 0);

        for (int i = 0; i < trainingTargetButtons.Count; i++)
        {
            bool selected = i == _selectedTargetIndex;

            if (trainingTargetSelectedFX != null && i < trainingTargetSelectedFX.Count && trainingTargetSelectedFX[i])
                trainingTargetSelectedFX[i].SetActive(selected);

            var btn = trainingTargetButtons[i];
            if (btn && btn.image)
                btn.image.color = selected ? selectedButtonTint : unselectedButtonTint;
        }
    }

    private void UnwireButtons()
    {
        if (!_wired) return;
        if (healButton) healButton.onClick.RemoveAllListeners();
        if (buffButton) buffButton.onClick.RemoveAllListeners();
        for (int i = 0; i < trainingTargetButtons.Count; i++)
            if (trainingTargetButtons[i]) trainingTargetButtons[i].onClick.RemoveAllListeners();
        _wired = false;
    }

    private void AutoWireTrainingTargetsIfNeeded()
    {
        if (trainingTargetButtons != null && trainingTargetButtons.Count > 0)
            return;

        trainingTargetButtons = new List<Button>(3);

        var allButtons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < allButtons.Length; i++)
        {
            var b = allButtons[i];
            if (!b) continue;
            if (b == healButton || b == buffButton) continue;

            string n = b.name ?? string.Empty;
            bool looksTarget = n.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("train", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!looksTarget) continue;

            trainingTargetButtons.Add(b);
            if (trainingTargetButtons.Count >= 3) break;
        }

        trainingTargetButtons.Sort((a, b) => string.CompareOrdinal(a != null ? a.name : string.Empty, b != null ? b.name : string.Empty));
    }

    private void AutoWireTargetVisualsIfNeeded()
    {
        if (trainingTargetButtons == null || trainingTargetButtons.Count == 0)
            return;

        EnsureListSize(trainingTargetIcons, trainingTargetButtons.Count);
        EnsureListSize(trainingTargetLabels, trainingTargetButtons.Count);
        EnsureListSize(trainingTargetHpTexts, trainingTargetButtons.Count);

        for (int i = 0; i < trainingTargetButtons.Count; i++)
        {
            var btn = trainingTargetButtons[i];
            if (!btn) continue;

            if (trainingTargetLabels[i] == null)
                trainingTargetLabels[i] = FindTextInButton(btn, "name", "label", "title");

            if (trainingTargetHpTexts[i] == null)
                trainingTargetHpTexts[i] = FindTextInButton(btn, "hp", "health");

            if (trainingTargetIcons[i] == null)
                trainingTargetIcons[i] = FindIconInButton(btn);
        }
    }

    private static void EnsureListSize<T>(List<T> list, int size)
    {
        if (list == null) return;
        while (list.Count < size) list.Add(default);
    }

    private static TextMeshProUGUI FindTextInButton(Button btn, params string[] preferredNameHints)
    {
        var texts = btn.GetComponentsInChildren<TextMeshProUGUI>(true);
        if (texts == null || texts.Length == 0) return null;

        for (int i = 0; i < preferredNameHints.Length; i++)
        {
            string hint = preferredNameHints[i] ?? string.Empty;
            for (int j = 0; j < texts.Length; j++)
            {
                var t = texts[j];
                if (!t) continue;
                string n = t.name ?? string.Empty;
                if (n.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            }
        }

        return texts[0];
    }

    private static Image FindIconInButton(Button btn)
    {
        var images = btn.GetComponentsInChildren<Image>(true);
        if (images == null || images.Length == 0) return null;

        for (int i = 0; i < images.Length; i++)
        {
            var img = images[i];
            if (!img) continue;
            if (img == btn.image) continue;

            string n = img.name ?? string.Empty;
            if (n.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("portrait", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("avatar", StringComparison.OrdinalIgnoreCase) >= 0)
                return img;
        }

        for (int i = 0; i < images.Length; i++)
        {
            var img = images[i];
            if (!img) continue;
            if (img == btn.image) continue;
            return img;
        }

        return null;
    }
}
