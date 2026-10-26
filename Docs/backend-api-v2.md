# DemoRestaurant Backend API Requirements v2 (Phase 1)

Date: 2026-10-25 Â· Owner: Bryan Â· Audience: backend team

The client now follows the reference game's rules. This document lists the backend APIs needed for Phase 1. It replaces the out-of-date parts of `Docs/backend-api-requests.md` (the 10/23 spec); anything not mentioned here stays as written there.

**Two requirements shape every API below:**

1. **Gameplay must be re-configurable later without new endpoints.** Every rule, number and mechanic choice lives in server config (a ruleset), not in code. Where a mechanic has alternative designs (e.g. buying indoor land vs. free room growth), the ruleset picks the mode and the API supports both.
2. **The shipped ruleset must match the reference game exactly.** The values in this document are that ruleset. Design will tune numbers later by editing config only.

---

## 1. Conventions (unchanged from 10/23)

| Item | Convention |
| --- | --- |
| Base path | `https://<project>.supabase.co/functions/v1/player-api/v1` |
| Envelope | Success `{ "data": ..., "requestId" }`; failure `{ "error": { "code", "message", "details" }, "requestId" }` |
| Auth | `apikey` + `Authorization: Bearer <access token>`. Player identity comes from the token only, never the body. |
| Idempotency | Every write touching coins, items, ingredients or XP carries a UUID `Idempotency-Key`. A replay returns the original result. |
| Server authority | Prices, rewards, odds and timers use only the server's config and clock. The client never sends prices, quantities, timestamps or random results. |
| Unknown fields | Rejected with `VALIDATION_ERROR`. |
| IDs | Lowercase ids matching the client content JSON (`stove_basic`, `nasi_lemak`). Never renamed once used; art swaps don't change ids. |
| Optimistic lock | Whole-document writes (layout, menu) carry `revision`; mismatch returns `REVISION_CONFLICT` and the client re-fetches. |

The client predicts locally (shows coins, places furniture at once) and then adopts the server's numbers. Offline, changes go into a local ledger that the server re-validates on sync (section 11).

---

## 2. Configurable rulesets (new, applies to everything)

### 2.1 Model

- A **ruleset** is a versioned JSON document: `{ "rulesetId": "malaysia", "version": 7, "modes": {...}, "rules": {...}, "economy": {...}, "catalog": {...} }`.
- Stored in a table (e.g. `rulesets(id, version, body jsonb, published_at)`). Published versions are immutable; design publishes a new version to change anything.
- Each player row stores `rulesetId` + `rulesetVersion`. New players get the current version. Migrating players to a new version is an explicit server job (so a balance change never silently breaks an existing save).
- Every validation in this document reads values from the player's ruleset. **No number in this doc may be hard-coded in an Edge Function.**
- A content pack (country) = a ruleset id. Adding a country should be adding a ruleset + catalog rows, not code.

### 2.2 Endpoint

`GET /config` â the player's ruleset (full body) plus `etag`. Client sends `If-None-Match`; `304` when unchanged. Every other response includes header `X-Ruleset-Version`; when it differs from the client's cached copy the client re-fetches `/config`.

### 2.3 Mechanic modes

Modes let design switch between alternative designs later. Phase 1 values (= reference game) in bold.

| Mode key | Options | Phase 1 |
| --- | --- | --- |
| `roomSize` | `growth` (free, by level) / `purchase` (buy land steps) | **`growth`** |
| `outdoorArea` | `none` / `terrace` (bought in steps) | **`terrace`** |
| `recipes` | `list` (learn from list) / `discovery` (ingredient experimenting) | **`list`** |
| `popularity` | `demand` (counter) / `rating` (0â5 stars) | **`demand`** |
| `hiring` | `free` (slots only) / `paid` (`hireBase + hirePerMember Ã team`) | **`paid`** â  see Â§13 |
| `tips` | `off` / `ambience` | **`off`** |
| `guestDrops` | `off` / `on` (`guestDropChance`) | **`off`** |
| `drinks` | `off` / `machine` (needs a `DrinkMachine` piece) | **`machine`** |
| `toilets` | `off` / `on` | **`on`** |
| `waitingArea` | integer spots (0 = none) | **`0`** |
| `tableCapacity` | `perTable` (one party per table) / `perChair` | **`perTable`** |

The backend implements both sides of each mode where the 10/23 spec already had the other side (rating, land purchase, discovery, tips, drops). Endpoints for a disabled mode return `FEATURE_DISABLED`.

---

## 3. Phase 1 ruleset values (must equal the reference game)

These come from the client's `rules.json` and the economy sheet `Global_FnB_City_Economy_v0.3.xlsx` (imported to `economy.json`). The sheet stays the source of truth; we will provide a script that converts it to the ruleset JSON so values are never retyped by hand.

