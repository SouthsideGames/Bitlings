using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


[DisallowMultipleComponent]
public sealed class BattleFeedbackManager : MonoBehaviour
{
    public enum BattleFeedbackSide { Player, Wild }
    public enum BattleFeedbackAction { Attack, Defend, Focus, Swap, Run }

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

    
    [Header("Optional: Wild Intent Telegraph (icon bubble)")]
    [Tooltip("Optional root GameObject for the wild intent bubble. If not set, intent icons will not display.")]
    [SerializeField] private GameObject wildIntentRoot;

    [Tooltip("Icon inside the wild intent bubble.")]
    [SerializeField] private Image wildIntentIcon;

    [Tooltip("Optional text inside the wild intent bubble (kept off by default; BattleManager may use narration instead).")]
    [SerializeField] private TMP_Text wildIntentText;

    [Header("Wild Intent Sprites")]
    [SerializeField] private Sprite wildIntentAttackSprite;
    [SerializeField] private Sprite wildIntentDefendSprite;
    [SerializeField] private Sprite wildIntentFocusSprite;
    [SerializeField] private Sprite wildIntentRunSprite;

    [Header("Wild Intent FX")]
    [SerializeField, Min(0.01f)] private float wildIntentPopTime = 0.10f;
    [SerializeField, Min(0f)] private float wildIntentStartScale = 0.85f;

    private Coroutine _wildIntentCR;
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

    [Header("Shiny Name Sparkle (Optional)")]
    [Tooltip("If enabled, shiny monster names will punch-scale and 'sparkle' when they appear or are swapped in.")]
    [SerializeField] private bool enableShinyNameSparkle = true;
    [SerializeField, Min(0.01f)] private float shinyNamePunchTime = 0.12f;
    [SerializeField, Range(1.01f, 1.40f)] private float shinyNamePunchScale = 1.18f;
    [SerializeField, Min(0.01f)] private float shinyNameSparkleTime = 0.22f;
    [SerializeField, Range(0f, 25f)] private float shinyNameWiggleDegrees = 8f;
    [SerializeField, Range(0f, 15f)] private float shinyNameWiggleDuration = 0.25f;

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

    [Header("HP Bar Animation (Optional)")]
    [Tooltip("If set, FeedbackManager will animate these bars when SetHPBars is called.")]
    [SerializeField] private Slider playerHPBar;
    [SerializeField] private Slider wildHPBar;

    [SerializeField] private bool smoothHPBars = true;
    [SerializeField, Min(0.01f)] private float hpBarSecondsForFull = 0.6f;

    private Coroutine _playerHPAnimCR;
    private Coroutine _wildHPAnimCR;

    private Coroutine _playerGuardAutoHideCR;
    private Coroutine _wildGuardAutoHideCR;

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

