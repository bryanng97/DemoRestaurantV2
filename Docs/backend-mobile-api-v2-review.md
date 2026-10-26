# DemoRestaurant: Backend Changes Required for Mobile API v2 Integration

**Date:** 2026-10-25  
**Audience:** Backend team, mobile team, product/design  
**Reviewed client revision:** `eb36489`  
**Input:** `mobile-api-v2.md`, supplied by the backend team

**Companion:** [Mobile App Gameplay and Backend Reference](mobile-app-backend-reference.md) describes actual client behavior and includes source-generated base content tables. Its discrepancy register also records differences in starter staffing, market reward quantity, snack energy recovery and unused legacy entertainment settings; resolve these before ruleset sign-off.

## 1. Decision and scope

The API can support the core restaurant game, but it is **not directly compatible with the current Unity client**. Connecting the existing client without adaptation would cause startup failures, incorrect layout mapping, inconsistent progression and rejected offline actions.

Some findings require Unity changes; they do not require the backend to restore the old API. Other findings require backend contract clarification or additional functionality before the current game can be fully synchronized.

This is a document-and-source review, not a live-server acceptance test. A field or endpoint absent from the integration guide is considered **unconfirmed**, not proof that the implementation lacks it. Every endpoint and JSON field explicitly labeled **Proposed** below is a requested design, not an existing API.

Preserve the existing economy workbook's prices, XP coefficients, rewards and Phase 1 level cap. Do not rebalance the economy as a side effect of integration. Temporary artwork is a client concern; the backend needs agreed logical item definitions, not image files.

## 2. Responsibility and priority

| ID | Priority | Required outcome | Owner |
| --- | --- | --- | --- |
| BE-01 | P0 | Publish the complete, current contract and ruleset fixtures | Backend |
| BE-02 | P0 | Make the new-player state immediately playable | Backend + product |
| BE-03 | P0 | Agree canonical content IDs, recipes and supported furniture definitions | Both teams |
| BE-04 | P0 | Resolve layout coordinates and preserve all supported decoration data | Both teams |
| BE-05 | P0 | Specify safe ledger replay, settlement and legacy-save handling | Both teams |
| BE-06 | P1 | Resolve XP representation, owner counting and staff energy behavior | Both teams + product |
| BE-07 | P1 | Add or explicitly scope entertainment income and functional-item persistence | Backend + mobile |
| BE-08 | P1 | Add or explicitly scope exterior facade persistence | Backend + mobile |
| FE-01 | P0 | Replace obsolete response/request models and wire authoritative state into gameplay | Mobile |

P0 items block a reliable core cloud integration. P1 items block full coverage of features already present in the client; they may be deferred only with explicit feature flags and an agreed local-only behavior.

## 3. Backend requests

### BE-01: Provide one complete, versioned contract

Please supply:

1. The referenced `api-v2.md`, `rulesets.md` and actual `malaysia.v1.json`.
2. The exact deployed ruleset version and a complete `/config` response, without omitted sections or ellipses.
3. Complete fixtures for fresh bootstrap, an established account, a level-up, a room-growth event, a terrace purchase, a layout conflict and partial ledger rejection.
4. OpenAPI/JSON schemas, or equivalent definitive field lists, including optional, nullable and read-only fields.
5. A feature-coverage matrix stating which current game features are supported online, unsupported, or disabled by Phase 1 rules.

Reconcile the mobile guide with the project's `Docs/backend-api-v2.md` and `Docs/backend-api-requests.md`. The guide says it was designed without the latter, so the documents must not be treated as interchangeable contracts.

**Acceptance:** Fixtures validate against the published schemas; unsupported request fields are identified before mobile implementation. Examples distinguish `data` payloads from complete response envelopes.

### BE-02: Fix or clarify the new-player bootstrap

**Finding:** The bootstrap example contains an empty recipe map and empty menu. `/sales` requires a learned dish on the menu, while `PUT /menu` rejects an empty menu. The current Unity game starts with known dishes. The guide also identifies an unresolved `coconut` versus `coconut_milk` recipe discrepancy.

An empty start is not necessarily a permanent deadlock because market ingredients may eventually permit learning. It is nevertheless a different onboarding flow and does not match the current immediately playable restaurant.

**Requested baseline:** Grant the currently known starter recipes at level 1 and put them on the starting menu, without charging ingredients or granting learning XP again:

