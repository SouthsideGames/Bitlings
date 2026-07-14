# Arena Server Authority — Known Gaps & Deployment Plan

Status: **open**. These issues cannot be fixed from the repo alone — they need new
Cloud Code endpoints deployed on the UGS dashboard and a coordinated client update
(old clients must keep working against currently-deployed scripts during rollout).

## Gap 1 — Leaderboard scores are client-written (forgeable)

The entire tournament is resolved on-device (`ArenaMatchResolver.ResolveRound`), and
the client then writes its own results straight to the global boards:

- `ArenaTournamentService` (submission after local resolution) calls
  `ArenaLeaderboardService.SubmitWeeklyPlacementAsync(placement)` and
  `SubmitAllTimeChampionshipsAsync(...)`.
- `ArenaLeaderboardService` calls `LeaderboardsService.Instance.AddPlayerScoreAsync`
  directly from the device.

A modified client (or a runtime hook) can submit `placement = 1` every week and an
arbitrary championship count. **The leaderboards are only as honest as the least
honest client.**

### Fix (server-side)

1. Add a Cloud Code endpoint (e.g. `SubmitTournamentResult.js`) that:
   - loads the locked bracket for the week (`LockAndAssignBrackets` already stores
     bracket state in Game Data),
   - re-resolves the bracket server-side from the frozen team snapshots using the
     same deterministic seed (the simulator is already seed-deterministic — port
     `ArenaBattleSimulator`'s resolution or just re-derive placement from the
     server-resolved bracket),
   - writes the score to `arena_weekly` / `arena_alltime` itself via the
     Leaderboards service API.
2. In the UGS dashboard, set both leaderboards to **server-authoritative writes
   only** so `AddPlayerScoreAsync` from clients is rejected.
3. Update `ArenaLeaderboardService` to call the endpoint instead of writing
   directly. Keep the direct write as fallback only while old scripts are live.

## Gap 2 — Bracket banding trusts client-computed score (sandbagging)

`RegisterForTournament.js` stores client-sent `arenaScore` / `scoreBand` verbatim.
A client can deflate its score to be seeded into a weaker bracket while its actual
snapshot stats stay strong.

Recomputing the score inside the endpoint from `teamSnapshotJson` is **not**
sufficient: the per-slot `monsterArenaScore` / `titleArenaScore` inside the
snapshot are also client-supplied, and they do not affect battle stats — so a
cheater can deflate those too without weakening the team.

### Fix (server-side)

1. Export a minimal server-side catalog (monsterId → arenaScore, type;
   titleId → arenaScore) as a Cloud Code module or Remote Config JSON. The source
   of truth is `Assets/CSV/Bitling Monsters.csv` + the title assets.
2. In `RegisterForTournament.js`, resolve each snapshot slot's `monsterId` /
   `titleId` against that catalog, recompute the team score (port
   `ArenaScoreCalculator.CalculateTypeSynergyBonus` — pure logic, no Unity deps)
   and derive the band server-side. Ignore the client-sent values (keep accepting
   them for old clients, but overwrite with server-computed ones).
3. While at it, validate snapshot stat totals against the catalog stat budgets so
   inflated-stat snapshots are rejected at registration rather than poisoning
   opponents' battles.

## Gap 3 — Future-week lock griefing (FIXED in repo, needs redeploy)

`GetTournamentBracket.js` lazy-locked whatever `weekId` the client sent, checking
only that it was Wednesday-or-later. Any client could therefore pre-lock FUTURE
weeks with zero registrations; the idempotent `tournament_lock_{weekId} = done`
status then made the real lock a no-op when that week arrived — no brackets for
anyone, every week, until the Game Data entities were manually deleted.

**Fixed in this repo** (both `GetTournamentBracket.js` and
`LockAndAssignBrackets.js` now reject any `weekId` other than the server-computed
current week), but the fix only takes effect once you **redeploy both scripts to
the UGS dashboard**. If you suspect this was ever exploited, check Cloud Save
Game Data for `tournament_lock_*` entities dated in the future and delete them.

## What was already fixed client-side (this branch)

- Speed-tie initiative bias in `ArenaBattleSimulator` (left slot always struck
  first on sustained ties) — ties now alternate, first tie decided by seeded RNG.
- `ResourceBank.Add/Set/TrySpend` now route `ArenaTicket` to its real store in
  `ArenaSaveData` instead of a dead `resourceCounts` slot.

These do not close Gaps 1–2; they are fairness/correctness fixes only.
