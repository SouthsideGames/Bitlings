using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ManualNavButtonUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Button button;

    [Header("Visuals")]
    [SerializeField] private Image background;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = new Color(0.8f, 0.9f, 1f, 1f);

    private Action _onClick;

    /// <summary>
    /// Initializes this nav button with a title and click handler.
    /// </summary>
    public void Setup(string title, Action onClick)
    {
        if (label != null)
            label.text = title;

        _onClick = onClick;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }

        SetSelected(false);
    }

    /// <summary>
    /// Visually mark this button as selected or not.
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (background != null)
        {
            background.color = selected ? selectedColor : normalColor;
        }
    }

    private void HandleClick()
    {
        _onClick?.Invoke();
    }
}
