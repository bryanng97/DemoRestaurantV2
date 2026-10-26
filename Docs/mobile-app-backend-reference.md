# DemoRestaurant Mobile App: Gameplay and Backend Reference

**Date:** 2026-10-25  
**Client source baseline:** `eb36489`  
**Audience:** Backend engineers, mobile engineers and game design

## 1. Purpose and document authority

This document describes the mobile app as implemented, the state that must survive a cloud round trip, and the gameplay events the backend must support. Use it together with [Backend API v2 Integration Review](backend-mobile-api-v2-review.md), which lists the required contract changes and acceptance criteria.

This is a **client implementation reference**, not a claim that every feature is already supported by the deployed API. Local JSON examples are explanatory device-save shapes, not approved HTTP request bodies. Proposed API changes are in the companion review. Do not send local save fields to strict API endpoints without an agreed mapper.

Where implementation, the economy workbook and the supplied `mobile-api-v2.md` disagree, the discrepancy is listed explicitly. Design and both engineering teams must resolve these decisions before publishing the authoritative ruleset. Preserve agreed existing prices, progression and rewards; do not silently rebalance during integration.

## 2. App and architecture

| Area | Current implementation |
| --- | --- |
| App | A 2D isometric restaurant management game, initially themed around Malaysia |
| Primary target | Landscape Android; iOS is a planned platform |
| Engine | Unity 6000.6.4f1, C# 9, Input System, URP 2D |
| Languages | English, Malay and Chinese |
| Simulation | Plain C# under `Assets/_Game/Scripts/Logic`, separate from Unity rendering |
| Content | Country-pack JSON under `Assets/_Game/Resources/Content/malaysia` |
| Current play | Offline-capable local simulation, device saves and a local economy authority |
| Networking | Supabase Auth and an older API adapter exist; v2 gameplay integration is incomplete |

The backend should validate ownership, money, inventory, progression, persistent timers and durable state. NPC paths, individual orders in flight, seating visuals, animation frames, camera movement and touch gestures remain client-side. There is no requirement to run every NPC frame on the server.

## 3. Player-facing screens and actions

| Screen/system | Player actions | Durable consequences |
| --- | --- | --- |
| Restaurant | Watch service; tap dirty tables or broken functional items | Meal income, demand, wear/repair; cleaning itself gives no second sale |
| Interior editor | Buy/place/move/rotate/store furniture; stack supported objects; paint floors/wallpaper; move door and wall pieces | Layout, ownership, purchases, painted cells and door location |
| Menu | Choose active dishes by category | Active dish IDs and menu revision |
| Recipes | Learn or upgrade a selected dish | Ingredient debit, recipe level, XP and possibly menu/progression changes |
| Staff | Hire/dismiss, change owner role, rest, use snacks/refill | Staff identities, roles, duty/energy, items and coins |
| Market | Collect ready ingredient baskets | Ingredients and basket timers/results |
| Garden | Buy random seed, water, harvest | Coins, crop state, watered growth and ingredients |
| Street/facade | Change roof, building front, door, windows, sign and decorations | Independent facade layout and ownership |
| Profile/settings | Edit name/avatar; choose language, music volume and frame rate | Profile/avatar are account candidates; language/audio/frame rate are device preferences |
| Social/events | Some buttons currently show placeholders | Do not infer that full friend relationships, events or multiplayer are implemented |

Actions and Explore navigation drawers start collapsed. Main buttons are larger touch targets; choosing a function collapses its drawer. The interior editor's Save must include a valid held placement without requiring a separate deselection. Editing uses a draft; Cancel discards it, and unpaid drafts must never become authoritative saves.

## 4. Normal customer and kitchen flow

### 4.1 Arrival and dining

1. Customers walk along the street, enter through the restaurant's door and seek an available seat/table.
2. A table supports **one dining customer**, even if multiple chairs surround it.
3. The table is reserved while the customer approaches, orders, waits, eats, and leaves dirty dishes. It becomes available after cleaning.
4. A customer orders from the active menu. The client chooses a category first, then a dish in that category.
5. With no seat, a customer may use an available entertainment machine. Otherwise they are turned away, or queue only if the ruleset enables waiting spots. Current Malaysia waiting spots = 0.
6. No-table, food-patience, queue and bathroom failures may reduce demand. They must not also become successful meal sales.

