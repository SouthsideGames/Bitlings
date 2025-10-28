using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TagListItemUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descText;
    [SerializeField] private TextMeshProUGUI lockText;
    [SerializeField] private Button assignBtn;
    [SerializeField] private Button removeBtn;

    private string _monsterId;
    private TagSO _tag;

    public void Bind(string monsterId, TagSO tag, int unlockLevel, MonsterTagRecord rec)
    {
        _monsterId = monsterId;
        _tag = tag;

        bool unlocked = tag && rec.unlockedTagIds.Contains(tag.id);
        bool equipped = tag && TagSave.IsEquipped(monsterId, tag.id);

        if (icon) icon.sprite = tag ? tag.icon : null;
        if (nameText) nameText.text = tag ? tag.displayName : "—";
        if (descText) descText.text = tag ? tag.desc : "";

        if (lockText)
        {
            lockText.gameObject.SetActive(!unlocked);
            if (!unlocked) lockText.text = $"Unlocks at Lv {unlockLevel}";
        }

        if (assignBtn)
        {
            assignBtn.gameObject.SetActive(unlocked && !equipped);
            assignBtn.onClick.RemoveAllListeners();
            assignBtn.onClick.AddListener(() =>
            {
                if (tag == null) return;
                if (TagSave.TryEquip(_monsterId, tag.id))
                {
                    SaveManager.Save();
                    // Re-bind using the same record instance
                    Bind(_monsterId, _tag, unlockLevel, TagSave.GetOrCreate(_monsterId));
                }
            });
        }

        if (removeBtn)
        {
            removeBtn.gameObject.SetActive(unlocked && equipped);
            removeBtn.onClick.RemoveAllListeners();
            removeBtn.onClick.AddListener(() =>
            {
                TagSave.Unequip(_monsterId, tag.id);
                SaveManager.Save();
                Bind(_monsterId, _tag, unlockLevel, TagSave.GetOrCreate(_monsterId));
            });
        }

        var cg = GetComponent<CanvasGroup>();
        if (cg) cg.alpha = unlocked ? 1f : 0.5f;
    }

}