```json
{
  "recipes": {
    "roti_canai": 1,
    "nasi_lemak": 1,
    "cendol": 1
  },
  "menu": {
    "revision": 1,
    "starter": ["roti_canai"],
    "main": ["nasi_lemak"],
    "dessert": ["cendol"],
    "drink": []
  }
}
```

This is a proposed initialization fragment; retain the other bootstrap fields. If product wants a learn-before-opening tutorial instead, define that alternative explicitly, including a guaranteed first learnable dish and all required ingredients.

Do not reset established players when bootstrap is called again. Define a one-time repair migration for existing development accounts accidentally created with an unplayable/unfinished starter state.

**Acceptance:** A fresh account can serve its first meal without waiting for a random ingredient drop or spending unrelated currency; repeated bootstrap grants nothing twice.

### BE-03: Align content IDs, recipes and catalog coverage

**Findings:**

- Backend examples use IDs such as `table_basic`, `chair_basic` and `stove_basic`; client definitions include `dining_table`, `wooden_chair` and `gas_stove`.
- Current client nasi lemak uses `rice`, `coconut_milk` and `anchovy`. The backend guide flags a workbook recipe using `coconut` instead. These are not automatically equivalent ingredients.
- The guide advertises 40 furniture entries. The development client has a much larger temporary catalog, including interactive machines and many decoration variants.
- The documented `kind` values do not include `Entertainment`.

**Required changes/decisions:**

1. Publish an agreed ID manifest for dishes, ingredients, furniture, floors, wallpapers, staff consumables and exterior items.
2. Resolve recipe ingredient membership with design. Use the agreed IDs in `/config`, recipe validation, market/garden rewards, inventories and bootstrap.
3. Define a stable mapping where local rendering keys differ from backend IDs. Do not merge distinct purchasable variants into one server item merely because they share a broad category.
4. Publish authoritative size, rotation, category, placement, stackability, availability, unlock level, price and break-count data for every supported item.
5. Register supported development-only items in a development catalog, or explicitly report them as unsupported. Do not silently replace or delete them when loading a cloud layout.

The corrected pinball footprint is **1 x 2 at facing 0**, and **2 x 1 at facings 1 and 3**. This matters to overlap validation and the NPC's front access tile.

**Acceptance:** A supported item round-trips through purchase, placement, storage, reload and sale without changing identity, dimensions or ownership. All ingredient rewards can be resolved by the client.

### BE-04: Agree layout coordinates and preserve decoration

The current conventions are different:

| Area | Current Unity convention | Mobile API guide |
| --- | --- | --- |
| Room | Positive X/Y; origin at the back corner | Nonnegative room X/Y; physical orientation needs confirmation |
| Terrace | `0 <= x < terraceWidth`, `roomHeight <= y < roomHeight + terraceDepth` | `0 <= x < terrace.width`, `-terrace.depth <= y < 0` |
| Wall side 0 | Back-left wall, X = 0, index along Y | Back-right wall, length = room width |
| Wall side 1 | Back-right wall, Y = 0, index along X | Back-left wall, length = room height |
| Facing | 0 = +Y, 1 = +X, 2 = -Y, 3 = -X | 0-3 and swapped footprints are specified; physical directions are not explicit |
| Street decoration | Designated cells outside the room, including negative X | No equivalent street-zone representation is specified |

**Requested decision:** Prefer a documented wire convention matching the existing client, or retain the backend convention and publish complete conversion fixtures. Either is implementable. Do not independently swap wall IDs or offset only the terrace without checking room orientation, multi-cell anchors, facings and adjacency.

If a deployed convention changes, version or negotiate it; do not silently reinterpret already stored layouts. The client has a local `entertainmentAxesVersion` migration marker, which is not an existing API request field and must not be sent accidentally.

**Also confirm/add the following:**