The client routes around occupied furniture cells. Saving a room that has an unreachable object is allowed with a warning; the backend does not need a full NPC pathfinding validator. Basic geometry, door/wall restrictions and ownership still require validation.

### 4.2 Cooking and serving

```text
Order -> free chef/stove -> cook -> prepared meal waits on that stove
      -> waiter collects the actual ordered meal -> customer eats -> one payment
      -> dirty table -> cleaning -> table available
```

- Chefs claim a stove and stand at their work position. The stove stays occupied until a waiter collects the prepared meal.
- There is no separate serving-counter stage in the active kitchen flow.
- Preparation duration uses the dish's `cookSeconds`, divided by the chef's effective work speed.
- Waiters carry the customer's actual food, not a fixed coffee image.
- A drink uses a reachable `DrinkMachine` and a waiter; it is not cooked by a chef. The playable level cap currently prevents reaching the workbook's drink unlock level.
- A successful meal generates one sale plus configured XP/demand changes. Clearing its plate must not issue another payment.

### 4.3 Cleaning

- Staff cleaning takes `cleanSeconds / effectiveWorkSpeed`.
- Cleaners clean; when no cleaner is on duty, idle waiters can clean.
- The player can queue table cleaning by tapping a dirty plate/table. Tasks run sequentially; tapping a waiting task cancels that queued task, while an active task finishes.
- Current manual duration is 5 seconds per table. Staff base cleaning duration is 4 seconds.
- Cleaning has visual progress/glow feedback and grants no additional coins or XP.

## 5. Current gameplay rules and progression

These values are a source snapshot. The approved server ruleset must identify which values are active; imported legacy settings are not automatically active gameplay.

| Rule | Current value/behavior | Source |
| --- | --- | --- |
| Intended starting coins | 400; scene/debug parameters can override local starting balance | `economy.json`; bootstrap scene configuration |
| Playable level cap | 10 in Phase 1; debug mode can exceed it locally | Economy settings and debug flag |
| Data table ceiling | 65 levels | Economy settings/table |
| XP to advance from level L | `80 + 60 * L` | Economy formula |
| XP per served dish | `6 + 2 * recipeLevel` | Economy formula |
| XP for learning/upgrading to recipe level N | `20 + 10 * N` | Economy formula |
| Recipe maximum | Level 10 | Economy settings |
| Ingredient cost per learn/upgrade | One configured recipe ingredient set | Recipe system/settings |
| Dish price | Base price times `1 + 0.2 * (recipeLevel - 1)`, rounded half away from zero | Economy formula |
| Level reward | Level-table reward, falling back to 100 | Progression code/settings |
| Hiring | `100 + 60 * hiredCount`; current count excludes owner | Session/formula |
| Owner | Free worker, does not occupy a hired-worker slot, cannot be dismissed | Session/staff controller |
| Drink unlock | Level 15, beyond current playable cap | Economy settings |
| Cleaner unlock | From the economy staff-role table; current Phase 1 configuration is level 6 | Economy role table |
| Idle income | Throughput estimate times 0.25, with at most 4 hours counted | Offline progress/settings |
| Name cooldown | First rename free; later renames wait 24 hours | Rules/profile |

The current client stores **XP within the current level**. At level 2 with 7 XP, its total earned XP is 147 because the level-1 threshold was 140. The backend guide instead returns cumulative XP. Keep the representations distinct; do not directly pass cumulative XP to the local progress restore method.

### 5.1 Demand and arrivals

| Parameter | Value |
| --- | --- |
| Starting demand | 120 |
| Minimum / maximum | 40 / 500 |
| Happy meal change | +1 |
| Unhappy guest change | -1 |
| Closed decay | 10 demand per hour |
| Arrival rate | `demand * 0.05` guests/minute; demand 120 gives 6/minute |
| No-table complaint wait | 20 seconds |

