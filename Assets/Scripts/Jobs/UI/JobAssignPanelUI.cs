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

    private JobType _job;
    private int _slotIndex;
    private WorkerRef _currentWorker;
    private MonsterDataSO _pendingDef;
    private string _pendingId;

    // Cache eligible types for the opened site
    private MonsterType[] _eligibleCache;

    public void Open(JobType job, int slotIndex)
    {
        _job = job;
        _slotIndex = slotIndex;

        var s = JobManager.I?.States.Find(x => x.config.jobType == _job);
        _currentWorker = null;
        if (s != null && slotIndex >= 0 && slotIndex < s.workers.Count)
            _currentWorker = s.workers[slotIndex];

        // cache eligible types for this site (allow-all if null/empty)
        _eligibleCache = s?.config?.eligibleTypes;

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

            // Eligibility filter: block monsters whose type isn't in site's allowed list (unless list is empty)
            bool allowed = (_eligibleCache == null || _eligibleCache.Length == 0);
            if (!allowed)
            {
                for (int i = 0; i < _eligibleCache.Length; i++)
                {
                    if (_eligibleCache[i] == def.type) { allowed = true; break; }
                }
            }
            if (!allowed) continue;

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
                    currentImage.color  = _pendingDef.icon ? Color.white : new Color(1f,1f,1f,0.6f);
                }
            });
        }
    }

    float EffectivenessScore(JobType job, MonsterDataSO def)
    {
        // Lightweight scoring; keep your existing mapping if you have one elsewhere.
        // This retains your prior behavior (shows strongest options first).
        float baseScore = 1f;
        if (def == null) return baseScore;

        // Example: nudge by rarity/tier if present
        baseScore += def.rarity switch
        {
            Rarity.Common => 0f,
            Rarity.Rare => 0.2f,
            Rarity.Epic => 0.45f,
            Rarity.Legendary => 0.7f,
            _ => 0f
        };

        // Example tie-breaker by attack/hp if your MonsterDataSO has those
        baseScore += def.attack * 0.01f + def.maxHP * 0.005f;

        return baseScore;
    }

    void OnConfirm()
    {
        if (JobManager.I == null)
        {
            Close();
            return;
        }

        if (_pendingDef == null)
        {
            Close();
            return;
        }

        // Double-check eligibility in case something slipped through
        if (_pendingDef != null)
        {
            bool allowed = (_eligibleCache == null || _eligibleCache.Length == 0);
            if (!allowed)
            {
                for (int i = 0; i < _eligibleCache.Length; i++)
                {
                    if (_eligibleCache[i] == _pendingDef.type) { allowed = true; break; }
                }
            }
            if (!allowed)
            {
                // optional: show toast/error sfx here
                Close();
                return;
            }
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
        var jobsPanel = FindAnyObjectByType<JobSiteView>(FindObjectsInactive.Include);
        if (jobsPanel) jobsPanel.RefreshAll();
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