- Per-cell floor painting. A single `floor` ID cannot represent a mixed floor design. A proposed extension is `floorOverrides: [{ "x": 2, "y": 3, "id": "floor_variant" }]`, with default room/terrace floors retained. Publish bounds, duplicate-cell rules, pricing and null/default behavior.
- Per-wall-segment wallpaper. The project contract already describes `wallpaperLeft` and `wallpaperRight`; the mobile guide does not establish whether they are accepted or omitted from examples. Include them in the definitive schema or provide an equivalent representation.
- Street-only decoration zones, outdoor permissions, door clearance and the distinction between a walkable purchased terrace and street decoration cells.
- Stable furniture instance identity; see BE-07. `stackedOn` array indexes may remain as a transport reference if validated against the same request, but must not be used as persistent identity.
- Server-owned room growth never removes already owned space. Clarify the guide's exact-size requirement versus the project contract's grandfathering of larger authoritative rooms. Client-only oversized debug saves are not trusted proof of ownership.
- Growth and terrace changes return or expose the resulting layout revision. Clients must re-fetch/rebase before saving an older draft.

Retain the reference behavior that a geometrically valid layout can be saved even if some pieces are unreachable. Backend full NPC pathfinding is not required for layout commits; the client handles service-access warnings and NPC routing.

**Acceptance:** Round-trip fixtures include all four facings of a non-square item, both walls, a moved door, mixed floors, wallpapers, stacked furniture, terrace furniture and supported street decoration. Geometry, ownership and purchase debits commit atomically; a rejected save changes none of them.

### BE-05: Define ledger replay, time settlement and save migration

**Finding:** The current local ledger stores `kind`, `amount`, `refId` and a timestamp. Consecutive sales of one dish are merged into a coin total. It does not retain all per-action UUIDs, recipe levels, staff instance references or complete layout/menu payloads required by `/sync/ledger`.

The backend must not accept those aggregate coin claims as authoritative. The mobile team must record a new ledger format for future actions. Old records cannot always be reconstructed accurately.

**Backend contract requirements:**

1. Define one economy action identity across direct requests and ledger replay. A sale retried through the ledger after a lost online response must not award money twice.
2. Document ordering, per-entry transaction boundaries, dependencies and partial failures. A failed hire/learn/purchase must not allow dependent actions to grant resources illegally.
3. Define how offline-created entities obtain stable IDs. Later offline `item_use` or `dismiss` entries may refer to a hire whose server ID did not yet exist. Support agreed client-generated entity IDs or return an explicit reference mapping.
4. Specify accepted, terminally rejected and retryable outcomes. Accepted and terminally rejected entries must not be replayed forever. Return enough information to explain rollback to the player.
5. Document revision-conflict handling for queued layout/menu actions. Never require changing the body of a request whose outcome is still unknown while reusing its old identity.
6. Define settlement when there are **no pending actions**. Should startup call `/sync/ledger` with `{ "entries": [] }`, or does bootstrap already settle time away? Provide one canonical flow.
7. Guarantee that retries, concurrent devices and multiple 200-entry batches cannot settle the same time-away interval twice. Apply the existing server-clock rate and cap once per eligible interval; ignore client-provided earnings.
8. Confirm whether `/state` includes market, garden, staff duty/energy and functional-item state. If not, specify the additional startup fetches needed before local state is replaced.
9. Specify sale rate-limit burst tolerance and receipt behavior. Multiple legitimate meals may complete close together; retries must not create a second payment or silently discard a completed sale.

**Legacy-account policy required:** Keep an offline backup and let the player distinguish cloud state from local test state. Do not automatically replace a valuable local save with a fresh 400-coin server account, and do not import arbitrary local balances, levels or owned items without server validation. If migration is unsupported, document that limitation and preserve the local save separately.

**Acceptance:** Test lost responses, online-to-offline replay of the same action, duplicate batches, dependent-action rejection, more than 200 actions, clock rollback, two devices and no-ledger startup. Money and time-away income are granted at most once.

### BE-06: Resolve progression and staff rules

**XP:** The API defines cumulative `xp`; Unity currently stores XP earned within the current level and subtracts thresholds when leveling up. Keeping cumulative XP is acceptable, but publish the exact cumulative thresholds, cap behavior and level-up event data. The mobile team will convert to the existing progress bar rather than feed cumulative XP into the old `Restore(level, xp)` method. A test fixture should cover crossing a level and reaching the cap, where `nextLevelXp` is null.

**Owner counting:** The guide includes the owner in recruitment cost. The current client excludes the owner from both hired-member count and occupied staff slots. For example, owner + one hired waiter currently gives a next-hire cost of `100 + 60 x 1 = 160`; counting the owner gives 220.

