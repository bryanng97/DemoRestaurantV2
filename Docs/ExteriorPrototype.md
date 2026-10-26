# Exterior prototype implementation

The gameplay reference is Dippys' Restaurant City client/server. The owner's decision on 2026-10-24 is to align the rules and interactions while keeping this project's economy workbook values, Malaysian recipes and current assets for the interior. This is a Unity implementation; it does not execute or embed the Flash client.

## Playable in this change

- The bottom-right **Restaurant street / 餐厅街道** button opens a side-on exterior. Enter restaurant, or tap your door, to return inside. The restaurant continues simulating while browsing the street. Street guests are drawn from the actual arriving/leaving customers, not a second income simulation.
- **Edit exterior / 编辑店外** pauses service and opens roof, wall, door, window, sign, wall decoration and pavement decoration categories. Mouse previews follow the pointer; touch uses tap/drag/release through the Input System UI module. Existing pieces can be moved, flipped or stored; Undo and Cancel operate on a draft. Save also accepts a valid active preview without requiring Deselect.
- Placements reject overlaps, out-of-bounds wall fixtures and pavement props blocking the entrance. One roof, wall and door are required. Removed pieces stay owned. Only copies beyond owned quantities are bought, once, on Save. A failed write changes neither ownership nor coins. Exterior prices are temporary catalog values imported from the reference archive; they do not modify the existing economy workbook or interior prices.
- The recipe book exposes **Learn recipe / 学习菜谱** and **Upgrade recipe / 升级菜谱**. Each operation consumes the catalog ingredients and the workbook's `ingredientSetsPerUpgrade`; upgrades stop at `maxDishLevel`. Serving prices, serving XP and upgrade XP use the existing workbook formulas. The ingredient experiment remains available.
- Save version 5 stores facade placements, owned exterior items and recipe levels. Version 1–4 saves receive the starter facade and level 1 for already known recipes. Indoor layouts remain separate and are preserved.
- Street buildings at the edges are explicitly labelled **Street preview / 街景示例**. They are static samples, not friends or live player restaurants.

## Replace the temporary artwork

`Assets/_Game/Resources/Content/malaysia/exterior.json` maps stable item IDs to independent `sprite` Resources paths, `nameKey`, category, price and dimensions in a 1000 × 680 design space. Coordinates represent bottom-centre anchors. Change a sprite path (or replace the corresponding PNG) while retaining the item ID to keep existing ownership and placements. Export replacements with the same aspect/visual bounds, or update dimensions and migrate old layouts deliberately. Add country packs through ContentLoader; gameplay code does not depend on Flash symbol names.

Wall tiles repeat across the facade. Roofs, doors, windows, signs and props use the exported sprite's full visual rectangle. Signs display the live profile name through Unity text. The building mask from the SWF is deliberately not imported: the original renderer uses it as an alpha mask for repeated wall tiles, not as finished artwork.

Prototype catalog roofs use the clean vector export from `prepare_exterior_export.ps1`. Its `mc_rect` object is an authoring marker that can hide real roof artwork; removing black pixels from a rendered PNG cannot reveal that artwork. `exterior_import.ps1` reads attachment widths from the original vector marker, then imports the roof with the marker removed. Roof attachment uses the native vector registration origin (`roofAnchorX/Y`) rather than the PNG bottom: the nest has downward twigs below its actual attachment point. `ExportRoofMetrics.java` reads this origin from the clean SWF bounds. Street framing uses the largest reachable facade plus all roof bounds at one fixed scale, so roof swaps preserve the wall and fixtures while level-ups visibly enlarge the wall. Selection tools use the same roof bounds at every building level.

Roof validation (2026-10-25): an isolated visual probe checked all 34 roof definitions at level-1 and future level-40 facade sizes, both facings (136 states), alongside the seven exterior layout tests; all eight checks passed. The future-size fixture raises the level cap only in memory; game progression values are unchanged. Captures: `Logs/roof-fixed-level1.png`, `Logs/roof-fixed-level40.png`, `Logs/roof-fixed-flipped.png`. An earlier touch regression run expected drag x=350 where the editor snapped to 360; the same failure was reproduced with its pre-change renderer (`Logs/exterior-touch-baseline.xml`).

### Provenance

