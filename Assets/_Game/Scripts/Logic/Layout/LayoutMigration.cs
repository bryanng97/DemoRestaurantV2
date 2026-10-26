using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Layout
{
    /// <summary>Keep legacy furniture when the entrance/queue moves to the visible doorway.</summary>
    public static class LayoutMigration
    {
        /// <summary>Preserve old blocked cells while correcting depthwise machines' catalog axes, once per save.</summary>
        public static bool UpgradeEntertainmentAxes(LayoutSnapshot snapshot, ContentPack pack)
        {
            if (snapshot == null || snapshot.entertainmentAxesVersion >= 1) return false;
            bool changed = false;
            if (snapshot.furniture != null) foreach (var record in snapshot.furniture)
            {
                if (record == null || !pack.HasFurniture(record.id) || !Enum.IsDefined(typeof(Facing), record.facing)) continue;
                var def = pack.GetFurniture(record.id);
                if (def.Kind != FurnitureKind.Entertainment || def.SizeY <= def.SizeX) continue;
                record.facing = (record.facing + 1) % 4;
                changed = true;
            }
            snapshot.entertainmentAxesVersion = 1;
            return changed;
        }

        /// <summary>Old versions let every decoration stand on the street. Store indoor-only pieces without losing ownership.</summary>
        public static bool TryStoreIndoorStreetDecor(LayoutSnapshot old, RestaurantLayout target, ContentPack pack,
            out LayoutSnapshot migrated)
        {
            migrated = null;
            if (old?.furniture == null || old.width <= 0 || old.height <= 0 || old.width > 128 || old.height > 128) return false;
            var entrance = old.entranceX >= 0 && old.entranceY >= 0 ? new GridPos(old.entranceX, old.entranceY) : target.Entrance;
            var street = new RestaurantLayout(old.width, old.height, entrance);
            var kept = new List<LayoutSnapshot.FurnitureRecord>();
            var counts = new Dictionary<string, int>();
            var occupiedStreet = new HashSet<GridPos>();
            bool changed = false;
            foreach (var record in old.furniture)
            {
                if (record == null || !Enum.IsDefined(typeof(Facing), record.facing)) return false;
                if (!pack.HasFurniture(record.id)) { kept.Add(record); continue; }
                var def = pack.GetFurniture(record.id);
                counts[def.Id] = FurnitureOwnership.Count(counts, def.Id) + 1;
                if (record.x < 0)
                {
                    if (def.Kind != FurnitureKind.Decor && def.Kind != FurnitureKind.Plant) return false;
                    bool rotated = record.facing == (int)Facing.SouthEast || record.facing == (int)Facing.NorthWest;
                    int sx = rotated ? def.SizeY : def.SizeX, sy = rotated ? def.SizeX : def.SizeY;
                    for (int y = record.y; y < record.y + sy; y++)
                    for (int x = record.x; x < record.x + sx; x++)
                        if (!street.IsStreetDecorCell(new GridPos(x, y)) || !occupiedStreet.Add(new GridPos(x, y))) return false;
                    if (!RestaurantLayout.AllowedOutdoors(def)) { changed = true; continue; }
                }
                kept.Add(record);
            }
            if (!changed) return false;
            if (old.wall != null) foreach (var wall in old.wall)
                if (wall != null && pack.HasFurniture(wall.id)) counts[wall.id] = FurnitureOwnership.Count(counts, wall.id) + 1;
            migrated = new LayoutSnapshot {
                version=old.version, entertainmentAxesVersion=old.entertainmentAxesVersion, width=old.width, height=old.height, terraceWidth=old.terraceWidth, terraceDepth=old.terraceDepth,
                entranceX=old.entranceX, entranceY=old.entranceY, waiting=old.waiting, furniture=kept.ToArray(), floors=old.floors,
                wall=old.wall, wallpaperLeft=old.wallpaperLeft, wallpaperRight=old.wallpaperRight,
                hasBalance=old.hasBalance, confirmedCoins=old.confirmedCoins
            };
            migrated.SetOwned(FurnitureOwnership.AfterPurchase(old.OwnedCounts(), counts));
            var probe = new RestaurantLayout(target.RoomWidth, target.RoomHeight, target.Entrance);
            if (migrated.TryRestore(probe, pack)) return true;
            migrated = null; return false;
        }

        /// <summary>The concept shelf now blocks its whole 2x2 base. Keep older layouts, relocating only shelves that no longer fit.</summary>
        public static bool TryUpdateShelfFootprints(LayoutSnapshot old, RestaurantLayout target, ContentPack pack,
            out LayoutSnapshot migrated)
        {
            migrated = null;
            if (old == null || (old.version != 1 && old.version != 2) || old.width <= 0 || old.height <= 0 ||
                old.width > 128 || old.height > 128 || old.furniture == null || old.floors == null ||
                (old.version == 1 && (old.width != target.Grid.Width || old.height != target.Grid.Height)) ||
                old.floors.Length != old.width * old.height || old.furniture.Length > old.width * old.height ||
                target.Entrance.X >= old.width || target.Entrance.Y >= old.height) return false;
            var result = new RestaurantLayout(old.width, old.height, target.Entrance);
            foreach (var spot in target.WaitingSpots) if (!result.Grid.InBounds(spot)) return false;
            result.SetWaitingArea(target.WaitingSpots);
            var shelves = new List<LayoutSnapshot.FurnitureRecord>();
            try
            {
                foreach (var record in old.furniture)
                {
                    if (record == null || !Enum.IsDefined(typeof(Facing), record.facing) ||
                        !result.Grid.InBounds(new GridPos(record.x, record.y))) return false;
                    var def = pack.GetFurniture(record.id);
                    if (record.id == "bread_shelf") shelves.Add(record);
                    else if (!result.TryPlace(def, new GridPos(record.x, record.y), (Facing)record.facing, out _)) return false;
                }
                if (shelves.Count == 0) return false;
                foreach (var shelf in shelves)
                {
                    var def = pack.GetFurniture(shelf.id);
                    var origin = new GridPos(shelf.x, shelf.y);
                    var candidates = new List<GridPos>();
                    for (int y = 0; y < old.height; y++)
                    for (int x = 0; x < old.width; x++) candidates.Add(new GridPos(x, y));
                    candidates.Sort((a, b) => {
                        int d = a.ManhattanDistance(origin).CompareTo(b.ManhattanDistance(origin));
                        return d != 0 ? d : (a.Y * old.width + a.X).CompareTo(b.Y * old.width + b.X);
                    });
                    bool fitted = false;
                    foreach (var cell in candidates)
                    {
                        if (!result.TryPlace(def, cell, (Facing)shelf.facing, out var placed)) continue;
                        if (new LayoutEditor(result, pack).ValidateForService() == ServiceLayoutIssue.BlockedAccess)
                        { result.Remove(placed); continue; }
                        fitted = true; break;
                    }
                    if (!fitted) return false;
                }
            }
            catch (ContentException) { return false; }
            migrated = LayoutSnapshot.Capture(result);
            migrated.floors = (string[])old.floors.Clone();
            migrated.hasBalance = old.hasBalance; migrated.confirmedCoins = old.confirmedCoins;
            migrated.owned = old.owned;
            migrated.wall = old.wall; migrated.wallpaperLeft = old.wallpaperLeft; migrated.wallpaperRight = old.wallpaperRight;
            return true;
        }

        public static bool TryMoveEntrance(LayoutSnapshot old, RestaurantLayout target, ContentPack pack, out LayoutSnapshot migrated)
        {
            migrated = null;
            if (old == null || old.version != 1 || old.width != target.Grid.Width || old.height != target.Grid.Height ||
                old.furniture == null || old.floors == null) return false;
            var occupied = new bool[old.width * old.height];
            try
            {
                foreach (var record in old.furniture)
                {
                    if (record == null || !Enum.IsDefined(typeof(Facing), record.facing)) return false;
                    var def = pack.GetFurniture(record.id);
                    bool rotated = record.facing == (int)Facing.SouthEast || record.facing == (int)Facing.NorthWest;
                    int sx = rotated ? def.SizeY : def.SizeX, sy = rotated ? def.SizeX : def.SizeY;
                    if (record.x < 0 || record.y < 0 || record.x >= old.width || record.y >= old.height ||
                        sx > old.width - record.x || sy > old.height - record.y) return false;
                    for (int y = record.y; y < record.y + sy; y++)
                    for (int x = record.x; x < record.x + sx; x++)
                    {
                        int i = y * old.width + x;
                        if (occupied[i]) return false;
                        occupied[i] = true;
                    }
                }
            }
            catch (ContentException) { return false; }
            int free = Array.FindIndex(occupied, cell => !cell);
            if (free < 0) return false;
            var source = new RestaurantLayout(old.width, old.height, new GridPos(free % old.width, free / old.width));
            if (!old.TryRestore(source, pack)) return false;
            var result = new RestaurantLayout(old.width, old.height, target.Entrance);
            result.SetWaitingArea(target.WaitingSpots);
            var relocate = new List<PlacedFurniture>();
            foreach (var f in source.Furniture)
            {
                bool reserved = f.Covers(result.Entrance);
                foreach (var spot in result.WaitingSpots) reserved |= f.Covers(spot);
                if (reserved) relocate.Add(f);
                else if (!result.TryPlace(f.Def, f.Origin, f.Facing, out _)) return false;
            }
            foreach (var f in relocate)
            {
                int bestDistance = int.MaxValue; GridPos best = default;
                for (int y = 0; y < old.height; y++)
                for (int x = 0; x < old.width; x++)
                {
                    var p = new GridPos(x, y);
                    int distance = p.ManhattanDistance(f.Origin);
                    if (distance < bestDistance && result.CanPlace(f.Def, p, f.Facing)) { best = p; bestDistance = distance; }
                }
                if (bestDistance == int.MaxValue || !result.TryPlace(f.Def, best, f.Facing, out _)) return false;
            }
            migrated = LayoutSnapshot.Capture(result);
            migrated.floors = (string[])old.floors.Clone();
            migrated.hasBalance = old.hasBalance; migrated.confirmedCoins = old.confirmedCoins;
            return true;
        }
    }
}
