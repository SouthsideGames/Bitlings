using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TagDetailPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private TagListItemUI itemPrefab;
    [SerializeField] private TextMeshProUGUI headerText;

    private string _monsterId;
    private MonsterDataSO _def;

    public void Show(string monsterId, MonsterDataSO def)
    {
        _monsterId = monsterId;
        _def = def;
        Refresh();
    }

    public void Refresh()
    {
        if (!_def || _def.tagTrack == null) return;

        foreach (Transform c in listRoot) Destroy(c.gameObject);

        var track = _def.tagTrack;
        var rec = TagSave.GetOrCreate(_monsterId);
        if (headerText) headerText.text = $"Tags (Lv {Mathf.Max(1, rec.jobLevel)}/{track.maxLevel})";

        for (int i = 0; i < track.tags.Length; i++)
        {
            var tag = track.tags[i];
            int need = (track.unlockLevels != null && i < track.unlockLevels.Length) ? track.unlockLevels[i] : 999;
            var ui = Instantiate(itemPrefab, listRoot);
            ui.Bind(_monsterId, tag, need, rec);
        }
    }
}
