using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class TitleAssignPanelUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] public RectTransform contentRoot;
    [SerializeField] public GameObject entryPrefab;

    [Header("Context (set via Open)")]
    [SerializeField] public string monsterOwnedId;
    [SerializeField] public MonsterDataSO monsterDef;
    [SerializeField] public int monsterLevel = 1;

    private readonly List<GameObject> _spawned = new();

    public static System.Action<string> OnTitlesChanged;

    
    

    public void Open(string ownedId, MonsterDataSO def, int level)
    {
        monsterOwnedId = ownedId;
        monsterDef     = def;
        monsterLevel   = Mathf.Max(1, level);

        Refresh();
        gameObject.SetActive(true);
    }

    public void Close()
    {
        Clear();
        gameObject.SetActive(false);
    }

    private void Refresh()
    {
        if (!contentRoot || !monsterDef || monsterDef.titleTrack == null || entryPrefab == null)
            return;

        Clear();

        var track    = monsterDef.titleTrack;
        var tiers    = track.tiers; // List<TitleTier>
        var equipped = TitleManager.I.GetEquippedList(monsterOwnedId, monsterDef, monsterLevel);

        for (int t = 0; t < tiers.Count; t++)
        {
            TitleTier tier = tiers[t];
            int levelReq   = Mathf.Max(1, tier.levelRequired);
            var selected   = (t < equipped.Count) ? equipped[t] : null;

            var options = tier.unlockChoices;
            if (options == null || options.Count == 0)
            {
                // Show a placeholder row so UX isn't confusing
                var empty = Instantiate(entryPrefab, contentRoot);
                _spawned.Add(empty);

                var item = empty.GetComponent<TitleOptionItem>();
                if (item)
                {
                    // Minimal row saying "No titles"
                    item.name = $"Tier{t+1}_Empty";
                    item.Setup(
                        monsterOwnedId, monsterDef, monsterLevel,
                        t, levelReq,
                        option: null,
                        equippedInTier: selected,
                        onChanged: Refresh
                    );
                    // If you want the row to visually look disabled, you can set its texts via TMP refs here.
                    var texts = empty.GetComponentsInChildren<TextMeshProUGUI>();
                    foreach (var txt in texts) txt.text = $"Tier {t+1}: <No titles>";
                }
                continue;
            }

            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                var go  = Instantiate(entryPrefab, contentRoot);
                _spawned.Add(go);

                var item = go.GetComponent<TitleOptionItem>();
                if (!item) continue;

                go.name = $"Tier{t+1}_{i}_{(opt ? opt.displayName : "null")}";

                item.Setup(
                    monsterOwnedId,
                    monsterDef,
                    monsterLevel,
                    t,
                    levelReq,
                    opt,
                    selected,
                    Refresh
                );

            }
        }
    }

    private void Clear()
    {
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i]) Destroy(_spawned[i]);
        _spawned.Clear();
    }
}