        ResetStatusIcons();
    }

    private void OnEnable()
    {
        CacheBaseScales();

        ResetStatusIcons();
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
    // Status icon toggles (GameObject active/inactive)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Call at battle start / swap / reset. Forces guard + charge icons to inactive.
    /// </summary>
    public void ResetStatusIcons()
    {
        StopGuardAutoHideCR(BattleFeedbackSide.Player);
        StopGuardAutoHideCR(BattleFeedbackSide.Wild);

        SetStatusIconVisible(playerGuardIcon, false);
        SetStatusIconVisible(wildGuardIcon, false);
        SetStatusIconVisible(playerChargeIcon, false);
        SetStatusIconVisible(wildChargeIcon, false);
    }

    public void SetCharge(BattleFeedbackSide side, bool on)
    {
        var icon = (side == BattleFeedbackSide.Player) ? playerChargeIcon : wildChargeIcon;
        SetStatusIconVisible(icon, on);
    }

    public void SetChargePlayer(bool on) => SetCharge(BattleFeedbackSide.Player, on);
    public void SetChargeWild(bool on) => SetCharge(BattleFeedbackSide.Wild, on);

    
    // ─────────────────────────────────────────────────────────────
    // Wild Intent Telegraph (readable AI)
    // ─────────────────────────────────────────────────────────────
    public void ShowWildIntent(BattleFeedbackAction action, float durationSeconds = 0.6f, bool showText = false, string textOverride = null)
    {
        if (wildIntentRoot == null || wildIntentIcon == null)
            return;

        // Stop any previous telegraph
        if (_wildIntentCR != null)
            StopCoroutine(_wildIntentCR);

        _wildIntentCR = StartCoroutine(Co_ShowWildIntent(action, durationSeconds, showText, textOverride));
    }

    public void HideWildIntent()
    {
        if (_wildIntentCR != null)
        {
            StopCoroutine(_wildIntentCR);
            _wildIntentCR = null;
        }

        if (wildIntentRoot) wildIntentRoot.SetActive(false);
    }

    private IEnumerator Co_ShowWildIntent(BattleFeedbackAction action, float durationSeconds, bool showText, string textOverride)
    {
        // Choose sprite; fall back to existing status sprites when possible.
        Sprite sprite = null;

        switch (action)
        {
            case BattleFeedbackAction.Attack: sprite = wildIntentAttackSprite; break;
            case BattleFeedbackAction.Defend: sprite = wildIntentDefendSprite ? wildIntentDefendSprite : (wildGuardIcon ? wildGuardIcon.sprite : null); break;
            case BattleFeedbackAction.Focus:  sprite = wildIntentFocusSprite  ? wildIntentFocusSprite  : (wildChargeIcon ? wildChargeIcon.sprite : null); break;
            case BattleFeedbackAction.Run:    sprite = wildIntentRunSprite; break;
            default: sprite = null; break;
        }

        if (sprite == null)
        {
            // Nothing to show
            wildIntentRoot.SetActive(false);
            yield break;
        }

        wildIntentIcon.sprite = sprite;

        if (wildIntentText)
        {
            wildIntentText.gameObject.SetActive(showText);
            if (showText)
                wildIntentText.text = string.IsNullOrEmpty(textOverride) ? action.ToString() : textOverride;
        }

        wildIntentRoot.SetActive(true);

        // Animate: quick pop-in (LeanTween if available, otherwise direct)
        var rt = wildIntentRoot.transform as RectTransform;
        if (rt != null)
        {
            rt.localScale = Vector3.one * Mathf.Max(0.01f, wildIntentStartScale);

            LeanTween.scale(rt, Vector3.one, wildIntentPopTime).setEaseOutBack().setIgnoreTimeScale(true);
        }

        // Hold
        float t = 0f;
        while (t < durationSeconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        wildIntentRoot.SetActive(false);
        _wildIntentCR = null;
    }
public void SetGuard(BattleFeedbackSide side, bool on)
    {
        var icon = (side == BattleFeedbackSide.Player) ? playerGuardIcon : wildGuardIcon;

        if (!on)
            StopGuardAutoHideCR(side);

        SetStatusIconVisible(icon, on);
    }

    private void SetStatusIconVisible(Image icon, bool on)
    {
        if (!icon) return;

        // Use SetActive to fully hide the icon (matches your requirement).
        var go = icon.gameObject;
        if (go && go.activeSelf != on) go.SetActive(on);

        // If it's being shown, ensure the alpha isn't stuck from prior effects.
        if (on)
        {
            var c = icon.color;
            c.a = 1f;
            icon.color = c;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Public API
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

            case BattleFeedbackAction.Swap:
                // Swap should read distinctly from Focus; a quick nudge + punch works well on mobile.
                PunchScale(icon, 1.08f, pressPunchTime);
                Nudge(icon.rectTransform, side == BattleFeedbackSide.Player ? +14f : -14f, windupTime);
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
                SetStatusIconVisible(playerGuardIcon, true);
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
            if (wildGuardIcon)
            {
                SetStatusIconVisible(wildGuardIcon, true);
                Punch(wildGuardIcon);
            }
            else if (wildIcon) Punch(wildIcon);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // HP Text + HP Bars (centralized here)
    // ─────────────────────────────────────────────────────────────

    public bool HasHPTextWired => (playerHPValueText != null) || (wildHPValueText != null);
    public bool HasHPBarsWired => (playerHPBar != null) || (wildHPBar != null);

    /// <summary>
    /// UI-only. Call whenever HP changes, when swapping, and at battle start.
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

    /// <summary>
    /// UI-only. If HP bars are wired, animates them smoothly (or snaps if disabled).
    /// Also triggers a quick shake when the target value decreases.
    /// </summary>
    public void SetHPBars(float playerCur, float playerMax, float wildCur, float wildMax)
    {
        if (playerHPBar)
        {
            float pMax = Mathf.Max(1f, playerMax);
            float pCur = Mathf.Clamp(playerCur, 0f, pMax);
            SetHPBarAnimated(playerHPBar, ref _playerHPAnimCR, pCur, pMax, isPlayer: true);
        }

        if (wildHPBar)
        {
            float wMax = Mathf.Max(1f, wildMax);
            float wCur = Mathf.Clamp(wildCur, 0f, wMax);
            SetHPBarAnimated(wildHPBar, ref _wildHPAnimCR, wCur, wMax, isPlayer: false);
        }
    }

    private void SetHPBarAnimated(Slider bar, ref Coroutine animCR, float targetValue, float maxValue, bool isPlayer)
    {
        if (!bar) return;

        maxValue = Mathf.Max(1f, maxValue);
        bar.maxValue = maxValue;

        targetValue = Mathf.Clamp(targetValue, 0f, maxValue);

        if (!smoothHPBars || !gameObject.activeInHierarchy)
        {
            if (animCR != null) { StopCoroutine(animCR); animCR = null; }
            bar.value = targetValue;
            return;
        }

        float current = bar.value;

        // Shake-on-decrease behavior moved here.
        if (current > targetValue)
        {
            if (isPlayer) PlayHPShakeForPlayer();
            else PlayHPShakeForWild();
        }

        if (Mathf.Approximately(current, targetValue))
        {
            if (animCR != null) { StopCoroutine(animCR); animCR = null; }
            bar.value = targetValue;
            return;
        }

        if (animCR != null) StopCoroutine(animCR);
        animCR = StartCoroutine(Co_AnimateHPBar(bar, current, targetValue));
    }

    private IEnumerator Co_AnimateHPBar(Slider bar, float start, float end)
    {
        if (!bar) yield break;

        float max = Mathf.Max(1f, bar.maxValue);
        float distance = Mathf.Abs(end - start);

        float duration = hpBarSecondsForFull * (distance / max);
        duration = Mathf.Max(0.05f, duration);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            bar.value = Mathf.Lerp(start, end, t);
            yield return null;
        }

        bar.value = end;
    }

    private void PunchTMP(TextMeshProUGUI tmp)
    {
        if (!tmp) return;

        var t = tmp.rectTransform;
        if (!t) return;

        LeanTween.cancel(t);
        t.localScale = Vector3.one;

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

    // ─────────────────────────────────────────────────────────────
    // Guard auto-hide helpers
    // ─────────────────────────────────────────────────────────────

    private void StartGuardAutoHide(BattleFeedbackSide side, float seconds)
    {
        StopGuardAutoHideCR(side);

        if (!gameObject.activeInHierarchy) return;

        if (side == BattleFeedbackSide.Player)
            _playerGuardAutoHideCR = StartCoroutine(Co_AutoHideGuard(side, seconds));
        else
            _wildGuardAutoHideCR = StartCoroutine(Co_AutoHideGuard(side, seconds));
    }

    private void StopGuardAutoHideCR(BattleFeedbackSide side)
    {
        if (side == BattleFeedbackSide.Player)
        {
            if (_playerGuardAutoHideCR != null) { StopCoroutine(_playerGuardAutoHideCR); _playerGuardAutoHideCR = null; }
        }
        else
        {
            if (_wildGuardAutoHideCR != null) { StopCoroutine(_wildGuardAutoHideCR); _wildGuardAutoHideCR = null; }
        }
    }

    private IEnumerator Co_AutoHideGuard(BattleFeedbackSide side, float seconds)
    {
        float wait = Mathf.Max(0.01f, seconds);
        yield return new WaitForSecondsRealtime(wait);

        var icon = (side == BattleFeedbackSide.Player) ? playerGuardIcon : wildGuardIcon;
        SetStatusIconVisible(icon, false);

        if (side == BattleFeedbackSide.Player) _playerGuardAutoHideCR = null;
        else _wildGuardAutoHideCR = null;
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

    // ─────────────────────────────────────────────────────────────
    // Shiny Name Sparkle (Optional)
    // ─────────────────────────────────────────────────────────────

    public void PlayShinyNameSparkle(TextMeshProUGUI label)
    {
        if (!enableShinyNameSparkle) return;
        if (!label) return;

        RectTransform rt = label.rectTransform;
        var go = rt.gameObject;

        // Cancel any prior tweens affecting this label so we don't stack effects on rapid swaps
        LeanTween.cancel(go);

        // Reset baseline (important if a prior tween was interrupted)
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        // Ensure we can safely manipulate alpha without permanently changing your style
        Color baseColor = label.color;

        // 1) Punch scale: up then back down, driven by shinyNamePunchTime & shinyNamePunchScale
        float punchIn = Mathf.Max(0.01f, shinyNamePunchTime);
        float punchOut = Mathf.Max(0.01f, shinyNamePunchTime);

        LeanTween.scale(rt, Vector3.one * Mathf.Max(1.01f, shinyNamePunchScale), punchIn)
            .setEaseOutBack()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                // Return to normal
                LeanTween.scale(rt, Vector3.one, punchOut)
                    .setEaseInBack()
                    .setIgnoreTimeScale(true);
            });

        // 2) Sparkle pulse: alpha ping-pong, driven by shinyNameSparkleTime
        // We do a quick fade down slightly then back up to baseline.
        float sparkleT = Mathf.Max(0.01f, shinyNameSparkleTime);
        float halfSparkle = sparkleT * 0.5f;

        // Fade to 70% alpha then back to original alpha
        float targetAlpha = Mathf.Clamp01(baseColor.a * 0.70f);

        LeanTween.value(go, baseColor.a, targetAlpha, halfSparkle)
            .setEaseOutSine()
            .setIgnoreTimeScale(true)
            .setOnUpdate((float a) =>
            {
                var c = label.color;
                c.a = a;
                label.color = c;
            })
            .setOnComplete(() =>
            {
                LeanTween.value(go, targetAlpha, baseColor.a, halfSparkle)
                    .setEaseInSine()
                    .setIgnoreTimeScale(true)
                    .setOnUpdate((float a) =>
                    {
                        var c = label.color;
                        c.a = a;
                        label.color = c;
                    });
            });

        // 3) Wiggle: rotateZ ping-pong, driven by shinyNameWiggleDegrees & shinyNameWiggleDuration
        float wiggleDeg = Mathf.Max(0f, shinyNameWiggleDegrees);
        float wiggleDur = Mathf.Max(0.01f, shinyNameWiggleDuration);

        if (wiggleDeg > 0.01f)
        {
            // One ping-pong loop: 0 -> +deg -> 0 (LeanTween pingpong from target)
            // We'll rotate to +deg and ping-pong once, then hard reset to 0.
            LeanTween.rotateZ(go, wiggleDeg, wiggleDur)
                .setEaseInOutSine()
                .setIgnoreTimeScale(true)
                .setLoopPingPong(1)
                .setOnComplete(() =>
                {
                    rt.localRotation = Quaternion.identity;
                });
        }
    }
}
