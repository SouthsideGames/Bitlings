// SubmitTournamentResult.js
// Deploy to UGS Cloud Code (same module as the other Arena scripts).
//
// PURPOSE — closes the "client writes its own leaderboard score" hole.
// The client no longer calls Leaderboards.AddPlayerScore directly. Instead it
// submits the FULL final standings it computed (the ordered list of entryIds,
// champion first) plus its own claimed placement. The server:
//
//   1. verifies the caller actually belongs to the tournament (via the
//      server-authored tournament_player_map_{weekId}) and that the claimed
//      entryId / placement are internally consistent with the submitted
//      standings;
//   2. cross-checks the submitted standings against OTHER real players in the
//      same bracket. Because every honest client resolves the identical
//      deterministic bracket (same snapshots, same seeds), honest submissions
//      produce an identical standings hash. A lone cheater's forged standings
//      will not match the plurality, so it is rejected;
//   3. once a standings hash reaches quorum (or the bracket has too few real
//      players for quorum to ever exist), the server itself writes the weekly
//      placement score and maintains an authoritative all-time championship
//      counter — the client never writes leaderboards.
//
// Deploy alongside a dashboard Access-Control policy that DENIES player tokens
// write access to the arena_weekly / arena_alltime leaderboards, so this
// endpoint (running under the service context) is the only writer.
//
// Game Data entities used (all project-scoped shared custom data):
//   "tournament_player_map_{weekId}" — key: playerId  → { tournamentId, entryId, scoreBand }   (written by LockAndAssignBrackets)
//   "tournament_brackets_{weekId}"   — key: tournamentId → bracket blob (has realPlayerCount)   (written by LockAndAssignBrackets)
//   "tournament_results_{weekId}"    — key: playerId  → { tournamentId, standingsHash, placement, entryId, submittedUtc }
//   "tournament_scored_{weekId}"     — key: playerId  → "done"   (idempotency guard: score already written)
//   "arena_championships"            — key: playerId  → integer career championship count (authoritative)
//
// Parameters:
//   weekId        (STRING)  — client-computed week ID (e.g., "W20260413")
//   tournamentId  (STRING)  — the bracket the player claims to have finished
//   entryId       (STRING)  — the player's own entryId within that bracket
//   placement     (NUMERIC) — claimed final placement (1 = champion)
//   standingsJson (STRING)  — JSON array of entryIds ordered by placement (index 0 = 1st)
//
// Returns:
//   { status: "scored",  placement, score }        — verified and written
//   { status: "pending", have, need }              — stored, waiting for corroboration; client should retry later
//   { status: "already_scored" }                    — idempotent no-op
//   { status: "rejected", reason }                   — failed validation / consensus

const { DataApi } = require("@unity-services/cloud-save-1.4");

// ── Leaderboard IDs (must match ArenaLeaderboardService.cs) ──
const WEEKLY_LEADERBOARD_ID = "arena_weekly";
const ALLTIME_LEADERBOARD_ID = "arena_alltime";

const BRACKET_SIZE = 32;

// Minimum number of real players (including the submitter) whose independently
// computed standings must AGREE before a result is trusted. Brackets whose real
// player count is below this can never reach quorum, so they fall back to
// single-submission trust (flagged in logs) — a lone real player beating 31 bots.
const CONSENSUS_QUORUM = 2;

