using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OwnedMonsterListItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI idText;
    [SerializeField] private Button rootButton;

    [Header("Detail Panel (Assign Mode)")]
    [SerializeField] private MonsterDetailPanelUI detailPanel; 

    private OwnedMonsterData _data;
    private MonsterDataSO _def;

    void Awake()
    {
        // Auto-find the panel once
        if (detailPanel == null)
            detailPanel = FindAnyObjectByType<MonsterDetailPanelUI>(FindObjectsInactive.Include);
    }

    public void Setup(OwnedMonsterData data)
    {
        var def = (data != null && !string.IsNullOrEmpty(data.monsterId))
            ? MonsterLibraryLocator.GetById(data.monsterId)
            : null;
        Setup(data, def);
    }

    public void Setup(OwnedMonsterData data, MonsterDataSO def)
    {
        _data = data;
        _def  = def;

        // ─── Icon ───
        if (icon)
        {
            if (def && def.icon)
            {
                icon.enabled = true;
                icon.sprite = def.icon;
            }
            else
            {
                icon.enabled = false;
                icon.sprite = null;
            }
        }

        // ─── Text Fields ───
        if (nameText)
            nameText.text = def
                ? (string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName)
                : "Unknown";

        if (idText)
            idText.text = (data != null && !string.IsNullOrEmpty(data.monsterId))
                ? data.monsterId
                : "—";

        // ─── Button ───
        if (rootButton)
        {
            rootButton.onClick.RemoveAllListeners();
            rootButton.onClick.AddListener(OnClickOpenDetails);
            rootButton.interactable = (data != null && !string.IsNullOrEmpty(data.monsterId));
        }
    }

    public void SetInteractable(bool on)
    {
        if (rootButton)
            rootButton.interactable = on;
    }

    private void OnClickOpenDetails()
    {
        if (detailPanel == null)
        {
            Debug.LogWarning("[OwnedMonsterListItemUI] Could not find MonsterDetailPanelUI in scene.");
            return;
        }

        if (_data == null || string.IsNullOrEmpty(_data.monsterId))
            return;

        detailPanel.ShowAssign(_data);
    }
}
