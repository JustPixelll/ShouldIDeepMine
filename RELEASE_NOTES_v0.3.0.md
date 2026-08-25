# Should I Deep Mine? v0.3.0

Deep Mine is now a full native Market Board evidence workstation rather than a single scope queue.

## Smart Scan

New Should I?-guided scanners:

- Total
- Sell
- Buy MB
- Buy Vendor
- Craft
- Gather

Smart planning deliberately stays understandable:

1. Should I? exposes already-ranked read-only candidate hints.
2. Deep Mine removes duplicate item IDs.
3. Recent native snapshots are skipped.
4. The remaining queue is capped by the user's request budget.
5. The user previews and explicitly starts the scan.

No complicated optimizer is used, and Should I? cannot remotely start Deep Mine.

## Full Scan

Explicit full scopes remain first-class:

- all Should I?-known owned items
- current listings
- player inventory
- inventory + saddlebags
- active retainer inventory
- active retainer listings
- all currently loaded containers
- FFXIV item categories
- custom item IDs
- entire marketable FFXIV item catalog (with explicit confirmation)

## Stale Data

New maintenance queues can refresh:

- stale/missing owned-item snapshots
- stale/missing current listings
- stale/missing smart candidates
- all cached snapshots older than a chosen age

Items never scanned natively can optionally be included.

## Update Lists

Create reusable named static item-ID lists, edit them, and rerun them whenever needed.

## Data Library

Search the local native cache by item name or ID, inspect snapshot age/listing/history counts, and refresh individual items.

## Scanner reliability / pacing

- default request spacing increased to 2500 ms
- default maximum attempts reduced to 2
- retries now back off
- failures receive a longer cooldown before moving on
- completed snapshots require both history and offerings
- the observer tracks the explicitly expected item so empty offering books can be associated correctly

## Should I? IPC

New optional read-only channel:

`ShouldI.ExternalMarketData.GetSmartCandidates.v1`

Should I? provides candidate metadata only. Native requests remain entirely inside Deep Mine and require explicit user initiation.
