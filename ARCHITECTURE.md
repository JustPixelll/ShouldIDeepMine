# Architecture

## Official-side Should I?

- Calculates Sell / Buy / Craft / Gather / Opportunities / Tycoon analytics.
- Uses Universalis and static game data.
- Passively observes Market Board packets produced by ordinary player interactions.
- Contains no queued native ItemSearch request engine.
- Receives optional Deep Mine snapshots through IPC.
- Exposes safe local owned/listing item-ID scopes to Deep Mine.

## Experimental-side Should I Deep Mine?

1. User explicitly picks a scan scope.
2. Scope resolves to item IDs locally or via Should I IPC.
3. Queue requests one item at a time through the native ItemSearch proxy.
4. Dalamud MarketBoard events are captured.
5. Completed item snapshots are cached locally.
6. Snapshot is published to Should I through `ShouldIDeepMine.SnapshotUpdated.v1`.

There is no shared database file and no cross-plugin file mutation. Each plugin owns its own storage; IPC is the integration boundary.
