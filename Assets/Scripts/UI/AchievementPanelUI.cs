using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AchievementPanelUI : MonoBehaviour
{
    private AchievementLibrarySO achievementLibrary;
    private MonsterLibrarySO monsterLibrary;

    [Header("UI")]
    [SerializeField] GameObject itemPrefab;
    [SerializeField] Transform contentRoot;
    [SerializeField] TextMeshProUGUI tokenLabel;

    void Awake()
    {
        if (achievementLibrary == null) achievementLibrary = Resources.Load<AchievementLibrarySO>("AchievementLibrary");
        if (monsterLibrary == null) monsterLibrary = Resources.Load<MonsterLibrarySO>("MonsterLibrary");
    }

    void OnEnable()
    {
        BuildList();
        RefreshAll();
    }

    void OnDisable()
    {
        ClearList();
    }

    void BuildList()
    {
        if (contentRoot == null || itemPrefab == null || achievementLibrary == null) return;
        ClearList();
        for (int i = 0; i < achievementLibrary.entries.Count; i++)
        {
            var e = achievementLibrary.entries[i];
            if (e == null) continue;
            var go = Instantiate(itemPrefab, contentRoot);
            var ui = go.GetComponent<AchievementItemUI>();
            if (ui) ui.Bind(e, monsterLibrary);
        }
    }

    void ClearList()
    {
        if (contentRoot == null) return;
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);
    }

    void RefreshTokens()
    {
        if (tokenLabel)
            tokenLabel.text = ResourceManager.I.Get(ResourceType.Gems).ToString(); // ← show Gems
    }


    public void RefreshAll()
    {
        if (contentRoot == null) return;
        for (int i = 0; i < contentRoot.childCount; i++)
        {
            var ui = contentRoot.GetChild(i).GetComponent<AchievementItemUI>();
            if (ui) ui.Refresh();
        }
        RefreshTokens();
    }
}
