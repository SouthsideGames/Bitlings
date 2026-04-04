using UnityEngine;

public class BattleTempBuffs : MonoBehaviour
{
    public static BattleTempBuffs I { get; private set; }

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    // ─────────────────────────────────────────────────────────────
    // Hard cap to prevent unlimited bonus stacking across activations
    // ─────────────────────────────────────────────────────────────
    private const int MAX_BONUS = 500;

    // ─────────────────────────────────────────────────────────────
    // ATTACK CHANNEL
    // ─────────────────────────────────────────────────────────────
    int atkBonus;
    float atkStart;
    float atkEnd;

    public void ActivatePlayerAtkBonus(int bonus, float durationSeconds)
    {
        float now = Time.unscaledTime;
        if (IsAtkBonusActive())
        {
            atkBonus = Mathf.Min(atkBonus + Mathf.Max(0, bonus), MAX_BONUS);
            float remain = Mathf.Max(0f, atkEnd - now);
            atkEnd = now + remain + Mathf.Max(0.001f, durationSeconds);
        }
        else
        {
            atkBonus = Mathf.Min(Mathf.Max(0, bonus), MAX_BONUS);
            atkStart = now;
            atkEnd = now + Mathf.Max(0.001f, durationSeconds);
        }
    }

    public int GetPlayerAtkBonus() => IsAtkBonusActive() ? atkBonus : 0;
    public bool IsAtkBonusActive() => Time.unscaledTime < atkEnd && atkBonus > 0;
    public float GetAtkBonusRemainingSeconds() => IsAtkBonusActive() ? Mathf.Max(0f, atkEnd - Time.unscaledTime) : 0f;

    public float GetAtkBonusTotalSecondsIfActive(float fallbackTotal)
    {
        if (!IsAtkBonusActive()) return 0f;
        float total = Mathf.Max(0f, atkEnd - atkStart);
        return total > 0.001f ? total : Mathf.Max(0.001f, fallbackTotal);
    }

    public void ClearPlayerAtkBonus() => atkBonus = 0;

    // ─────────────────────────────────────────────────────────────
    // HP CHANNEL
    // ─────────────────────────────────────────────────────────────
    int hpBonus;
    float hpStart;
    float hpEnd;

    public void ActivatePlayerHPBonus(int bonus, float durationSeconds)
    {
        float now = Time.unscaledTime;
        if (IsHPBonusActive())
        {
            hpBonus = Mathf.Min(hpBonus + Mathf.Max(0, bonus), MAX_BONUS);
            float remain = Mathf.Max(0f, hpEnd - now);
            hpEnd = now + remain + Mathf.Max(0.001f, durationSeconds);
        }
        else
        {
            hpBonus = Mathf.Min(Mathf.Max(0, bonus), MAX_BONUS);
            hpStart = now;
            hpEnd = now + Mathf.Max(0.001f, durationSeconds);
        }
    }

    public int GetPlayerHPBonus() => IsHPBonusActive() ? hpBonus : 0;
    public bool IsHPBonusActive() => Time.unscaledTime < hpEnd && hpBonus > 0;
    public float GetHPBonusRemainingSeconds() => IsHPBonusActive() ? Mathf.Max(0f, hpEnd - Time.unscaledTime) : 0f;

    public float GetHPBonusTotalSecondsIfActive(float fallbackTotal)
    {
        if (!IsHPBonusActive()) return 0f;
        float total = Mathf.Max(0f, hpEnd - hpStart);
        return total > 0.001f ? total : Mathf.Max(0.001f, fallbackTotal);
    }

    public void ClearPlayerHPBonus() => hpBonus = 0;

    // ─────────────────────────────────────────────────────────────
    // SPEED CHANNEL
    // ─────────────────────────────────────────────────────────────
    int speedFlatBonus;
    float speedStart;
    float speedEnd;

    public void ActivatePlayerSpeedBonus(int flatBonus, float durationSeconds)
    {
        float now = Time.unscaledTime;
        if (IsSpeedBonusActive())
        {
            speedFlatBonus = Mathf.Min(speedFlatBonus + Mathf.Max(0, flatBonus), MAX_BONUS);
            float remain = Mathf.Max(0f, speedEnd - now);
            speedEnd = now + remain + Mathf.Max(0.001f, durationSeconds);
        }
        else
        {
            speedFlatBonus = Mathf.Min(Mathf.Max(0, flatBonus), MAX_BONUS);
            speedStart = now;
            speedEnd = now + Mathf.Max(0.001f, durationSeconds);
        }
    }

