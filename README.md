<p align="center">
  <img src="images/icon.png" width="170" alt="Should I Deep Mine? icon">
</p>

<h1 align="center">Should I Deep Mine?</h1>

<p align="center">
  <strong>Smart native Market Board evidence for Should I?</strong>
</p>

<p align="center">
  An intentionally experimental Dalamud companion for user-started native FFXIV Market Board verification, full scans, stale-data maintenance and reusable update lists.
</p>

<p align="center">
  <img alt="Dalamud API 15" src="https://img.shields.io/badge/Dalamud-API%2015-6f42c1">
  <img alt="Experimental companion" src="https://img.shields.io/badge/status-experimental-orange">
  <a href="https://github.com/JustPixelll/ShouldIDeepMine/actions/workflows/build.yml"><img alt="Build" src="https://github.com/JustPixelll/ShouldIDeepMine/actions/workflows/build.yml/badge.svg"></a>
</p>

---

## What is Deep Mine?

**Should I?** is the normal economy decision-support plugin. It evaluates Sell, Buy, Craft, Gather and Tycoon information using Universalis, game data, inventory observations and the user's own trading history.

**Should I Deep Mine?** is the experimental native-data sidecar. It can ask FFXIV for current Market Board data through the game's own ItemSearch path, cache the resulting snapshots locally, and publish them back to Should I? over versioned Dalamud IPC.

The split is deliberate:

- Should I? never needs Deep Mine to function.
- Should I? does not contain a queued native ItemSearch engine.
- Should I? may expose **read-only candidate hints** describing which market facts would be useful.
- Deep Mine never starts a scan merely because a plugin loaded or a candidate exists.
- **The user always starts the native scan in Deep Mine.**
- Deep Mine does not buy, sell, list, cancel or reprice anything.

Deep Mine is custom-repository / experimental software and is **not intended for the official Dalamud plugin list**.

---

## v0.3: Market-data workstation

Deep Mine now has seven main areas:

| Tab | Purpose |
|---|---|
| **Dashboard** | Current native-data health, last run and quick Total verification |
| **Smart Scan** | Should I?-guided Total, Sell, Buy MB, Buy Vendor, Craft and Gather plans |
| **Full Scan** | Explicit owned, inventory, retainer, category, custom-ID and entire-market scopes |
| **Stale Data** | Refresh missing or old native snapshots by a simple age threshold |
| **Update Lists** | Save named reusable item-ID lists and run them whenever you want |
| **Data Library** | Browse cached native snapshots and refresh individual items |
| **Settings** | Request spacing, retry policy, smart budget, freshness and maintenance controls |

### Smart scans intentionally stay simple

The smart planner is not meant to become an opaque optimizer.

1. Should I? provides already-ranked candidate hints.
2. Deep Mine removes duplicate item IDs.
3. Recent native snapshots are skipped according to your freshness window.
4. The remaining items are capped by your request budget.
5. You inspect the plan and explicitly start it.

That is enough to avoid obvious waste without trying to turn a 30-item queue into 25 through complicated math.

---

## Smart Scan modules

### Total

Cross-module verification. If the same item matters to Sell, Buy, Craft and Gather, it is still queried once.

### Sell

Prioritizes current listings and Should I?'s strongest owned-item Sell candidates.

### Buy MB

Verifies current Should I Buy? Market Board opportunities before you commit gil.

### Buy Vendor

Verifies the market **exit** side of vendor-to-market opportunities. Vendor acquisition prices themselves come from static game data and do not require a native Market Board query.

### Craft

Starts with promising craft outputs and also includes a small number of the largest Market Board input-cost drivers for those crafts.

### Gather

Verifies the market value behind the strongest current Should I Gather? candidates.

Smart plans use a configurable native-data freshness window and request budget. Full scans ignore both because they are explicit coverage requests.

---

## Full Scan

Full scans remain first-class. Sometimes you simply want coverage rather than a recommendation-aware subset.

Available scopes include:

- Should I? — all known owned items
- Should I? — current listings
- player inventory
- player inventory + saddlebags
- active retainer inventory
- active retainer listings
- all currently loaded supported containers
- one FFXIV ItemUICategory up to the configured category cap
- explicit custom item IDs
- **entire marketable FFXIV item catalog**

The full-market catalog scan is deliberately behind an explicit confirmation in the UI because it can create a very large queue. It is never selected by Smart Scan.

---

## Stale Data

The Stale Data tab deliberately uses understandable rules instead of a predictive model.

Choose an age threshold and build a maintenance queue from:

- owned items
- current listings
- current Should I? smart candidates
- all cached native snapshots already older than the threshold

You can optionally include items that have never been scanned natively.

---

## Update Lists

Reusable update lists are static named item-ID sets stored in Deep Mine's normal plugin configuration.

Examples:

- Raid Consumables
- Materia
- Housing
- FC Workshop
- Personal Flips
- Rare Glamour

Create, edit, run and delete lists from the **Update Lists** tab. They intentionally remain simple so more dynamic list types can be added later without locking the feature into a complicated rule system now.

---

## Data Library

Deep Mine keeps completed native snapshots in its own local cache. The Data Library lets you search the current world's cache by item name or ID and see:

- snapshot age
- current listing count
- captured sale-history count
- a one-item **Scan Now** action

Should I? can synchronize and consume these snapshots as additional `LiveGame` evidence.

---

## Request behavior

Deep Mine queues one item at a time through FFXIV's native ItemSearch path.

v0.3 uses more conservative defaults:

- default request spacing: **2500 ms**
- default maximum attempts: **2**
- failed attempts back off before retrying
- repeated failure of an item produces a longer cooldown before the next item
- a snapshot is published only after Deep Mine has observed both **history** and **offerings** for the expected item
- empty offering books are associated with the explicitly expected request item rather than blindly relying on the prior history packet

These controls live in Deep Mine rather than Should I?.

---

## Installation

Add this URL under **Dalamud Settings → Experimental → Custom Plugin Repositories**:

```text
https://raw.githubusercontent.com/JustPixelll/ShouldIDeepMine/main/pluginmaster.json
```

Save the settings, open `/xlplugins`, search for **Should I Deep Mine?**, and install it.

For module-aware smart scans, use a Should I? build that exposes `ShouldI.ExternalMarketData.GetSmartCandidates.v1`. Deep Mine still works without it for manual/full scopes and falls back to owned items for basic Sell/Total planning where possible.

---

## Chat commands

Open the main workstation with:

```text
/deepmine
```

Smart verification:

| Command | Action |
|---|---|
| `/deepmine smart` | Build and start Total smart verification |
| `/deepmine smart sell` | Sell candidates |
| `/deepmine smart buymb` | Market Board buy candidates |
| `/deepmine smart buyvendor` | Vendor-to-market candidates |
| `/deepmine smart craft` | Craft outputs / major MB inputs |
| `/deepmine smart gather` | Gather candidates |

Explicit scopes:

| Command | Action |
|---|---|
| `/deepmine all` | All marketable items Should I? currently knows you own |
| `/deepmine listings` | Should I?'s known current listings |
| `/deepmine inventory` | Loaded player inventory |
| `/deepmine saddlebags` | Player inventory + loaded saddlebags |
| `/deepmine retainer` | Active retainer inventory |
| `/deepmine retainerlistings` | Active retainer listings |
| `/deepmine loaded` | All currently loaded supported containers |
| `/deepmine category <id or name>` | One FFXIV item UI category |
| `/deepmine items <id> <id> ...` | Explicit item IDs |
| `/deepmine status` | Show engine status and open the window |
| `/deepmine stop` | Stop the active queue |
| `/deepmine help` | Print command reference |

The entire-market catalog scan is UI-only so the very large scope always receives an explicit on-screen confirmation.

---

## Should I? IPC boundary

Deep Mine publishes:

- `ShouldI.ExternalMarketData.SnapshotUpdated.v1`
- `ShouldI.ExternalMarketData.GetSnapshots.v1`

Should I? exposes:

- `ShouldI.ExternalMarketData.GetOwnedMarketableItemIds.v1`
- `ShouldI.ExternalMarketData.GetCurrentListingItemIds.v1`
- `ShouldI.ExternalMarketData.GetSmartCandidates.v1`

The smart-candidate payload contains read-only fields such as module, item ID, item name, priority, reason, opportunity score, confidence and known market freshness.

The direction remains important: **Should I? exposes facts and candidate hints; Deep Mine's user decides whether to send native requests.**

---

## Build

The project targets Dalamud API 15 and .NET 10.

```powershell
dotnet restore .\ShouldIDeepMine\ShouldIDeepMine.csproj
dotnet build .\ShouldIDeepMine\ShouldIDeepMine.csproj --configuration Release --no-restore
```

GitHub Actions performs the same Release build for pull requests.

---

## Related project

**Should I?** — economy decision/analytics plugin:

https://github.com/JustPixelll/ShouldISell
