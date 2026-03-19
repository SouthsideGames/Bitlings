using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


[DisallowMultipleComponent]
public sealed class BattleFeedbackManager : MonoBehaviour
{
    public enum BattleFeedbackSide { Player, Wild }
    public enum BattleFeedbackAction { Attack, Defend, Focus, Swap, Run }

    [Header("Battle Events")]
    [Tooltip("If set, FeedbackManager will subscribe to BattleManager's Battle Events. If left empty it will auto-find one.")]
    [SerializeField] private BattleManager battleManager;

    [Header("Icon Targets")]
    [SerializeField] private Graphic playerIcon;
    [SerializeField] private Graphic wildIcon;

    [Header("Battle Start Icon Intro")]
    [Tooltip("If enabled, player/wild icons fade in sequentially at battle start.")]
    [SerializeField] private bool enableSpeedOrderedIconIntro = true;
    [SerializeField, Min(0.01f)] private float iconIntroFadeTime = 0.10f;
    [Tooltip("Multiplier applied to the first icon fade (the faster monster) so it reads more clearly.")]
    [SerializeField, Range(1f, 2f)] private float iconIntroFirstFadeMult = 1.35f;
    [SerializeField, Min(0f)] private float iconIntroGap = 0.03f;
    [SerializeField] private bool iconIntroPunch = true;
    [SerializeField, Range(1.01f, 1.35f)] private float iconIntroPunchScale = 1.18f;
    [SerializeField, Min(0.01f)] private float iconIntroPunchTime = 0.08f;

    [Header("HP Roots (shake on damage)")]
    [SerializeField] private RectTransform playerHPShakeRoot;
    [SerializeField] private RectTransform wildHPShakeRoot;

    [Header("Impact Roots (recoil/squash on hit)")]
    [Tooltip("Optional root for hit impact recoil/squash. If null, uses the icon RectTransform.")]
    [SerializeField] private RectTransform playerImpactRoot;
    [Tooltip("Optional root for hit impact recoil/squash. If null, uses the icon RectTransform.")]
    [SerializeField] private RectTransform wildImpactRoot;

    [Header("Guard Icons (defend FX)")]
    [SerializeField] private Image playerGuardIcon;
    [SerializeField] private Image wildGuardIcon;

    [Header("Charge Icons (focus/charge status)")]
    [SerializeField] private Image playerChargeIcon;
    [SerializeField] private Image wildChargeIcon;

    [Header("Primary Status (Synergy/Status System)")]
    [Tooltip("Optional status icon shown for the unit (Burn/Freeze/etc). Leave null to ignore.")]
    [SerializeField] private Image playerPrimaryStatusIcon;
    [SerializeField] private Image wildPrimaryStatusIcon;

    [Tooltip("Optional TMP counter for remaining turns. Hidden for persistent statuses and when no status.")]
    [SerializeField] private TMP_Text playerPrimaryStatusTurns;
    [SerializeField] private TMP_Text wildPrimaryStatusTurns;

    [Header("Wild Intent Telegraph (icon bubble)")]
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

    [Header("Action SFX")]
    [SerializeField] private AudioClip chargeSfx;
    [SerializeField] private AudioClip defendSfx;
    [SerializeField] private AudioClip runSfx;
    
    [Header("Action Buttons (press feedback)")]
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
    [Tooltip("Optional explicit spawn roots for attack prefabs. If null, falls back to active battle roots.")]
    [SerializeField] private Transform playerAttackSpawnRoot;
    [SerializeField] private Transform wildAttackSpawnRoot;

    [Header("Screen Shake (optional)")]
    [Tooltip("If empty, will fall back to Camera.main.transform")]
    [SerializeField] private Transform screenShakeRoot;
    [SerializeField, Range(0f, 50f)] private float heavyHitShakeMagnitude = 12f;
    [SerializeField, Min(0.01f)] private float heavyHitShakeDuration = 0.15f;


    [Header("Micro-Juice (Optional)")]
    [Tooltip("If enabled, applies a tiny timeScale pause on crits/heavy hits for punchy impact.")]
    [SerializeField] private bool enableHitStop = true;

    [SerializeField, Min(0f)] private float hitStopTimeScale = 0.05f;
    [SerializeField, Min(0.01f)] private float hitStopCritSeconds = 0.05f;
    [SerializeField, Min(0.01f)] private float hitStopHeavySeconds = 0.04f;

    [Tooltip("KO slow motion timeScale for a short burst.")]
    [SerializeField] private bool enableKOSlowMo = true;
    [SerializeField, Range(0.05f, 1f)] private float koSlowMoTimeScale = 0.20f;
    [SerializeField, Min(0.01f)] private float koSlowMoSeconds = 0.20f;

    [Header("Vignette Flash (Optional)")]
    [Tooltip("Optional full-screen Image used for a subtle KO flash (alpha anim).")]
    [SerializeField] private Image vignetteFlash;
    [SerializeField, Range(0f, 1f)] private float vignetteFlashAlpha = 0.25f;
    [SerializeField, Min(0.01f)] private float vignetteFlashIn = 0.06f;
    [SerializeField, Min(0.01f)] private float vignetteFlashOut = 0.14f;

    [Header("Crit Tag (Optional)")]
    [Tooltip("Optional TMP label near the icon that flashes 'CRIT!' when a crit lands.")]
    [SerializeField] private TMP_Text playerCritTag;
    [SerializeField] private TMP_Text wildCritTag;
    [SerializeField, Min(0.01f)] private float critTagSeconds = 0.35f;
    [SerializeField, Range(1.01f, 1.6f)] private float critTagPunch = 1.25f;

    [Header("Shake Scaling")]
    [Tooltip("Damage ratio (damage / maxHP) that maps to full shake. Keeps shake consistent across HP values.")]
    [SerializeField, Range(0.05f, 1f)] private float ratioForMaxShake = 0.35f;
    [SerializeField, Range(0f, 30f)] private float minScreenShake = 1.5f;
    [SerializeField, Range(0f, 60f)] private float maxScreenShake = 10f;

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

