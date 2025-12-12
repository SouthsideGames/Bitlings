using UnityEngine;
using TMPro;

public class CreditsTextUI : MonoBehaviour
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
        int credits = ResourceBank.Get(ResourceType.Credits);
        _label.text = credits.ToString(); // just the number
    }
}
