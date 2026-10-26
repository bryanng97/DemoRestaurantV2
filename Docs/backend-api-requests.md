# Backend API spec for the economy (Phase 1)

Requested by the game client team, 2026-10-23. Source of the rules and numbers:
`Docs/Design/Global_FnB_City_Economy_v0.3.xlsx` (the "economy sheet"). This extends the
sixteen operations in `mobile-integration.md`; everything here follows that contract unless
stated otherwise.

## 0. Conventions

- **Base path:** `https://<project>.supabase.co/functions/v1/player-api/v1` (renamed from `game-api`).
- **Envelope:** success `{ "data": ..., "requestId": "..." }`, failure `{ "error": { "code", "message", "details" }, "requestId" }`.
- **Auth:** `apikey` + `Authorization: Bearer <user access token>`; verified email required.
  Player identity always comes from the token, never from the body.
- **Idempotency:** every operation that grants or spends coins, ingredients, furniture or XP takes a
  UUID `Idempotency-Key` header (**Key** below). Replays return the original result.
- **Server authority:** prices, rewards, drop chances and timers come from the server's own config and
  clock. The client never sends a price, amount, timestamp or random outcome.
- **Unknown fields are rejected** (`VALIDATION_ERROR`), as today.
- **Ids** are the lowercase ids from the economy sheet. Once used they are never renamed.
- **Phase gate:** the server rejects content above `phase1MaxLevel` (10) or rows marked "In Phase 1 = No"
  with `NOT_IN_PHASE`.
- **Priority:** **P1** = needed for the Phase 1 vertical slice; **P2** = after Phase 1.

### Decisions already made by the game team

| Topic | Decision |
| --- | --- |
| Dishes | Phase 1 uses Malaysian dishes only. Dessert slot uses `cendol`. Pizza, burger and pancakes move to later country packs. The sheet will be updated (v0.4). |
| Starting resources | The backend's bootstrap grant is authoritative (today 1,000 coins, 2 tables, 4 chairs, 1 counter). Keep it in server config so design can change it. |
| Tables and chairs | Separate items, as in the current catalog. One table serves one guest at a time, regardless of chair count. Reserve the whole table during arrival and dining; it stays unavailable until dirty dishes are cleared. Use distinct usable tables for dining capacity checks. |
| Recipes | Learn a selected catalog recipe using its exact ingredient set, then upgrade with the configured sets. The ingredient experiment also remains available. Decision updated 2026-10-24 to follow Restaurant City interactions. |
| Dish levels | Dishes level up from 1 to 10. |
| Progression | One **restaurant level** (XP curve) replaces the old popularity level. A **rating of 0–5 stars** drives guest arrival speed. |
| Staff | Chef, waiter and cleaner are all in Phase 1. |

## 1. Economy config (P1)

The server must hold every number from the sheet in a **config table**, not in code, so designers can
change values without a deploy. Suggested: import the sheet's Settings, Levels, Dishes, Ingredients,
Furniture, Expansions and Staff tabs into config rows keyed by id.

| Method | Path | Result |
| --- | --- | --- |
| GET | `/config/economy` | The public config the client needs to display and predict (same shape as below). Includes `configVersion`. |

```json
{
  "configVersion": "v0.4-2026-10-30",
  "settings": { "xpCurveBase": 80, "xpCurvePerLevel": 60, "levelUpCoins": 100, "dishPriceStepPerLevel": 0.2,
                "maxDishLevel": 10, "phase1MaxLevel": 10, "...": "every Settings row" },
  "levels":     [ { "level": 1, "xpToNext": 140, "menuCategories": 3, "dishesPerCategory": 1,
                    "staffSlots": 3, "gardenPlots": 0, "coinsReward": 0 } ],
  "dishes":     [ { "dishId": "nasi_lemak", "category": "main", "ingredients": ["rice","coconut_milk","anchovy"],
                    "basePrice": 12, "cookSeconds": 5, "unlockLevel": 1 } ],
  "ingredients":[ { "ingredientId": "chili", "source": "garden", "rarity": 1, "growHours": 24 } ],
  "furniture":  [ { "itemId": "table", "category": "function", "price": 80, "ambience": 0, "unlockLevel": 1, "themedSet": null } ],
  "expansions": [ { "expansionId": "indoor_1", "width": 10, "height": 12, "unlockLevel": 6, "price": 1500 } ]
}
```

