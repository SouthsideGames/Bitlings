using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EvolutionPanelUI : MonoBehaviour
{
    [Header("Wires")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Image currentIcon;
    [SerializeField] private TextMeshProUGUI currentName;
    [SerializeField] private TextMeshProUGUI currentLevel;
    [SerializeField] private Image evolutionIcon;
    [SerializeField] private TextMeshProUGUI evolutionName;
    [SerializeField] private TextMeshProUGUI titleText;

    [Header("Buttons")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private string _pendingMonsterId;

    void Awake()
    {
        Hide();
    }

    void OnEnable()
    {
        GameEvents.EvolutionOffered += OnEvolutionOffered;
        if (confirmButton) confirmButton.onClick.AddListener(OnYes);
        if (cancelButton)  cancelButton.onClick.AddListener(Hide);
    }

    void OnDisable()
    {
        GameEvents.EvolutionOffered -= OnEvolutionOffered;
        if (confirmButton) confirmButton.onClick.RemoveListener(OnYes);
        if (cancelButton)  cancelButton.onClick.RemoveListener(Hide);
    }

    private void OnEvolutionOffered(string monsterId)
    {
        _pendingMonsterId = monsterId;

        var lib = MonsterLibraryLocator.Lib;
        var def = lib ? lib.GetById(monsterId) : null;
        var next = def ? def.evolutionForm : null;

        if (panelRoot) panelRoot.SetActive(true);

        OwnedMonsterData om = null;
        if (SaveManager.Data != null && SaveManager.Data.owned != null)
            om = SaveManager.Data.owned.Find(o => o.monsterId == monsterId);

        if (currentName) currentName.text = def ? def.displayName : monsterId;
        if (currentLevel) currentLevel.text = om != null ? $"Lv {om.level}" : "";

        if (evolutionName) evolutionName.text = next ? next.displayName : "—";

        if (titleText)
        {
            if (def != null && def.evolutionLevel > 0 && next != null)
                titleText.text = $"Evolve {def.displayName} into {next.displayName}?";
            else
                titleText.text = "This monster can evolve. Evolve now?";
        }

        if (currentIcon) currentIcon.sprite = def ? def.icon : null;
        if (evolutionIcon) evolutionIcon.sprite = next ? next.icon : null;
    }

    private void OnYes()
    {
        if (!string.IsNullOrEmpty(_pendingMonsterId))
        {
            EvolutionManager.TryEvolve(_pendingMonsterId, MonsterLibraryLocator.Lib);
        }
        Hide();
    }

    private void Hide()
    {
        if (panelRoot) panelRoot.SetActive(false);
        _pendingMonsterId = null;
    }
}
