# Frontend: mobile decoration UI

Updated: 2026-10-27

## Implemented: indoor restaurant editor

- Retain the existing cream panels, wood borders and native furniture artwork.
- Use a separate 1280 × 720 decoration reference canvas, scaled by height. The rest of the HUD keeps its existing scale.
- Keep Shop, Storage, Undo, More and Done in a bottom dock within the safe area. Dock buttons share the available width; Rotate and Store appear when furniture is selected.
- Shop and Storage toggle a collapsible catalog. Selecting a different tab opens that catalog. Selecting furniture or a floor/wallpaper tool collapses it to expose the room.
- Open the catalog as a left drawer using 30% of the safe-area width. Browse a two-column furniture grid vertically; category tabs remain horizontally scrollable at the top. Give furniture images, two-line names and prices separate regions.
- Use compact, flat category tabs with subtle edge chevrons and a thin position indicator underneath. Chevrons scroll one tab width and disable at either end. There is no separate navigation row or swipe instruction text. Hide indicators in Storage or when categories fit; keep the furniture list directly under the header.
- Lock catalog gestures to their first clear direction: vertical movement scrolls and cannot become placement; rightward dragging transfers to placement only after crossing the drawer’s right edge. Collapse the drawer after a successful selection. Tapping a card also selects it.
- Put Redo, Fill, Store all and Cancel renovation in More. Hide the hint strip while More is open so it cannot cover menu actions.
- Keep the furniture hand actions in the bottom dock rather than floating over the selected piece.
- Localize the additional labels in English, Malay and Chinese.

## Layout details

All values below are Unity canvas reference units, not device dp or pt.

| Element | Reference dimensions |
| --- | --- |
| Bottom dock | Top edge at 116, bottom inset 8 |
| Category tab | 128 × 80; 8-unit gap; gold selected fill and dark underline |
| Catalog | Left 30% of safe width; from dock top to safe-area top |
| Furniture card | Two columns, adaptive width, 208 high, 12-unit gaps |
| More menu | 480 × 332 |

The dock background ignores the horizontal layout group. It must remain stretched behind the buttons and must never occupy a button slot.

## Validation and remaining checks

- Menu, Staff, Recipes, Market and Garden use the shared slide-panel position: 16 canvas reference units inside the left safe-area edge, replacing the old 150-unit toolbar reservation. Actions collapses and hides when a panel opens, stays hidden through closing animations or panel switches, and returns collapsed after all panels finish closing.

- Game and render-test C# projects compile successfully. Gesture regression assertions passed inside Unity: vertical scroll lock, rightward drag lock, tap threshold and leftward rejection.
- The left drawer has not received a completed visual review. Further validation should run in the background; foreground computer control requires prior user agreement.
- Physical Android/iOS touch sizing, narrow landscape screens, cutouts and the longest labels still need device validation. Reference units alone do not guarantee a physical touch size.
- Exterior visual and touch checks also remain pending; compilation does not confirm device layout.

## Restaurant camera and continuous street

- Street scenery now repeats across the inverse-projected camera viewport, with two spare segments beyond each end. Native road/sidewalk artwork is reused; fallback scenery repeats the near sidewalk, road and far sidewalk in short strips.
- Segments use a stable grid phase and a reusable pool. Panning and zooming reposition existing segments; they do not create an increasingly long road from the restaurant origin. Rebuilding a restaurant resets the pool with the outdoor root.
- Camera panning constrains the whole viewport within the room/garden scenery rectangle plus lawn margin. The far zoom limit also accounts for viewport width and height, and is rechecked after aspect-ratio changes. Guest routes and the playable sidewalk cells are unchanged.
- Background validation: game/test compilation; 60 viewport combinations covering extreme pans, zoom limits and five aspect ratios; 36 street coverage combinations across four landscape ratios and distant camera positions. These assertions ran against compiled assemblies without operating the user's desktop. Visual seam and device checks remain pending.

## Exterior and long-label improvements

- HUD drawer labels wrap onto two lines with a reserved 60-unit text region; icons are shortened to avoid touching the labels.
- Indoor category selection uses a stronger gold fill and a dark underline. Long category labels can wrap and shrink within their tab.
- Exterior keeps its 1920 × 1080 canvas. The editor has an Exit button at the top left, Undo and a fixed-width Done button at the top right, and a separate cost label under Done. Category tabs are 228 × 96 and scroll horizontally; item cards are 240 × 208 with separate name and price regions.
- Exterior item names and menu labels wrap and fit within their bounds. The English Save separator encoding is corrected.
- Editing hides sample neighbors and fits the player's facade into the space between the toolbar and shop. Browse mode restores the wide street view. Editing starts from RC's 1.2 zoom, fitting only the current facade body and normal roof headroom into the available work area. Unrelated roof catalog bounds do not affect zoom; switching roofs preserves the framing. The Enter restaurant button is hidden in edit mode.
- Existing roof-framing test expectations now distinguish edit framing from browse framing. Game and render-test projects compile; runtime and device validation have not been completed for these changes.

## Relevant sources

- `Assets/_Game/Scripts/Game/UI/RestaurantEditorPanel.cs`
- `Assets/_Game/Scripts/Game/Controls/RestaurantEditorController.cs`
- `Assets/_Game/Resources/Localization/{en,ms,zh}.json`

## RC exterior source comparison

- Reference: [WorldCustomiseBuilding.as](https://github.com/Dippys/Restaurant-City-client/blob/main/scripts/com/playfish/games/cooking/WorldCustomiseBuilding.as) defines `ZOOM_SCALE = 1.2`, centers the building and uses a bottom ItemChooser. Existing pieces are picked up on mouse-down; new pieces are taken from the chooser.
- [WorldStreet.as](https://github.com/Dippys/Restaurant-City-client/blob/main/scripts/com/playfish/games/cooking/WorldStreet.as) transitions into that zoom and restores street zoom on exit.
- The mobile adaptation retains the existing touch drag/drop, tap-to-select, grid/attachment validation and Save/Cancel behavior. It does not implement RC gifting, selling or banner-text editing in this change.
- The zoom regression test passed against the compiled game assembly in a background .NET host: the starter facade stays above 1× at the target work-area size, fits the horizontal bounds and uses RC's 1.2 zoom when sufficient space is available. The game and test projects compile. Full Unity interaction tests and a visual review remain pending.

## Exterior action placement and exit protection

- Keep the roof center clear: Exit is top left; Undo and Done are top right. The Done label does not change with the price. Display cost separately beneath it.
- Show Flip and Store above the right side of the catalog only while a piece is held or selected. Required walls/roofs/doors cannot be stored; roofs can still be flipped. Storing a new preview cancels that preview without changing ownership.
- Exit with a changed draft or changed pending placement opens Save changes / Discard / Keep editing. Picking up an unchanged existing piece alone does not count as an edit. Failed saves retain the draft.
- Background validation passed: game/test assembly compilation, edit zoom regression and unsaved-change detection (unchanged pickup, pending movement, new item, flipped item and removal). Full Unity interaction/visual checks remain pending.