module.exports = async ({ params, context, logger }) => {
  const { projectId, playerId } = context;
  const { weekId, tournamentId, entryId, placement, standingsJson } = params;

  // ── Parameter validation ──
  if (!weekId || typeof weekId !== "string") {
    return { status: "rejected", reason: "Invalid week ID." };
  }
  if (!tournamentId || typeof tournamentId !== "string") {
    return { status: "rejected", reason: "Invalid tournament ID." };
  }
  if (!entryId || typeof entryId !== "string") {
    return { status: "rejected", reason: "Invalid entry ID." };
  }
  if (
    typeof placement !== "number" ||
    placement < 1 ||
    placement > BRACKET_SIZE
  ) {
    return { status: "rejected", reason: "Invalid placement." };
  }
  if (!standingsJson || typeof standingsJson !== "string") {
    return { status: "rejected", reason: "Standings are required." };
  }

  let standings;
  try {
    standings = JSON.parse(standingsJson);
  } catch (e) {
    return { status: "rejected", reason: "Malformed standings." };
  }
  if (!Array.isArray(standings) || standings.length === 0) {
    return { status: "rejected", reason: "Standings must be a non-empty array." };
  }

  const cloudSave = new DataApi(context);

  // ── Idempotency: has this player already been scored for the week? ──
  const scoredEntity = `tournament_scored_${weekId}`;
  if (await hasItem(cloudSave, projectId, scoredEntity, playerId, logger)) {
    return { status: "already_scored" };
  }

  // ── Authority check: the player must be mapped to THIS tournament by the
  //    server-authored bracket assignment, with THIS entryId. ──
  const mapEntity = `tournament_player_map_${weekId}`;
  const mapping = await getItem(cloudSave, projectId, mapEntity, playerId, logger);
  if (!mapping) {
    return { status: "rejected", reason: "You are not registered for this week." };
  }
  let mapObj;
  try {
    mapObj = JSON.parse(mapping);
  } catch (e) {
    return { status: "rejected", reason: "Server mapping unreadable." };
  }
  if (mapObj.tournamentId !== tournamentId || mapObj.entryId !== entryId) {
    return { status: "rejected", reason: "Tournament/entry mismatch." };
  }

  // ── Internal consistency: the claimed placement must match the position of
  //    the player's own entryId inside the submitted standings. ──
  const claimedIndex = standings.indexOf(entryId);
  if (claimedIndex === -1) {
    return { status: "rejected", reason: "Your entry is absent from the standings." };
  }
  if (claimedIndex + 1 !== placement) {
    return {
      status: "rejected",
      reason: "Claimed placement disagrees with submitted standings.",
    };
  }

  // ── Store this submission (keyed by playerId in the shared results entity) ──
  const standingsHash = hashString(JSON.stringify(standings));
  const resultsEntity = `tournament_results_${weekId}`;
  try {
    await cloudSave.setPrivateCustomItem(projectId, resultsEntity, {
      key: playerId,
      value: JSON.stringify({
        tournamentId,
        standingsHash,
        placement,
        entryId,
        submittedUtc: Math.floor(Date.now() / 1000),
      }),
    });
  } catch (e) {
    logger.error(`Failed to store result for ${playerId}: ${e.message}`);
    return { status: "rejected", reason: "Server error saving result." };
  }

  // ── Gather all submissions for this tournament and tally standings hashes ──
  const allResults = await getAllItems(cloudSave, projectId, resultsEntity, logger);
  let matchingVotes = 0;
  const hashVotes = {};
  for (const r of allResults) {
    let obj;
    try {
      obj = JSON.parse(r.value);
    } catch (e) {
      continue;
    }
    if (obj.tournamentId !== tournamentId) continue;
    hashVotes[obj.standingsHash] = (hashVotes[obj.standingsHash] || 0) + 1;
    if (obj.standingsHash === standingsHash) matchingVotes++;
  }

  // Plurality hash across all real submissions for this bracket.
  let bestHash = null;
  let bestVotes = 0;
  for (const h of Object.keys(hashVotes)) {
    if (hashVotes[h] > bestVotes) {
      bestVotes = hashVotes[h];
      bestHash = h;
    }
  }

  // How many real players are in this bracket? Small brackets can never reach
  // quorum, so we allow single-submission trust for them (but log it).
  const realPlayerCount = await getBracketRealPlayerCount(
    cloudSave,
    projectId,
    weekId,
    tournamentId,
    logger,
  );
  const effectiveQuorum = Math.min(
    CONSENSUS_QUORUM,
    Math.max(1, realPlayerCount),
  );

  // The submitter's standings must be the plurality AND meet quorum.
  const consensusReached =
    bestHash === standingsHash && matchingVotes >= effectiveQuorum;

  if (!consensusReached) {
    // Either the submitter disagrees with the plurality (likely forged), or we
    // don't yet have enough corroborating submissions.
    if (bestHash !== standingsHash && bestVotes >= effectiveQuorum) {
      // A different standings already reached quorum → this submission is a
      // minority (probably tampered). Reject outright.
      logger.warn(
        `Player ${playerId} standings disagree with quorum for ${tournamentId} (their votes=${matchingVotes}, quorum hash votes=${bestVotes}).`,
      );
      return { status: "rejected", reason: "Result did not match bracket consensus." };
    }
    // Not enough corroboration yet — keep the submission and ask the client to retry.
    return {
      status: "pending",
      have: matchingVotes,
      need: effectiveQuorum,
    };
  }

  if (realPlayerCount < CONSENSUS_QUORUM) {
    logger.info(
      `Bracket ${tournamentId} has ${realPlayerCount} real player(s); scoring ${playerId} on single-submission trust.`,
    );
  }

  // ── Consensus reached (or tiny-bracket fallback): server writes the score. ──
  const weeklyScore = BRACKET_SIZE + 1 - placement; // 1st → 32, 32nd → 1

  try {
    await writeLeaderboardScore(context, projectId, WEEKLY_LEADERBOARD_ID, playerId, weeklyScore, logger);
  } catch (e) {
    logger.error(`Weekly score write failed for ${playerId}: ${e.message}`);
    return { status: "rejected", reason: "Server error writing score." };
  }

  // ── Authoritative all-time championship counter (placement 1 only) ──
  if (placement === 1) {
    try {
      const champCount = await bumpChampionships(cloudSave, projectId, playerId, logger);
      await writeLeaderboardScore(
        context,
        projectId,
        ALLTIME_LEADERBOARD_ID,
        playerId,
        champCount,
        logger,
      );
    } catch (e) {
      logger.error(`All-time write failed for ${playerId}: ${e.message}`);
      // Weekly already written; do not fail the whole call for the all-time bump.
    }
  }

  // ── Mark scored so re-submits are no-ops ──
  try {
    await cloudSave.setPrivateCustomItem(projectId, scoredEntity, {
      key: playerId,
      value: "done",
    });
  } catch (e) {
    logger.error(`Failed to mark ${playerId} scored: ${e.message}`);
  }

  logger.info(
    `Scored ${playerId}: ${tournamentId} placement ${placement} → weekly ${weeklyScore}.`,
  );
  return { status: "scored", placement, score: weeklyScore };
};

