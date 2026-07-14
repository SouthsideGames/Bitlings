// RegisterForTournament.js
// Deploy to UGS Cloud Code: Dashboard → Cloud Code → Scripts → Create
//
// Registers a player for the current week's arena tournament.
// Stores the player's team snapshot + metadata in shared Game Data
// so it can be grouped into a bracket when registration closes.
//
// Game Data entities used:
//   "tournament_reg_{weekId}"  — key: playerId, value: JSON registration blob
//
// Parameters:
//   teamSnapshotJson (STRING) — JSON-serialized ArenaTeamSnapshot
//   arenaScore       (NUMERIC) — pre-computed team arena score
//   scoreBand        (NUMERIC) — 0=Low, 1=Standard, 2=High, 3=Elite
//   displayName      (STRING)  — player's arena username
//   weekId           (STRING)  — client-computed week ID (e.g., "W20260413")
//
// Returns:
//   { success: true, weekId }                     — registration stored
//   { success: false, error: "reason" }           — validation failure

const { DataApi } = require("@unity-services/cloud-save-1.4");

module.exports = async ({ params, context, logger }) => {
  const { projectId, playerId } = context;
  const { teamSnapshotJson, arenaScore, scoreBand, displayName, weekId } =
    params;

  // ── Validate parameters ──

  if (!teamSnapshotJson || typeof teamSnapshotJson !== "string") {
    return { success: false, error: "Team snapshot is required." };
  }

  if (typeof arenaScore !== "number" || arenaScore < 0) {
    return { success: false, error: "Invalid arena score." };
  }

  if (typeof scoreBand !== "number" || scoreBand < 0 || scoreBand > 3) {
    return { success: false, error: "Invalid score band." };
  }

  if (!displayName || typeof displayName !== "string") {
    return { success: false, error: "Display name is required." };
  }

  if (!weekId || typeof weekId !== "string" || !weekId.startsWith("W")) {
    return { success: false, error: "Invalid week ID." };
  }

  // ── Validate registration window ──

  const serverWeekId = getCurrentWeekId();
  if (weekId !== serverWeekId) {
    return {
      success: false,
      error: "Registration week mismatch. Please refresh.",
    };
  }

  if (!isRegistrationOpen()) {
    return { success: false, error: "Registration is currently closed." };
  }

  // ── Validate team snapshot parses ──

  let snapshot;
  try {
    snapshot = JSON.parse(teamSnapshotJson);
  } catch (e) {
    return { success: false, error: "Invalid team snapshot format." };
  }

  if (
    !snapshot.slotSnapshots ||
    !Array.isArray(snapshot.slotSnapshots) ||
    snapshot.slotSnapshots.length !== 3
  ) {
    return { success: false, error: "Team must have exactly 3 Bitlings." };
  }

  const cloudSave = new DataApi(context);

  // ── Recompute arena score + band SERVER-SIDE from the trusted catalogs. ──
  // The client-sent arenaScore / scoreBand (and the per-slot scores inside the
  // snapshot) are untrusted — a modified client could deflate them to sandbag
  // into a weaker bracket. We resolve each slot's monsterId / titleId against
  // the admin-uploaded catalog and recompute. If the catalog isn't uploaded yet,
  // we fall back to the client value and log a warning (safe to deploy first).
  let finalScore = arenaScore;
  let finalBand = scoreBand;

  const catalog = await loadCatalog(cloudSave, projectId, logger);
  if (catalog) {
    const recomputed = computeTeamScore(snapshot.slotSnapshots, catalog);
    if (recomputed != null) {
      finalScore = recomputed;
      finalBand = bandForScore(recomputed);
      if (finalScore !== arenaScore || finalBand !== scoreBand) {
        logger.warn(
          `Player ${playerId} score/band corrected: client(${arenaScore},${scoreBand}) → server(${finalScore},${finalBand}).`,
        );
      }
    }
  } else {
    logger.warn(
      "arena_catalog not uploaded — trusting client score/band. Run 'Tools → Arena → Upload Catalogs to Cloud'.",
    );
  }

  // ── Store registration ──

  const regEntity = `tournament_reg_${weekId}`;

  const registrationData = {
    playerId,
    displayName: displayName.trim(),
    teamSnapshotJson, // Store raw JSON to avoid re-serialisation issues
    arenaScore: finalScore,
    scoreBand: finalBand,
    registeredUtc: Math.floor(Date.now() / 1000),
  };

  try {
    await cloudSave.setPrivateCustomItem(projectId, regEntity, {
      key: playerId,
      value: JSON.stringify(registrationData),
    });
    logger.info(
      `Player ${playerId} registered for ${weekId} (band=${scoreBand}, score=${arenaScore})`,
    );
  } catch (e) {
    logger.error("Failed to store registration: " + e.message);
    return { success: false, error: "Server error saving registration." };
  }

  return { success: true, weekId };
};

// ═══════════════════════════════════════════════════════════════
//  Schedule helpers (ET timezone via Intl)
// ═══════════════════════════════════════════════════════════════

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

const DAY_MAP = { Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6, Sun: 0 };

function isRegistrationOpen() {
  const et = getETComponents();
  const dow = DAY_MAP[et.weekday];
  // Mon (1) or Tue (2)
  return dow === 1 || dow === 2;
}

function getCurrentWeekId() {
  const et = getETComponents();
  const year = parseInt(et.year);
  const month = parseInt(et.month) - 1;
  const day = parseInt(et.day);
  const dow = DAY_MAP[et.weekday]; // 0=Sun, 1=Mon, ... 6=Sat

  // Monday-based offset
  const mondayOffset = dow === 0 ? -6 : 1 - dow;
  const mondayDate = new Date(year, month, day + mondayOffset);

  const y = mondayDate.getFullYear();
  const m = String(mondayDate.getMonth() + 1).padStart(2, "0");
  const d = String(mondayDate.getDate()).padStart(2, "0");
  return `W${y}${m}${d}`;
}

