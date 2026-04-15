// Assets/Scripts/Arena/ArenaOnboardingManager.cs
// BRN Arena v1 — Orchestrates the first-open onboarding flow:
//   1. Intro tutorial overlay (weekly tournament basics)
//   2. Username creation popup
//   3. Redirect to Directory for Battle Team setup
// All steps are gated by persistent save flags so they only run once.

using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Static manager that drives the arena onboarding sequence.
/// Called from <see cref="ArenaMainPanelUI.OnEnable"/> each time the arena panel opens.
/// Each step is idempotent — flags in <see cref="ArenaSaveData"/> prevent repeats.
/// </summary>
public static class ArenaOnboardingManager
{
    // ── Tutorial key (matches the TutorialOverlayPanel instance on the arena panel) ──
    public const string ArenaIntroTutorialKey = "tut_arena_intro_v1";

    // ── Username constraints ──
    public const int UsernameMinLength = 2;
    public const int UsernameMaxLength = 16;

    // ═════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>true</c> if any onboarding step still needs to run.
    /// </summary>
    public static bool NeedsOnboarding()
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return false;
        if (!arena.arenaUnlocked) return false;

        return !arena.introCompleted || !arena.usernameCreated;
    }

    /// <summary>
    /// Runs the next pending onboarding step. Called from the arena panel's OnEnable.
    /// Returns <c>true</c> if a step was triggered (caller should wait before doing
    /// normal panel refresh).
    /// </summary>
    public static bool TryAdvanceOnboarding()
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return false;
        if (!arena.arenaUnlocked) return false;

        // Step 1: Intro tutorial.
        if (!arena.introCompleted)
        {
            // If the tutorial was already completed via SaveManager but CompleteIntro()
            // was never called (no callback wired), sync the flag now so we can advance.
            if (SaveManager.IsTutorialComplete(ArenaIntroTutorialKey))
            {
                arena.introCompleted = true;
                SaveManager.Save();
            }
            else
            {
                TutorialOverlayPanel.RequestOpen(ArenaIntroTutorialKey);

                // If the overlay actually opened, wait for it to complete before
                // advancing to the next step.  If it didn't open (panel missing
                // from scene, no pages configured, etc.) auto-complete so we
                // don't permanently block the rest of onboarding.
                if (TutorialOverlayPanel.IsAnyOverlayOpen)
                {
                    return true;
                }

                arena.introCompleted = true;
                SaveManager.SetTutorialComplete(ArenaIntroTutorialKey, true);
                SaveManager.Save();
            }
        }

        // Step 2: Username creation.
        if (!arena.usernameCreated)
        {
            RequestUsernamePopup();
            return true;
        }

        return false;
    }

    // ═════════════════════════════════════════════════════════════
    //  Step 1 — Intro tutorial completion callback
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Called when the intro tutorial overlay is dismissed.
    /// Marks the intro as complete and immediately advances to the next step.
    /// Should be wired from the TutorialOverlayPanel's onComplete callback or
    /// polled after <c>SaveManager.IsTutorialComplete</c> returns true.
    /// </summary>
    public static void CompleteIntro()
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return;

        if (arena.introCompleted) return;

        arena.introCompleted = true;
        SaveManager.SetTutorialComplete(ArenaIntroTutorialKey, true);
        SaveManager.Save();

        // Advance to username step.
        TryAdvanceOnboarding();
    }

    // ═════════════════════════════════════════════════════════════
    //  Step 2 — Username creation
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates and commits the player's chosen arena username.
    /// Returns <c>true</c> on success. The username becomes permanent.
    /// This is the LOCAL-ONLY fallback — used when offline.
    /// Prefer <see cref="TrySetUsernameAsync"/> when online.
    /// </summary>
    public static bool TrySetUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;

        string trimmed = username.Trim();

        if (trimmed.Length < UsernameMinLength || trimmed.Length > UsernameMaxLength)
            return false;

        if (!IsUsernameSafe(trimmed))
            return false;

        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return false;

        // Already has a permanent username — no overwrites.
        if (arena.usernameCreated && !string.IsNullOrEmpty(arena.arenaUsername))
            return false;

        arena.arenaUsername = trimmed;
        arena.usernameCreated = true;
        SaveManager.Save();

        GameEvents.ArenaDataChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Server-validated username registration. Calls the Cloud Code endpoint
    /// to check global uniqueness, then writes locally on success.
    /// Returns (success, errorMessage).
    /// </summary>
    public static async Task<(bool success, string error)> TrySetUsernameAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return (false, "Please enter a name.");

        string trimmed = username.Trim();

        if (trimmed.Length < UsernameMinLength)
            return (false, $"Name must be at least {UsernameMinLength} characters.");

        if (trimmed.Length > UsernameMaxLength)
            return (false, $"Name must be at most {UsernameMaxLength} characters.");

        if (!IsUsernameSafe(trimmed))
            return (false, "Name contains invalid characters.");

        var arena = SaveManager.GetArenaSaveData();
        if (arena != null && arena.usernameCreated && !string.IsNullOrEmpty(arena.arenaUsername))
            return (false, "You already have a username.");

        // Call server for uniqueness check + registration.
        var result = await ArenaCloudCodeService.ValidateAndSetUsernameAsync(trimmed);

        if (!result.success)
            return (false, result.error ?? "Unable to set name.");

        // Server confirmed — update local cache.
        if (arena != null)
        {
            arena.arenaUsername = trimmed;
            arena.usernameCreated = true;
            SaveManager.Save();
        }

        GameEvents.ArenaDataChanged?.Invoke();
        return (true, null);
    }

    /// <summary>
    /// Basic client-side username validation: printable ASCII, no angle brackets,
    /// no leading/trailing whitespace.
    /// </summary>
    public static bool IsUsernameSafe(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            // Reject control characters.
            if (c < ' ') return false;
            // Reject angle brackets (HTML/XSS risk in future display contexts).
            if (c == '<' || c == '>') return false;
        }

        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  Step 3 — Redirect to Directory
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the Directory panel in Arena loadout mode so the player can set
    /// their Battle Team. Called after username creation or from the main panel
    /// Edit Team button.
    /// </summary>
    public static void OpenDirectoryForTeamSetup()
    {
        if (UIManager.I == null) return;

        UIManager.I.Show(PanelId.Directory);

        var root = UIManager.I.GetRoot(PanelId.Directory);
        if (root != null)
        {
            var dir = root.GetComponent<DirectoryPanelUI>();
            if (dir != null) dir.OpenInArenaMode();
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>true</c> if the intro tutorial has been completed.
    /// Checks both the arena save flag and the tutorial system (belt-and-suspenders).
    /// </summary>
    public static bool IsIntroComplete()
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena != null && arena.introCompleted) return true;
        return SaveManager.IsTutorialComplete(ArenaIntroTutorialKey);
    }

    /// <summary>
    /// Returns <c>true</c> if the player has a permanent arena username.
    /// </summary>
    public static bool HasUsername()
    {
        return ArenaSaveHelper.HasArenaUsername();
    }

    private static void RequestUsernamePopup()
    {
        var popup = ArenaUsernamePopupUI.I;
        if (popup == null)
        {
            // Singleton may be null if the popup GameObject was inactive (Awake never ran).
            popup = UnityEngine.Object.FindAnyObjectByType<ArenaUsernamePopupUI>(FindObjectsInactive.Include);
            if (popup != null)
            {
                popup.gameObject.SetActive(true);
            }
        }

        if (popup != null)
        {
            popup.Show();
        }
        else
        {
            Debug.LogWarning("[ArenaOnboardingManager] ArenaUsernamePopupUI not found in scene.");
        }
    }
}