### 3.1 Demand (popularity)

| Key | Value | Meaning |
| --- | --- | --- |
| `demand.start` | 120 | New restaurant |
| `demand.min` / `max` | 40 / 500 | Clamp |
| `demand.perHappyGuest` | +1 | Guest paid |
| `demand.perUnhappyGuest` | â1 | Turned away, gave up, no table, angry, no toilet |
| `demand.closedDecayPerHour` | 10 | While closed (no chef or no waiter on duty, or offline) |
| `demand.guestsPerMinutePerDemand` | 0.05 | Arrival rate = demand Ã 0.05 per minute |

### 3.2 Room growth (`roomSize = growth`)

Free, applied on level-up, never shrinks.

| Level | 1 | 3 | 5 | 7 | 9 | 11 | 13 | 15 | 18 | 21 | 24 | 27 | 30 | 34 | 38 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Width Ã height | 12Ã11 | 13Ã11 | 13Ã12 | 14Ã12 | 14Ã13 | 15Ã13 | 15Ã14 | 16Ã14 | 16Ã15 | 17Ã15 | 17Ã16 | 18Ã16 | 18Ã17 | 19Ã18 | 19Ã19 |

### 3.3 Terrace steps (`outdoorArea = terrace`)

| Step | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Width Ã depth | 7Ã6 | 9Ã8 | 11Ã10 | 13Ã12 | 15Ã14 | 17Ã16 | 19Ã18 |
| Level | 10 | 15 | 20 | 25 | 30 | 40 | 50 |
| Price (coins) | 2,500 | 7,500 | 16,000 | 35,000 | 75,000 | 155,000 | 275,000 |

### 3.4 Other rules

| Key | Value |
| --- | --- |
| `toilets` | fromLevel 8, chance 0.333, toiletSeconds 10, sinkSeconds 4, fixSeconds 6; per-piece `breakCount` in catalog |
| `drinksUnlockLevel` | 15 |
| `waitingSpots` | 0 |
| `noTableWaitSeconds` | 20 |
| `cleanSeconds` / `idleCleanTripSeconds` | 4 / 8 |
| `staffEnergy` | drain 1/min, recover 5/min, auto-return at 100; speed bands 80+ Ã1, 60+ Ã0.8, 40+ Ã0.5, 20+ Ã0.3, below Ã0.05 |
| `market` | 4 baskets, restock 90 s |
| `nameChangeCooldownHours` | 24 (first rename free) |
| Economy sheet `settings` | startingCoins 400, xpCurveBase 80, xpCurvePerLevel 60, levelUpCoins 100, hireBase 100, hirePerMember 60, ingredientSetsPerUpgrade 1, maxDishLevel 10, sellBackFraction 1/3, offlineRate 0.25, offlineCapHours 4, phase1MaxLevel 10, â¦ (whole `settings` object goes into the ruleset as-is) |
| Economy sheet tables | `levels` (65 rows: xpToNext, coinsReward, menu slots, staff slots, garden plots), `dishes` (25), `ingredients` (38), `furniture` (38), `staffRoles` (3) |

### 3.5 Starting state (`POST /players/bootstrap`)

From ruleset `start` block (not code):

- Coins 400, demand 120, level 1.
- Furniture owned and placed: 1 stove, 2 tables, 4 chairs (2 per table).
- Wall pieces: free menu holder (`tapAction: "menu"`) + 2 windows.
- Items: 3 Ã `kopi_o_kaw`. Ingredients: rice 2, egg 2, chili 2, flour 2, noodles 1, coconut_milk 1, palm_sugar 1.
- Staff: owner avatar (free, no slot) + per `startingStaff`.

---

## 4. Changes from the 10/23 spec

| Old spec | Now | APIs affected |
| --- | --- | --- |
| 0â5 star `rating` | `demand` counter (mode `popularity`) | `/me`, `/progress`, `/sales`, offline income |
| Guests drop ingredients | Off (mode `guestDrops`) | `/sales` returns no `drops` |
| Tips from ambience | Off (mode `tips`) | `/sales` returns no `tips` |
| `POST /recipes/discoveries` | `POST /recipes/{dishId}/learn` (mode `recipes = list`) | Remove `RECIPE_CLOSE`, `RECIPE_NOT_FOUND` from the Phase 1 path |
| `POST /restaurants/{id}/expansions` (indoor land) | Room grows free with level; terrace sold via `POST /restaurants/{id}/terrace` | Layout size validation |
| Start with 1 serving counter | No serving counter at all; plates wait on the stove | `/players/bootstrap`, catalog |
| Drinks need `drinks_counter` | Drinks need any `kind: DrinkMachine` piece | Menu, `/sales` |
| Toilets, drinks under "Later" | In Phase 1 (toilets L8, drinks L15) | Â§9 |
| One guest per chair | One party per table (mode `tableCapacity`) | Offline income estimate |
| Layout returns `ambience` | Not needed for now | Layout |

