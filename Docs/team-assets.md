# Shared prototype assets

The theme loads `Resources/Art/RefProto` first and falls back to `Art/Concept`.
Previously, the RefProto folders were ignored by Git. A new checkout therefore
showed the older Concept art. Imported art, catalogs, localized names, sprite
maps and Unity `.meta` files are now intended to travel with this repository.
PNG images use Git LFS through the existing `.gitattributes` rules.

Recovered bundle: 2,714 PNG images and two music files (about 202 MiB),
including 383 catalog furniture items, 55 floors, 48 wallpapers and 117
animated furniture views. Every sprite-map entry and animation sheet was checked.
Eight isolated Unity 6000.6.4f1 checks passed, including game startup, recovered
skin resolution and regeneration of destroyed placeholder sprites. Staged LFS
pointers were checked against working files and local LFS object sizes/hashes.

## Teammate setup

Install Git LFS, then run from the repository:

```powershell
git lfs install
git pull
git lfs pull
python Tools/RefProto/verify_assets.py
```

Open with Unity 6000.6.4f1 and let imports finish. Use
DemoRestaurant > Online > Start Online Game to open the game scene. A normal
checkout does not need Java, FFDec, the original archive or the avatar baker.

Commit the imported PNG files, their `.meta` files, `skin.json`, catalogs,
localizations and generated `art_map_*.json` together. Do not commit Library,
Builds, source export archives or local account sessions. Git uploads the LFS
objects during a normal push; teammates must download them with `git lfs pull`.

## Source and reproducibility

The linked repositories provide reference code:

- https://github.com/Dippys/Restaurant-City-client
- https://github.com/Dippys/Restaurant-City-Server

The client README explains that graphics live in separate asset SWFs. The
server's `public/swf` and `public/data` directories are excluded from Git.
The recovered artwork instead comes from the preservation archive already
recorded in `Docs/ExteriorPrototype.md`:

- https://www.mediafire.com/file/rpiw869pkbbmilj/Restuarant_City_0.9.143a.zip/file
- Original ZIP SHA256: `4493BE9BC4C2F87EF80CA7AFA5321E12BB94A7EC6ED2F0A673B4D48B19170EF4`
- Exporter: JPEXS FFDec 26.4.0; native SWF graphics exported without running ActionScript.
- Import tools: `Tools/RefProto`, `Tools/ExteriorAssets` and the isolated Unity avatar baker.

These remain prototype reference assets with their existing provenance and
release-build checks. Tracking them for team development does not alter their
ownership or the existing release policy.

## Saved restaurants

Artwork and player saves are separate. These assets restore the newer visual
style. The level-54 furniture arrangement shown in the reference screenshot
requires that device's `save_malaysia.json` and `restaurant-layout-malaysia.json`.
It is not recreated by importing artwork. Back up current saves before importing
someone else's test state; do not commit personal account credentials.