There is no daily customer limit. Demand replaces the old star-rating model. Legacy rating, tip and guest-drop fields still exist in imported economy data but must not be activated simply because they are present. Current gameplay does not award ingredient drops or ambience tips for served guests.

### 5.2 Room growth and terrace

Indoor space grows automatically with level and never shrinks. It is not bought through the legacy indoor-expansion mechanic.

| From level | Room width x height |
| --- | --- |
| 1 | 12 x 11 |
| 3 | 13 x 11 |
| 5 | 13 x 12 |
| 7 | 14 x 12 |
| 9 | 14 x 13 |

Later growth tiers exist in `rules.json`, but are beyond the current level cap. A terrace is a separately purchased, walkable outdoor extension. Its first step unlocks at level 10, costs 2,500 coins and adds 7 x 6 cells. Publish all enabled terrace steps from the authoritative ruleset and return the new layout revision on growth/purchase.

## 6. Starter state and current content

The normal client seeds one stove, two tables and four chairs, plus a menu holder and two windows. The three starter recipes are known at recipe level 1:

```json
{
  "recipes": { "roti_canai": 1, "nasi_lemak": 1, "cendol": 1 },
  "activeMenu": {
    "starter": ["roti_canai"],
    "main": ["nasi_lemak"],
    "dessert": ["cendol"],
    "drink": []
  },
  "startingItems": { "kopi_o_kaw": 3 },
  "startingIngredients": {
    "rice": 2, "egg": 2, "chili": 2, "flour": 2,
    "noodles": 1, "coconut_milk": 1, "palm_sugar": 1
  }
}
```

This fragment describes gameplay, not the exact API response schema. Starting recipes do not consume ingredients again, so the initial ingredient pack need not contain everything required for a later upgrade.

**Staff discrepancy requiring agreement:** Normal source startup ensures one free **hired chef**, one free **hired waiter**, and a separate owner, whose default role is waiter. That is three actors and two hired slots. The backend guide's example instead has an owner chef plus one hired waiter. Both arrangements can operate, but they have different staffing, throughput and next-hire cost. Do not silently treat them as identical.

### 6.1 Data-source boundaries

- `dishes.json` currently contains 9 loaded dish definitions, `ingredients.json` 16 ingredients, `furniture.json` 12 base furniture definitions, `staff.json` 6 visual/work types including owner variants, and `items.json` one consumable.
- The economy workbook/import contains additional design rows. Its dish/ingredient lists are not automatically appended to the loaded runtime lists by `ContentLoader`.
- A private development catalog optionally adds many further furniture definitions, floors, wallpapers and exterior pieces. It is not a production content entitlement.
- A backend item ID, a local definition ID and a sprite/animation key are different concepts. Publish an explicit mapping where they differ. Never collapse separately owned variants into one generic item.
- The appendices below provide the actual base IDs and definitions at this revision. Resolve deviations before server catalog generation.

## 7. Staff, market and garden

### 7.1 Staff

Hireable roles are chef, waiter and cleaner. Owner role variants are `owner_chef`, `owner_waiter` and `owner_cleaner`; they describe one owner's assigned job, not three separate owners.

- Hired staff share the level's slot cap. The owner is additional and is excluded from the current recruitment-price count.
- Energy is 0-100. Active staff lose 1 energy/minute, including idle time. Resting staff recover 5/minute and return automatically at 100.
- A busy worker sent to rest finishes the current job before going off duty.
- Energy speed bands are 80+: x1; 60+: x0.8; 40+: x0.5; 20+: x0.3; below 20: x0.05. They affect movement and work speed.
- The current consumable `kopi_o_kaw` costs 30 and restores **40 energy** in `items.json`, capped at 100. The backend guide shows 25; see the discrepancy register.
- Refill-all spends available consumables on the most tired workers. It is not an unlimited free restore.

Backend persistent identities must distinguish two hires of the same role. Current local `StaffSave.typeId` is not a globally unique employee ID.

### 7.2 Ingredient market