---

## 5. Progression and sales

### `POST /sales` (Key)

Request unchanged except: `{ dishId, recipeLevel, happy: true }`.

Response:
```json
{ "coins": 1240, "xp": 356, "level": 4, "levelUp": null, "demand": 131, "room": { "width": 13, "height": 11 } }
```
- No `rating`, `drops` or `tips` in Phase 1 (their fields exist only when the matching mode is on).
- On level-up: `levelUp: { "level": 5, "coinsReward": 100, "menuSlots": {...}, "staffSlots": 4 }`; when `roomSize = growth` the server also sets the room to the matching `roomGrowth` step and returns it.
- Validation: dish is on the player's menu and learned; drink sales require `drinksUnlockLevel` and a drink machine in the saved layout; the saved layout has at least one stove and one chair; rate limit â `demand Ã guestsPerMinutePerDemand` per minute plus tolerance.

### `POST /demand-events` (Key, batched)

Guests who left without paying:
```json
{ "events": [ { "type": "turned_away" }, { "type": "no_table" } ] }
```
Types: `turned_away`, `gave_up`, `no_table`, `angry`, `no_toilet`. Each applies `perUnhappyGuest`. Server applies closed-time decay itself from its clock. Response: `{ "demand": 128 }`.

---

## 6. Layout save: `PUT /restaurants/{id}/layout` (Key)

```json
{
  "revision": 12,
  "room": { "width": 14, "height": 13 },
  "terrace": { "width": 7, "depth": 6 },
  "entrance": { "side": 1, "index": 5 },
  "floor": "floor_wood",
  "terraceFloor": "terrace_tile",
  "furniture": [
    { "id": "table_basic", "x": 3, "y": 4, "facing": 1 },
    { "id": "flat_tv", "x": 3, "y": 4, "facing": 1, "stackedOn": 0 }
  ],
  "walls": [ { "id": "window_basic", "side": 0, "index": 4 } ],
  "wallpaperLeft": [null, "ref_wall18", "ref_wall18"],
  "wallpaperRight": ["ref_wall18", null],
  "storage": { "chair_basic": 2 },
  "toiletUses": { "5": 2 },
  "buy": { "chair_basic": 2 }
}
```

Server validation (all limits from the ruleset/catalog):

1. `room` â¤ the size the player's level allows (or the size already stored, whichever is larger); never smaller than stored. `terrace` equals the purchased step (0Ã0 if none).
2. Every floor piece lies inside the room or terrace, inside the catalog footprint, no overlaps. Outdoor-only pieces only outside.
3. `stackedOn` is an index in the same array; only `stackable` pieces on `surface` pieces, fully on top, one piece per spot.
4. Walls: `side` 0 = back-right, 1 = back-left; the piece's `sizeX` cells fit the wall; not on the door cell; no overlap.
   Wallpaper: `wallpaperLeft` / `wallpaperRight` give a wallpaper id per wall cell from the back corner (null = default wall); length ≤ the wall, ids must be in the catalog's wallpapers. Free in Phase 1 like floors (price field reserved).
5. Per id: placed + stored â¤ owned + `buy`. The `buy` pieces are charged at server catalog price and level-gated by `unlockLevel` (`INSUFFICIENT_COINS`, `LEVEL_TOO_LOW`). Write layout, then debit, in one transaction.
6. At least one chair and one stove. Pieces nobody can reach are allowed (reference behaviour); no pathing check.

Response: `{ "revision": 13, "coins": 980, "owned": { ... } }`. Ownership mismatch â `OWNERSHIP_MISMATCH` with the server's `owned`.

`GET /restaurants/{id}/layout` returns the same shape (used for load and friend visits).

### Selling (`POST /furniture/{id}/sell`, Key)

`{ "count": 1 }` from storage. Refund = `price Ã sellBackFraction` (1/3 now). Returns `{ coins, storage }`.

---

## 7. Terrace: `POST /restaurants/{id}/terrace` (Key)

- Request `{ "step": 2 }`.
- Must be the next step (`NOT_NEXT_STEP`), level â¥ step level (`LEVEL_TOO_LOW`), coins â¥ price (`INSUFFICIENT_COINS`).
- Write the new size, then debit, in one transaction.
- Response `{ "terrace": { "width": 9, "depth": 8 }, "coins": 3200, "revision": 14 }`.
- When `roomSize = purchase` (future), the old `POST /restaurants/{id}/expansions` is used instead and reads its steps from `ruleset.expansions`.

