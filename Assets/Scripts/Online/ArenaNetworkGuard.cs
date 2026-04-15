using UnityEngine;

/// <summary>
/// Centralized check for whether online arena features are available.
/// Query <see cref="IsOnline"/> before any arena action that requires the server
/// (entering tournaments, setting username, viewing leaderboards, etc.).
/// </summary>
public static class ArenaNetworkGuard
{
    /// <summary>
    /// Returns <c>true</c> when UGS is initialised, the player is authenticated,
    /// and the device has network connectivity.
    /// </summary>
    public static bool IsOnline
    {
        get
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
                return false;

            if (UGSInitializer.I == null || !UGSInitializer.I.IsReady)
                return false;

            return true;
        }
    }

    /// <summary>
    /// Returns a player-friendly reason string when <see cref="IsOnline"/> is false.
    /// Returns <c>null</c> when online.
    /// </summary>
    public static string GetOfflineReason()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable)
            return "No internet connection. Connect to play Arena.";

        if (UGSInitializer.I == null)
            return "Online services are loading. Please wait.";

        if (UGSInitializer.I.IsInitializing)
            return "Connecting to online services…";

        if (!UGSInitializer.I.IsReady)
        {
            if (!string.IsNullOrEmpty(UGSInitializer.I.LastError))
                return $"Unable to connect: {UGSInitializer.I.LastError}";
            return "Online services unavailable. Try again later.";
        }

        return null;
    }
}
