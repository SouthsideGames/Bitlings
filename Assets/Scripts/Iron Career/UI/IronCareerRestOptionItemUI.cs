using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI row/card for a Rest option (RestOptionPrefab).
/// Pure view: displays title/description/preview and handles selection visuals.
/// </summary>
public sealed class IronCareerRestOptionItemUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI titleTMP;
    [SerializeField] private TextMeshProUGUI descTMP;
    [SerializeField] private TextMeshProUGUI previewTMP;

    [Header("Selection Visuals")]
    [SerializeField] private GameObject selectedFrame;
    [SerializeField] private Image background;
    [SerializeField] private Color selectedTint = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color unselectedTint = new Color(0.9f, 0.9f, 0.9f, 1f);

    public IronCareerRestPanelUI.RestOption Option { get; private set; } = IronCareerRestPanelUI.RestOption.None;

    private Action _onClick;

    private void Awake()
    {
        if (!button) button = GetComponentInChildren<Button>(true);
        if (button) button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button) button.onClick.RemoveListener(HandleClick);
    }

    public void Bind(IronCareerRestPanelUI.RestOption option, string title, string desc, string preview)
    {
        EnsureButtonHierarchyActive();

        Option = option;

        if (titleTMP) titleTMP.text = title ?? string.Empty;
        if (descTMP) descTMP.text = desc ?? string.Empty;
        if (previewTMP) previewTMP.text = preview ?? string.Empty;
    }

    public void SetOnClick(Action onClick)
    {
        EnsureButtonHierarchyActive();

        _onClick = onClick;
        if (button) button.interactable = onClick != null;
    }

    public void SetSelected(bool selected)
    {
        if (selectedFrame) selectedFrame.SetActive(selected);
        if (background) background.color = selected ? selectedTint : unselectedTint;
    }

    public void SetInteractable(bool interactable)
    {
        EnsureButtonHierarchyActive();
        if (button) button.interactable = interactable;
    }

    private void EnsureButtonHierarchyActive()
    {
        if (!button) return;

        var current = button.transform;
        while (current)
        {
            if (!current.gameObject.activeSelf) current.gameObject.SetActive(true);
            if (current == transform) break;
            current = current.parent;
        }
    }

    private void HandleClick()
    {
        _onClick?.Invoke();
    }
}