The client caches by `configVersion`. Every other response may include `configVersion`; when it
changes, the client refetches.

## 2. Changes to existing operations

| # | Operation | Change | P |
| --- | --- | --- | --- |
| 2.1 | `POST /players/bootstrap` | Grants come from config. Also creates progress (level 1, XP 0, rating = `startingRating` 3.0), the starting ingredients and the starting staff (1 chef, 1 waiter). Return them. | P1 |
| 2.2 | `GET /me` | Add `level`, `xp`, `xpToNext`, `rating` (0–5, one decimal). | P1 |
| 2.3 | `GET /catalog` | Add `ambience`, `unlockLevel`, `sellBackPrice`, `themedSet`. Categories: `function`, `decor`, `fun`, `floor`, `wall`. | P1 |
| 2.4 | `POST /purchases` | Reject items above the player's level: `LEVEL_TOO_LOW`. | P1 |
| 2.5 | `PUT /restaurants/{id}/layout` | Validate against the restaurant's **unlocked area** (grows with expansions) instead of a fixed 20×20. Return `area: {width,height}` and computed `ambience` (sum of placed items + themed set bonus). | P1 |
| 2.6 | `POST /restaurants/{id}/visits` | Grant the visitor's reward by visit order that UTC day: 1st **100**, 2nd **25**, 3rd–5th **10**, then **3** coins. Return `grantedAmount`, `wallet`. | P2 |

## 3. Progression and sales (P1)

### 3.1 Rules (from the sheet)

- XP to next level = `xpCurveBase + xpCurvePerLevel × level` (80 + 60 × level). Level 10 is the Phase 1 cap.
- Level-up reward: `levelUpCoins` (100) per level reached.
- XP per served dish = `xpServeBase + xpServePerDishLevel × dishLevel` (6 + 2 × dishLevel).
- Dish price at level N = `round(basePrice × (1 + 0.2 × (N − 1)))`, half away from zero.
- Tip = bill × min(`tipCap` 0.4, `tipPerAmbience` 0.03 × ambience).
- Rating: +0.06 when served with at least 50 % patience left, +0.03 otherwise, −0.15 when a guest leaves angry. Clamp 0–5.
- Guest arrival interval (seconds) = max(2.2, 9 − 1.1 × rating − 0.25 × level). Used for the throughput check below.
- 25 % chance a happy guest leaves one random ingredient (`guestDropChance`).

### 3.2 Endpoints

| Method | Path | Body | Result |
| --- | --- | --- | --- |
| GET | `/progress` | — | `level`, `xp`, `xpToNext`, `rating`, `menu`, `dishLevels` |
| POST | `/sales` | batch, ≤ 100 events; **Key** | accepted coins, tips, XP, rating, level-ups, rewards, ingredient drops, `wallet` |

```json
POST /sales
{
  "events": [
    { "eventId": "uuid", "type": "served", "dishId": "satay", "servedAt": "2026-10-23T10:00:05Z",
      "patienceLeft": 0.62 },
    { "eventId": "uuid", "type": "angry",  "servedAt": "2026-10-23T10:00:41Z" }
  ]
}
```

Server rules:

- Price, dish level, tip and XP come from the server. The client sends **no amounts**.
- Reject dishes not discovered or not on the active menu (`DISH_NOT_ON_MENU`).
- **Throughput check:** cap accepted serves per window by the restaurant's capacity (staff, stoves,
  seats, arrival interval). Excess events are returned as `rejected` with a reason, not an error.
- Duplicate `eventId`s are ignored (counted once forever).
- `servedAt` is only used for ordering; the server clamps it to its own clock.
- Guest ingredient drops are rolled **by the server** and returned in `drops`.

Response:

```json
{
  "accepted": 2, "rejected": [],
  "coins": 27, "tips": 6, "xp": 8,
  "rating": 3.06, "level": 2, "levelUps": [ { "level": 2, "coins": 100 } ],
  "drops": [ { "ingredientId": "rice", "quantity": 1 } ],
  "wallet": { "softCurrency": 1133, "updatedAt": "..." }
}
```

## 4. Recipes, dish levels and menu (P1)

