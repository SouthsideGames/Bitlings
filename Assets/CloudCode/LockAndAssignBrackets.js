// LockAndAssignBrackets.js
// Deploy to UGS Cloud Code: Dashboard → Cloud Code → Scripts → Create
//
// Closes registration for a tournament week and assigns players to brackets.
// This script is idempotent — re-running it for the same weekId is a no-op
// if brackets are already built.
//
// The script groups registered players by score band, merges undersized bands,
// splits into 32-player bracket slots, assigns tournament IDs and bracket seeds,
// and writes the results to Game Data for client retrieval.
//
// Game Data entities written:
//   "tournament_brackets_{weekId}" — key: tournamentId, value: bracket JSON
//   "tournament_player_map_{weekId}" — key: playerId, value: tournamentId
//   "tournament_lock_{weekId}" — key: "status", value: "done"
//
// Parameters:
//   weekId (STRING) — week ID to lock (e.g., "W20260413")
//
// Returns:
//   { success: true, bracketCount: N, playerCount: M }
//   { success: false, error: "reason" }

const { DataApi } = require("@unity-services/cloud-save-1.4");

const BRACKET_SIZE = 32;
const MIN_REAL_FOR_MERGE = 8;
const BAND_NAMES = ["Low", "Standard", "High", "Elite"];

module.exports = async ({ params, context, logger }) => {
  const { projectId } = context;
  const weekId = params.weekId;

  if (!weekId || typeof weekId !== "string") {
    return { success: false, error: "weekId is required." };
  }

  const cloudSave = new DataApi(context);

  // ── Check if already locked ──

  const lockEntity = `tournament_lock_${weekId}`;
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
        logger.info(`Week ${weekId} already locked — no-op.`);
        return {
          success: true,
          bracketCount: 0,
          playerCount: 0,
          alreadyLocked: true,
        };
      }
    }
  } catch (e) {
    const status = e?.response?.status || e?.status;
    if (status !== 404) {
      logger.error("Error checking lock status: " + e.message);
    }
    // 404 = entity doesn't exist = not locked yet — continue
  }

  // ── Read all registrations ──

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
          const reg = JSON.parse(item.value);
          registrations.push(reg);
        } catch (e) {
          logger.error(
            `Bad registration data for key ${item.key}: ${e.message}`,
          );
        }
      }
    }
  } catch (e) {
    const status = e?.response?.status || e?.status;
    if (status === 404) {
      logger.info(`No registrations found for ${weekId}.`);
      // Write lock and return
      await writeLock(cloudSave, projectId, lockEntity, logger);
      return { success: true, bracketCount: 0, playerCount: 0 };
    }
    logger.error("Failed to read registrations: " + e.message);
    return { success: false, error: "Server error reading registrations." };
  }

  logger.info(`Found ${registrations.length} registration(s) for ${weekId}.`);

  if (registrations.length === 0) {
    await writeLock(cloudSave, projectId, lockEntity, logger);
    return { success: true, bracketCount: 0, playerCount: 0 };
  }

  // ── Group by score band ──

  const pools = { 0: [], 1: [], 2: [], 3: [] };
  for (const reg of registrations) {
    const band = Math.max(0, Math.min(3, reg.scoreBand || 0));
    pools[band].push(reg);
  }

  // ── Merge small bands ──

  for (let band = 0; band <= 3; band++) {
    if (pools[band].length > 0 && pools[band].length < MIN_REAL_FOR_MERGE) {
      // Find nearest non-empty adjacent band (prefer larger pool)
      let target = findMergeTarget(pools, band);
      if (target !== -1 && target !== band) {
        logger.info(
          `Merging ${BAND_NAMES[band]} (${pools[band].length}) into ${BAND_NAMES[target]}`,
        );
        pools[target].push(...pools[band]);
        pools[band] = [];
      }
    }
  }

  // ── Create brackets ──

  const bracketsEntity = `tournament_brackets_${weekId}`;
  const playerMapEntity = `tournament_player_map_${weekId}`;

  let totalBrackets = 0;
  const weekStartUtc = weekIdToEpoch(weekId);
  const weekEndUtc = weekStartUtc + 7 * 24 * 60 * 60 - 1;

  for (let band = 0; band <= 3; band++) {
    const pool = pools[band];
    if (pool.length === 0) continue;

    // Shuffle the pool deterministically
    const poolSeed = hashCode(`${weekId}_${band}`);
    shuffleArray(pool, poolSeed);

    // Split into 32-player bracket chunks
    for (let i = 0; i < pool.length; i += BRACKET_SIZE) {
      const chunk = pool.slice(i, i + BRACKET_SIZE);
      const bracketIndex = totalBrackets;
      const tournamentId = `T${weekId}_${BAND_NAMES[band]}_${bracketIndex}`;
      const bracketSeed = hashCode(`${tournamentId}_seed`);

      // Build real entry objects with entryIds and seedOrders
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

      // Write bracket data
      try {
        await cloudSave.setPrivateCustomItem(projectId, bracketsEntity, {
          key: tournamentId,
          value: JSON.stringify(bracketData),
        });
      } catch (e) {
        logger.error(`Failed to write bracket ${tournamentId}: ${e.message}`);
        return { success: false, error: "Server error writing bracket data." };
      }

      // Write player → bracket mapping for each real player
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
          logger.error(
            `Failed to map player ${entry.playerId} → ${tournamentId}: ${e.message}`,
          );
        }
      }

      totalBrackets++;
      logger.info(
        `Bracket ${tournamentId}: ${realEntries.length} real + ${BRACKET_SIZE - realEntries.length} bots`,
      );
    }
  }

  // ── Mark week as locked ──

  await writeLock(cloudSave, projectId, lockEntity, logger);

  logger.info(
    `Week ${weekId} locked: ${totalBrackets} bracket(s), ${registrations.length} player(s).`,
  );
  return {
    success: true,
    bracketCount: totalBrackets,
    playerCount: registrations.length,
  };
};

