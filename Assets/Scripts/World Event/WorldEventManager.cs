using System;
using System.Collections.Generic;
using UnityEngine;


public sealed class WorldEventManager : MonoBehaviour
{
    public static WorldEventManager I { get; private set; }

    public event Action Changed;

    [Serializable]
    public sealed class Item
    {
        public string id;
        public string message;
        public long expiresUnix;
        public bool hasEffect;
    }

    private readonly List<Item> _items = new();
    private int _serial;

    public IReadOnlyList<Item> Items => _items;

    // -------------------------------------------------------------------------
    // Weekly rotation
    // -------------------------------------------------------------------------

    [Header("Weekly Rotation")]
    [Tooltip("Ordered list of events to rotate through, one per week.")]
    [SerializeField] private List<WorldEventSO> weeklyEvents = new();

    [Tooltip("ISO date (YYYY-MM-DD, a Monday) treated as week 0. Defaults to 2025-01-06.")]
    [SerializeField] private string weeklyEpochDate = "2025-01-06";

    /// <summary>
    /// The event selected for the current week, derived deterministically from the
    /// current date and <see cref="weeklyEpochDate"/>. Null when the list is empty.
    /// </summary>
    public WorldEventSO ActiveWeeklyEvent => GetActiveWeeklyEvent();

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    private void OnDestroy()
    {
        if (I == this) I = null;
    }

    private bool _deferringChanged;
    private float _expiryTimer;

    private void Update()
    {
        if (_items.Count == 0) return;

        _expiryTimer += Time.unscaledDeltaTime;
        if (_expiryTimer < 1f) return;
        _expiryTimer = 0f;

        long now = SaveManager.NowUnix();
        bool changed = false;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (it == null) { _items.RemoveAt(i); changed = true; continue; }
            if (it.expiresUnix > 0 && now >= it.expiresUnix)
            {
                _items.RemoveAt(i);
                changed = true;
            }
        }
        if (changed && !_deferringChanged)
        {
            _deferringChanged = true;
            try { Changed?.Invoke(); }
            finally { _deferringChanged = false; }
        }
    }

    public string Add(string message, float ttlSeconds = 0f, bool hasEffect = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        _serial++;
        string id = $"WE_FEED::{_serial}";
        long expires = 0;
        if (ttlSeconds > 0f)
            expires = SaveManager.NowUnix() + Mathf.Max(1, Mathf.RoundToInt(ttlSeconds));

        _items.Add(new Item { id = id, message = message.Trim(), expiresUnix = expires, hasEffect = hasEffect });
        Changed?.Invoke();
        return id;
    }

    public void Remove(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i] != null && string.Equals(_items[i].id, id, StringComparison.Ordinal))
            {
                _items.RemoveAt(i);
                Changed?.Invoke();
                return;
            }
        }
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Weekly rotation helpers
    // -------------------------------------------------------------------------

    private WorldEventSO GetActiveWeeklyEvent()
    {
        if (weeklyEvents == null || weeklyEvents.Count == 0) return null;

        // Count valid (non-null) entries without allocating a second list.
        int validCount = 0;
        for (int i = 0; i < weeklyEvents.Count; i++)
            if (weeklyEvents[i] != null) validCount++;

        if (validCount == 0) return null;

        int target = GetCurrentWeekIndex() % validCount;

        // Walk the list, skip nulls, return the entry at position `target`.
        int seen = 0;
        for (int i = 0; i < weeklyEvents.Count; i++)
        {
            if (weeklyEvents[i] == null) continue;
            if (seen == target) return weeklyEvents[i];
            seen++;
        }

        return null;
    }

    /// <summary>
    /// Returns how many full 7-day periods have elapsed since <see cref="weeklyEpochDate"/>.
    /// Always >= 0; clamps dates before the epoch to week 0.
    /// </summary>
    private int GetCurrentWeekIndex()
    {
        if (!DateTimeOffset.TryParse(weeklyEpochDate, out DateTimeOffset epoch))
            epoch = new DateTimeOffset(2025, 1, 6, 0, 0, 0, TimeSpan.Zero);

        long secondsSinceEpoch = SaveManager.NowUnix() - epoch.ToUnixTimeSeconds();
        return secondsSinceEpoch >= 0L ? (int)(secondsSinceEpoch / 604800L) : 0;
    }

    // -------------------------------------------------------------------------
    // UI surface
    // -------------------------------------------------------------------------

    /// <summary>
    /// Flat, allocation-free snapshot of the active weekly event for UI panels.
    /// Read from <see cref="TryGetWeeklyEventView"/>.
    /// </summary>
    public readonly struct WeeklyEventView
    {
        public readonly string displayName;
        public readonly string description;
        /// <summary>
        /// Human-readable time until the event rotates, e.g. "6d 14h", "3h 22m", "Ends soon".
        /// Always non-null.
        /// </summary>
        public readonly string  countdownText;

        internal WeeklyEventView(string displayName, string description, string countdownText)
        {
            this.displayName   = displayName;
            this.description   = description;
            this.countdownText = countdownText;
        }
    }

    /// <summary>
    /// Fills <paramref name="view"/> with the active weekly event data and returns true.
    /// Returns false (and an empty view) when no event is active — UI can hide its panel.
    /// </summary>
    public bool TryGetWeeklyEventView(out WeeklyEventView view)
    {
        var evt = ActiveWeeklyEvent;
        if (evt == null)
        {
            view = default;
            return false;
        }

        view = new WeeklyEventView(
            displayName:   string.IsNullOrWhiteSpace(evt.displayName) ? evt.id : evt.displayName,
            description:   evt.description ?? string.Empty,
            countdownText: GetWeekCountdownText()
        );
        return true;
    }

    /// <summary>
    /// Returns a short string describing how long until the week resets, e.g. "6d 14h".
    /// Safe to call even when no event is active (used for e.g. a persistent "next event" banner).
    /// </summary>
    public string GetWeekCountdownText()
    {
        long secondsLeft = SecondsUntilNextWeek();
        if (secondsLeft <= 0)    return "Ends soon";

        int days  = (int)(secondsLeft / 86400);
        int hours = (int)(secondsLeft % 86400 / 3600);
        int mins  = (int)(secondsLeft % 3600  / 60);

        if (days  >= 1) return $"{days}d {hours}h";
        if (hours >= 1) return $"{hours}h {mins}m";
        if (mins  >= 1) return $"{mins}m";
        return "Ends soon";
    }

    // Returns seconds until the next Monday 00:00 local time.
    private static long SecondsUntilNextWeek()
    {
        var now       = DateTimeOffset.Now;
        int dow       = (int)now.DayOfWeek;           // Sun=0 … Sat=6
        int daysToMon = (8 - dow) % 7;                // days until next Monday
        if (daysToMon == 0) daysToMon = 7;            // already Monday → count to *next* Monday

        var nextMonday = new DateTimeOffset(
            now.Date.AddDays(daysToMon).Year,
            now.Date.AddDays(daysToMon).Month,
            now.Date.AddDays(daysToMon).Day,
            0, 0, 0, now.Offset);

        long delta = nextMonday.ToUnixTimeSeconds() - SaveManager.NowUnix();
        return delta < 0 ? 0 : delta;
    }
}
