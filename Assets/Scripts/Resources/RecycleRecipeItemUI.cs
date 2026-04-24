using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RecycleRecipeItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button rootButton;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image fromIconImage;
    [SerializeField] private TextMeshProUGUI fromAmountText;
    [SerializeField] private Image toIconImage;
    [SerializeField] private TextMeshProUGUI toAmountText;
    [SerializeField] private TextMeshProUGUI nameText;

    [Header("Colors")]
    [SerializeField] private Color selectedColor;
    [SerializeField] private Color unselectedColor;

    public RecycleRecipeSO Recipe { get; private set; }

    private Action<RecycleRecipeItemUI> _onClicked;

    public void Bind(
        RecycleRecipeSO recipe,
        Sprite fromSprite,
        Sprite toSprite,
        Action<RecycleRecipeItemUI> onClicked)
    {
        Recipe = recipe;
        _onClicked = onClicked;

        if (nameText)
            nameText.text = recipe ? recipe.displayName : string.Empty;

        if (fromIconImage)
        {
            fromIconImage.sprite = fromSprite;
            fromIconImage.enabled = fromSprite != null;
        }

        if (toIconImage)
        {
            toIconImage.sprite = toSprite;
            toIconImage.enabled = toSprite != null;
        }

        if (fromAmountText)
            fromAmountText.text = recipe ? recipe.fromAmount.ToString() : string.Empty;

        if (toAmountText)
            toAmountText.text = recipe ? recipe.toAmount.ToString() : string.Empty;

        if (rootButton)
        {
            rootButton.onClick.RemoveAllListeners();
            rootButton.onClick.AddListener(OnClick);
        }

        // Default state
        SetSelected(false);
    }

    private void OnClick()
    {
        AudioManager.I?.PlayClick();
        _onClicked?.Invoke(this);
    }

    public void SetSelected(bool selected)
    {
        if (backgroundImage)
            backgroundImage.color = selected ? selectedColor : unselectedColor;
    }
}