Requested baseline: preserve the client's owner-excluded recruitment count and free owner slot, while retaining the workbook coefficients. If design intends owner-included cost, record that as an explicit approved rule change and return the authoritative `nextHireCost` to avoid conflicting UI quotes.

**Energy/rest:** The guide provides energy values and snacks, but does not define how going off duty, resting, recovery and automatic return are synchronized. Specify whether this is already supported. If missing, add an idempotent duty-state command, for example **Proposed** `PUT /staff/{id}/duty`, plus authoritative duty state, energy and update timestamps in staff/state responses. Use the configured drain/recovery rates and speed bands; do not trust client timestamps or repeatedly charge offline drain.

Keep Phase 1's level cap at 10. Features gated beyond that cap, such as drinks at level 15, must be explicitly marked unavailable in Phase 1 rather than treated as an integration defect.

**Acceptance:** Both teams agree on the next hire quote, free owner slot, XP bar, energy band and resting behavior before/after logout or reconnect.

### BE-07: Support entertainment and persistent functional wear

The client now supports a guest choosing entertainment when no dining seat is available, using it for 30 seconds, automatically paying one coin and leaving. Machines have exclusive occupancy, front access, native visual states and catalog break counts. The current prototype grants no entertainment XP, meal count or ingredient reward.

The guide does not define this income, its ledger type or the `Entertainment` catalog kind. Treating the item as ordinary decor would preserve its appearance but would not synchronize its gameplay.

**Required functionality:**

- `Entertainment` item definitions with correct dimensions, price/unlock rules and break count.
- Stable IDs for individual functional items, including toilets and machines. Array indexes are insufficient because moving, removing or reordering furniture changes them.
- Server-owned usage counts/broken state that survive movement, storage, reload and reordering. Clients cannot reset wear by submitting smaller counts in a layout.
- An idempotent repair operation with an explicit configured price, including zero if repair remains free. It must not introduce an unapproved repair fee.
- A validated completed-use reward path with one payout and one wear increment per use.

One possible **Proposed** online contract is:

| Method | Route | Purpose |
| --- | --- | --- |
| POST, Key | `/restaurants/{id}/functional-uses` | Begin a use for `{ "instanceId": "..." }`; return a server use ID and ready time |
| POST, Key | `/functional-uses/{useId}/complete` | Check server elapsed time and item eligibility; atomically return wallet and wear after completion |
| POST, Key | `/furniture-instances/{instanceId}/repair` | Repair an eligible owned instance and return authoritative wear/wallet |

Equivalent routes are acceptable. Define cancellation/expiry of abandoned uses, per-machine concurrency, move/store behavior during a use and retry handling. Client NPC reachability remains locally simulated in Phase 1; elapsed-time validation alone is not proof that a physical NPC visit occurred.

For offline uses, either add a documented, validated `functional_use` ledger action with server-derived payout and a bounded offline-use policy, or explicitly flag offline functional income as not synchronized yet. Reconcile its time budget with ordinary idle income so the same interval is not rewarded twice. Do not silently discard `EntertainmentIncome` or accept claimed coin totals.

**Acceptance:** An NPC use pays once; an early, duplicate, invalid or broken-item use pays nothing. The configured wear threshold causes breakage, and moving/storing the machine does not repair it. The pinball machine occupies the correct cells in all four facings.

### BE-08: Support the exterior facade separately from the terrace

The client already has a street-facing facade editor: roof, building/body, door, windows, sign, wall ornaments and street decorations. A purchased walkable terrace is not a substitute for this facade.

Confirm whether the routes requested in the project's existing backend requirements have been implemented. If not, add the following or an equivalent contract:

| Method | Proposed route | Purpose |
| --- | --- | --- |
| GET | `/exterior` | Return facade pieces, ownership and revision |
| POST, Key | `/exterior/commits` | Atomically validate decoration, buy missing copies and return the facade, revision and wallet |

Each piece needs stable instance identity, catalog item ID, position and flip state. The existing renderer uses a 1000 x 680 design space with bottom-center anchors. This is separate from the isometric restaurant grid.

Publish facade catalog bounds/placement metadata, price, availability and unlock levels. Validate the building, roof and door requirements, fixture overlap, wall/ground placement and door clearance. Store owned pieces without losing them. Preserve stable item identity when temporary artwork is replaced.

