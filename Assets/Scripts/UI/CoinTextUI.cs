using UnityEngine;
using TMPro;

public class CoinTextUI : MonoBehaviour
{
    private TextMeshProUGUI _label;

    void Awake()
    {
        _label = GetComponent<TextMeshProUGUI>();
    }

    void OnEnable()
    {
        GameEvents.OnResourcesChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= Refresh;
    }

    private void Refresh()
    {
        if (!_label) return;
        int coins = ResourceBank.Get(ResourceType.Coin);
        _label.text = coins.ToString(); // just the number
    }
}