    [Header("Impact Micro-Juice (Optional)")]
    [Tooltip("If enabled, applies a small recoil + squash/stretch on the target when hit.")]
    [SerializeField] private bool enableImpactSquash = true;

    [SerializeField, Min(0.01f)] private float impactSquashTime = 0.08f;
    [SerializeField, Range(1.01f, 1.25f)] private float impactSquashX = 1.10f;
    [SerializeField, Range(0.75f, 0.99f)] private float impactSquashY = 0.90f;
    [SerializeField, Range(0f, 30f)] private float impactRecoilPixels = 10f;

    private const float PlayerIconDefaultXYScale = 1.25f;

    private BattleManager _battleManager;

    private int _lastPlayerCur = int.MinValue;
    private int _lastPlayerMax = int.MinValue;
    private int _lastWildCur = int.MinValue;
    private int _lastWildMax = int.MinValue;
    private int _lastPlayerShield = int.MinValue;
    private int _lastWildShield = int.MinValue;

    private Vector3 _playerIconBaseScale = Vector3.one;
    private Vector3 _wildIconBaseScale = Vector3.one;

    private Coroutine _timeScaleCR;
    private Coroutine _playerCritHideCR;
    private Coroutine _wildCritHideCR;
    private Coroutine _wildIntentCR;
    private Coroutine _playerHPAnimCR;
    private Coroutine _wildHPAnimCR;
    private Coroutine _playerGuardAutoHideCR;
    private Coroutine _wildGuardAutoHideCR;

    private float CurrentBattleSpeed
        => Mathf.Max(0.25f, _battleManager != null ? _battleManager.BattleSpeed : 1f);

    private float ScaleFeedbackDuration(float seconds, float minSeconds = 0.01f)
    {
        return Mathf.Max(minSeconds, seconds / CurrentBattleSpeed);
    }

    private bool _chargePlayerOn;
    private bool _chargeWildOn;
    private bool _guardPlayerOn;
    private bool _guardWildOn;
    private bool _slowMoActive;
    private float _slowMoPrevTimeScale = 1f;
    private float _slowMoPrevFixedDeltaTime = 0.02f;


    private void Awake()
    {
        CacheBaseScales();
        WireOptionalButtonPresses();

        ResetStatusIcons();
        ResetMicroJuiceOptionals();
    }

    private void OnEnable()
    {
        BindBattleManager();
        Subscribe();

        CacheBaseScales();

        ResetStatusIcons();
        ResetMicroJuiceOptionals();
    }

    private void OnDisable()
    {
        Unsubscribe();

        CancelActiveSlowMo(forceNormalTime: true);

        ResetMicroJuiceOptionals();
    }

    private void OnDestroy()
    {
        CancelActiveSlowMo(forceNormalTime: true);
    }

    private void CancelActiveSlowMo(bool forceNormalTime)
    {
        if (_timeScaleCR != null)
        {
            StopCoroutine(_timeScaleCR);
            _timeScaleCR = null;
        }

        if (!_slowMoActive)
            return;

        if (forceNormalTime)
        {
            Time.timeScale = 1f;
            if (_slowMoPrevFixedDeltaTime > 0f)
                Time.fixedDeltaTime = _slowMoPrevFixedDeltaTime;
        }
        else
        {
            Time.timeScale = _slowMoPrevTimeScale;
            Time.fixedDeltaTime = _slowMoPrevFixedDeltaTime;
        }

        _slowMoActive = false;
        _slowMoPrevTimeScale = 1f;
        _slowMoPrevFixedDeltaTime = Time.fixedDeltaTime;
    }

    private void ResetMicroJuiceOptionals()
    {
        // Vignette should be inactive when not being used.
        if (vignetteFlash)
        {
            LeanTween.cancel(vignetteFlash.gameObject);
            var c = vignetteFlash.color;
            c.a = 0f;
            vignetteFlash.color = c;
            vignetteFlash.gameObject.SetActive(false);
        }

        // Crit tags are meant to start inactive; the script activates them temporarily.
        if (playerCritTag)
            playerCritTag.gameObject.SetActive(false);
        if (wildCritTag)
            wildCritTag.gameObject.SetActive(false);

        if (_playerCritHideCR != null)
        {
            StopCoroutine(_playerCritHideCR);
            _playerCritHideCR = null;
        }
        if (_wildCritHideCR != null)
        {
            StopCoroutine(_wildCritHideCR);
            _wildCritHideCR = null;
        }
    }