Current local behavior: capacity 4, starts full, one basket becomes ready every 90 seconds until full. Collect empties all currently ready baskets; each ready basket rolls **one ingredient unit** from the loaded rarity-weighted ingredient pool.

The backend guide describes independently indexed baskets with three ingredient units each and a per-basket collect command. Adopting that API requires both a mobile UI/model change and an explicit reward-quantity decision. The server should return actual awarded ingredients and authoritative ready times; the client must not also grant its local random roll.

### 7.3 Garden

- Nine plots have level gates in the economy table: 3, 6, 9, 13, 18, 22, 26, 30, 34. Only the first three are reachable under the current cap.
- Planting costs 300 coins and selects a crop from eligible garden ingredients; it is not a client-selected arbitrary ingredient grant.
- Growth uses each ingredient's watered grow duration. A watering period is 9 hours. A dry crop pauses and never dies; watering a grown crop must not reset its progress.
- Current harvest quantity is 3.
- Persist crop ID, watered growth already earned, water expiry and last update time. The server clock must determine authoritative online progression and rewards.

## 8. Functional furniture: toilets, sinks and entertainment

### 8.1 Bathroom flow

From restaurant level 8, a guest has a configured 0.333 chance of visiting a working toilet after paying. Current durations: toilet 10 seconds, sink 4 seconds, cleaner repair 6 seconds. With no working available toilet, the guest leaves unhappy. A sink is used if available after the toilet.

Toilets have per-definition break counts. A cleaner can repair one, or the player can tap a broken fixture for immediate repair. Current player repair does not debit a fee. Sitting/hand motion, activity bubbles and progress indicators are rendered locally.

### 8.2 Entertainment flow

```text
No dining seat -> reserve reachable working machine -> walk to its front
               -> play 30 seconds -> one automatic 1-coin payment
               -> increment wear -> release machine -> leave
```

- A machine accepts one guest at a time. Its front access tile must be reachable.
- This is a fallback to dining, not an additional activity automatically awarded after every meal.
- No manual coin-collection tap is required.
- Entertainment currently adds no meal-served count, XP, ingredient or happy-meal demand reward.
- At the definition's wear threshold the machine breaks; it cannot accept another guest until repaired.
- Machine on/off/broken states and guest motion/progress are client visuals.
- Human-played arcade minigames are not implemented by this feature.

The private catalog currently includes `ref_arcade_machine_snake` (1 x 1), `ref_pinball_machine` (1 x 2), and `ref_wishing_well` (3 x 3). Definitions carry their own costs, unlock levels and break counts; the well is future level-gated content.

Durable wear needs a stable furniture instance ID. Current local instance numbers/coordinate-based legacy records are not an adequate cloud identity. Moving, storing or reordering a piece must not reset authoritative wear. The local inventory currently aggregates copies by definition; individual stored-copy wear requires further client/backend work.

## 9. Interior layout geometry and editing

### 9.1 Canonical client coordinates

- Grid origin (0,0) is the back corner.
- +X projects down-right on screen; +Y projects down-left.
- A floor item's origin is the minimum X/Y corner of its rotated footprint.
- Room: `0 <= x < RoomWidth`, `0 <= y < RoomHeight`.
- Terrace: `0 <= x < TerraceWidth`, `RoomHeight <= y < RoomHeight + TerraceDepth`.
- The containing floor grid has width `max(RoomWidth, TerraceWidth)` and height `RoomHeight + TerraceDepth`; cells outside the actual room/terrace shape remain blocked.
- The left street is a separate exterior strip, not part of the purchased terrace. Only explicitly permitted outdoor decor/plants may occupy designated side cells; the walking lane and door route remain clear.

| Facing | Direction | Rotated footprint |
| --- | --- | --- |
| 0, SouthWest | +Y | Definition sizeX x sizeY |
| 1, SouthEast | +X | Definition sizeY x sizeX |
| 2, NorthEast | -Y | Definition sizeX x sizeY |
| 3, NorthWest | -X | Definition sizeY x sizeX |

