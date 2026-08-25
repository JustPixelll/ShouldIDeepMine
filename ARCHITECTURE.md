# Architecture

## Official-side Should I?

- Calculates Sell / Buy / Craft / Gather / Opportunities / Tycoon analytics.
- Uses Universalis, static game data, inventory observations and personal trading history.
- Passively observes Market Board packets produced by ordinary player interactions.
- Contains no queued native ItemSearch request engine.
- Receives optional Deep Mine snapshots through IPC.
- Exposes safe local owned/listing item-ID scopes to Deep Mine.
- Exposes read-only ranked smart-candidate hints through `ShouldI.ExternalMarketData.GetSmartCandidates.v1`.
- Never instructs Deep Mine to begin a native scan.

## Experimental-side Should I Deep Mine?

Deep Mine is split conceptually into three layers.

### 1. Candidate / scope sources

Sources answer: **what could be scanned?**

Examples:

- Should I? Total / Sell / Buy MB / Buy Vendor / Craft / Gather candidates
- all known owned items
- current listings
- loaded inventory / saddlebags / retainer containers
- FFXIV ItemUICategory
- entire marketable game catalog
- explicit item IDs
- stale/missing native data
- user-saved update lists

### 2. SmartScanPlanner

The planner intentionally stays simple and inspectable.

For a smart scan it:

1. reads Should I?'s already-ranked candidate hints;
2. deduplicates by item ID;
3. skips native snapshots inside the configured freshness window;
4. keeps the highest-ranked items up to the user's request budget;
5. returns a previewable queue plan.

It does **not** try to run a complex optimizer or remotely start the scanner.

Full/manual scans bypass this planning logic.

For stale-data maintenance it applies a simple age threshold and optional "never scanned" rule.

### 3. DeepScanEngine

The engine answers: **how is the explicitly approved queue executed?**

1. User starts a plan or full scope.
2. Queue requests one item at a time through `InfoProxyItemSearch`.
3. `MarketBoardObserver` is told which item is expected before each request.
4. Dalamud MarketBoard history and offering events are captured.
5. The engine waits for both history and offerings for the expected item.
6. Completed snapshots are cached locally by `DeepMinePublisher`.
7. Snapshot JSON is published to Should I? through `ShouldI.ExternalMarketData.SnapshotUpdated.v1`.
8. Failed requests back off before retrying / moving on.

## Storage

- Deep Mine owns `deep-mine-cache.json` inside its plugin config directory.
- Saved update lists live in Deep Mine's normal Dalamud plugin configuration.
- Should I? owns its own local stores.
- There is no shared database file and no cross-plugin file mutation.

## IPC contract

Deep Mine publishes:

- `ShouldI.ExternalMarketData.SnapshotUpdated.v1`
- `ShouldI.ExternalMarketData.GetSnapshots.v1`

Should I? exposes:

- `ShouldI.ExternalMarketData.GetOwnedMarketableItemIds.v1`
- `ShouldI.ExternalMarketData.GetCurrentListingItemIds.v1`
- `ShouldI.ExternalMarketData.GetSmartCandidates.v1`

IPC uses primitive/JSON payloads so neither repository takes a binary dependency on the other.

## Safety boundary

The architectural invariant is:

> Should I? may describe useful evidence. Deep Mine may collect native evidence. Only an explicit user action starts native collection.

Nothing in the smart candidate feed is a remote scan command.