    private void CacheBaseScales()
    {
        if (playerIcon && playerIcon.rectTransform)
        {
            var rt = playerIcon.rectTransform;
            float z = rt.localScale.z;

            _playerIconBaseScale = new Vector3(PlayerIconDefaultXYScale, PlayerIconDefaultXYScale, z);
            rt.localScale = _playerIconBaseScale;
        }

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
    // Battle Events wiring
    // ─────────────────────────────────────────────────────────────

    private void BindBattleManager()
    {
        if (_battleManager != null) return;

        _battleManager = battleManager != null
            ? battleManager
            : (GetComponentInParent<BattleManager>() ?? FindFirstObjectByType<BattleManager>());

        // Register so BattleManager can avoid legacy direct-calls when we are present.
        if (_battleManager != null)
            _battleManager.RegisterBattleEventConsumer();
    }

    public void BindToBattleManager(BattleManager manager)
    {
        if (manager == null) return;

        if (_battleManager == manager)
        {
            Subscribe();
            return;
        }

        if (_battleManager != null)
        {
            _battleManager.OnBattleEvent -= HandleBattleEvent;
            _battleManager.UnregisterBattleEventConsumer();
        }

        battleManager = manager;
        _battleManager = manager;
        _battleManager.RegisterBattleEventConsumer();
        Subscribe();
    }

    private void Subscribe()
    {
        if (_battleManager == null) return;
        _battleManager.OnBattleEvent -= HandleBattleEvent;
        _battleManager.OnBattleEvent += HandleBattleEvent;
    }

    private void Unsubscribe()
    {
        if (_battleManager != null)
        {
            _battleManager.OnBattleEvent -= HandleBattleEvent;
            _battleManager.UnregisterBattleEventConsumer();
            _battleManager = null;
        }
    }

    private void HandleBattleEvent(BattleEvent e)
    {
        switch (e.kind)
        {
            case BattleEvent.Kind.ActionWindup:
                PlayAttackWindup(ToFeedbackSide(e.source));
                break;

            case BattleEvent.Kind.StatusApplied:
                if (e.statusId == "DefendShieldFX")
                    PlayDefendShieldFX(isPlayer: e.source == BattleSide.Player);
                break;

            case BattleEvent.Kind.Damage:
                PlayHitReaction(ToFeedbackSide(e.target), e.crit, e.ratio01, wasGuarded: e.wasGuardedOrShielded);
                break;

            case BattleEvent.Kind.DefendResult:
                PlayDefendResult(ToFeedbackSide(e.source), e.success);
                break;

            case BattleEvent.Kind.KO:
                PlayKO(ToFeedbackSide(e.target));
                break;

            case BattleEvent.Kind.Swap:
                PlayButtonPress(BattleFeedbackAction.Swap);
                break;

            case BattleEvent.Kind.GuardChanged:
                SetGuard(ToFeedbackSide(e.source), e.stateEnabled);
                break;

            case BattleEvent.Kind.ChargeChanged:
                SetCharge(ToFeedbackSide(e.source), e.stateEnabled);
                break;

            case BattleEvent.Kind.IntentTelegraph:
                ShowWildIntent(e.statusId);
                break;

            case BattleEvent.Kind.ActionQueued:
                if (e.source == BattleSide.Player)
                    PulseQueuedAction(e.statusId);
                break;

           case BattleEvent.Kind.UIRefreshHP:
                RefreshHPFromBattle();
                break;
        }
    }

    private BattleFeedbackSide ToFeedbackSide(BattleSide s)
        => s == BattleSide.Player ? BattleFeedbackSide.Player : BattleFeedbackSide.Wild;

    private void ShowWildIntent(string intentId)
    {
        if (string.IsNullOrEmpty(intentId)) return;

        BattleFeedbackAction a = BattleFeedbackAction.Attack;
        if (intentId == "Defend") a = BattleFeedbackAction.Defend;
        else if (intentId == "Focus") a = BattleFeedbackAction.Focus;
        else if (intentId == "Run") a = BattleFeedbackAction.Run;

        ShowWildIntent(a, durationSeconds: 0.6f, showText: false, textOverride: null);
    }

    private void PulseQueuedAction(string actionId)
    {
        // If you already bind button presses, this is optional. Keep it tiny + safe.
        if (string.IsNullOrEmpty(actionId)) return;

        if (actionId == "Attack") PlayButtonPress(BattleFeedbackAction.Attack);
        else if (actionId == "Defend") PlayButtonPress(BattleFeedbackAction.Defend);
        else if (actionId == "Focus") PlayButtonPress(BattleFeedbackAction.Focus);
        else if (actionId == "Run") PlayButtonPress(BattleFeedbackAction.Run);
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
        ClearPrimaryStatus(BattleFeedbackSide.Player);
        ClearPrimaryStatus(BattleFeedbackSide.Wild);
    }

    public void SetCharge(BattleFeedbackSide side, bool on)
    {
        var icon = (side == BattleFeedbackSide.Player) ? playerChargeIcon : wildChargeIcon;
        bool wasOn = (side == BattleFeedbackSide.Player) ? _chargePlayerOn : _chargeWildOn;

        SetStatusIconVisible(icon, on);

        if (on && !wasOn)
            PlayChargeSfx();

        if (side == BattleFeedbackSide.Player)
            _chargePlayerOn = on;
        else
            _chargeWildOn = on;
    }

    public void SetChargePlayer(bool on) => SetCharge(BattleFeedbackSide.Player, on);
    public void SetChargeWild(bool on) => SetCharge(BattleFeedbackSide.Wild, on);

    


    // ─────────────────────────────────────────────────────────────
    // Primary Status (Synergy/Status System) - Icon + Turn Counter
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears the primary status UI for a side (icon + counter).
    /// Safe to call even if refs are null.
    /// </summary>
    public void ClearPrimaryStatus(BattleFeedbackSide side)
    {
        var icon  = (side == BattleFeedbackSide.Player) ? playerPrimaryStatusIcon : wildPrimaryStatusIcon;
        var turns = (side == BattleFeedbackSide.Player) ? playerPrimaryStatusTurns : wildPrimaryStatusTurns;

        if (icon) icon.gameObject.SetActive(false);
        if (turns) turns.gameObject.SetActive(false);
    }

    /// <summary>
    /// Sets the primary status UI for a side.
    /// - If sprite is null, clears the status.
    /// - If persistent, hides the turn counter.
    /// - If turnsRemaining &lt;= 0 and not persistent, clears.
    /// </summary>
    public void SetPrimaryStatus(BattleFeedbackSide side, Sprite sprite, int turnsRemaining, bool persistent)
    {
        SetPrimaryStatus(side, sprite, turnsRemaining, persistent, tooltipTitle: null, tooltipDescription: null);
    }

    /// <summary>
    /// Sets the primary status UI for a side, with optional tooltip text.
    /// If a TooltipTrigger component is present on the icon GameObject, its message/subtitle are updated.
    /// </summary>
    public void SetPrimaryStatus(BattleFeedbackSide side, Sprite sprite, int turnsRemaining, bool persistent, string tooltipTitle, string tooltipDescription)
    {
        var icon  = (side == BattleFeedbackSide.Player) ? playerPrimaryStatusIcon : wildPrimaryStatusIcon;
        var turns = (side == BattleFeedbackSide.Player) ? playerPrimaryStatusTurns : wildPrimaryStatusTurns;

        if (!icon)
            return; // nothing to do (UI not wired)

        if (sprite == null || (!persistent && turnsRemaining <= 0))
        {
            ClearPrimaryStatus(side);
            return;
        }

        icon.sprite = sprite;
        icon.gameObject.SetActive(true);

        // Optional: status tooltip
        TryConfigureStatusTooltip(icon.gameObject, tooltipTitle, tooltipDescription);

        if (turns)
        {
            if (persistent)
            {
                turns.gameObject.SetActive(false);
            }
            else
            {
                turns.text = turnsRemaining.ToString();
                turns.gameObject.SetActive(true);
            }
        }
    }

    private static void TryConfigureStatusTooltip(GameObject iconGO, string title, string description)
    {
        if (iconGO == null) return;

        var tt = iconGO.GetComponent<TooltipTrigger>();
        if (tt == null) return;

        // Title goes on the main line, description goes on the smaller secondary line.
        // TooltipTrigger will handle formatting.
        tt.message = title ?? string.Empty;
        tt.subtitle = description ?? string.Empty;
    }

    public void SetPrimaryStatusPlayer(Sprite sprite, int turnsRemaining, bool persistent) =>
        SetPrimaryStatus(BattleFeedbackSide.Player, sprite, turnsRemaining, persistent);

    public void SetPrimaryStatusWild(Sprite sprite, int turnsRemaining, bool persistent) =>
        SetPrimaryStatus(BattleFeedbackSide.Wild, sprite, turnsRemaining, persistent);

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

        bool wasOn = (side == BattleFeedbackSide.Player) ? _guardPlayerOn : _guardWildOn;

        if (!on)
            StopGuardAutoHideCR(side);

        SetStatusIconVisible(icon, on);

        if (on && !wasOn)
            PlayDefendSfx();

        if (side == BattleFeedbackSide.Player)
            _guardPlayerOn = on;
        else
            _guardWildOn = on;
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

        /// <summary>Copies animation timing values from another BattleFeedbackManager.
        /// Called by IronBattleUIRoot to ensure Iron Career uses identical timing to regular battles.</summary>
        public void CopyAnimationTimingsFrom(BattleFeedbackManager src)
        {
            if (src == null || src == this) return;
            pressPunchTime = src.pressPunchTime;
            pressPunchScale = src.pressPunchScale;
            windupTime = src.windupTime;
            windupScale = src.windupScale;
            hitFlashTime = src.hitFlashTime;
            hitShakeTime = src.hitShakeTime;
            hitShakePixels = src.hitShakePixels;
            defendPulseTime = src.defendPulseTime;
            hpShakeDuration = src.hpShakeDuration;
            hpShakeStrength = src.hpShakeStrength;
            impactSquashTime = src.impactSquashTime;
            impactSquashX = src.impactSquashX;
            impactSquashY = src.impactSquashY;
            impactRecoilPixels = src.impactRecoilPixels;
            heavyHitShakeMagnitude = src.heavyHitShakeMagnitude;
            heavyHitShakeDuration = src.heavyHitShakeDuration;
            critExtraShakeMult = src.critExtraShakeMult;
            heavyHitThreshold01 = src.heavyHitThreshold01;
            heavyExtraShakeMult = src.heavyExtraShakeMult;
        }

    public void SetSpawnAttackPrefabs(bool on)
    {
        spawnAttackPrefabs = on;
    }

    public void SetIconAlphaImmediate(BattleFeedbackSide side, float alpha)
    {
        SetGraphicAlpha(GetIcon(side), alpha);
    }

    public IEnumerator Co_FadeInIcon(
        BattleFeedbackSide side,
        MonsterDataSO def,
        float fadeTimeOverride = -1f,
        Func<MonsterDataSO, IEnumerator> onSpawnAnnounce = null)
    {
        var icon = GetIcon(side);
        float fadeTime = (fadeTimeOverride > 0f)
            ? fadeTimeOverride
            : Mathf.Max(0.01f, iconIntroFadeTime);

        if (icon)
        {
            SetGraphicAlpha(icon, 0f);
            PlayIconIntroFor(icon, fadeTime);
        }

        if (fadeTime > 0f)
            yield return new WaitForSecondsRealtime(fadeTime);

        PlayMonsterSpawnSfx(def);

        if (onSpawnAnnounce != null)
            yield return onSpawnAnnounce(def);

        float postGap = Mathf.Max(0f, iconIntroGap);
        if (postGap > 0f)
            yield return new WaitForSecondsRealtime(postGap);
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

        // Optional: a tiny anticipation on the portrait/visual root.
        if (enableImpactSquash)
        {
            var rt = GetImpactRoot(attackerSide, icon);
            if (rt) ImpactAnticipation(rt, attackerSide);
        }
    }

    
    public void PlayHitReaction(BattleFeedbackSide targetSide, bool crit, float damageRatio01 = -1f)
    {
        PlayHitReaction(targetSide, crit, damageRatio01, wasGuarded: false);
    }

    /// <summary>
    /// Extended variant that can reduce shake / change SFX when the hit was guarded/shielded.
    /// </summary>
    public void PlayHitReaction(BattleFeedbackSide targetSide, bool crit, float damageRatio01, bool wasGuarded)
    {
        var icon = GetIcon(targetSide);
        if (!icon) return;

        float ratio01 = Mathf.Max(0f, damageRatio01);
        bool heavy = (damageRatio01 >= 0f && ratio01 >= heavyHitThreshold01);

        // Shake scaling: consistent across HP values
        float shakeT = (damageRatio01 < 0f) ? 0f : Mathf.Clamp01(ratio01 / Mathf.Max(0.01f, ratioForMaxShake));
        float screenMag = Mathf.Lerp(minScreenShake, maxScreenShake, shakeT);

        float shakeMult = 1f;
        if (crit) shakeMult *= critExtraShakeMult;
        if (heavy) shakeMult *= heavyExtraShakeMult;
        if (damageRatio01 >= 0f && ratio01 >= heavyHitThreshold01) shakeMult *= heavyExtraShakeMult;

        // Guarded hits feel "clanky" and less violent
        if (wasGuarded)
        {
            shakeMult *= 0.55f;
            screenMag *= 0.55f;
        }

        Flash(icon, crit ? flashCrit : (wasGuarded ? flashDefend : flashNormal), hitFlashTime);
        Shake(icon.rectTransform, hitShakePixels * shakeMult, hitShakeTime);

        // Optional: squash/recoil on the target portrait/visual root.
        if (enableImpactSquash)
        {
            var rt = GetImpactRoot(targetSide, icon);
            if (rt) ImpactHit(rt, targetSide, crit, wasGuarded, shakeT);
        }

        // Crit reads best with a tiny punch-scale on the target icon.
        if (crit)
            PunchScale(icon, 1.08f, Mathf.Min(0.08f, hitFlashTime));

        var hpRoot = (targetSide == BattleFeedbackSide.Player) ? playerHPShakeRoot : wildHPShakeRoot;
        if (hpRoot) PlayHPShake(hpRoot);

        if (damageRatio01 >= 0f)
        {
            float finalScreenMag = heavy ? Mathf.Max(screenMag, heavyHitShakeMagnitude) : screenMag;
            float finalScreenDur = heavy ? heavyHitShakeDuration : hitShakeTime;
            ScreenShake(finalScreenMag, finalScreenDur);
        }

        // Micro-juice: hitstop on crit / heavy hits
        if (enableHitStop)
        {
            float seconds = crit ? hitStopCritSeconds : heavy ? hitStopHeavySeconds : 0f;
            if (seconds > 0f && !wasGuarded)
                HitStop(seconds);
        }

        if (crit)
        {
            PlayCritTag(targetSide);
            // Slightly higher pitch on crit for extra satisfaction.
            AudioManager.I?.PlaySfx(SfxType.CritHit, 1.15f);
        }
        else if (wasGuarded)
        {
            // Distinct "clank"
            AudioManager.I?.PlaySfx(SfxType.Defend, 0.95f);
        }

        
    }

    // ─────────────────────────────────────────────────────────────
    // Impact Squash / Recoil (clarity + feel)
    // ─────────────────────────────────────────────────────────────

    private RectTransform GetImpactRoot(BattleFeedbackSide side, Graphic icon)
    {
        if (side == BattleFeedbackSide.Player)
            return playerImpactRoot != null ? playerImpactRoot : icon.rectTransform;
        return wildImpactRoot != null ? wildImpactRoot : icon.rectTransform;
    }

    private void ImpactAnticipation(RectTransform rt, BattleFeedbackSide side)
    {
        if (!rt) return;

        // Very small pre-squash to make actions read more tactile.
        LeanTween.cancel(rt.gameObject);
        var baseScale = GetBaseScale(rt, side);
        rt.localScale = baseScale;

        float dir = (side == BattleFeedbackSide.Player) ? +1f : -1f;
        Vector2 basePos = rt.anchoredPosition;
        Vector2 nudge = basePos + new Vector2(dir * (impactRecoilPixels * 0.35f), 0f);

        Vector3 squashScale = new Vector3(impactSquashX, impactSquashY, 1f);
        float duration = ScaleFeedbackDuration(Mathf.Min(0.06f, impactSquashTime * 0.75f));

        LeanTween.value(rt.gameObject, 0f, 1f, duration)
            .setIgnoreTimeScale(true)
            .setOnUpdate((float t) =>
            {
                if (!rt) return;
                rt.anchoredPosition = Vector2.Lerp(basePos, nudge, t);
                rt.localScale = Vector3.Lerp(baseScale, Vector3.Scale(baseScale, squashScale), t);
            })
            .setOnComplete(() =>
            {
                if (!rt) return;
                rt.anchoredPosition = basePos;
                rt.localScale = baseScale;
            });
    }

    private void ImpactHit(RectTransform rt, BattleFeedbackSide side, bool crit, bool wasGuarded, float scaled01)
    {
        if (!rt) return;

        // Guarded hits should feel less "juicy".
        float guardMult = wasGuarded ? 0.55f : 1f;
        float critMult = crit ? 1.15f : 1f;
        float heavyMult = Mathf.Lerp(0.9f, 1.25f, Mathf.Clamp01(scaled01));

        float dir = (side == BattleFeedbackSide.Player) ? -1f : +1f;
        float px = impactRecoilPixels * guardMult * critMult * heavyMult;

        Vector2 basePos = rt.anchoredPosition;
        Vector2 hitPos = basePos + new Vector2(dir * px, 0f);

        float t = ScaleFeedbackDuration(Mathf.Max(0.04f, impactSquashTime), 0.04f);
        Vector3 baseScale = GetBaseScale(rt, side);
        Vector3 squashScale = new Vector3(impactSquashX, impactSquashY, 1f);
        Vector3 hitScale = Vector3.Scale(baseScale, squashScale);
        LeanTween.cancel(rt.gameObject);

        // Hit phase
        LeanTween.value(rt.gameObject, 0f, 1f, t)
            .setEaseOutQuad()
            .setIgnoreTimeScale(true)
            .setOnUpdate((float a) =>
            {
                if (!rt) return;
                rt.anchoredPosition = Vector2.Lerp(basePos, hitPos, a);
                rt.localScale = Vector3.Lerp(baseScale, hitScale, a);
            })
            .setOnComplete(() =>
            {
                if (!rt) return;

                // Return phase
                LeanTween.value(rt.gameObject, 0f, 1f, t * 1.15f)
                    .setEaseOutBack()
                    .setIgnoreTimeScale(true)
                    .setOnUpdate((float b) =>
                    {
                        if (!rt) return;
                        rt.anchoredPosition = Vector2.Lerp(hitPos, basePos, b);
                        rt.localScale = Vector3.Lerp(hitScale, baseScale, b);
                    })
                    .setOnComplete(() =>
                    {
                        if (!rt) return;
                        rt.anchoredPosition = basePos;
                        rt.localScale = baseScale;
                    });
            });
    }


    public void PlayDefendResult(BattleFeedbackSide side, bool success)
    {
        var icon = GetIcon(side);
        if (!icon) return;

        Flash(icon, success ? flashDefend : flashFail, defendPulseTime);
        PunchScale(icon, success ? 1.06f : 1.03f, defendPulseTime * 0.8f);

        if (!success)
            Shake(icon.rectTransform, hitShakePixels * 0.6f, hitShakeTime * 0.75f);

        // Optional audio read: successful defend is a satisfying clank, failure is a softer fail.
        if (success)
            AudioManager.I?.PlaySfx(SfxType.Defend, 0.98f);
        else
            AudioManager.I?.PlaySfx(SfxType.Defend, 0.92f);
    }


    public void PlayKO(BattleFeedbackSide side)
    {
        if (enableKOSlowMo)
            SlowMo(koSlowMoTimeScale, koSlowMoSeconds);

        if (vignetteFlash)
            FlashVignette();

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


    private void HitStop(float seconds)
    {
        SlowMo(hitStopTimeScale, seconds);
    }

    private void SlowMo(float timeScale, float seconds)
    {
        if (seconds <= 0f) return;

        // If a prior slow-mo was interrupted/restarted, always restore first.
        CancelActiveSlowMo(forceNormalTime: false);

        _timeScaleCR = StartCoroutine(Co_TimeScale(timeScale, seconds));
    }

    private IEnumerator Co_TimeScale(float timeScale, float seconds)
    {
        _slowMoPrevTimeScale = Time.timeScale;
        _slowMoPrevFixedDeltaTime = Time.fixedDeltaTime;
        _slowMoActive = true;

        // Apply
        Time.timeScale = Mathf.Clamp(timeScale, 0.001f, 1f);
        Time.fixedDeltaTime = _slowMoPrevFixedDeltaTime * Time.timeScale;

        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // Restore
        Time.timeScale = _slowMoPrevTimeScale;
        Time.fixedDeltaTime = _slowMoPrevFixedDeltaTime;
        _slowMoActive = false;
        _slowMoPrevTimeScale = 1f;
        _slowMoPrevFixedDeltaTime = Time.fixedDeltaTime;

        _timeScaleCR = null;
    }

    private void FlashVignette()
    {
        if (!vignetteFlash) return;

        LeanTween.cancel(vignetteFlash.gameObject);

        var c = vignetteFlash.color;
        c.a = 0f;
        vignetteFlash.color = c;

        vignetteFlash.gameObject.SetActive(true);

        LeanTween.value(vignetteFlash.gameObject, 0f, vignetteFlashAlpha, vignetteFlashIn)
            .setIgnoreTimeScale(true)
            .setOnUpdate((float a) =>
            {
                if (!vignetteFlash) return;
                var cc = vignetteFlash.color;
                cc.a = a;
                vignetteFlash.color = cc;
            })
            .setOnComplete(() =>
            {
                LeanTween.value(vignetteFlash.gameObject, vignetteFlashAlpha, 0f, vignetteFlashOut)
                    .setIgnoreTimeScale(true)
                    .setOnUpdate((float a) =>
                    {
                        if (!vignetteFlash) return;
                        var cc = vignetteFlash.color;
                        cc.a = a;
                        vignetteFlash.color = cc;
                    })
                    .setOnComplete(() =>
                    {
                        if (vignetteFlash)
                            vignetteFlash.gameObject.SetActive(false);
                    });
            });
    }

    private void PlayCritTag(BattleFeedbackSide side)
    {
        TMP_Text tag = (side == BattleFeedbackSide.Player) ? playerCritTag : wildCritTag;
        if (!tag) return;

        // Ensure only one hide coroutine runs per-side.
        if (side == BattleFeedbackSide.Player)
        {
            if (_playerCritHideCR != null) StopCoroutine(_playerCritHideCR);
            _playerCritHideCR = null;
        }
        else
        {
            if (_wildCritHideCR != null) StopCoroutine(_wildCritHideCR);
            _wildCritHideCR = null;
        }

        tag.gameObject.SetActive(true);
        tag.text = "CRIT!";

        var rt = tag.rectTransform;
        if (rt)
        {
            LeanTween.cancel(rt.gameObject);
            var baseScale = rt.localScale;
            rt.localScale = baseScale;

            LeanTween.scale(rt, baseScale * critTagPunch, 0.08f)
                .setEaseOutBack()
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    if (!rt) return;
                    LeanTween.scale(rt, baseScale, 0.10f).setEaseOutQuad().setIgnoreTimeScale(true);
                });
        }

        if (side == BattleFeedbackSide.Player)
            _playerCritHideCR = StartCoroutine(Co_HideCritTag(tag, critTagSeconds, BattleFeedbackSide.Player));
        else
            _wildCritHideCR = StartCoroutine(Co_HideCritTag(tag, critTagSeconds, BattleFeedbackSide.Wild));
    }

