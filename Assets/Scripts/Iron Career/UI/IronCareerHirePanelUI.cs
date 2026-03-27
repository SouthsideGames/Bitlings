using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class IronCareerHirePanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("UI")]
    [SerializeField] private Image portrait;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI statLabel;


    [Header("Buttons")]
    [SerializeField] private Button hireButton;
    [SerializeField] private Button skipButton;
    [SerializeField] private TextMeshProUGUI skipLabel;

    [Header("Hire Decision Result Prefabs")]
    [SerializeField] private Transform hireResultSpawnPoint;
    [SerializeField] private GameObject hireAgreePrefab;
    [SerializeField] private GameObject hireDenyPrefab;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();
        if (hireButton) hireButton.onClick.AddListener(OnClickHire);
        if (skipButton) skipButton.onClick.AddListener(OnClickSkip);
    }

    private void OnDestroy()
    {
        if (hireButton) hireButton.onClick.RemoveAllListeners();
        if (skipButton) skipButton.onClick.RemoveAllListeners();
    }

    public void Bind(IronMonster offer, bool skipAllowed)
    {
        ClearHireResultVisuals();

        if (portrait) portrait.sprite = offer != null && offer.def ? MonsterNameFormatter.GetIcon(offer.def, offer.isShiny, false) : null;
        if (nameLabel) nameLabel.text = offer != null && offer.def ? MonsterNameFormatter.Format(offer.def, offer.isShiny) : "-";
        if (levelLabel) levelLabel.text = offer != null ? $"Lv {Mathf.Max(1, offer.level)}" : string.Empty;
        if (titleLabel) titleLabel.text = (offer != null && offer.lockedTitle) ? offer.lockedTitle.displayName : "";

        if (statLabel)
        {
            if (offer != null && offer.def != null)
            {
                float hp = Mathf.Max(1f, BattleCalc.CalcHP(offer.def, Mathf.Max(1, offer.level)));
                float atk = BattleCalc.CalcBaseAttack(offer.def, Mathf.Max(1, offer.level), 0, 0);
                int def = BattleCalc.CalcDefense(offer.def, Mathf.Max(1, offer.level));
                int spd = BattleCalc.CalcSpeed(offer.def, Mathf.Max(1, offer.level));

                statLabel.text = $"HP {Mathf.RoundToInt(hp)}   ATK {Mathf.RoundToInt(atk)}   DEF {def}   SPD {spd}";
            }
            else
            {
                statLabel.text = "-";
            }
        }

        if (skipButton)
        {
            skipButton.interactable = skipAllowed;
            skipButton.gameObject.SetActive(skipAllowed);
        }
        if (skipLabel) skipLabel.text = skipAllowed ? "Skip" : "Skip (Hardcore)";
    }

    private void OnClickHire()
    {
        bool success = manager != null && manager.OnHireAccepted();
        SpawnHireResult(success);
    }

    private void OnClickSkip()
    {
        if (manager != null)
            manager.OnHireSkipped();

        SpawnHireResult(false);
    }

    private void SpawnHireResult(bool success)
    {
        if (!hireResultSpawnPoint) return;

        GameObject prefab = success ? hireAgreePrefab : hireDenyPrefab;
        if (!prefab) return;

        ClearHireResultVisuals();
        Instantiate(prefab, hireResultSpawnPoint.position, hireResultSpawnPoint.rotation, hireResultSpawnPoint);
    }

    private void ClearHireResultVisuals()
    {
        if (!hireResultSpawnPoint) return;

        for (int i = hireResultSpawnPoint.childCount - 1; i >= 0; i--)
        {
            var child = hireResultSpawnPoint.GetChild(i);
            if (child) Destroy(child.gameObject);
        }
    }
}
