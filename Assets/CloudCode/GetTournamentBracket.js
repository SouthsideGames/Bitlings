// GetTournamentBracket.js
// Deploy to UGS Cloud Code: Dashboard → Cloud Code → Scripts → Create
//
// Returns the calling player's bracket assignment for the current week.
// If brackets haven't been built yet but it's past the lock time,
// this script triggers LockAndAssignBrackets lazily.
//
// Game Data entities read:
//   "tournament_player_map_{weekId}" — key: playerId → { tournamentId, entryId }
//   "tournament_brackets_{weekId}"   — key: tournamentId → full bracket data
//   "tournament_lock_{weekId}"       — key: "status" → "done"
//
// Parameters:
//   weekId (STRING) — client-computed week ID (e.g., "W20260413")
//
// Returns:
//   { assigned: true, bracket: { ... } }
//   { assigned: false, reason: "..." }

const { DataApi } = require("@unity-services/cloud-save-1.4");

const BRACKET_SIZE = 32;
const MIN_REAL_FOR_MERGE = 8;
const BAND_NAMES = ["Low", "Standard", "High", "Elite"];

module.exports = async ({ params, context, logger }) => {
  const { projectId, playerId } = context;
  const weekId = params.weekId;

  if (!weekId || typeof weekId !== "string") {
    return { assigned: false, reason: "Invalid week ID." };
  }

  // SECURITY: only the CURRENT week may be (lazily) locked or read. Without this
  // check, any client could send a FUTURE weekId on a Wednesday+, lazy-lock that
  // week with zero registrations, and the idempotent "done" lock would then stop
  // real brackets from ever being built when that week arrives.
  if (weekId !== getCurrentWeekId()) {
    return { assigned: false, reason: "Week mismatch. Please refresh." };
  }

  const cloudSave = new DataApi(context);

  // ── Check if brackets are built ──

  const lockEntity = `tournament_lock_${weekId}`;
  let bracketsBuilt = false;

  try {
    const lockResult = await cloudSave.getPrivateCustomItems(
      projectId,
      lockEntity,
    );
    if (lockResult?.data?.results?.length > 0) {
      const statusItem = lockResult.data.results.find(
        (r) => r.key === "status",
      );
      if (statusItem && statusItem.value === "done") {
        bracketsBuilt = true;
      }
    }
  } catch (e) {
    const status = e?.response?.status || e?.status;
    if (status !== 404) {
      logger.error("Error checking lock: " + e.message);
    }
    // 404 = not locked yet
  }

  // ── If not built and past lock time, build now (lazy lock) ──

  if (!bracketsBuilt) {
    if (!isPastLockTime()) {
      return {
        assigned: false,
        reason: "Brackets haven't been assigned yet. Check back Wednesday.",
      };
    }

    logger.info(`Lazy-locking week ${weekId}...`);
    const lockResult = await buildBrackets(
      cloudSave,
      projectId,
      weekId,
      logger,
    );
    if (!lockResult.success) {
      return { assigned: false, reason: "Error building brackets. Try again." };
    }
    bracketsBuilt = true;
  }

  // ── Look up player's bracket assignment ──

  const playerMapEntity = `tournament_player_map_${weekId}`;
  let mapping = null;

  try {
    const mapResult = await cloudSave.getPrivateCustomItems(
      projectId,
      playerMapEntity,
    );
    if (mapResult?.data?.results) {
      const item = mapResult.data.results.find((r) => r.key === playerId);
      if (item) {
        mapping = JSON.parse(item.value);
      }
    }
  } catch (e) {
    const status = e?.response?.status || e?.status;
    if (status !== 404) {
      logger.error("Error reading player map: " + e.message);
      return { assigned: false, reason: "Server error. Try again." };
    }
  }

  if (!mapping) {
    return {
      assigned: false,
      reason: "You are not registered for this week's tournament.",
    };
  }

  // ── Fetch bracket data ──

  const bracketsEntity = `tournament_brackets_${weekId}`;
  let bracketData = null;

  try {
    const bracketResult = await cloudSave.getPrivateCustomItems(
      projectId,
      bracketsEntity,
    );
    if (bracketResult?.data?.results) {
      const item = bracketResult.data.results.find(
        (r) => r.key === mapping.tournamentId,
      );
      if (item) {
        bracketData = JSON.parse(item.value);
      }
    }
  } catch (e) {
    logger.error("Error reading bracket: " + e.message);
    return { assigned: false, reason: "Server error reading bracket." };
  }

  if (!bracketData) {
    return {
      assigned: false,
      reason: "Bracket data not found. Try again later.",
    };
  }

  return {
    assigned: true,
    entryId: mapping.entryId,
    bracket: bracketData,
  };
};