Wall side 0 is **back-left**: X = 0, index along Y, length RoomHeight. Wall side 1 is **back-right**: Y = 0, index along X, length RoomWidth. These labels differ from the supplied backend guide. Establish one conversion convention with asymmetric-room fixtures; do not assume equal dimensions hide the problem.

### 9.2 Placement rules

- Floor objects block their complete rotated footprint. Independent floor furniture cannot overlap, extend off valid floor, or occupy the entrance/waiting cells.
- Chairs and tables are separate pieces; a chair's facing/adjoining table determines its dining association.
- Wall pieces consume wall slots, not floor cells, and cannot overlap a door or each other.
- `stackable` objects may stand on `surface` objects when the fit is valid. The stacked object blocks no additional floor cells; it follows/stores with its support.
- Floor IDs are stored per cell, and wallpaper IDs per wall segment. A single default-floor string does not preserve a mixed painted design.
- Shop copies beyond owned quantity are charged on Save. Storing is not selling: stored pieces remain owned. Do not debit again for moving or re-placing an owned copy.
- Undo/Cancel operate on a draft. Failed purchase/layout validation must not partially charge or partially replace the live room.
- Legacy pinball layouts are migrated once using `entertainmentAxesVersion`, keeping occupied cells by correcting facing. This marker is local migration metadata, not an approved API field.

## 10. Exterior facade

This is a second editor, separate from the indoor grid and walkable terrace.

- Kinds: Wall, Roof, Door, Window, Sign, WallDecor and Ground.
- Coordinates use a 1000 x 680 design space, with bottom-center item anchors and independent flip state. Design-space Y increases downward; keep this distinct from the isometric floor grid.
- Each saved piece has an instance, item ID, X, Y and flip flag. Ownership is saved independently from placement.
- Swapping a roof must not resize the building body to the roof's bitmap bounds.
- Building size uses level tiers 5/10/15/20/30/40. Tiers beyond 10 are future content; they do not override the Phase 1 playable cap.
- Save validates required facade pieces, fixture overlap, attachment/placement bounds, ownership and purchases.

Backend facade storage/purchase support is requested in BE-08 of the companion review. An indoor layout or terrace response cannot reproduce this state alone.

## 11. State ownership, persistence and lifecycle

| State | Current local representation | Required cloud behavior |
| --- | --- | --- |
| Coins/progress/demand | `SaveData.coins`, level, per-level XP, demand | Server-authoritative balances, rewards and progression; convert XP explicitly |
| Recipes/menu | Recipe levels and active dish IDs | Validate learn costs/category slots; preserve revisions |
| Ingredients/items | ID/count lists | Server awards/debits; never duplicate local rolls |
| Staff | Type, home cell, energy, resting | Stable server employee identity, role, owner flag, duty and energy timestamps |
| Indoor layout | `LayoutSnapshot`, floors, walls, door, owned counts | Full geometry/paint/ownership round trip and atomic buys |
| Functional wear | Legacy coordinate records and machine usageCount | Stable instance-bound authoritative wear/repair |
| Garden/market | Crop timestamps/progress; aggregate ready-basket state | Server time and exact grants; agreed per-basket model |
| Facade | `ExteriorSave.pieces` and `owned` | Independent saved facade and purchases |
| Profile/avatar | Player ID, name, avatar parts, linked-account metadata | Owned account profile; agreed rename/avatar support |
| Audio/language/frame rate | Per-device preferences | Remain device-local unless account sync is explicitly desired |
| NPCs/orders/animation | Transient simulation objects | Not saved as authoritative per-frame cloud state |

`SaveData` is currently version 6. `LayoutSnapshot` is version 2 with additional migration metadata. They are separate local save concerns. Raw local staff type IDs and runtime furniture InstanceIds must not be mistaken for persistent backend entity IDs.

Normal play saves roughly every 30 seconds and on pause/quit, using temporary-file replacement. On resume, the local idle system estimates activity. Customers and in-flight orders are not persisted: the dining room restarts its visits. Decoration pauses the restaurant simulation while an unpaid draft is being edited; pending local economy settlement can still run.

