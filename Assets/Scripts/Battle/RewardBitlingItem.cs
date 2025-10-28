using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RewardBitlingItem : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text countText; 

    public void Set(Sprite sprite, string displayName, int count)
    {
        if (icon) icon.sprite = sprite;
        if (nameText)  nameText.text  = displayName ?? "";
        if (countText) countText.text = count > 1 ? $"×{count}" : "";
    }
}