// ═══════════════════════════════════════════════════════════════
//  Lazy bracket building (inline copy of LockAndAssignBrackets logic)
// ═══════════════════════════════════════════════════════════════

async function buildBrackets(cloudSave, projectId, weekId, logger) {
  const lockEntity = `tournament_lock_${weekId}`;

  // Read registrations
  const regEntity = `tournament_reg_${weekId}`;
  let registrations = [];

  try {
    const regResult = await cloudSave.getPrivateCustomItems(
      projectId,
      regEntity,
    );
    if (regResult?.data?.results) {
      for (const item of regResult.data.results) {
        try {
          registrations.push(JSON.parse(item.value));
        } catch (_) {}
      }
    }
  } catch (e) {
    const status = e?.response?.status || e?.status;
    if (status === 404) {
      await writeLock(cloudSave, projectId, lockEntity, logger);
      return { success: true };
    }
    return { success: false };
  }

  if (registrations.length === 0) {
    await writeLock(cloudSave, projectId, lockEntity, logger);
    return { success: true };
  }

  // Group by band
  const pools = { 0: [], 1: [], 2: [], 3: [] };
  for (const reg of registrations) {
    pools[Math.max(0, Math.min(3, reg.scoreBand || 0))].push(reg);
  }

  // Merge small bands
  for (let band = 0; band <= 3; band++) {
    if (pools[band].length > 0 && pools[band].length < MIN_REAL_FOR_MERGE) {
      const target = findMergeTarget(pools, band);
      if (target !== -1 && target !== band) {
        pools[target].push(...pools[band]);
        pools[band] = [];
      }
    }
  }

  // Create brackets
  const bracketsEntity = `tournament_brackets_${weekId}`;
  const playerMapEntity = `tournament_player_map_${weekId}`;
  const weekStartUtc = weekIdToEpoch(weekId);
  const weekEndUtc = weekStartUtc + 7 * 24 * 60 * 60 - 1;
  let totalBrackets = 0;

  for (let band = 0; band <= 3; band++) {
    const pool = pools[band];
    if (pool.length === 0) continue;

    shuffleArray(pool, hashCode(`${weekId}_${band}`));

    for (let i = 0; i < pool.length; i += BRACKET_SIZE) {
      const chunk = pool.slice(i, i + BRACKET_SIZE);
      const bracketIndex = totalBrackets;
      const tournamentId = `T${weekId}_${BAND_NAMES[band]}_${bracketIndex}`;
      const bracketSeed = hashCode(`${tournamentId}_seed`);

      const realEntries = chunk.map((reg, idx) => ({
        entryId: `${tournamentId}_E_${idx}`,
        playerId: reg.playerId,
        displayName: reg.displayName,
        teamSnapshotJson: reg.teamSnapshotJson,
        arenaScore: reg.arenaScore,
        isBot: false,
      }));

      const bracketData = {
        tournamentId,
        weekStartUtc,
        weekEndUtc,
        scoreBand: band,
        bracketSeed,
        realEntries,
        realPlayerCount: realEntries.length,
        botsNeeded: BRACKET_SIZE - realEntries.length,
      };

      try {
        await cloudSave.setPrivateCustomItem(projectId, bracketsEntity, {
          key: tournamentId,
          value: JSON.stringify(bracketData),
        });
      } catch (e) {
        logger.error(`Failed to write bracket ${tournamentId}: ${e.message}`);
        return { success: false };
      }

      for (const entry of realEntries) {
        try {
          await cloudSave.setPrivateCustomItem(projectId, playerMapEntity, {
            key: entry.playerId,
            value: JSON.stringify({
              tournamentId,
              entryId: entry.entryId,
              scoreBand: band,
            }),
          });
        } catch (e) {
          logger.error(`Failed to map player ${entry.playerId}: ${e.message}`);
        }
      }

      totalBrackets++;
    }
  }

  await writeLock(cloudSave, projectId, lockEntity, logger);
  logger.info(`Lazy-lock complete: ${totalBrackets} bracket(s).`);
  return { success: true };
}