For online integration, retain responsive local predictions, but reconcile to one server result. Never run both local idle rewards and server settlement as two confirmed grants for the same interval. Do not overwrite a local legacy test save with a fresh cloud account without an agreed migration/backup flow.

### 11.1 Backend event boundaries

| Mobile event | Backend responsibility | Must not happen |
| --- | --- | --- |
| Meal paid | Validate dish/menu/recipe/rate; settle coins, XP, demand and level growth once | Pay again when the plate is cleaned |
| Guest unhappy | Apply one appropriate demand event | Also record a successful meal for that failure |
| Recipe trained | Consume ingredients, raise recipe level and grant configured XP | Consume/grant twice after a retry |
| Menu saved | Validate learned dishes, categories, slots and revision | Keep an invalid empty/duplicate menu |
| Renovation saved | Validate layout and ownership; debit new copies atomically | Save an unpaid draft or reset wear |
| Staff changed/snack used | Validate slots/role/resources; return staff and wallet/items | Lose ownership or apply snack twice |
| Market/garden collected | Grant server-chosen rewards and update timers/state | Also grant local random rewards |
| Functional use completed | Validate eligible instance/use and settle wear/reward once | Trust client coin totals or array indexes as durable identity |
| Facade saved | Validate independent facade and purchase transaction | Confuse facade design coordinates with grid cells |
| Reconnect/return | Replay supported actions and settle eligible time once | Double-settle the same receipt/time window |

### 11.2 Target API mapping and startup flow

The routes below are described in the backend-supplied mobile guide; they are not proof that the current Unity adapter already calls them correctly. Use the companion review for missing contracts and proposed extensions.

| Mobile intent | Documented v2 operation | Integration note |
| --- | --- | --- |
| Initialize player | `POST /players/bootstrap` | Idempotent initialization; preserve established state |
| Load active rules | `GET /config` | Cache exact ETag and ruleset version; handle 304 |
| Recover authoritative state | `GET /state`, `/progress`, `/wallet` | Model cumulative XP, coins and revisions explicitly |
| Submit meal / failure | `POST /sales`, `/demand-events` | One outcome per relevant event; authoritative rewards |
| Learn or upgrade | `POST /recipes/{dishId}/learn` | Server decides ingredient consumption and recipe level |
| Save menu | `PUT /menu` | Whole menu with its own revision |
| Read/save restaurant | `GET /restaurants/{id}/layout`, `PUT /restaurants/{id}/layout` | Whole mapped layout, buys, loaded revision and required key |
| Sell stored furniture | `POST /furniture/{id}/sell` | A backend capability; does not imply a completed mobile sell UI |
| Buy terrace | `POST /restaurants/{id}/terrace` | Apply returned size and refreshed layout revision |
| Staff/resources | `/staff`, `/staff/owner`, `/items/{id}/buy`, `/items/{id}/use` | Persistent employee IDs; duty/rest contract still needs confirmation |
| Market/garden | `/market/{index}/collect`, `/garden/{plot}/plant`, `/water`, `/harvest` | Explicit basket-model adaptation and server-time state |
| Recover offline actions | `POST /sync/ledger` | Full per-action payloads, keys and dependency/order rules |
| Discover/visit | `/restaurants/discover`, `/restaurants/{id}/layout`, `/restaurants/{id}/visits` | Public read-only restaurant visit; no implied multiplayer/help reward |
| Daily reward | `/rewards/daily`, `/rewards/daily/claims` | Backend capability requiring mobile wiring; not a general events system |

Target startup: verified Supabase sign-in -> bootstrap -> fetch/revalidate config -> settle/replay supported offline actions -> apply authoritative state -> load any market/garden/functional state omitted from that state -> resume local NPC simulation. The empty-ledger time-settlement step remains an explicit BE-05 decision; do not omit or duplicate it by assumption. Resolve uncertain pending writes with their original identity before treating the outcome as a new action. Keep pending queues and save ownership separated by account.

## 12. Decisions to resolve before backend implementation is finalized