---

## 8. Catalog, recipes and menu

### `GET /catalog` (cached with `/config`)

Furniture entries gain:

| Field | Meaning |
| --- | --- |
| `kind` | `Table`, `Chair`, `Stove`, `DrinkMachine`, `Decor`, `Plant`, `WallDecor`, `Toilet`, `Sink` |
| `category` | Shop tab: `Kitchen`, `Table`, `Chair`, `Decoration`, `Functional`, `Outdoor`, `Wall`, `Floor` |
| `sizeX`, `sizeY` | Footprint (most pieces 1Ã1, as in the reference) |
| `stackable`, `surface` | Can stand on a table / can hold stacked pieces |
| `breakCount` | Toilet uses before breaking; 0 = never |
| `tapAction` | e.g. `"menu"` |
| `price`, `unlockLevel` | Server values are the truth |

New kinds or categories must be addable as data. The server treats unknown `kind` values as plain decor for validation.

### `POST /recipes/{dishId}/learn` (Key)

Learn or upgrade one dish (up to `maxDishLevel`). Consumes `ingredientSetsPerUpgrade` sets of the dish's ingredients; grants `xpUpgradeBase + xpUpgradePerLevel Ã level`; learning adds the dish to the menu if a slot is free. Level too low â `LEVEL_TOO_LOW`, nothing consumed. Missing ingredients â `INSUFFICIENT_INGREDIENTS`.

### `PUT /menu` (Key)

`{ "revision": 3, "starter": [], "main": ["nasi_lemak"], "dessert": [], "drink": [] }`. Each category â¤ the level's slots (`MENU_SLOT_FULL`), dishes learned, menu not empty, `drink` empty before `drinksUnlockLevel`.

---

## 9. Staff, kitchen and toilets

Kitchen flow, staff movement and queues are simulated on the client only. The server only validates what changes coins or progress.

- `POST /staff` (Key) `{ "role": "chef" | "waiter" | "cleaner" }`: slot available, role unlocked (`staffRoles`); cost per `hiring` mode. `DELETE /staff/{id}`. The owner avatar takes no slot and cannot be dismissed. `PUT /staff/owner { "role": ... }` switches the owner's job (free).
- `POST /items/{id}/use` (Key) `{ "staffId" }`: consumes one item, returns new energy. `POST /items/{id}/buy` at catalog price.
- Toilets: uses are stored in the layout's `toiletUses`; no coin effect. Fixing (cleaner or tap) is free and needs no endpoint.

---

## 10. Market and garden

Unchanged from 10/23 except values come from the ruleset (`marketBaskets`, `marketRestockSeconds`, `gardenPlotLevels`, `seedCost`, `wateringHours`, `gardenHarvest`). Garden is 9 plots of 2Ã2; plot opening by level is server-checked.

---

## 11. Offline ledger sync: `POST /sync/ledger`

- The client uploads its `OfflineLedger`: sales, demand events, idle income, market, recipes, hires, renames, layout changes. Each entry has its own `Idempotency-Key` and client time.
- The server re-validates every entry with its own prices and rules. Idle income is computed by the server from its own clock (last online â now) Ã `offlineRate`, capped at `offlineCapHours`, using table count (not chairs), staff, stoves and demand. A clock moved backwards counts as 0.
- Response: full authoritative state (coins, level, XP, demand, layout + revision, menu, recipes, staff, inventory) plus per-entry `{ key, accepted, reason }`. The client replaces its local state with it.

---

## 12. Error codes (new)

`FEATURE_DISABLED`, `NOT_NEXT_STEP`, `LEVEL_TOO_LOW`, `OWNERSHIP_MISMATCH`, `INVALID_STACK`, `INVALID_WALL_SLOT`, `MENU_SLOT_FULL`, `NO_DRINK_MACHINE`, `INSUFFICIENT_INGREDIENTS`, `RULESET_OUTDATED` (client sent a stale `X-Ruleset-Version` on a write). Everything else as in 10/23.

---

## 13. Build order and open questions

Suggested order:

1. Ruleset table + `GET /config` + modes (everything else depends on it)
2. `/sales` with demand + `/demand-events` + room growth on level-up
3. New layout save (walls, stacking, terrace size, buy on save) + sell
4. Terrace purchase
5. Recipe learn + menu
6. Staff, owner, items
7. Offline ledger sync

Open questions (answers change config only, not the API):

- **Hiring cost:** the reference game hires for free (slots only), but the economy sheet charges `hireBase + hirePerMember Ã team`. Phase 1 currently uses the sheet (`paid`). If we want an exact reference match, set `hiring = free`.
- **Broken toilets:** stored in the layout only; no server effect. Confirm design doesn't need it for anti-cheat.
