using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ManualPanelUI : MonoBehaviour
{
    [Header("Sections Data")]
    [SerializeField]
    private List<ManualSection> sections = new();  // Fill this in the Inspector

    [Header("Nav (Left Column)")]
    [SerializeField] private Transform navButtonContainer;   // Content object under VerticalLayoutGroup
    [SerializeField] private GameObject navButtonPrefab;     // Prefab with ManualNavButtonUI

    [Header("Content (Right Column)")]
    [SerializeField] private TextMeshProUGUI sectionTitleLabel;
    [SerializeField] private TextMeshProUGUI sectionBodyLabel;
    [SerializeField] private ScrollRect contentScrollRect;   // Right side ScrollRect

    private int _currentIndex = -1;
    private readonly List<ManualNavButtonUI> _navButtons = new();

    private void Awake()
    {
        BuildNavButtons();

        if (sections.Count > 0)
        {
            SelectSection(0);
        }
        else
        {
            Debug.LogWarning("[ManualPanelUI] No sections configured.");
        }
    }

    /// <summary>
    /// Clears and rebuilds all navigation buttons from the sections list.
    /// </summary>
    private void BuildNavButtons()
    {
        // Clear existing buttons
        for (int i = navButtonContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(navButtonContainer.GetChild(i).gameObject);
        }
        _navButtons.Clear();

        // Create a nav button for each section
        for (int i = 0; i < sections.Count; i++)
        {
            ManualSection section = sections[i];

            GameObject go = Instantiate(navButtonPrefab, navButtonContainer);
            ManualNavButtonUI navButton = go.GetComponent<ManualNavButtonUI>();

            if (navButton == null)
            {
                Debug.LogError("[ManualPanelUI] Nav button prefab is missing ManualNavButtonUI component.");
                continue;
            }

            int indexCapture = i; // avoid closure issue
            navButton.Setup(section.title, () => SelectSection(indexCapture));

            _navButtons.Add(navButton);
        }
    }

    /// <summary>
    /// Switches the right-hand content to show the section at index.
    /// </summary>
    private void SelectSection(int index)
    {
        if (index < 0 || index >= sections.Count)
        {
            Debug.LogWarning($"[ManualPanelUI] Invalid section index: {index}");
            return;
        }

        _currentIndex = index;
        ManualSection section = sections[index];

        if (sectionTitleLabel != null)
            sectionTitleLabel.text = section.title;

        if (sectionBodyLabel != null)
            sectionBodyLabel.text = section.body;

        // Update visual selected state on nav buttons
        for (int i = 0; i < _navButtons.Count; i++)
        {
            bool isSelected = (i == index);
            _navButtons[i].SetSelected(isSelected);
        }

        // Reset content scroll to top
        if (contentScrollRect != null)
        {
            contentScrollRect.verticalNormalizedPosition = 1f;
            // If using inertia, you might also want to force-update layout
        }
    }

    /// <summary>
    /// Optional: if you want to select a section by ID from code.
    /// </summary>
    public void SelectSectionById(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        for (int i = 0; i < sections.Count; i++)
        {
            if (sections[i].id == id)
            {
                SelectSection(i);
                return;
            }
        }
        Debug.LogWarning($"[ManualPanelUI] No section with id '{id}' found.");
    }
}