| Topic | Current mobile implementation | Other documented value | Required decision |
| --- | --- | --- | --- |
| Starter staff | Owner waiter + hired chef + hired waiter | BE example: owner chef + hired waiter | Agree starter composition and paid-hire count |
| Starter furniture | Two tables, four chairs, one stove | Imported settings still contain startingTables = 4 | Use the agreed starter block; do not activate this legacy field blindly |
| Starting recipes | Three known starter dishes | BE bootstrap example empty | Agree playable onboarding |
| Nasi lemak ingredients | rice, coconut_milk, anchovy | Imported workbook/BE question refers to coconut | Agree canonical recipe IDs |
| Market reward | One ingredient unit per ready basket; collect all ready | BE: three units per indexed basket | Agree reward quantity and collect UI/semantics |
| Snack energy | 40 from loaded item definition | BE: 25; workbook snackRefill 0.25 | Agree one authoritative restore value |
| Entertainment | Rules: 30 seconds, 1 coin/use; catalog wear; player tap repair free | Workbook has funCoinsPerUse 3, default uses 8, repair cost 40 | Agree active fields; do not activate unused legacy settings blindly |
| Recruitment count | Excludes owner | BE includes owner | Agree formula interpretation without silently changing coefficients |
| XP representation | Within-level XP | BE cumulative XP | Define conversion and fixtures; no need to change award coefficients |
| Layout | Positive-Y terrace; wall 0 = left | BE negative-Y terrace; wall 0 = right | Agree mapping/versioning |
| Outdoor flag | outdoorDecor permits designated street placement; it does not itself prohibit indoor placement | BE outdoorOnly denotes an outside-only restriction | Define permission versus restriction separately |
| Content coverage | Loaded base files plus optional development catalog | Workbook rows and BE catalog are different sets | Publish one supported manifest and mappings |

These are implementation differences and product decisions, not evidence of live-server defects. Keep unresolved features explicitly unsupported/local-only rather than silently changing values or dropping data.

## 13. Appendix A: Loaded base dishes

Generated from the checked-in `dishes.json`. Base price and durations are loaded definition values, not a substitute for an agreed server catalog or the workbook's numeric authority. The final sale price also applies recipe-level scaling. Recipe amounts are one unit per listed ingredient in this snapshot.

| ID | Category | Unlock level | Base price | Cook / eat seconds | Known at start | Recipe ingredient IDs |
| --- | --- | --- | --- | --- | --- | --- |
| `roti_canai` | starter | 1 | 10 | 4 / 6 | Yes | `flour`, `egg`, `curry_spice` |
| `satay` | starter | 2 | 14 | 6 / 7 | No | `chicken`, `peanut`, `lemongrass` |
| `nasi_lemak` | main | 1 | 12 | 5 / 8 | Yes | `rice`, `coconut_milk`, `anchovy` |
| `mee_goreng_mamak` | main | 3 | 13 | 5 / 7 | No | `noodles`, `egg`, `chili` |
| `char_kway_teow` | main | 4 | 15 | 6 / 8 | No | `noodles`, `prawn`, `egg` |
| `nasi_kerabu` | main | 7 | 16 | 6 / 8 | No | `rice`, `chicken`, `chili` |
| `laksa` | main | 9 | 17 | 7 / 9 | No | `noodles`, `coconut_milk`, `prawn` |
| `cendol` | dessert | 1 | 9 | 4 / 5 | Yes | `coconut_milk`, `palm_sugar`, `pandan` |
| `teh_tarik` | drink | 5 | 8 | 3 / 4 | No | `tea_leaves`, `condensed_milk`, `palm_sugar` |

## 14. Appendix B: Loaded base furniture

Generated from the checked-in `furniture.json`. Dimensions are before facing rotation. These are logical IDs used by the client; optional private-catalog additions are not included. A false surface field is treated as true for Tables by the runtime definition.