Facade visual tiers currently use levels 5/10/15/20/30/40. Phase 1 only reaches 10; later tiers remain future content and must not raise the playable level cap implicitly.

**Acceptance:** Facade purchases and saves survive relaunch and same-account device changes; retries do not double-charge. Public visits can show the saved facade if that is included in the agreed feature scope.

## 4. Mobile changes: not backend regressions

The mobile team owns these changes once the contract is agreed:

- Replace obsolete DTOs: current bootstrap expects `wallet`, catalog expects a paginated generic `items` list, and layout expects `{ version, layout }`. Those are different from the new guide.
- Replace old request bodies/routes: current layout save sends `{ expectedVersion, layout }` without the new required key; the old generic `/purchases` and restaurant-creation flow must be removed or adapted to bootstrap plus layout `buy` and item-specific purchases.
- Connect sales, demand events, recipes, menus, staff, inventories and purchases to the authoritative server result. Existing local simulation and the online connection are not yet one integrated gameplay authority.
- Implement config caching, ETag/304 handling, ruleset headers/version refresh and the new error-code/details shapes. Keep authentication refresh and bounded retries.
- Parse and round-trip the guide's nested maps and nullable values using a tested serializer/model implementation. Validate against exact JSON fixtures, not only successful HTTP status codes.
- Apply coordinate/content mappings, XP conversion, stable instance IDs and revised save revisions.
- Record complete per-action ledger payloads and stable keys before sending; reconcile accepted/rejected actions without keeping their predictions as additional confirmed rewards.
- Keep NPC movement, collision, seating, cooking, bathroom/entertainment animations and touch UI in Unity. Backend integration does not implement these visuals or create real-time multiplayer.

The backend does **not** need to restore `wallet` inside bootstrap solely to satisfy the current client. The client should adapt to the agreed v2 shape.

## 5. Integration acceptance sequence

1. **Bootstrap:** Fresh, verified account can open the restaurant, serve a starter dish and reconnect without duplicate starter grants.
2. **Economy:** One meal produces one confirmed sale, XP/demand update and any level reward; retries and ledger replay cannot double-pay.
3. **Layout:** Buy, place, rotate, stack, store, paint floors/walls and reload supported pieces. Test all non-square facings, both walls, terrace coordinates and door placement.
4. **Progression:** Cross room-growth and terrace unlock boundaries, then save a formerly stale draft through the documented conflict flow.
5. **Staff and resources:** Hire/dismiss, change the owner role, rest/recover, buy/use snacks, collect market baskets and plant/water/harvest using server time.
6. **Offline:** Test partial rejection, operation ordering, same-action replay, multiple batches, no-ledger settlement and legacy local-save protection.
7. **Existing extensions:** Validate entertainment rewards/wear/repair and facade commits, or explicitly demonstrate that unsupported features remain local-only without corrupting cloud state.
8. **Visits:** Load another player's supported public layout as read-only; confirm there is no implied real-time NPC sharing or friend-help reward unless a separate contract provides it.

Backend completion should include updated schemas/rulesets and passing request/response fixtures for these cases. Mobile acceptance then requires a development-server round trip and physical-device testing; this review alone does not establish production readiness.

## 6. Source references

- Backend-supplied `mobile-api-v2.md`: sections 2, 4-9 and 12-14.
- Project contract: `Docs/backend-api-v2.md` and `Docs/backend-api-requests.md`.
- Existing feature notes: `Docs/EntertainmentPrototype.md` and `Docs/ExteriorPrototype.md`.
- Existing API adapter: `Assets/_Game/Scripts/Game/Online/GameApiClient.cs`, `GameApiModels.cs`, `OnlineSession.cs` and `SupabaseHttp.cs`.
- Layout conventions: `Assets/_Game/Scripts/Logic/Layout/RestaurantLayout.Terrace.cs`, `RestaurantLayout.Walls.cs` and `LayoutSnapshot.cs`.
- Existing progression and ledger: `Assets/_Game/Scripts/Logic/Progression/RestaurantProgress.cs`, `Assets/_Game/Scripts/Logic/Session/RestaurantSession.cs` and `Assets/_Game/Scripts/Logic/Save/OfflineLedger.cs`.
- Content evidence: `Assets/_Game/Resources/Content/malaysia/dishes.json`, `rules.json`, the imported `economy.json` and `Tools/RefProto/catalog_import.js`.
