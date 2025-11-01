using UnityEngine;
using UnityEngine.UI;

public class BattleEndUI : MonoBehaviour
{
    public static BattleEndUI I { get; private set; }

    [Header("Refs")]
    [SerializeField] private GameObject nextButtonRoot; // parent holder or button itself
    [SerializeField] private Button nextButton;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        if (nextButton) nextButton.onClick.AddListener(OnNextPressed);
        SetVisible(false);
    }

    public void ShowNextButton(bool show)
    {
        SetVisible(show);
    }

    void SetVisible(bool v)
    {
        if (nextButtonRoot) nextButtonRoot.SetActive(v);
        else if (nextButton) nextButton.gameObject.SetActive(v);
    }

    void OnNextPressed()
    {
        // Hide immediately to prevent double presses
        SetVisible(false);

        // Tell EncounterManager to proceed with the summary now (manual flow)
        EncounterManager.I?.OnUserPressedNextFromBattle();
    }
}