| ID | Kind | Base footprint | Price | Unlock level | Stackable / surface / outdoor |
| --- | --- | --- | --- | --- | --- |
| `dining_table` | Table | 1 x 1 | 80 | 1 | No / Yes / No |
| `wooden_chair` | Chair | 1 x 1 | 20 | 1 | No / No / No |
| `jar_cabinet` | Counter | 1 x 1 | 90 | 3 | No / Yes / No |
| `potted_palm` | Plant | 1 x 1 | 30 | 1 | No / No / No |
| `menu_board` | WallDecor | 1 x 1 | 0 | 1 | No / No / No |
| `window_basic` | WallDecor | 1 x 1 | 150 | 1 | No / No / No |
| `window_classic` | WallDecor | 1 x 1 | 600 | 2 | No / No / No |
| `clay_oven` | Stove | 1 x 1 | 250 | 3 | No / No / No |
| `gas_stove` | Stove | 1 x 1 | 120 | 1 | No / No / No |
| `sink` | Sink | 1 x 1 | 90 | 2 | No / No / No |
| `fridge` | DrinkMachine | 1 x 1 | 150 | 1 | No / No / No |
| `bread_shelf` | Decor | 2 x 1 | 90 | 2 | No / No / No |

## 15. Appendix C: Loaded ingredients and staff types

Ingredient IDs, generated from `ingredients.json`:

`rice`, `egg`, `flour`, `noodles`, `chicken`, `peanut`, `condensed_milk`, `coconut_milk`, `anchovy`, `palm_sugar`, `tea_leaves`, `curry_spice`, `prawn`, `chili`, `lemongrass`, `pandan`.

Staff work-speed defaults to 1 when absent from the JSON:

| Type ID | Role | Owner variant | Base walk cells/second | Base work multiplier |
| --- | --- | --- | --- | --- |
| `chef` | Chef | No | 2.7 | 1.2 |
| `waiter` | Waiter | No | 3.4 | 1 |
| `cleaner` | Cleaner | No | 3.0 | 1.5 |
| `owner_chef` | Chef | Yes | 2.6 | 1.0 |
| `owner_waiter` | Waiter | Yes | 3.0 | 1 |
| `owner_cleaner` | Cleaner | Yes | 2.8 | 1.2 |

## 16. Source map and backend handoff checklist

| Subject | Repository source |
| --- | --- |
| Kitchen/customer behavior | `Assets/_Game/Scripts/Logic/Simulation/RestaurantSim*.cs` |
| Wallet/progression/actions | `Assets/_Game/Scripts/Logic/Session/RestaurantSession*.cs` |
| Economy formulas | `Assets/_Game/Scripts/Logic/Content/EconomyTable.cs` |
| Active content construction | `Assets/_Game/Scripts/Game/Content/ContentLoader.cs` |
| Staff initialization | `Assets/_Game/Scripts/Game/StressTest/StressTestBootstrap.cs`, `Game/Controls/StaffController.cs` |
| Market/garden | `Assets/_Game/Scripts/Logic/Progression/IngredientMarket.cs`, `Garden.cs` |
| Local save/idle | `Assets/_Game/Scripts/Logic/Save/SaveData.cs`, `OfflineLedger.cs`, `OfflineProgress.cs` |
| Coordinates/layout | `Assets/_Game/Scripts/Logic/Layout/RestaurantLayout*.cs`, `LayoutSnapshot.cs`, `LayoutMigration.cs` |
| Facade | `Assets/_Game/Scripts/Logic/Layout/ExteriorLayout.cs`, `Logic/Session/RestaurantSession.Exterior.cs` |
| Existing network adapter | `Assets/_Game/Scripts/Game/Online/*.cs`, `Assets/_Game/Scripts/Logic/Online/*.cs` |
| Content snapshot | `Assets/_Game/Resources/Content/malaysia/*.json` |
| Pinball/private catalog import | `Tools/RefProto/catalog_import.js` |

Before declaring the backend ready for mobile integration:

- Resolve the discrepancy register with product/mobile and publish the actual active ruleset.
- Publish complete schemas, versioning, ID mappings and supported-feature flags.
- Supply executable request/response fixtures for bootstrap, first sale, level growth, mixed decoration, staff/resources, retries, ledger replay and time settlement.
- Cover entertainment/facade persistence or explicitly agree their temporary local-only treatment.
- Pass the acceptance sequence in the companion API review, then perform real development-server and physical-device integration tests.
