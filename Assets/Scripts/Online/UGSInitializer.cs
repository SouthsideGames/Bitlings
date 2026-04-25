using System;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Bootstraps Unity Gaming Services (Core + Authentication).
/// Lives on a persistent GameObject — once initialized, stays alive across scenes.
///
/// Other systems query <see cref="IsReady"/> and <see cref="PlayerId"/> to
/// gate online features (arena, leaderboards, cloud save).
/// </summary>
public sealed class UGSInitializer : MonoBehaviour
{
    public static UGSInitializer I { get; private set; }

    // ── Public state ──

    /// <summary>True once UGS Core + Auth sign-in have completed successfully.</summary>
    public bool IsReady { get; private set; }

    /// <summary>True while initialization is in progress.</summary>
    public bool IsInitializing { get; private set; }

    /// <summary>The UGS player ID (stable across sessions for anonymous auth).</summary>
    public string PlayerId { get; private set; }

    /// <summary>Non-null if the last init attempt failed.</summary>
    public string LastError { get; private set; }

    /// <summary>Fired once when initialization succeeds (IsReady becomes true).</summary>
    public static event Action OnReady;

    /// <summary>Fired if initialization fails. Passes the error message.</summary>
    public static event Action<string> OnFailed;

    // ═════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═════════════════════════════════════════════════════════════

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        InitializeAsync();
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    // ═════════════════════════════════════════════════════════════
    //  Initialization
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Kicks off UGS Core init + anonymous sign-in.
    /// Safe to call multiple times — early-outs if already ready or in progress.
    /// </summary>
    public async void InitializeAsync()
    {
        if (IsReady || IsInitializing) return;

        IsInitializing = true;
        LastError = null;

        try
        {
            // 1. Initialize UGS Core (required before any service call).
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            // 2. Sign in anonymously (creates or resumes a persistent player ID).
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            PlayerId = AuthenticationService.Instance.PlayerId;
            IsReady = true;

            Debug.Log($"[UGSInitializer] Ready. PlayerId={PlayerId}");
            OnReady?.Invoke();
        }
        catch (AuthenticationException ex)
        {
            LastError = ex.Message;
            Debug.LogError($"[UGSInitializer] Auth failed: {ex}");
            OnFailed?.Invoke(LastError);
        }
        catch (RequestFailedException ex)
        {
            LastError = ex.Message;
            Debug.LogError($"[UGSInitializer] Request failed: {ex}");
            OnFailed?.Invoke(LastError);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.LogError($"[UGSInitializer] Unexpected error: {ex}");
            OnFailed?.Invoke(LastError);
        }
        finally
        {
            IsInitializing = false;
        }
    }

    /// <summary>
    /// Allows retrying after a failure (e.g. network came back).
    /// Resets error state and kicks off init again.
    /// </summary>
    public void Retry()
    {
        if (IsReady) return;
        LastError = null;
        InitializeAsync();
    }
}
