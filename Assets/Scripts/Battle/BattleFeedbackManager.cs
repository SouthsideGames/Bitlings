using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Centralized “juice” manager for battle.
/// Owns LeanTween feedback: button presses, icon punches, hit reactions, damage numbers,
/// defend/guard feedback, HP shakes, screen shakes, panel reveals.
/// 
/// This is presentation-only: BattleManager calls into this to update visuals.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleFeedbackManager : MonoBehaviour
{
    public enum BattleFeedbackSide { Player, Wild }
    public enum BattleFeedbackAction { Attack, Defend, Focus, Run }

    [Header("Icon Targets")]
    [SerializeField] private Graphic playerIcon;
    [SerializeField] private Graphic wildIcon;

    [Header("Optional: HP Roots (shake on damage)")]
    [SerializeField] private RectTransform playerHPShakeRoot;
    [SerializeField] private RectTransform wildHPShakeRoot;

    [Header("Optional: Guard Icons (defend FX)")]
    [SerializeField] private Image playerGuardIcon;
    [SerializeField] private Image wildGuardIcon;

    [Header("Optional: Charge Icons (focus/charge status)")]
    [SerializeField] private Image playerChargeIcon;
    [SerializeField] private Image wildChargeIcon;

    [Header("Optional: Action Buttons (press feedback)")]
    [SerializeField] private Button attackBtn;
    [SerializeField] private Button defendBtn;
    [SerializeField] private Button focusBtn;
    [SerializeField] private Button runBtn;

    [Header("FX - Timing (unscaled)")]
    [SerializeField, Min(0.01f)] private float pressPunchTime = 0.08f;
    [SerializeField, Min(0.01f)] private float windupTime = 0.10f;
    [SerializeField, Min(0.01f)] private float hitFlashTime = 0.10f;
    [SerializeField, Min(0.01f)] private float hitShakeTime = 0.12f;
    [SerializeField, Min(0.01f)] private float defendPulseTime = 0.16f;

    [Header("FX - Strength")]
    [SerializeField, Range(1.01f, 1.30f)] private float pressPunchScale = 1.08f;
    [SerializeField, Range(1.01f, 1.35f)] private float windupScale = 1.10f;
    [SerializeField, Range(0f, 30f)] private float hitShakePixels = 10f;

    [Header("HP Shake Settings")]
    [SerializeField, Min(0.01f)] private float hpShakeDuration = 0.25f;
    [SerializeField, Range(0f, 30f)] private float hpShakeStrength = 8f;

    [Header("FX - Colors (icon flash)")]
    [SerializeField] private Color flashNormal = Color.white;
    [SerializeField] private Color flashCrit = new Color(1f, 0.92f, 0.30f);
    [SerializeField] private Color flashDefend = new Color(0.55f, 0.85f, 1f);
    [SerializeField] private Color flashFail = new Color(1f, 0.45f, 0.45f);

    [Header("Crit / Heavy Hit Extras")]
    [SerializeField, Range(1.0f, 2.0f)] private float critExtraShakeMult = 1.35f;
    [SerializeField, Range(0f, 1f)] private float heavyHitThreshold01 = 0.30f;
    [SerializeField, Range(1.0f, 2.5f)] private float heavyExtraShakeMult = 1.6f;

    [Header("Attack Prefab VFX (optional)")]
    [SerializeField] private bool spawnAttackPrefabs = true;

    [Header("Damage Number FX (optional)")]
    [SerializeField] private DamageNumberUI damageNumberPrefab;
    [SerializeField] private RectTransform playerDamageAnchor;
    [SerializeField] private RectTransform wildDamageAnchor;

    [Header("Damage Number Colors (optional)")]
    [SerializeField] private Color dmgNormalColor = Color.white;
    [SerializeField] private Color dmgCritColor = new Color(1f, 0.9f, 0.35f);
    [SerializeField] private Color dmgWeakColor = new Color(0.55f, 0.8f, 1f);
    [SerializeField] private Color dmgResistColor = new Color(0.75f, 0.75f, 0.75f);

    [Header("Screen Shake (optional)")]
    [Tooltip("If empty, will fall back to Camera.main.transform")]
    [SerializeField] private Transform screenShakeRoot;
    [SerializeField, Range(0f, 50f)] private float heavyHitShakeMagnitude = 12f;
    [SerializeField, Min(0.01f)] private float heavyHitShakeDuration = 0.15f;

     [Header("HP Text Feedback (Current/Max)")]
    [SerializeField] private TextMeshProUGUI playerHPValueText;
    [SerializeField] private TextMeshProUGUI wildHPValueText;

    [Tooltip("If enabled, briefly punches the HP text when the value changes.")]
    [SerializeField] private bool hpTextPunchOnChange = true;

    [SerializeField, Min(0.01f)] private float hpTextPunchScale = 1.12f;
    [SerializeField, Min(0.01f)] private float hpTextPunchTime = 0.10f;

    private int _lastPlayerCur = int.MinValue;
    private int _lastPlayerMax = int.MinValue;
    private int _lastWildCur = int.MinValue;
    private int _lastWildMax = int.MinValue;

    private Vector3 _playerIconBaseScale = Vector3.one;
    private Vector3 _wildIconBaseScale = Vector3.one;

    private void Awake()
    {
        CacheBaseScales();
        WireOptionalButtonPresses();
    }

    private void OnEnable()
    {
        CacheBaseScales();
    }

    private void CacheBaseScales()
    {
        if (playerIcon && playerIcon.rectTransform) _playerIconBaseScale = playerIcon.rectTransform.localScale;
        if (wildIcon && wildIcon.rectTransform) _wildIconBaseScale = wildIcon.rectTransform.localScale;
    }

    private void WireOptionalButtonPresses()
    {
        if (attackBtn) attackBtn.onClick.AddListener(() => PlayButtonPress(BattleFeedbackAction.Attack));
        if (defendBtn) defendBtn.onClick.AddListener(() => PlayButtonPress(BattleFeedbackAction.Defend));
        if (focusBtn) focusBtn.onClick.AddListener(() => PlayButtonPress(BattleFeedbackAction.Focus));
        if (runBtn) runBtn.onClick.AddListener(() => PlayButtonPress(BattleFeedbackAction.Run));
    }

    // ─────────────────────────────────────────────────────────────
    // Status icon toggles (these replace BattleManager chargeIcon.enabled)
    // ─────────────────────────────────────────────────────────────

    public void SetCharge(BattleFeedbackSide side, bool on)
    {
        var icon = (side == BattleFeedbackSide.Player) ? playerChargeIcon : wildChargeIcon;
        if (icon) icon.enabled = on;
    }

    public void SetChargePlayer(bool on) => SetCharge(BattleFeedbackSide.Player, on);
    public void SetChargeWild(bool on) => SetCharge(BattleFeedbackSide.Wild, on);

    public void SetGuard(BattleFeedbackSide side, bool on)
    {
        var icon = (side == BattleFeedbackSide.Player) ? playerGuardIcon : wildGuardIcon;
        if (icon) icon.enabled = on;
    }

    // ─────────────────────────────────────────────────────────────
    // Public API (existing + expanded)
    // ─────────────────────────────────────────────────────────────

    public void PlayButtonPress(BattleFeedbackAction action)
    {
        var btn = GetButton(action);
        if (!btn) return;

        LeanTween.cancel(btn.gameObject);

        var rt = btn.GetComponent<RectTransform>();
        if (!rt) return;

        var baseScale = rt.localScale;
        LeanTween.scale(rt, baseScale * 0.94f, pressPunchTime)
            .setEaseOutQuad()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (!rt) return;
                LeanTween.scale(rt, baseScale, pressPunchTime)
                    .setEaseOutBack()
                    .setIgnoreTimeScale(true);
            });
    }

    public void PlayActionQueued(BattleFeedbackSide side, BattleFeedbackAction action)
    {
        var icon = GetIcon(side);
        if (!icon) return;

        switch (action)
        {
            case BattleFeedbackAction.Attack:
                PunchScale(icon, pressPunchScale, pressPunchTime);
                break;

            case BattleFeedbackAction.Defend:
                Flash(icon, flashDefend, defendPulseTime);
                PunchScale(icon, 1.05f, defendPulseTime * 0.5f);
                break;

            case BattleFeedbackAction.Focus:
                PunchScale(icon, 1.06f, defendPulseTime);
                break;

            case BattleFeedbackAction.Run:
                Shake(icon.rectTransform, hitShakePixels * 0.6f, hitShakeTime * 0.8f);
                break;
        }
    }

    public void PlayAttackWindup(BattleFeedbackSide attackerSide)
    {
        var icon = GetIcon(attackerSide);
        if (!icon) return;

        PunchScale(icon, windupScale, windupTime);
        Nudge(icon.rectTransform, attackerSide == BattleFeedbackSide.Player ? +10f : -10f, windupTime);
    }

    public void PlayHitReaction(BattleFeedbackSide targetSide, bool crit, float damageRatio01 = -1f)
    {
        var icon = GetIcon(targetSide);
        if (!icon) return;

        float shakeMult = 1f;
        if (crit) shakeMult *= critExtraShakeMult;
        if (damageRatio01 >= 0f && damageRatio01 >= heavyHitThreshold01) shakeMult *= heavyExtraShakeMult;

        Flash(icon, crit ? flashCrit : flashNormal, hitFlashTime);
        Shake(icon.rectTransform, hitShakePixels * shakeMult, hitShakeTime);

        var hpRoot = (targetSide == BattleFeedbackSide.Player) ? playerHPShakeRoot : wildHPShakeRoot;
        if (hpRoot) PlayHPShake(hpRoot);

        if (damageRatio01 >= 0f && damageRatio01 >= heavyHitThreshold01)
            ScreenShake(heavyHitShakeMagnitude, heavyHitShakeDuration);
    }

    public void PlayDefendResult(BattleFeedbackSide side, bool success)
    {
        var icon = GetIcon(side);
        if (!icon) return;

        Flash(icon, success ? flashDefend : flashFail, defendPulseTime);
        PunchScale(icon, success ? 1.06f : 1.03f, defendPulseTime * 0.8f);

        if (side == BattleFeedbackSide.Player && playerGuardIcon) Punch(playerGuardIcon);
        if (side == BattleFeedbackSide.Wild && wildGuardIcon) Punch(wildGuardIcon);

        if (!success)
            Shake(icon.rectTransform, hitShakePixels * 0.6f, hitShakeTime * 0.75f);
    }

    public void PlayKO(BattleFeedbackSide side)
    {
        var icon = GetIcon(side);
        if (!icon) return;

        LeanTween.cancel(icon.gameObject);

        var rt = icon.rectTransform;
        if (!rt) return;

        var baseScale = rt.localScale;
        LeanTween.scale(rt, baseScale * 0.85f, 0.15f)
            .setEaseInBack()
            .setIgnoreTimeScale(true);

        FadeGraphic(icon, 0.35f, 0.18f);
    }

    public void ResetIconVisuals()
    {
        if (playerIcon && playerIcon.rectTransform)
        {
            LeanTween.cancel(playerIcon.gameObject);
            playerIcon.rectTransform.localScale = _playerIconBaseScale;
            RestoreGraphicColor(playerIcon);
            RestoreGraphicAlpha(playerIcon);
        }

        if (wildIcon && wildIcon.rectTransform)
        {
            LeanTween.cancel(wildIcon.gameObject);
            wildIcon.rectTransform.localScale = _wildIconBaseScale;
            RestoreGraphicColor(wildIcon);
            RestoreGraphicAlpha(wildIcon);
        }
    }

    public IEnumerator Co_RevealPanels(CanvasGroup wildCG, CanvasGroup playerCG, float duration)
    {
        float dur = Mathf.Max(0f, duration);

        if (wildCG) LeanTween.alphaCanvas(wildCG, 1f, dur).setIgnoreTimeScale(true);
        if (playerCG) LeanTween.alphaCanvas(playerCG, 1f, dur).setIgnoreTimeScale(true);

        if (dur > 0f)
            yield return new WaitForSecondsRealtime(dur);
    }

    public void SpawnBasicAttackVfx(bool isPlayerSide, MonsterDataSO playerDef, MonsterDataSO wildDef)
    {
        if (!spawnAttackPrefabs) return;

        MonsterDataSO def = isPlayerSide ? playerDef : wildDef;
        if (!def || !def.basicAttackPrefab) return;

        Transform spawnRoot = null;
        if (EncounterManager.I != null)
        {
            spawnRoot = isPlayerSide
                ? EncounterManager.I.EnemySpawnPoint
                : EncounterManager.I.PlayerSpawnPoint;
        }

        Vector3 pos = spawnRoot ? spawnRoot.position : Vector3.zero;
        Quaternion rot = spawnRoot ? spawnRoot.rotation : Quaternion.identity;

        var inst = Instantiate(def.basicAttackPrefab, pos, rot);
        if (spawnRoot) inst.transform.SetParent(spawnRoot, worldPositionStays: true);

        float life = Mathf.Max(0f, def.basicAttackPrefabLifetime);
        if (life > 0f) Destroy(inst, life);
    }

    public void SpawnDamageNumber(int amount, bool isCrit, float effectiveness, bool hitPlayer)
    {
        if (!damageNumberPrefab) return;

        RectTransform anchor = hitPlayer ? playerDamageAnchor : wildDamageAnchor;
        if (!anchor) return;

        var inst = Instantiate(damageNumberPrefab, anchor);

        Color color = dmgNormalColor;
        if (isCrit) color = dmgCritColor;
        else
        {
            if (effectiveness > 1.25f) color = dmgWeakColor;
            else if (effectiveness < 0.85f) color = dmgResistColor;
        }

        inst.Init(amount, color);
    }

    public void ScreenShake(float magnitude, float duration)
    {
        Transform target = screenShakeRoot;
        if (!target)
        {
            var cam = Camera.main;
            if (cam) target = cam.transform;
        }
        if (!target) return;

        Vector3 original = target.localPosition;

        LeanTween.value(gameObject, 0f, magnitude, duration)
            .setIgnoreTimeScale(true)
            .setOnUpdate(val =>
            {
                if (!target) return;
                float offset = Mathf.Sin(Time.unscaledTime * 80f) * val;
                target.localPosition = original + new Vector3(offset, 0f, 0f);
            })
            .setOnComplete(() =>
            {
                if (target) target.localPosition = original;
            });
    }

    public void PlayHPShake(RectTransform target)
    {
        if (!target) return;

        LeanTween.cancel(target.gameObject);

        Vector2 originalPos = target.anchoredPosition;

        LeanTween.moveX(target, originalPos.x + Random.Range(-hpShakeStrength, hpShakeStrength), hpShakeDuration)
            .setEasePunch()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (target) target.anchoredPosition = originalPos;
            });
    }

    public void PlayHPShakeForPlayer() => PlayHPShake(playerHPShakeRoot);
    public void PlayHPShakeForWild() => PlayHPShake(wildHPShakeRoot);

    public void PlayDefendShieldFX(bool isPlayer)
    {
        if (isPlayer)
        {
            if (playerGuardIcon)
            {
                Punch(playerGuardIcon);

                var g = playerGuardIcon;
                float startA = g.color.a;

                LeanTween.value(g.gameObject, 0.35f, startA, 0.35f)
                    .setIgnoreTimeScale(true)
                    .setOnUpdate(a =>
                    {
                        if (!g) return;
                        var c = g.color; c.a = a; g.color = c;
                    });
            }
        }
        else
        {
            if (wildGuardIcon) Punch(wildGuardIcon);
            else if (wildIcon) Punch(wildIcon);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private Button GetButton(BattleFeedbackAction a)
    {
        return a switch
        {
            BattleFeedbackAction.Attack => attackBtn,
            BattleFeedbackAction.Defend => defendBtn,
            BattleFeedbackAction.Focus => focusBtn,
            BattleFeedbackAction.Run => runBtn,
            _ => null
        };
    }

    private Graphic GetIcon(BattleFeedbackSide side)
    {
        return side == BattleFeedbackSide.Player ? playerIcon : wildIcon;
    }

    public void Punch(Graphic g)
    {
        if (!g || !g.rectTransform) return;

        var rt = g.rectTransform;
        LeanTween.cancel(g.gameObject);

        var baseScale = rt.localScale;
        LeanTween.scale(rt, baseScale * 1.06f, 0.08f)
            .setLoopPingPong(1)
            .setIgnoreTimeScale(true);
    }

    private void PunchScale(Graphic g, float scaleMult, float time)
    {
        if (!g || !g.rectTransform) return;

        var rt = g.rectTransform;
        LeanTween.cancel(g.gameObject);

        var baseScale = rt.localScale;
        LeanTween.scale(rt, baseScale * scaleMult, Mathf.Max(0.01f, time))
            .setEaseOutBack()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (!rt) return;
                LeanTween.scale(rt, baseScale, Mathf.Max(0.01f, time * 0.9f))
                    .setEaseOutQuad()
                    .setIgnoreTimeScale(true);
            });
    }

    private void Nudge(RectTransform rt, float pixelsX, float time)
    {
        if (!rt) return;

        Vector2 basePos = rt.anchoredPosition;
        LeanTween.moveX(rt, basePos.x + pixelsX, Mathf.Max(0.01f, time))
            .setEaseOutQuad()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (!rt) return;
                LeanTween.moveX(rt, basePos.x, Mathf.Max(0.01f, time * 0.9f))
                    .setEaseOutQuad()
                    .setIgnoreTimeScale(true);
            });
    }

    private void Flash(Graphic g, Color flashColor, float time)
    {
        if (!g) return;

        Color baseColor = g.color;
        LeanTween.value(g.gameObject, 0f, 1f, Mathf.Max(0.01f, time))
            .setIgnoreTimeScale(true)
            .setOnUpdate(t =>
            {
                if (!g) return;

                float up = (t <= 0.5f) ? (t / 0.5f) : (1f - (t - 0.5f) / 0.5f);
                g.color = Color.Lerp(baseColor, flashColor, up);
            })
            .setOnComplete(() =>
            {
                if (!g) return;
                g.color = baseColor;
            });
    }

    private void FadeGraphic(Graphic g, float targetAlpha, float time)
    {
        if (!g) return;

        var baseColor = g.color;
        float startA = baseColor.a;

        LeanTween.value(g.gameObject, startA, targetAlpha, Mathf.Max(0.01f, time))
            .setIgnoreTimeScale(true)
            .setOnUpdate(a =>
            {
                if (!g) return;
                var c = g.color;
                c.a = a;
                g.color = c;
            });
    }

    private void RestoreGraphicColor(Graphic g)
    {
        if (!g) return;
        var c = g.color;
        c.r = 1f; c.g = 1f; c.b = 1f;
        g.color = c;
    }

    private void RestoreGraphicAlpha(Graphic g)
    {
        if (!g) return;
        var c = g.color;
        c.a = 1f;
        g.color = c;
    }

    private void Shake(RectTransform rt, float pixels, float time)
    {
        if (!rt) return;

        LeanTween.cancel(rt.gameObject);

        Vector3 basePos = rt.localPosition;

        LeanTween.value(rt.gameObject, 0f, 1f, Mathf.Max(0.01f, time))
            .setIgnoreTimeScale(true)
            .setOnUpdate(t =>
            {
                if (!rt) return;

                float strength = Mathf.Lerp(pixels, 0f, t);
                float x = Random.Range(-strength, strength);
                float y = Random.Range(-strength, strength) * 0.35f;

                rt.localPosition = basePos + new Vector3(x, y, 0f);
            })
            .setOnComplete(() =>
            {
                if (!rt) return;
                rt.localPosition = basePos;
            });
    }

    public bool HasHPTextWired => (playerHPValueText != null) || (wildHPValueText != null);

    /// <summary>
    /// UI-only. Call from BattleManager whenever HP changes, when swapping, and at battle start.
    /// </summary>
    public void SetHPTexts(float playerCur, float playerMax, float wildCur, float wildMax)
    {
        int pCur = Mathf.CeilToInt(Mathf.Max(0f, playerCur));
        int pMax = Mathf.CeilToInt(Mathf.Max(1f, playerMax));
        int wCur = Mathf.CeilToInt(Mathf.Max(0f, wildCur));
        int wMax = Mathf.CeilToInt(Mathf.Max(1f, wildMax));

        bool pChanged = (pCur != _lastPlayerCur) || (pMax != _lastPlayerMax);
        bool wChanged = (wCur != _lastWildCur) || (wMax != _lastWildMax);

        _lastPlayerCur = pCur; _lastPlayerMax = pMax;
        _lastWildCur = wCur; _lastWildMax = wMax;

        if (playerHPValueText)
        {
            playerHPValueText.text = $"{pCur}/{pMax}";
            if (hpTextPunchOnChange && pChanged) PunchTMP(playerHPValueText);
        }

        if (wildHPValueText)
        {
            wildHPValueText.text = $"{wCur}/{wMax}";
            if (hpTextPunchOnChange && wChanged) PunchTMP(wildHPValueText);
        }
    }

    private void PunchTMP(TextMeshProUGUI tmp)
    {
        if (!tmp) return;

        // LeanTween-safe punch. Does not require a CanvasGroup.
        var t = tmp.rectTransform;
        if (!t) return;

        LeanTween.cancel(t);
        t.localScale = Vector3.one;

        // Quick up then back.
        LeanTween.scale(t, Vector3.one * hpTextPunchScale, hpTextPunchTime)
            .setEaseOutQuad()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (!t) return;
                LeanTween.scale(t, Vector3.one, hpTextPunchTime)
                    .setEaseInQuad()
                    .setIgnoreTimeScale(true);
            });
    }
}
