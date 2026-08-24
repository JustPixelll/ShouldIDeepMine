# Should I Deep Mine?

Experimental companion plugin for **Should I?**.

Deep Mine owns the deliberately experimental boundary: user-started queued native Market Board data collection. **Should I?** remains the analysis/product plugin and can consume Deep Mine snapshots without containing the queued request engine itself.

## Scan scopes

- Should I? — all known owned marketable items
- Should I? — current listed items
- Player inventory
- Player inventory + saddlebags
- Active retainer inventory
- Active retainer listings
- All currently loaded containers
- One FFXIV item UI category
- Custom item IDs

No scan begins automatically when the plugin loads. Starting one scope creates a queue; the engine then processes that queue using configurable spacing, timeout, and retry settings.

## IPC contract

Deep Mine publishes:

- `ShouldIDeepMine.SnapshotUpdated.v1` — one JSON snapshot message
- `ShouldIDeepMine.GetSnapshots.v1` — returns all cached JSON snapshots

Should I? may expose:

- `ShouldI.GetOwnedMarketableItemIds.v1`
- `ShouldI.GetCurrentListingItemIds.v1`

The shared contract intentionally uses JSON/primitive IPC types so the two repositories do not need a shared binary dependency.

## Build

With Dalamud development files installed in the normal XIVLauncher development path:

`dotnet build ./ShouldIDeepMine/ShouldIDeepMine.csproj --configuration Release`

## Distribution

This project is intentionally experimental/custom-repository territory. Keep it separate from the official Should I? submission and review its behavior independently.