| Method | Path | Body | Result |
| --- | --- | --- | --- |
| GET | `/recipes` | — | Discovered dishes with `level` (1–10) |
| POST | `/recipes/discoveries` | `{"ingredients":["rice","coconut_milk","anchovy"]}`; **Key** | Exact match: consumes one of each, adds the dish at level 1, grants XP (`xpUpgradeBase + xpUpgradePerLevel × 1` = 30) |
| POST | `/recipes/{dishId}/upgrades` | `{}`; **Key** | Consumes `ingredientSetsPerUpgrade` (1) full set of the dish's ingredients, raises the level by 1 (max 10), grants XP `20 + 10 × newLevel` |
| GET | `/menu` | — | Active dishes per category and the slot limits for the current level |
| PUT | `/menu` | `{"starter":["roti_canai"],"main":["nasi_lemak"],"dessert":["cendol"]}` | Saved menu |

Rules:

- Discovery near miss (one ingredient off) returns `RECIPE_CLOSE` and **consumes nothing**; wrong
  combination returns `RECIPE_NOT_FOUND` and consumes nothing.
- Dish `unlockLevel` applies to discovery (`LEVEL_TOO_LOW`).
- Menu limits come from the Levels tab: 3 categories at level 1; drinks open at level 15 and need a
  `drinks_counter` placed; 1 dish per category, 2 from level 10, 3 from level 20.
- The menu can only contain discovered dishes and can never be empty.

Errors: `RECIPE_CLOSE`, `RECIPE_NOT_FOUND`, `INSUFFICIENT_INGREDIENTS`, `MAX_DISH_LEVEL`, `MENU_SLOT_LIMIT`, `DISH_NOT_DISCOVERED`.

## 5. Ingredients and market (P1)

| Method | Path | Body | Result |
| --- | --- | --- | --- |
| GET | `/ingredients` | `limit`, `cursor` | Owned quantities |
| GET | `/market` | — | `nextCrateAt` (server time), today's deals, `serverTime` |
| POST | `/market/crates/claims` | `{}`; **Key** | The crate's ingredients |
| POST | `/market/deals/{dealId}/purchases` | `{}`; **Key** | Charged coins, ingredient, `wallet` |

Rules:

- Crate cooldown `crateCooldownSeconds` (20 s is a **testing value**; design will set the real timer).
- Each crate has `crateItems` (3) slots. Each slot has a `crateMenuChance` (75 %) chance to be an
  ingredient used by the player's menu; otherwise a random Phase 1 ingredient weighted by
  `12 − 2 × rarity` (minimum 1).
- `dailyDeals` (3) discounted ingredients per UTC day at `dailyDealDiscount` (50 %) off.
- Errors: `CRATE_NOT_READY` (with `details.nextCrateAt`), `DEAL_SOLD_OUT`, `INSUFFICIENT_FUNDS`.

## 6. Staff (P1)

| Method | Path | Body | Result |
| --- | --- | --- | --- |
| GET | `/staff` | — | Staff list: `staffId`, `role`, `energy` (0–1), `onBreakUntil`, `slots` used/total |
| POST | `/staff/hires` | `{"role":"chef"}`; **Key** | New staff member, charged coins, `wallet` |
| DELETE | `/staff/{staffId}` | — | Dismissed (no refund) |
| POST | `/staff/{staffId}/refills` | `{"item":"snack"}` or `{"item":"meal"}`; **Key** | New energy, `wallet` |
| POST | `/staff/{staffId}/breaks` | `{}` | `onBreakUntil` |

Rules:

- Hire cost = `hireBase + hirePerMember × teamSize` (100 + 60 × team size before hiring).
- Staff slots from the Levels tab (3 at level 1, +1 at levels 2 and 5; 5 in Phase 1). Role
  `unlockLevel` from the Staff tab. The player's own avatar is a free extra worker, outside the slots.
- Energy is computed by the **server from timestamps**: full energy lasts `staffEnergyHours` (3 h) of
  work; tired staff slow down but never stop completely.
- Snack `snackCost` (30) restores `snackRefill` (25 %); meal `mealCost` (100) restores to full.
- A free break lasts `breakMinutes` (60): staff stop working and return full.
- Errors: `NO_STAFF_SLOT`, `ROLE_LOCKED`, `INSUFFICIENT_FUNDS`, `STAFF_ON_BREAK`.

## 7. Furniture, ambience and expansions

| Method | Path | Body | Result | P |
| --- | --- | --- | --- | --- |
| POST | `/restaurants/{id}/expansions` | `{"expansionId":"indoor_1"}`; **Key** | New `area`, charged coins, `wallet` | P1 |
| POST | `/inventory/sales` | `{"itemId":"lantern","quantity":1}`; **Key** | Refund, `wallet`, new quantity | P1 |