// ═══════════════════════════════════════════════════════════════
//  Shared helpers
// ═══════════════════════════════════════════════════════════════

function isPastLockTime() {
  const et = getETComponents();
  const dow = { Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6, Sun: 0 }[
    et.weekday
  ];
  // Wednesday (3) or later in the week (Thu=4, Fri=5, Sat=6, Sun=0)
  // Mon-based: Mon=0, Tue=1, Wed=2, Thu=3, Fri=4, Sat=5, Sun=6
  const monBased = dow === 0 ? 6 : dow - 1;
  return monBased >= 2;
}

function getCurrentWeekId() {
  const et = getETComponents();
  const year = parseInt(et.year);
  const month = parseInt(et.month) - 1;
  const day = parseInt(et.day);
  const dow = { Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6, Sun: 0 }[
    et.weekday
  ];
  // Monday-based offset (same logic as RegisterForTournament.js)
  const mondayOffset = dow === 0 ? -6 : 1 - dow;
  const mondayDate = new Date(year, month, day + mondayOffset);
  const y = mondayDate.getFullYear();
  const m = String(mondayDate.getMonth() + 1).padStart(2, "0");
  const d = String(mondayDate.getDate()).padStart(2, "0");
  return `W${y}${m}${d}`;
}

function getETComponents() {
  const now = new Date();
  const parts = {};
  for (const p of new Intl.DateTimeFormat("en-US", {
    timeZone: "America/New_York",
    weekday: "short",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).formatToParts(now)) {
    parts[p.type] = p.value;
  }
  return parts;
}

async function writeLock(cloudSave, projectId, lockEntity, logger) {
  try {
    await cloudSave.setPrivateCustomItem(projectId, lockEntity, {
      key: "status",
      value: "done",
    });
  } catch (e) {
    logger.error("Failed to write lock: " + e.message);
  }
}

function findMergeTarget(pools, sourceBand) {
  const candidates = [];
  for (let d = 1; d <= 3; d++) {
    if (sourceBand - d >= 0 && pools[sourceBand - d].length > 0)
      candidates.push({
        band: sourceBand - d,
        size: pools[sourceBand - d].length,
      });
    if (sourceBand + d <= 3 && pools[sourceBand + d].length > 0)
      candidates.push({
        band: sourceBand + d,
        size: pools[sourceBand + d].length,
      });
    if (candidates.length > 0) break;
  }
  if (candidates.length === 0) return sourceBand;
  candidates.sort((a, b) => b.size - a.size);
  return candidates[0].band;
}

function shuffleArray(arr, seed) {
  const rng = mulberry32(seed);
  for (let i = arr.length - 1; i > 0; i--) {
    const j = Math.floor(rng() * (i + 1));
    [arr[i], arr[j]] = [arr[j], arr[i]];
  }
}

function mulberry32(seed) {
  let s = seed | 0;
  return function () {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function hashCode(str) {
  let hash = 0;
  for (let i = 0; i < str.length; i++) {
    hash = (Math.imul(31, hash) + str.charCodeAt(i)) | 0;
  }
  return hash;
}

function weekIdToEpoch(weekId) {
  const dateStr = weekId.substring(1);
  const y = parseInt(dateStr.substring(0, 4));
  const m = parseInt(dateStr.substring(4, 6)) - 1;
  const d = parseInt(dateStr.substring(6, 8));
  const utc = Date.UTC(y, m, d, 5, 0, 0);
  return Math.floor(utc / 1000);
}