// ═══════════════════════════════════════════════════════════════
//  Helpers
// ═══════════════════════════════════════════════════════════════

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
  // Search adjacent bands outward, prefer larger pool
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
    if (candidates.length > 0) break; // Found at least one at this distance
  }
  if (candidates.length === 0) return sourceBand; // No merge target — keep as is
  candidates.sort((a, b) => b.size - a.size);
  return candidates[0].band;
}

/**
 * Fisher-Yates shuffle with a seeded PRNG (mulberry32).
 */
function shuffleArray(arr, seed) {
  const rng = mulberry32(seed);
  for (let i = arr.length - 1; i > 0; i--) {
    const j = Math.floor(rng() * (i + 1));
    [arr[i], arr[j]] = [arr[j], arr[i]];
  }
}

/**
 * Mulberry32 — fast 32-bit seeded PRNG producing values in [0, 1).
 */
function mulberry32(seed) {
  let s = seed | 0;
  return function () {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * Simple string hash (Java hashCode equivalent).
 */
function hashCode(str) {
  let hash = 0;
  for (let i = 0; i < str.length; i++) {
    hash = (Math.imul(31, hash) + str.charCodeAt(i)) | 0;
  }
  return hash;
}

/**
 * Converts a weekId like "W20260413" to a UTC epoch (approximate Monday 00:00 ET).
 * Uses the date as-is and subtracts 5 hours for EST (close enough for bracket metadata).
 */
function weekIdToEpoch(weekId) {
  const dateStr = weekId.substring(1); // "20260413"
  const y = parseInt(dateStr.substring(0, 4));
  const m = parseInt(dateStr.substring(4, 6)) - 1;
  const d = parseInt(dateStr.substring(6, 8));
  // Monday 00:00 ET ≈ Monday 04:00/05:00 UTC (EDT/EST)
  const utc = Date.UTC(y, m, d, 5, 0, 0); // Approximate — 05:00 UTC = 00:00 EST
  return Math.floor(utc / 1000);
}