Rules:

- Expansion: checks `unlockLevel` (6 for `indoor_1`), the price and that the previous expansion is owned.
  Phase 1 has only `indoor_1` (10 × 12, 1,500 coins).
- Sell-back refunds `round(price × sellBackFraction)` (one third). Only unplaced copies can be sold
  (`ITEM_PLACED`).
- Ambience = sum of placed items' `ambience` + `themedSetBonus` (5) when every piece of a themed set is
  placed. The server computes it on layout save; it feeds patience, tips and rating in `/sales`.

## 8. Offline income and ledger sync (P1)

| Method | Path | Body | Result |
| --- | --- | --- | --- |
| POST | `/sync/ledger` | Ordered client ledger entries since the last sync; **Key** | Accepted/rejected entries and the authoritative wallet, ingredients, progress |
| POST | `/offline-income/claims` | `{}`; **Key** | Coins earned while away |

Rules:

- Offline income is computed by the server from **its own clock** since the last activity, capped at
  `offlineCapHours` (4 h) and paid at `offlineRate` (25 %) of the active rate for the restaurant's
  current capacity and menu.
- A client clock set backwards counts as zero time.
- Ledger entries are re-validated with server prices and limits; never trusted as sent.
- Body cap: 128 KiB (as for layout saves).

## 9. Garden (P1)

| Method | Path | Body | Result |
| --- | --- | --- | --- |
| GET | `/garden` | — | Plots: `plotIndex`, `locked`, `ingredientId`, `plantedAt`, `waterUntil`, `readyAt`, `serverTime` |
| POST | `/garden/plots/{n}/seeds` | `{}`; **Key** | Random garden ingredient planted, charged `seedCost` (300), `wallet` |
| POST | `/garden/plots/{n}/water` | `{}` | New `waterUntil` |
| POST | `/garden/plots/{n}/harvest` | `{}`; **Key** | Ingredient granted, plot emptied |

Rules:

- Plots unlock at levels 3, 6 and 9 in Phase 1 (`gardenPlotLevels`).
- Growth time per ingredient comes from the Ingredients tab (`growHours`, 24–48 h).
- One watering lasts `wateringHours` (9 h). A dry plant **pauses**, it never dies; growth only counts
  watered time, all on the server clock.
- Errors: `PLOT_LOCKED`, `PLOT_BUSY`, `NOT_READY`, `INSUFFICIENT_FUNDS`.

## 10. Daily systems

| Method | Path | Body | Result | P |
| --- | --- | --- | --- | --- |
| GET | `/goals/today` | — | 3 goals from a pool of 15, progress, rewards | P1 |
| POST | `/goals/{goalId}/claims` | `{}`; **Key** | Reward, `wallet` | P1 |
| GET | `/surprise-visitor` | — | Next appearance (≤ 2 per UTC day) and its 3 reward choices | P1 |
| POST | `/surprise-visitor/{visitId}/choices` | `{"choice":1}`; **Key** | The chosen reward | P1 |
| GET | `/rewards/login-streak` | — | Current step and today's reward | P2 |
| POST | `/rewards/login-streak/claims` | `{}`; **Key** | 1 ingredient on day 1, +1 per day up to 5; a missed day steps back one level | P2 |
| GET | `/quiz/today` | — | Question (server starts a 10 s timer) | P2 |
| POST | `/quiz/today/answers` | `{"answer":2}`; **Key** | 1 ingredient when correct within 10 s of the server's start time | P2 |

Goal progress is counted by the server from `/sales` and the other endpoints; the client never
reports "goal complete". Phase 1 has one surprise visitor type: the travelling chef.

## 11. Social

| Method | Path | Body | Result | P |
| --- | --- | --- | --- | --- |
| GET | `/friends/demo` | — | One server-owned demo restaurant to visit in Phase 1 (a seeded NPC player) | P1 |
| POST | `/gifts` | `{"to":"<playerId>","itemId":"..."}`; **Key** | Up to `freeGiftsPerDay` (20) free gifts per day | P2 |
| GET | `/mailbox` | `limit`, `cursor` | Received gifts | P2 |
| POST | `/mailbox/{giftId}/claims` | `{}`; **Key** | Item granted | P2 |
| POST | `/restaurants/{id}/trash/cleanups` | `{"pieces":3}`; **Key** | 1 coin per piece on a friend's restaurant; may drop an ingredient | P2 |