- Original reference code: https://github.com/Dippys/Restaurant-City-client and https://github.com/Dippys/Restaurant-City-Server .
- Temporary original artwork: Restaurant City early beta **0.9.143a**, `outdoor_asset.swf`, from the public preservation archive linked by its uploader at https://www.reddit.com/r/RestaurantCity/comments/1n73lwg/need_help_archiving_restaurant_city_made_by/ .
- Archive link: https://www.mediafire.com/file/rpiw869pkbbmilj/Restuarant_City_0.9.143a.zip/file . ZIP SHA256: `4493BE9BC4C2F87EF80CA7AFA5321E12BB94A7EC6ED2F0A673B4D48B19170EF4`.
- Native vector-to-PNG export: JPEXS FFDec 26.4.0, 2× scale, first sprite frame, transparent background. No SWF/ActionScript execution. `Tools/ExteriorAssets/prepare_exterior_export.ps1` removes only the `mc_rect` authoring markers (normally removed at runtime) and the `tf_name` Flash text field from the native vector display list before exporting. `Tools/ExteriorAssets/import_exterior.py` copies 15 exported frames and generates the data catalog and localized names. Original class names are recorded in that script; local archives/tool binaries remain in ignored `Builds/`.
- The original artwork belongs to its original rights holders; availability in a preservation archive is not a commercial-use license. These files are temporary prototype art at the owner's request and must be replaced/cleared before distribution. The archive is an early version, so it does not contain all later screenshot cosmetics.

## Remaining original-game systems

This change is not a complete Restaurant City remake. Friend street navigation, visiting real player restaurants, gifts/trading, original challenges/collections, toilets/sinks and the original demand algorithm are not implemented here. The existing staff energy, menu categories, cooking/serving/cleaning, recipe inventory, level/rating, garden, furniture editor and land expansion remain the project's own implementations with its workbook tuning.

Exterior purchases and recipe upgrades currently use offline authority and the offline ledger. They are **not** sent to a new live Supabase route. Cross-device exterior/recipe-level sync and authoritative online validation need backend contracts; see `backend-api-requests.md`. Do not treat the static street samples as online visits or local ledger entries as server-confirmed purchases.

## Validation (2026-10-24)

All 353 EditMode tests passed in the isolated `Builds/ValidationProject` clone. This includes real Input System touchscreen state events through the UI module for dragging, purchase/save, cancellation, language switching and returning through the door; ownership/bounds/overlap/transaction tests; recipe ingredient/level/save/authority tests; and the existing furniture/pathfinding/floor regressions. Batch tests explicitly route device input to the headless GameView and restore those editor settings afterward.

An IL2CPP ARM64 offline prototype APK was built successfully at `Builds/Android/DemoRestaurant-outdoor-20261024.apk` and its APK signature verified. No online configuration was copied into the validation clone. Native 1920 × 1080 UI render captures are in `Logs/rc-outdoor-street.png`, `Logs/rc-outdoor-editor.png` and `Logs/rc-outdoor-editor-zh.png`. Source scripts, content JSON and PNG hashes matched the build clone. Physical-device touch behavior has not been tested. The user's open Unity editor was left running.

Stable-framing regression (2026-10-25): `ExteriorRoofFramingTests` checks actual screen-space wall, door and window bounds across every available roof, flipping, Undo, Save and reopening, at level-1 and future level-40 facade sizes. Together with the roof visual probe and layout tests, all nine focused checks passed (`Logs/exterior-stable-framing-tests.xml`).

Exterior growth wiring (2026-10-25): the open street refreshes on the session's real LevelUp event without resetting decoration. Existing visual tiers remain 5/10/15/20/30/40: 5 and 10 widen the front, and later tiers add height. The workbook's current Phase 1 cap stays 10; prices, XP and level rewards are unchanged. The source archive lists multiple Body items with separate purchase prices; automatic level-based growth is this project's requested rule.

Registration/growth validation: 24 focused checks passed (`Logs/exterior-growth-attachment-tests-final.xml`), including the nest's internal anchor, both facings of all roofs, live XP-driven 1→5→10 growth, future 15/20/30/40 height tiers, unchanged fixture coordinates and saved decoration, layout validation and save/idle regressions. Later-level tests raise the cap only in memory. Native captures: `Logs/exterior-growth-level1.png`, `-level5.png`, `-level10.png`; combined preview: `Logs/exterior-growth-preview.mp4`.