    private IEnumerator Co_HideCritTag(TMP_Text tag, float seconds, BattleFeedbackSide side)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (tag) tag.gameObject.SetActive(false);

        if (side == BattleFeedbackSide.Player)
            _playerCritHideCR = null;
        else
            _wildCritHideCR = null;
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

    public IEnumerator Co_RevealPanels(
        CanvasGroup wildCG,
        CanvasGroup playerCG,
        float duration,
        bool playerFirstBySpeed,
        MonsterDataSO playerDef,
        MonsterDataSO wildDef,
        Action<BattleFeedbackSide, float> onRevealStart = null,
        float betweenSpawnDelay = 0f,
        Func<MonsterDataSO, IEnumerator> onSpawnAnnounce = null)
    {
        float dur = Mathf.Max(0f, duration);

        if (!enableSpeedOrderedIconIntro)
        {
            PrepareIconIntroFade();

            onRevealStart?.Invoke(BattleFeedbackSide.Player, dur);
            onRevealStart?.Invoke(BattleFeedbackSide.Wild, dur);

            if (wildCG) LeanTween.alphaCanvas(wildCG, 1f, dur).setIgnoreTimeScale(true);
            if (playerCG) LeanTween.alphaCanvas(playerCG, 1f, dur).setIgnoreTimeScale(true);

            FadeGraphicAlpha(playerIcon, 1f, dur);
            FadeGraphicAlpha(wildIcon, 1f, dur);

            if (dur > 0f)
                yield return new WaitForSecondsRealtime(dur);

            yield break;
        }

        PrepareIconIntroFade();

        float fadeTime = Mathf.Max(0.01f, iconIntroFadeTime);
        float firstFadeTime = fadeTime * Mathf.Max(1f, iconIntroFirstFadeMult);

        Graphic firstIcon = playerFirstBySpeed ? playerIcon : wildIcon;
        Graphic secondIcon = playerFirstBySpeed ? wildIcon : playerIcon;

        CanvasGroup firstPanel = playerFirstBySpeed ? playerCG : wildCG;
        CanvasGroup secondPanel = playerFirstBySpeed ? wildCG : playerCG;

        MonsterDataSO firstDef = playerFirstBySpeed ? playerDef : wildDef;
        MonsterDataSO secondDef = playerFirstBySpeed ? wildDef : playerDef;
        float spawnGap = Mathf.Max(0f, betweenSpawnDelay);

        onRevealStart?.Invoke(playerFirstBySpeed ? BattleFeedbackSide.Player : BattleFeedbackSide.Wild, firstFadeTime);
        yield return RevealSingleMonster(firstPanel, firstIcon, firstDef, firstFadeTime, onSpawnAnnounce);

        if (spawnGap > 0f)
            yield return new WaitForSecondsRealtime(spawnGap);

        onRevealStart?.Invoke(playerFirstBySpeed ? BattleFeedbackSide.Wild : BattleFeedbackSide.Player, fadeTime);
        yield return RevealSingleMonster(secondPanel, secondIcon, secondDef, fadeTime, onSpawnAnnounce);

        float minTotal = firstFadeTime + fadeTime + spawnGap;
        if (dur > minTotal)
            yield return new WaitForSecondsRealtime(dur - minTotal);
    }

