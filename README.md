<p align="center">
  <img src="images/icon.png" width="170" alt="Should I Deep Mine? icon">
</p>

<h1 align="center">Should I Deep Mine?</h1>

<p align="center">
  <strong>Explicit deep Market Board scans for Should I?</strong>
</p>

<p align="center">
  An intentionally experimental Dalamud companion that mines native FFXIV Market Board snapshots only when you tell it exactly what to scan.
</p>

<p align="center">
  <img alt="Dalamud API 15" src="https://img.shields.io/badge/Dalamud-API%2015-6f42c1">
  <img alt="Experimental companion" src="https://img.shields.io/badge/status-experimental-orange">
  <a href="https://github.com/JustPixelll/ShouldIDeepMine/actions/workflows/build.yml"><img alt="Build" src="https://github.com/JustPixelll/ShouldIDeepMine/actions/workflows/build.yml/badge.svg"></a>
</p>

---

## What is Deep Mine?

**Should I?** is the normal economy-analysis plugin. It uses Universalis, inventory data, passive Market Board observations and your own trading history to answer questions such as *Should I sell this?*, *Should I buy this?*, *Should I craft this?* and *what should I do next?*

**Should I Deep Mine?** is deliberately separate. Its only job is to perform **user-started, explicitly scoped native Market Board searches** and hand the completed snapshots back to Should I? over Dalamud IPC.

That separation is intentional:

- Should I? never needs Deep Mine to function.
- Deep Mine never starts a scan merely because the plugin loaded.
- You choose the exact scope before a queue exists.
- The experimental request engine and pacing controls live here, not inside Should I?.
- Should I? consumes completed snapshots as additional evidence; it does not remotely command Deep Mine to scan.

Deep Mine is custom-repository / experimental software and is **not intended for the official Dalamud plugin list**.

---

## Installation

Add the following URL under **Dalamud Settings → Experimental → Custom Plugin Repositories**:

```text
https://raw.githubusercontent.com/JustPixelll/ShouldIDeepMine/main/pluginmaster.json
```

Save the settings, open `/xlplugins`, search for **Should I Deep Mine?**, and install it.

For the full workflow, install **Should I?** as well. Deep Mine can run independently, but its Should-I-specific scopes and snapshot consumption are most useful when both plugins are enabled.

---

## Using the window

Open the control window with:

```text
/deepmine
```

Available scopes include:

| Scope | What it scans |
|---|---|
| **Should I? — All Known Owned** | Marketable item IDs Should I? currently knows you own |
| **Should I? — Current Listings** | Item IDs Should I? currently knows you have listed |
| **Player Inventory** | Currently loaded normal player inventory |
| **Player + Saddlebags** | Player inventory plus loaded saddlebag containers |
| **Active Retainer Inventory** | The currently loaded retainer inventory |
| **Active Retainer Listings** | The active retainer's loaded Market Board listing container |
| **All Currently Loaded Containers** | Every supported inventory container currently available to the client |
| **FFXIV Item Category** | One selected Item UI category, up to your configured cap |
| **Custom Item IDs** | A hand-written set of exact item IDs |

The window also exposes request spacing, response timeout, retry count and category-size cap. Those controls intentionally stay in Deep Mine.

---

## Chat commands

Every common scope can be started without opening the window:

| Command | Action |
|---|---|
| `/deepmine all` | Scan all marketable items Should I? currently knows you own |
| `/deepmine listings` | Scan Should I?'s known current listings |
| `/deepmine inventory` | Scan loaded player inventory |
| `/deepmine saddlebags` | Scan player inventory + loaded saddlebags |
| `/deepmine retainer` | Scan the active retainer inventory |
| `/deepmine retainerlistings` | Scan the active retainer's listings |
| `/deepmine loaded` | Scan all currently loaded supported containers |
| `/deepmine category <id or name>` | Scan one FFXIV item UI category |
| `/deepmine items <id> <id> ...` | Scan explicit item IDs |
| `/deepmine status` | Show the current engine status and open the window |
| `/deepmine stop` | Stop the active queue |
| `/deepmine help` | Print the command reference |

Useful aliases such as `owned`, `listed`, `inv`, `bags`, `cat`, `ids`, `cancel` and `allloaded` are also accepted.

If Should I? is not available, the `all`/`listings` scopes fall back to data Deep Mine can currently see locally rather than fabricating a remembered inventory.

---

## How the Should I? connection works

The two plugins communicate through a deliberately tiny, versioned IPC contract using primitive/JSON payloads so neither repository depends on the other's binary assembly.

Deep Mine publishes:

- `ShouldI.ExternalMarketData.SnapshotUpdated.v1` — completed snapshot JSON
- `ShouldI.ExternalMarketData.GetSnapshots.v1` — cached snapshot JSON collection

Should I? exposes safe scope helpers:

- `ShouldI.ExternalMarketData.GetOwnedMarketableItemIds.v1`
- `ShouldI.ExternalMarketData.GetCurrentListingItemIds.v1`

The direction matters: **Should I? provides IDs and consumes completed data; it does not instruct Deep Mine to begin a scan.**

---

## What happens during a scan?

1. You choose a scope or run a command.
2. Deep Mine resolves that scope to unique marketable item IDs.
3. The queue walks those IDs through FFXIV's native ItemSearch path using the configured spacing/retry policy.
4. Completed listing/history snapshots are cached locally.
5. When Should I? is present, Deep Mine publishes the fresh snapshot over IPC.
6. Should I? can then use that native snapshot as fresher evidence alongside its normal Universalis/passive data.

A scan can take a long time by design. Deep Mine favors an explicit, paced queue over trying to make native searches resemble a bulk web API.

---

## Safety / expectations

- **Nothing scans automatically at startup.**
- Deep Mine does not buy, sell, list, cancel or reprice anything.
- It does not make economic decisions; Should I? does that.
- Native Market Board behavior can change after FFXIV updates, so this companion should be treated as experimental.
- Stop a queue at any time with `/deepmine stop` or the window button.
- Use sensible scopes rather than scanning the entire catalog when you only need a subset.

---

## Build

The project targets Dalamud API 15 and .NET 10.

With Dalamud development files installed in XIVLauncher's normal development path:

```powershell
dotnet restore .\ShouldIDeepMine\ShouldIDeepMine.csproj
dotnet build .\ShouldIDeepMine\ShouldIDeepMine.csproj --configuration Release --no-restore
```

CI performs the same Release build before changes are merged.

---

## Related project

**Should I?** — the economy decision/analytics plugin:

https://github.com/JustPixelll/ShouldISell

Issues and reproducible bug reports for Deep Mine belong in this repository so its experimental scanner can evolve independently from Should I?.
