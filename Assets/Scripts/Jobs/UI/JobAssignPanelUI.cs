using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class JobAssignPanelUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private PanelId panelId = PanelId.JobAssign; 

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
        _pendingId  = null;

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
        if (data == null || data.owned == null)
        {
            Debug.LogWarning("[JobAssignPanelUI] No save data or owned list.");
            return;
        }
        if (monsterButtonPrefab == null)
        {
            Debug.LogError("[JobAssignPanelUI] monsterButtonPrefab is not assigned.");
            return;
        }

        // 1) De-dupe owned list to enforce: at most one normal + one shiny per species.
        //    If duplicates exist, keep the 'best' entry (higher level, then XP, then shinyTier).
        int rawOwnedCount = data.owned.Count;

        // key = speciesId + "|S" (shiny) or "|N" (normal)
        var bestByKey = new Dictionary<string, OwnedMonsterData>(64);
        for (int i = 0; i < data.owned.Count; i++)
        {
            var o = data.owned[i];
            if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;

            // ensure a unique ownedUID (used for assignments/cooldowns)
            if (string.IsNullOrEmpty(o.ownedUID))
                o.ownedUID = System.Guid.NewGuid().ToString("N");

            string key = o.monsterId + (o.isShiny ? "|S" : "|N");
            if (!bestByKey.TryGetValue(key, out var cur))
            {
                bestByKey[key] = o;
            }
            else
            {
                // choose the better entry
                bool better =
                    o.level > cur.level ||
                    (o.level == cur.level && o.currentXP > cur.currentXP) ||
                    (o.level == cur.level && o.currentXP == cur.currentXP && o.shinyTier > cur.shinyTier);

                if (better) bestByKey[key] = o;
            }
        }

        // 2) Build eligible entries from the de-duped set
        var entries = new List<(MonsterDataSO def, string ownedUid, float score)>();
        int eligibleCount = 0;
        foreach (var kv in bestByKey)
        {
            var owned = kv.Value;
            var def = MonsterLibraryLocator.GetById(owned.monsterId);
            if (!def) continue;

            // Ask JobManager (single source of truth) if this type can work here.
            bool allowed = JobManager.I == null ? true : JobManager.I.IsTypeEligibleFor(_job, def.type);
            if (!allowed) continue;

            eligibleCount++;
            float score = EffectivenessScore(_job, def);
            entries.Add((def, owned.ownedUID, score));
        }

        // Highest score first
        entries.Sort((a, b) => b.score.CompareTo(a.score));

        // 3) Debug that matches what you actually see
        Debug.Log($"[JobAssignPanelUI] Job={_job} RawOwned={rawOwnedCount} DistinctOwned={bestByKey.Count} Eligible={eligibleCount}");

        // 4) UI rows
        if (entries.Count == 0)
        {
            var placeholder = new GameObject("NoEligibleHint", typeof(RectTransform));
            placeholder.transform.SetParent(listContent, false);
            var text = placeholder.AddComponent<TextMeshProUGUI>();
            text.text = "No eligible workers";
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 18;
            text.fontSizeMax = 28;
            var rt = (RectTransform)placeholder.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0, 80);
            return;
        }

        foreach (var e in entries)
        {
            var go = Instantiate(monsterButtonPrefab, listContent);
            var ui = go.GetComponent<JobMonsterEntryUI>();

            if (!ui)
            {
                var btn = go.GetComponent<Button>();
                var label = go.GetComponentInChildren<TextMeshProUGUI>();
                if (label) label.text = e.def.displayName;
                if (btn)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() =>
                    {
                        _pendingDef = e.def;
                        _pendingId  = e.ownedUid;

                        if (currentImage)
                        {
                            currentImage.sprite = _pendingDef.icon ? _pendingDef.icon : emptySlotSprite;
                            currentImage.color  = _pendingDef.icon ? Color.white : new Color(1, 1, 1, 0.6f);
                        }
                    });
                }
                continue;
            }

            if (ui.icon)
            {
                ui.icon.sprite = e.def.icon;
                ui.icon.enabled = e.def.icon;
            }
            if (ui.nameText)  ui.nameText.text  = e.def.displayName;
            if (ui.scoreText) ui.scoreText.text = $"x{e.score:0.##}";
            if (ui.typeIcon)  ui.typeIcon.sprite = e.def.typeIcon;

            ui.button.onClick.RemoveAllListeners();
            ui.button.onClick.AddListener(() =>
            {
                _pendingDef = e.def;
                _pendingId  = e.ownedUid;

                if (currentImage)
                {
                    currentImage.sprite = _pendingDef.icon ? _pendingDef.icon : emptySlotSprite;
                    currentImage.color  = _pendingDef.icon ? Color.white : new Color(1, 1, 1, 0.6f);
                }

                AudioManager.I.PlayClick();
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
        if (_pendingDef == null) { Close(); return; }

        // Re-check eligibility on confirm (defensive)
        if (!JobManager.I.IsTypeEligibleFor(_job, _pendingDef.type))
        {
            Debug.LogWarning($"[JobAssignPanelUI] {_pendingDef.displayName} not eligible for {_job}.");
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