Real friends come in Phase 2.

## 12. Later (not in Phase 1)

| Feature | Notes |
| --- | --- |
| Fun machines | `POST /fun-machines/{instanceId}/uses` (3 coins per use, breaks after 8 uses) and `/repairs` (40 coins; friends repair free) |
| Toilets | Needed from level 8; cleaners keep them clean |
| Drinks | Level 15 and a `drinks_counter` |
| More expansions and terraces | Rows `indoor_2`… `terrace_7`, `second_city` in the Expansions tab |
| Premium currency | Not designed yet |

## 13. New error codes

| HTTP | Code | Client action |
| --- | --- | --- |
| 409 | `LEVEL_TOO_LOW` | Show the required level |
| 409 | `NOT_IN_PHASE` | Hide the item |
| 409 | `INSUFFICIENT_INGREDIENTS` | Refresh ingredients |
| 409 | `RECIPE_CLOSE` / `RECIPE_NOT_FOUND` | Show the hint; nothing consumed |
| 409 | `MAX_DISH_LEVEL` | Disable upgrade |
| 409 | `MENU_SLOT_LIMIT` / `DISH_NOT_DISCOVERED` / `DISH_NOT_ON_MENU` | Reload the menu |
| 409 | `CRATE_NOT_READY` / `NOT_READY` | Use `details.nextCrateAt` / `details.readyAt` |
| 409 | `DEAL_SOLD_OUT` | Refresh the market |
| 409 | `NO_STAFF_SLOT` / `ROLE_LOCKED` / `STAFF_ON_BREAK` | Refresh staff |
| 409 | `PLOT_LOCKED` / `PLOT_BUSY` | Refresh the garden |
| 409 | `ITEM_PLACED` | Remove it from the layout first |

## 14. Rate and size

Existing per-player budgets apply. Suggested additions: `/sales` 30/minute, `/sync/ledger` 10/minute,
market and garden writes 30/minute. `/sales` and `/sync/ledger` bodies up to 128 KiB.

## 15. Suggested build order (P1)

1. Economy config table + `GET /config/economy`; bootstrap/`/me`/catalog changes (2.1–2.4).
2. `/sales` with progression, rating, tips, drops; `/progress`.
3. Recipes (discovery, upgrades) and menu.
4. Ingredients and market.
5. Staff.
6. Expansions, sell-back, ambience in layout save (2.5).
7. Offline income and ledger sync.
8. Garden, daily goals, surprise visitor, demo friend restaurant.

## 16. Questions for the backend team

1. Can the config be imported straight from the sheet (or a CSV export of it) so design changes need no deploy?
2. Is the throughput check in `/sales` (capacity per time window) acceptable, or do you prefer server-simulated sales?
3. Offline income: should the server simulate from the saved layout and staff, or use a stored "active rate" snapshot?
4. Expected dates for the P1 items, so the client knows which features stay "unsynced" in the prototype.

## 17. Exterior facade (requested 2026-10-24; not a live route)

The offline prototype now has a separate side-on facade editor. It does not submit these writes to an existing layout route. Proposed additions:

| Method | Path | Body / result |
| --- | --- | --- |
| GET | `/exterior` | Current `pieces`, owned quantities and revision. Each piece has `instance`, stable `itemId`, `x`, `y`, `flipped`. Coordinates are bottom-centre anchors in a 1000 × 680 design space. |
| POST | `/exterior/commits` | `{ "revision": ..., "pieces": [...] }`; **Key**. Atomically validate the whole facade, buy copies beyond owned quantities using server catalog prices, and return the new facade, ownership, revision and confirmed wallet. Client-supplied ownership/prices are not accepted. |

The catalog must include exterior kind, dimensions, price and unlock level. Validate one wall, roof and door, at most one sign, unique instance IDs, at most 64 pieces, finite coordinates, wall/ground bounds, no overlapping fixtures and a clear path in front of the door. Stored pieces remain owned; moving/storing an owned piece costs nothing. A failed commit must change nothing. Conflicting revisions require refetching the facade. The original artwork is temporary; item IDs must survive a later sprite replacement.

`ExteriorSaved` ledger entries and local version-5 facade/recipe-level saves are presently unsynced. The selected-recipe Learn action can call the proposed `/recipes/discoveries` contract with the exact catalog ingredient list once that endpoint is available; upgrades use the already requested section-6 contract. Never accept local recipe levels or predicted upgraded sale prices as authority.