// ═══════════════════════════════════════════════════════════════
//  Server-side arena scoring (ports ArenaScoreCalculator + BattleTypeChart).
//  Pure integer / threshold math — no floating-point battle simulation — so it
//  is safe and deterministic to reproduce here.
// ═══════════════════════════════════════════════════════════════

// Band thresholds — must match ArenaConstants.cs.
const BAND_STANDARD = 50;
const BAND_HIGH = 100;
const BAND_ELITE = 175;

function bandForScore(score) {
  if (score >= BAND_ELITE) return 3; // Elite
  if (score >= BAND_HIGH) return 2; // High
  if (score >= BAND_STANDARD) return 1; // Standard
  return 0; // Low
}

/**
 * Loads the admin-uploaded catalogs from Cloud Save. Returns
 * { monsters: {id→{score,type}}, titles: {id→score}, chart: {atk→{def→mult}} }
 * or null if not uploaded / unreadable.
 */
async function loadCatalog(cloudSave, projectId, logger) {
  try {
    const res = await cloudSave.getPrivateCustomItems(projectId, "arena_catalog");
    if (!res?.data?.results) return null;

    let monstersRaw = null;
    let titlesRaw = null;
    let chartRaw = null;
    for (const item of res.data.results) {
      if (item.key === "monsters") monstersRaw = item.value;
      else if (item.key === "titles") titlesRaw = item.value;
      else if (item.key === "typechart") chartRaw = item.value;
    }
    if (!monstersRaw || !titlesRaw) return null;

    const monstersArr = JSON.parse(monstersRaw).monsters || [];
    const titlesArr = JSON.parse(titlesRaw).titles || [];

    const monsters = {};
    for (const m of monstersArr) {
      if (m && m.id) monsters[m.id] = { score: m.arenaScore | 0, type: m.type | 0 };
    }
    const titles = {};
    for (const t of titlesArr) {
      if (t && t.titleId) titles[t.titleId] = t.arenaScore | 0;
    }

    const chart = {};
    if (chartRaw) {
      const entries = JSON.parse(chartRaw).entries || [];
      for (const e of entries) {
        if (!chart[e.attackerType]) chart[e.attackerType] = {};
        chart[e.attackerType][e.defenderType] = e.multiplier;
      }
    }

    return { monsters, titles, chart };
  } catch (e) {
    logger.error("loadCatalog failed: " + e.message);
    return null;
  }
}

/**
 * Recomputes the full team arena score from trusted catalog values.
 * Returns null if any slot's monster is not in the catalog (caller then keeps
 * the client value rather than mis-scoring).
 */
function computeTeamScore(slotSnapshots, catalog) {
  const types = [];
  let baseSum = 0;

  for (const slot of slotSnapshots) {
    if (!slot || !slot.monsterId) continue;
    const mon = catalog.monsters[slot.monsterId];
    if (!mon) return null; // unknown species → don't trust; fall back to client value
    baseSum += Math.max(0, mon.score);
    if (slot.titleId && catalog.titles[slot.titleId] != null) {
      baseSum += Math.max(0, catalog.titles[slot.titleId]);
    }
    types.push(mon.type);
  }

  return baseSum + computeTypeSynergyBonus(types, catalog.chart);
}

function typeMultiplier(chart, atk, def) {
  if (atk === def) return 1;
  if (chart[atk] && chart[atk][def] != null) return chart[atk][def];
  return 1;
}

// Attacker types that are super-effective (>1) against defenderType.
function getThreatTypes(chart, defenderType) {
  const threats = [];
  for (const atkKey of Object.keys(chart)) {
    const atk = parseInt(atkKey, 10);
    const row = chart[atkKey];
    if (row[defenderType] != null && row[defenderType] > 1) threats.push(atk);
  }
  return threats;
}

// Ports ArenaScoreCalculator.CalculateTypeSynergyBonus.
function computeTypeSynergyBonus(teamTypes, chart) {
  if (!teamTypes || teamTypes.length < 2) return 0;

  let totalThreats = 0;
  let coveredPoints = 0;
  const uncoveredCounts = {};

  for (let m = 0; m < teamTypes.length; m++) {
    const memberType = teamTypes[m];
    const threats = getThreatTypes(chart, memberType);

    for (const threat of threats) {
      totalThreats++;
      let fullCover = false;
      let partialCover = false;

      for (let a = 0; a < teamTypes.length; a++) {
        if (a === m) continue;
        const allyType = teamTypes[a];
        // Resist: threat attacking ally has multiplier < 1.
        if (typeMultiplier(chart, threat, allyType) < 1) {
          fullCover = true;
          break;
        }
        // Pressure: ally attacking threat has multiplier > 1.
        if (!partialCover && typeMultiplier(chart, allyType, threat) > 1) {
          partialCover = true;
        }
      }

      if (fullCover) coveredPoints += 1;
      else if (partialCover) coveredPoints += 0.5;
      else uncoveredCounts[threat] = (uncoveredCounts[threat] || 0) + 1;
    }
  }

  if (totalThreats === 0) return synergyTierFromPercent(1);

  const coverage = coveredPoints / totalThreats;
  let tier = synergyTierFromPercent(coverage);

  for (const k of Object.keys(uncoveredCounts)) {
    if (uncoveredCounts[k] >= 2) return Math.min(tier, 5);
  }
  return tier;
}

function synergyTierFromPercent(coverage) {
  if (coverage >= 0.85) return 15;
  if (coverage >= 0.65) return 10;
  if (coverage >= 0.4) return 5;
  return 0;
}