    private IEnumerator RevealSingleMonster(CanvasGroup panel, Graphic icon, MonsterDataSO def, float revealDuration, Func<MonsterDataSO, IEnumerator> onSpawnAnnounce)
    {
        float revealTime = Mathf.Max(0f, revealDuration);

        if (panel)
            LeanTween.alphaCanvas(panel, 1f, revealTime).setIgnoreTimeScale(true);

        if (icon)
            PlayIconIntroFor(icon, revealTime);

        if (revealTime > 0f)
            yield return new WaitForSecondsRealtime(revealTime);

        PlayMonsterSpawnSfx(def);

        if (onSpawnAnnounce != null)
            yield return onSpawnAnnounce(def);

        float postGap = Mathf.Max(0f, iconIntroGap);
        if (postGap > 0f)
            yield return new WaitForSecondsRealtime(postGap);
    }

    private void PrepareIconIntroFade()
    {
        SetGraphicAlpha(playerIcon, 0f);
        SetGraphicAlpha(wildIcon, 0f);
    }

    private void FadeGraphicAlpha(Graphic g, float targetAlpha, float duration)
    {
        if (!g) return;

        float target = Mathf.Clamp01(targetAlpha);
        float t = Mathf.Max(0f, duration);
        if (t <= 0f)
        {
            SetGraphicAlpha(g, target);
            return;
        }

        float start = Mathf.Clamp01(g.color.a);
        LeanTween.value(g.gameObject, start, target, t)
            .setIgnoreTimeScale(true)
            .setEaseOutQuad()
            .setOnUpdate((float a) =>
            {
                if (!g) return;
                SetGraphicAlpha(g, a);
            });
    }

