## Plan: Job-Linked Stock Market V1

Add a lightweight stock market loop that uses existing economy primitives: hourly price ticks (offline-safe), buy/sell portfolio, and dividend payouts. Keep jobs as indirect input (earn/spend existing currency), avoid complex financial mechanics, and persist all market state through the current save pipeline with migration-safe schema additions.

**Steps**
1. Phase 1 — Domain model and save schema (blocks all later phases)
	- Add stock market save data structures to the unified save model (market clock, per-stock current price, optional short history, player holdings, average cost, pending/last dividend payout).
	- Extend save version and add additive migration defaults so existing saves load without market data loss.
	- Define static stock definitions in ScriptableObject(s): id, display name, base price, volatility band, dividend yield, optional category.
2. Phase 2 — Market simulation and offline reconciliation (depends on 1)
	- Implement StockMarketManager that calculates hourly updates from SaveManager.NowUnix() and reconciles missed ticks on load.
	- Use deterministic tick stepping (whole-hour boundaries) so prices are reproducible and not frame-rate dependent.
	- Apply bounded random walk per stock (respect min/max clamps) and store resulting current prices.
3. Phase 3 — Trading and dividends (depends on 2)
	- Implement buy/sell transaction methods with validation (non-negative quantity, sufficient funds, sufficient shares).
	- Reuse existing currency API for all money movement (deduct/add via ResourceBank for Credits).
	- Implement dividend accrual/payout on cadence (hourly or every N ticks), based on holdings at payout time and per-stock yield.
4. Phase 4 — Catch/Battle integration (depends on 3)
	- Add a stock-affinity multiplier layer on battle/catch Credits rewards at the central credits grant point (reuse existing multiplier stacking pattern with titles).
	- Drive affinity from existing monster metadata (type/rarity, optionally personality) so players can intentionally build teams for “market-favored” hunts.
	- Keep multiplier modest and bounded so market play is meaningful without invalidating core combat economy.
5. Phase 5 — Jobs integration (parallel with 4 after 2 is stable)
	- Keep jobs unchanged mechanically; define jobs as the stable income source that funds market positions.
	- Surface “investable Credits” in stock panel to reinforce jobs → invest loop.
6. Phase 6 — UX motivation and feedback surfaces (depends on 4)
	- Add reward breakdown lines to post-battle and idle reward summaries that explicitly show stock-related bonus contribution.
	- Add simple “Why Stocks?” onboarding copy (one short tooltip/tutorial card) explaining: hunt certain monsters → earn better Credits → invest for dividends.
	- Ensure every stock-relevant gain event has visible attribution (source tag: battle bonus vs dividend vs trade).
7. Phase 7 — UI panel and interaction flow (depends on 3 and 6)
	- Add a dedicated stock panel through existing UIManager panel routing.
	- Provide minimal views: stock list (name, current price, 1h delta), holdings summary (shares, avg cost, unrealized P/L), buy/sell controls, next dividend/next price tick timer.
	- Hook refresh to panel-open and market/resource change events.
8. Phase 8 — Balance pass and hardening (depends on 2–7)
	- Tune volatility, dividend yield, stock-affinity reward multipliers, and position limits to prevent runaway economy inflation.
	- Add safeguards: max shares per stock/account, transaction caps, anti-negative overflow checks.

**Relevant files**
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Save/SaveManager.cs — add schema version, migration defaults, save/load wiring for market data.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Managers/PlayerManager.cs — extend player save root model for holdings/market fields if this is the current canonical data container.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Resources/ResourceBank.cs — reuse Add/Get/Set and batching for transaction and dividend money flows.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/UI/UIManager.cs — register new PanelId and panel behavior.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/UI/PanelButtonUI.cs — hook entry button behavior if a new stock button is added.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Jobs/JobManager.cs — reference-only for cadence/event patterns; no core job-loop rewrite in V1.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Monster/Packs/MonsterPackSeasonRotationSO.cs — reference pattern for deterministic time-index logic.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/World Event/WorldEventSystem.cs — reference pattern for scheduled state and cooldown-like timing.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Resources/ResourceManager.cs — primary injection point for battle/catch credit multipliers and title-style stacking.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Battle/Static/BattleRewards.cs — base reward math and rarity scaling reference.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Encounter/Core/EncounterManager.BattleFlow.cs — battle outcome grant path and result payload construction.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Battle/PostBattleSummaryManager.cs — reward breakdown payload extension point.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Battle/UI/PostBattleSummaryPanelUI.cs — visible explanation of stock-linked reward bonuses.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Idle/IdleBattleRewardPanelUI.cs — offline/idle reward attribution for stock-linked gains.
- /Users/kareem/Desktop/Southside Games/Bitlings/Assets/Scripts/Monster/MonsterDataSO.cs — monster rarity/type metadata source for stock affinity mapping.

**Verification**
1. Save migration check: load a pre-market save and verify no errors, market data initializes with defaults, and save reopens cleanly.
2. Hourly tick check: advance device/editor time (or debug time offset) by 1h/6h/24h and confirm deterministic price progression and reconciliation on reload.
3. Transaction check: validate buy/sell constraints, correct Credits deltas, and persistence after app restart.
4. Dividend check: hold shares across payout boundary and verify exact payout amount, no double-payout on repeated reload.
5. Battle/catch reward attribution check: complete battles with different monster type/rarity setups and verify stock-affinity bonus appears in reward breakdown and matches expected multiplier math.
6. Idle/offline attribution check: run idle battles, reopen reward panel, and verify stock-linked reward contributions are labeled and not double-counted.
7. UI sync check: opening/closing panel, collecting job income, and making trades all refresh values without stale data.
8. Economy sanity check: run a long offline simulation (e.g., 72h) and verify no negative balances, overflow, or runaway multipliers, including stacked title + stock bonuses.

**Decisions**
- Included: buy/sell, hourly market updates, dividends, Credits-funded investing, offline-safe reconciliation, minimal dedicated UI panel, and battle/catch stock-affinity reward attribution.
- Excluded (V1): shorting/options, direct job-to-stock stat modifiers, worker-assignment sector buffs, advanced charting/analytics, and separate stock-only combat mode.
- Persistence approach: integrate into existing unified save with additive migration, not a separate parallel save file.

**Further Considerations**
1. Dividend cadence choice: hourly micro-dividends (smoother feedback) vs daily lump sum (cleaner UX). Recommendation: daily lump sum for easier player understanding.
2. Number of stocks in V1: 4–6 (readable) vs 8–12 (depth). Recommendation: start with 5.
3. Price floor/ceiling policy: hard global clamps vs per-stock clamps. Recommendation: per-stock clamps from ScriptableObject definitions.