    public bool IsSpeedBonusActive() => Time.unscaledTime < speedEnd && speedFlatBonus > 0;
    public int GetPlayerSpeedFlatBonus() => IsSpeedBonusActive() ? speedFlatBonus : 0;
    public float GetSpeedBonusRemainingSeconds() => IsSpeedBonusActive() ? Mathf.Max(0f, speedEnd - Time.unscaledTime) : 0f;

    public float GetSpeedBonusTotalSecondsIfActive(float fallbackTotal)
    {
        if (!IsSpeedBonusActive()) return 0f;
        float total = Mathf.Max(0f, speedEnd - speedStart);
        return total > 0.001f ? total : Mathf.Max(0.001f, fallbackTotal);
    }

    public int ApplyPlayerSpeedBonus(int baseSpeed)
    {
        int result = baseSpeed + GetPlayerSpeedFlatBonus();
        return Mathf.Max(1, result);
    }

    public void ClearPlayerSpeedBonus() => speedFlatBonus = 0;

    // ─────────────────────────────────────────────────────────────
    // DEFENSE CHANNEL
    // ─────────────────────────────────────────────────────────────
    int defBonus;
    float defStart;
    float defEnd;

    public void ActivatePlayerDefenseBonus(int bonus, float durationSeconds)
    {
        float now = Time.unscaledTime;
        if (IsDefenseBonusActive())
        {
            defBonus = Mathf.Min(defBonus + Mathf.Max(0, bonus), MAX_BONUS);
            float remain = Mathf.Max(0f, defEnd - now);
            defEnd = now + remain + Mathf.Max(0.001f, durationSeconds);
        }
        else
        {
            defBonus = Mathf.Min(Mathf.Max(0, bonus), MAX_BONUS);
            defStart = now;
            defEnd = now + Mathf.Max(0.001f, durationSeconds);
        }
    }

    public bool IsDefenseBonusActive() => Time.unscaledTime < defEnd && defBonus > 0;
    public int GetPlayerDefenseBonus() => IsDefenseBonusActive() ? defBonus : 0;
    public float GetDefenseBonusRemainingSeconds() => IsDefenseBonusActive() ? Mathf.Max(0f, defEnd - Time.unscaledTime) : 0f;

    public float GetDefenseBonusTotalSecondsIfActive(float fallbackTotal)
    {
        if (!IsDefenseBonusActive()) return 0f;
        float total = Mathf.Max(0f, defEnd - defStart);
        return total > 0.001f ? total : Mathf.Max(0.001f, fallbackTotal);
    }

    public void ClearPlayerDefenseBonus() => defBonus = 0;

    // ─────────────────────────────────────────────────────────────
    // TYPE RESIST CHANNEL
    // ─────────────────────────────────────────────────────────────
    float trStart;
    float trEnd;

    public void ActivatePlayerTypeResist(float durationSeconds)
    {
        float now = Time.unscaledTime;
        if (IsTypeResistActive())
        {
            float remain = Mathf.Max(0f, trEnd - now);
            trEnd = now + remain + Mathf.Max(0.001f, durationSeconds);
        }
        else
        {
            trStart = now;
            trEnd = now + Mathf.Max(0.001f, durationSeconds);
        }
    }

    public bool IsTypeResistActive() => Time.unscaledTime < trEnd;
    public float GetTypeResistRemainingSeconds() => IsTypeResistActive() ? Mathf.Max(0f, trEnd - Time.unscaledTime) : 0f;

    public float GetTypeResistTotalSecondsIfActive(float fallbackTotal)
    {
        if (!IsTypeResistActive()) return 0f;
        float total = Mathf.Max(0f, trEnd - trStart);
        return total > 0.001f ? total : Mathf.Max(0.001f, fallbackTotal);
    }

    /// <summary>
    /// Reduces effectiveness of super-effective hits to neutral while resist is active.
    /// </summary>
    public float AdjustEffectivenessForPlayerDefense(float effectiveness)
    {
        if (!IsTypeResistActive()) return effectiveness;
        if (effectiveness > 1f) return 1f;
        return effectiveness;
    }

    // ─────────────────────────────────────────────────────────────
    // CLEAR ALL
    // ─────────────────────────────────────────────────────────────
    public void ClearAll()
    {
        ClearPlayerAtkBonus();
        ClearPlayerHPBonus();
        ClearPlayerSpeedBonus();
        ClearPlayerDefenseBonus();
        trStart = trEnd = 0f;
    }
}