// ═══════════════════════════════════════════════════════════════
//  Leaderboard write — VERIFY THIS AGAINST YOUR INSTALLED SDK VERSION.
//  The UGS Leaderboards Cloud Code SDK is versioned; the method name and
//  argument shape below match @unity-services/leaderboards-1.x. If your project
//  uses a different major version, adjust here (this is the ONLY place the
//  leaderboard write happens).
// ═══════════════════════════════════════════════════════════════

async function writeLeaderboardScore(
  context,
  projectId,
  leaderboardId,
  playerId,
  score,
  logger,
) {
  const { LeaderboardsApi } = require("@unity-services/leaderboards-1.4");
  const leaderboards = new LeaderboardsApi(context);
  await leaderboards.addLeaderboardPlayerScore(projectId, leaderboardId, playerId, {
    score,
  });
}

// ═══════════════════════════════════════════════════════════════
//  Cloud Save helpers
// ═══════════════════════════════════════════════════════════════

async function bumpChampionships(cloudSave, projectId, playerId, logger) {
  const entity = "arena_championships";
  let count = 0;
  const existing = await getItem(cloudSave, projectId, entity, playerId, logger);
  if (existing != null) {
    const n = parseInt(existing, 10);
    if (!isNaN(n) && n > 0) count = n;
  }
  count += 1;
  await cloudSave.setPrivateCustomItem(projectId, entity, {
    key: playerId,
    value: String(count),
  });
  return count;
}

async function getBracketRealPlayerCount(cloudSave, projectId, weekId, tournamentId, logger) {
  const bracketsEntity = `tournament_brackets_${weekId}`;
  const raw = await getItem(cloudSave, projectId, bracketsEntity, tournamentId, logger);
  if (raw == null) return 0;
  try {
    const b = JSON.parse(raw);
    if (typeof b.realPlayerCount === "number") return b.realPlayerCount;
    if (Array.isArray(b.realEntries)) return b.realEntries.length;
  } catch (e) {
    logger.error(`Bad bracket data for ${tournamentId}: ${e.message}`);
  }
  return 0;
}

async function hasItem(cloudSave, projectId, entity, key, logger) {
  return (await getItem(cloudSave, projectId, entity, key, logger)) != null;
}

async function getItem(cloudSave, projectId, entity, key, logger) {
  try {
    const res = await cloudSave.getPrivateCustomItems(projectId, entity);
    if (res?.data?.results) {
      const item = res.data.results.find((r) => r.key === key);
      if (item) return item.value;
    }
  } catch (e) {
    const status = e?.response?.status || e?.status;
    if (status !== 404) {
      logger.error(`getItem(${entity},${key}) error: ${e.message}`);
    }
  }
  return null;
}

async function getAllItems(cloudSave, projectId, entity, logger) {
  try {
    const res = await cloudSave.getPrivateCustomItems(projectId, entity);
    if (res?.data?.results) return res.data.results;
  } catch (e) {
    const status = e?.response?.status || e?.status;
    if (status !== 404) {
      logger.error(`getAllItems(${entity}) error: ${e.message}`);
    }
  }
  return [];
}

// Deterministic 32-bit string hash (Java hashCode equivalent) — matches the
// hashing style already used in LockAndAssignBrackets.js.
function hashString(str) {
  let hash = 0;
  for (let i = 0; i < str.length; i++) {
    hash = (Math.imul(31, hash) + str.charCodeAt(i)) | 0;
  }
  return hash;
}
