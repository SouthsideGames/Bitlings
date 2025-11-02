using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class JobAssignPanelUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private PanelId panelId = PanelId.JobAssign; // centralize open/close via UIManager

    [Header("Wiring")]
    [SerializeField] private Image currentImage;
    [SerializeField] private Sprite emptySlotSprite;
    [SerializeField] private RectTransform listContent;
    [SerializeField] private GameObject monsterButtonPrefab;
    [SerializeField] private Button confirmBtn;
    [SerializeField] private Button removeBtn;

// Cache of site eligibility pulled when panel opens
    private MonsterType[] _eligibleCache;

    private JobType _job;
    private int _slotIndex;
    private WorkerRef _currentWorker;
    private MonsterDataSO _pendingDef;
    private string _pendingId;

    public void Open(JobType job, int slotIndex)
    {
        _job = job;
        _slotIndex = slotIndex;

        var s = JobManager.I?.States.Find(x => x.config.jobType == _job);
        _currentWorker = null;
        if (s != null && slotIndex >= 0 && slotIndex < s.workers.Count)
            _currentWorker = s.workers[slotIndex];

        // reset pending selection
        _pendingDef = null;
        _pendingId = null;

        if (currentImage)
        {
            if (_currentWorker != null && _currentWorker.def && _currentWorker.def.icon)
            {
                currentImage.sprite = _currentWorker.def.icon;
                currentImage.color  = Color.white;
            }
            else
            {
                currentImage.sprite = emptySlotSprite;
                currentImage.color  = new Color(1f, 1f, 1f, 0.6f);
            }
        }

        BuildList();

        if (confirmBtn)
        {
            confirmBtn.onClick.RemoveAllListeners();
            confirmBtn.onClick.AddListener(OnConfirm);
            confirmBtn.interactable = true;
        }

        if (removeBtn)
        {
            removeBtn.gameObject.SetActive(true);
            removeBtn.onClick.RemoveAllListeners();
            removeBtn.onClick.AddListener(OnRemove);
            removeBtn.interactable = (_currentWorker != null);
        }

        OpenSelf();
    }

    void BuildList()
    {
        if (!listContent) return;

        for (int i = listContent.childCount - 1; i >= 0; i--)
            Destroy(listContent.GetChild(i).gameObject);

        var data = SaveManager.Data;
        if (data == null || data.owned == null || monsterButtonPrefab == null) return;

        var entries = new List<(MonsterDataSO def, string id, float score)>();
        foreach (var o in data.owned)
        {
            if (string.IsNullOrEmpty(o?.monsterId)) continue;
            var def = MonsterLibraryLocator.GetById(o.monsterId);
            if (!def) continue;

            float score = EffectivenessScore(_job, def);
            entries.Add((def, o.monsterId, score));
        }
        entries.Sort((a, b) => b.score.CompareTo(a.score));

        foreach (var e in entries)
        {
            var go = Instantiate(monsterButtonPrefab, listContent);
            var ui = go.GetComponent<JobMonsterEntryUI>();
            if (!ui) continue;

            if (ui.icon)
            {
                ui.icon.sprite = e.def.icon;
                ui.icon.enabled = e.def.icon;
            }
            if (ui.nameText)  ui.nameText.text  = e.def.displayName;
            if (ui.scoreText) ui.scoreText.text = $"x{e.score:0.##}";
            if( ui.typeIcon) ui.typeIcon.sprite = e.def.typeIcon;

            ui.button.onClick.RemoveAllListeners();
            ui.button.onClick.AddListener(() =>
            {
                _pendingDef = e.def;
                _pendingId  = e.id;

                if (currentImage)
                {
                    currentImage.sprite = _pendingDef.icon ? _pendingDef.icon : emptySlotSprite;
                    currentImage.color  = _pendingDef.icon ? Color.white : new Color(1, 1, 1, 0.6f);
                }
            });
        }
    }

    float EffectivenessScore(JobType job, MonsterDataSO def)
    {
        return def.jobSkill
             * JobBalance.RarityMult(def.rarity)
             * JobBalance.EvolutionMult(def.evolutionStage)
             * JobBalance.AffinityMult(job, def.type);
    }

    void OnConfirm()
    {
        if (JobManager.I == null) { Close(); return; }

        if (_pendingDef == null)
        {
            Close();
            return;
        }

        JobManager.I.RemoveFromAnyJob(_pendingId);
        JobManager.I.TryAssignWorkerAt(_job, _slotIndex, _pendingDef, _pendingId);
        Close();
    }

    void OnRemove()
    {
        if (JobManager.I == null) { Close(); return; }
        if (_currentWorker != null)
        {
            string id = !string.IsNullOrEmpty(_currentWorker.monsterId)
                        ? _currentWorker.monsterId
                        : _currentWorker.def?.id;
            if (!string.IsNullOrEmpty(id))
                JobManager.I.RemoveWorker(_job, id);
        }
        Close();
    }

    void Close()
    {
        CloseSelf();

        // Ask Jobs panel to refresh (non-blocking)
        var jobsUI = FindFirstObjectByType<JobPanelUI>();
        if (jobsUI) jobsUI.SendMessage("Refresh", SendMessageOptions.DontRequireReceiver);
    }

    // --- UIManager glue ---

    void OpenSelf()
    {
        if (UIManager.I) UIManager.I.Show(panelId);
        else gameObject.SetActive(true); // fallback if UIManager missing
    }

    void CloseSelf()
    {
        if (UIManager.I) UIManager.I.Hide(panelId);
        else gameObject.SetActive(false); // fallback
    }
}
