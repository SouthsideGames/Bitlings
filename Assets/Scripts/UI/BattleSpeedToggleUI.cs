using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BattleSpeedToggleUI : MonoBehaviour
{
    [SerializeField] private BattleManager battle;
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI label;

    static readonly float[] OPTIONS = { 1f, 2f, 3f };
    int idx = 0;

    void Awake()
    {
        if (!battle) battle = FindFirstObjectByType<BattleManager>(FindObjectsInactive.Include);

        float saved = 1f;
        if (SaveManager.Data != null && SaveManager.Data.settings != null)
            saved = Mathf.Clamp(SaveManager.Data.settings.battleSpeed, 0.25f, 5f);

        idx = ClosestIndex(saved);
        Apply(OPTIONS[idx], save:false);

        if (button) button.onClick.AddListener(OnClick);
        RefreshLabel();
    }

    void OnDestroy()
    {
        if (button) button.onClick.RemoveListener(OnClick);
    }

    void OnClick()
    {
        idx = (idx + 1) % OPTIONS.Length;
        Apply(OPTIONS[idx], save:true);
        RefreshLabel();
    }

    void Apply(float speed, bool save)
    {
        if (battle) battle.SetBattleSpeed(speed);
        if (save && SaveManager.Data != null && SaveManager.Data.settings != null)
        {
            SaveManager.Data.settings.battleSpeed = speed;
            SaveManager.Save();
        }
    }

    void RefreshLabel()
    {
        if (!label) return;
        float s = (battle != null) ? battle.BattleSpeed : OPTIONS[idx];
        label.text = $"x{s:0.#}";
    }

    int ClosestIndex(float s)
    {
        int ci = 0; float best = Mathf.Infinity;
        for (int i = 0; i < OPTIONS.Length; i++)
        {
            float d = Mathf.Abs(OPTIONS[i] - s);
            if (d < best) { best = d; ci = i; }
        }
        return ci;
    }
}
