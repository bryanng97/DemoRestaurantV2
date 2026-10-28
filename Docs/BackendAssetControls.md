# Backend Asset Controls

Delivery roadmap Phase 1, 2026-10-28. Implemented and verified locally, with hosted rollout pending.
Backend contract: `C:/Bryan/Personal/restaurant-city-backend/docs/backoffice-asset-controls.md`.

## Client behavior

`ServerCatalog.ApplyFurniture` refreshes existing furniture commerce fields in place, preserving
references held by placements. For new floor/wall furniture records it recognizes a matching
`assetKey` template, adds a definition and reuses its art through `LoadedContentPack.ArtForFurniture`.
Unsupported templates or mismatched kinds/footprints are not created. Existing unregistered
prototype content keeps the project's local-only behavior.

`ContentLoader` applies cached `online_config_<pack>.json` metadata before decoding saved layouts.
`OnlineSession.LoadConfig` applies fresh data after a ruleset refresh. Localized names use synthetic
`catalog.furniture.<server-id>` keys through `Loc`, including language switching. Storage still shows
owned withdrawn pieces, while new purchases are rejected by the local authority as well as the server.

Wallet/Level/XP corrections use existing server-state reconciliation: queued predictions stay above
the server value until their results resolve, then the authoritative wallet/progression is adopted.
Level changes must satisfy the backend's capacity/menu/crop checks; rooms never shrink.

## Delivery boundaries

- Configuring effects is Phase 1; applying cooking, demand, patience and bed effects is Phase 3/4.
- This mechanism reuses shipped furniture art. It is not a remote artwork download system.
- Floors/wallpaper and new staff-item/facade identities still require compatible client content.
- Numeric configuration changes do not replace a client update for a new mechanic or template.
- Hosted migration/API/backoffice rollout and a compatible device build remain pending.

## Verified results

- Game and logic-test C# projects compiled successfully.
- Unity 6000.6.4f1 EditMode logic suite: **437 passed, 0 failed, 0 skipped**.
- Unity catalog presentation group: **2 passed, 0 failed, 0 skipped**.
- New logic cases cover commerce refresh without replacing placed definitions, idempotent variant
  loading, snapshot restoration, unknown templates/geometry, withdrawal with retained ownership,
  and administrative state changes alongside pending sales.
- Presentation checks cover the actual loaded client art mapping and English/Malay/Chinese names.

Reports: `Logs/phase1-controls-logic-tests.xml`, `Logs/phase1-controls-unity.log`,
`Logs/phase1-catalog-presentation-tests.xml`, `Logs/phase1-catalog-presentation-unity.log`.
Physical-device and full-scene visual checks were not part of these headless runs.