    private void PlayMonsterSpawnSfx(MonsterDataSO def)
    {
        if (def == null || def.spawnSfx == null) return;
        if (AudioManager.I == null) return;
        AudioManager.I.PlayClipOneShot(def.spawnSfx);
    }

    public void PlayChargeSfx()
    {
        if (chargeSfx == null) return;
        if (AudioManager.I == null) return;
        AudioManager.I.PlayClipOneShot(chargeSfx);
    }

    public void PlayDefendSfx()
    {
        if (defendSfx == null) return;
        if (AudioManager.I == null) return;
        AudioManager.I.PlayClipOneShot(defendSfx);
    }

    public void PlayRunSfx()
    {
        if (runSfx == null) return;
        if (AudioManager.I == null) return;
        AudioManager.I.PlayClipOneShot(runSfx);
    }

    private void PlayIconIntroFor(Graphic icon, float fadeTime)
    {
        if (!icon) return;

        FadeGraphic(icon, 1f, fadeTime);

        if (!iconIntroPunch || !icon.rectTransform)
            return;

        RectTransform rt = icon.rectTransform;
        Vector3 baseScale = rt == playerIcon?.rectTransform ? _playerIconBaseScale : _wildIconBaseScale;

        rt.localScale = baseScale;
        float punchTime = Mathf.Max(0.01f, iconIntroPunchTime);
        float peakScale = Mathf.Max(1.01f, iconIntroPunchScale);

        LeanTween.scale(rt, baseScale * peakScale, punchTime * 0.45f)
            .setEaseOutBack()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (!rt) return;
                LeanTween.scale(rt, baseScale, punchTime * 0.55f)
                    .setEaseOutQuad()
                    .setIgnoreTimeScale(true);
            });
    }

    public void SpawnBasicAttackVfx(bool isPlayerSide, MonsterDataSO playerDef, MonsterDataSO wildDef)
    {
        if (!spawnAttackPrefabs) return;

        MonsterDataSO def = isPlayerSide ? playerDef : wildDef;
        if (!def || !def.basicAttackPrefab) return;

        Transform spawnRoot = null;

        if (isPlayerSide)
        {
            if (playerAttackSpawnRoot && playerAttackSpawnRoot.gameObject.activeInHierarchy)
                spawnRoot = playerAttackSpawnRoot;
        }
        else
        {
            if (wildAttackSpawnRoot && wildAttackSpawnRoot.gameObject.activeInHierarchy)
                spawnRoot = wildAttackSpawnRoot;
        }

        if (!spawnRoot && EncounterManager.I != null)
        {
            var encounterRoot = isPlayerSide
                ? EncounterManager.I.EnemySpawnPoint
                : EncounterManager.I.PlayerSpawnPoint;

            if (encounterRoot && encounterRoot.gameObject.activeInHierarchy)
                spawnRoot = encounterRoot;
        }

        if (!spawnRoot)
        {
            var iconRoot = isPlayerSide
                ? (playerIcon != null ? playerIcon.transform : null)
                : (wildIcon != null ? wildIcon.transform : null);

            if (iconRoot && iconRoot.gameObject.activeInHierarchy)
                spawnRoot = iconRoot;
        }

        Vector3 pos = spawnRoot ? spawnRoot.position : Vector3.zero;
        Quaternion rot = spawnRoot ? spawnRoot.rotation : Quaternion.identity;

        var inst = Instantiate(def.basicAttackPrefab, pos, rot);
        if (spawnRoot) inst.transform.SetParent(spawnRoot, worldPositionStays: true);

        // Scale prefab animations to match battleSpeed so VFX don't lag behind the turn pace.
        float speed = (_battleManager != null) ? Mathf.Max(1f, _battleManager.BattleSpeed) : 1f;
        if (speed > 1f)
        {
            foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.simulationSpeed *= speed;
            }
            foreach (var anim in inst.GetComponentsInChildren<Animator>(true))
            {
                anim.speed *= speed;
            }
        }

        float life = Mathf.Max(0f, def.basicAttackPrefabLifetime);
        if (life > 0f) Destroy(inst, life / speed);
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

        LeanTween.moveX(target, originalPos.x + UnityEngine.Random.Range(-hpShakeStrength, hpShakeStrength), hpShakeDuration)
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
        // Backwards-compatible call site (no shield info).
        SetHPTexts(playerCur, playerMax, wildCur, wildMax, 0, 0);
    }

    /// <summary>
    /// UI-only. Call whenever HP changes, when swapping, and at battle start.
    /// Supports optional shield display (e.g., Title battle-start shield).
    /// </summary>
    public void SetHPTexts(float playerCur, float playerMax, float wildCur, float wildMax, int playerShield, int wildShield)
    {
        int pCur = Mathf.CeilToInt(Mathf.Max(0f, playerCur));
        int pMax = Mathf.CeilToInt(Mathf.Max(1f, playerMax));
        int wCur = Mathf.CeilToInt(Mathf.Max(0f, wildCur));
        int wMax = Mathf.CeilToInt(Mathf.Max(1f, wildMax));

        int pSh  = Mathf.Max(0, playerShield);
        int wSh  = Mathf.Max(0, wildShield);

        bool pChanged = (pCur != _lastPlayerCur) || (pMax != _lastPlayerMax) || (pSh != _lastPlayerShield);
        bool wChanged = (wCur != _lastWildCur) || (wMax != _lastWildMax) || (wSh != _lastWildShield);

        _lastPlayerCur = pCur; _lastPlayerMax = pMax; _lastPlayerShield = pSh;
        _lastWildCur = wCur; _lastWildMax = wMax; _lastWildShield = wSh;

        if (playerHPValueText)
        {
            playerHPValueText.text = (pSh > 0) ? $"{pCur}/{pMax} (+{pSh})" : $"{pCur}/{pMax}";
            if (hpTextPunchOnChange && pChanged) PunchTMP(playerHPValueText);
        }

        if (wildHPValueText)
        {
            wildHPValueText.text = (wSh > 0) ? $"{wCur}/{wMax} (+{wSh})" : $"{wCur}/{wMax}";
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

    private Vector3 GetBaseScale(RectTransform rt, BattleFeedbackSide side)
    {
        if (!rt) return Vector3.one;

        if (side == BattleFeedbackSide.Player && playerIcon && rt == playerIcon.rectTransform)
            return _playerIconBaseScale;

        if (side == BattleFeedbackSide.Wild && wildIcon && rt == wildIcon.rectTransform)
            return _wildIconBaseScale;

        return rt.localScale;
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
        float scaledTime = ScaleFeedbackDuration(time);
        LeanTween.scale(rt, baseScale * scaleMult, scaledTime)
            .setEaseOutBack()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (!rt) return;
                LeanTween.scale(rt, baseScale, ScaleFeedbackDuration(time * 0.9f))
                    .setEaseOutQuad()
                    .setIgnoreTimeScale(true);
            });
    }

    private void Nudge(RectTransform rt, float pixelsX, float time)
    {
        if (!rt) return;

        Vector2 basePos = rt.anchoredPosition;
        float scaledTime = ScaleFeedbackDuration(time);
        LeanTween.moveX(rt, basePos.x + pixelsX, scaledTime)
            .setEaseOutQuad()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (!rt) return;
                LeanTween.moveX(rt, basePos.x, ScaleFeedbackDuration(time * 0.9f))
                    .setEaseOutQuad()
                    .setIgnoreTimeScale(true);
            });
    }

    private void Flash(Graphic g, Color flashColor, float time)
    {
        if (!g) return;

        Color baseColor = g.color;
        LeanTween.value(g.gameObject, 0f, 1f, ScaleFeedbackDuration(time))
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
                g.canvasRenderer.SetAlpha(a);
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
        g.canvasRenderer.SetAlpha(1f);
    }

    private void SetGraphicAlpha(Graphic g, float alpha)
    {
        if (!g) return;
        float a = Mathf.Clamp01(alpha);
        var c = g.color;
        c.a = a;
        g.color = c;
        g.canvasRenderer.SetAlpha(a);
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
                float x = UnityEngine.Random.Range(-strength, strength);
                float y = UnityEngine.Random.Range(-strength, strength) * 0.35f;

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

    private void RefreshHPFromBattle()
    {
        if (_battleManager == null)
            return;

        float pCur = _battleManager.GetActivePlayerCurHP();
        float pMax = _battleManager.GetActivePlayerMaxHP();
        float wCur = _battleManager.GetWildCurHP();
        float wMax = _battleManager.GetWildMaxHP();

        // IMPORTANT: Always include shield pools here.
        // A common UI pattern in this project is to emit a UIRefreshHP event after micro-juice.
        // If this refresh omits shield data, the (+Shield) suffix will appear briefly then get wiped.
        int pShield = _battleManager.GetActivePlayerTitleShieldTotal();
        int wShield = _battleManager.GetWildTitleShieldTotal();

        SetHPBars(pCur, pMax, wCur, wMax);
        SetHPTexts(pCur, pMax, wCur, wMax, pShield, wShield);
    }

}
