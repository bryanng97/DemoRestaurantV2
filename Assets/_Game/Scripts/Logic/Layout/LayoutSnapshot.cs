using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Layout
{
    /// <summary>Versioned, engine-independent save format. No runtime object references.</summary>
    [Serializable]
    public sealed class LayoutSnapshot
    {
        public int version = 2;
        /// <summary>0: old catalog put every long machine across X. 1: machine depth follows its front.</summary>
        public int entertainmentAxesVersion;
        /// <summary>The room (without the terrace).</summary>
        public int width, height;
        /// <summary>The terrace bought in front of the room (0 = none; missing in older saves).</summary>
        public int terraceWidth, terraceDepth;
        private int GridWidth => Math.Max(width, terraceWidth > 0 && terraceDepth > 0 ? terraceWidth : 0);
        private int GridHeight => height + (terraceWidth > 0 && terraceDepth > 0 ? terraceDepth : 0);
        public bool hasBalance;
        public long confirmedCoins;
        public FurnitureRecord[] furniture;
        public string[] floors;
        /// <summary>Furniture owned (placed or in storage). Missing in older saves: everything placed counts as owned.</summary>
        public OwnedRecord[] owned;
        /// <summary>The door cell; -1 in saves from before the door could move (the restaurant's default is kept).</summary>
        public int entranceX = -1, entranceY = -1;
        /// <summary>Waiting area, front of the line first (with the door).</summary>
        public CellRecord[] waiting;
        /// <summary>Pieces hung on the back walls. Missing in older saves (nothing hung).</summary>
        public WallRecord[] wall;
        /// <summary>Wallpaper per wall cell from the back corner (null = default). Missing in older saves.</summary>
        public string[] wallpaperLeft, wallpaperRight;

        [Serializable]
        public sealed class WallRecord
        {
            public string id;
            /// <summary>0 = back-left wall, 1 = back-right wall (<see cref="WallSide"/>).</summary>
            public int side;
            public int index;
        }

        [Serializable]
        public sealed class CellRecord
        {
            public int x, y;
        }

        private bool HasEntrance => entranceX >= 0 && entranceY >= 0 && waiting != null;

        [Serializable]
        public sealed class OwnedRecord
        {
            public string id;
            public int count;
        }

        public void SetOwned(IReadOnlyDictionary<string, int> counts)
        {
            var list = new List<OwnedRecord>();
            if (counts != null)
                foreach (var pair in counts)
                    if (pair.Value > 0) list.Add(new OwnedRecord { id = pair.Key, count = pair.Value });
            list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            owned = list.ToArray();
        }

        /// <summary>Saved ownership, or null for a save from before furniture had to be bought.</summary>
        public Dictionary<string, int> OwnedCounts()
        {
            if (owned == null) return null;
            var counts = new Dictionary<string, int>();
            foreach (var r in owned)
                if (r != null && !string.IsNullOrEmpty(r.id) && r.count > 0) counts[r.id] = r.count;
            return counts;
        }

        [Serializable]
        public sealed class FurnitureRecord
        {
            public string id;
            public int x, y, facing;
            public int usageCount;
        }

        public static LayoutSnapshot Capture(RestaurantLayout layout)
        {
            var result = new LayoutSnapshot
            {
                entertainmentAxesVersion = 1,
                width = layout.RoomWidth, height = layout.RoomHeight,
                terraceWidth = layout.TerraceWidth, terraceDepth = layout.TerraceDepth,
                furniture = new FurnitureRecord[layout.Furniture.Count],
                floors = new string[layout.Grid.Width * layout.Grid.Height],
                entranceX = layout.Entrance.X,
                entranceY = layout.Entrance.Y,
                waiting = new CellRecord[layout.WaitingSpots.Count],
                wall = new WallRecord[layout.WallItems.Count],
            };
            for (int i = 0; i < result.wall.Length; i++)
            {
                var w = layout.WallItems[i];
                result.wall[i] = new WallRecord { id = w.Def.Id, side = (int)w.Side, index = w.Index };
            }
            result.wallpaperLeft = WallpaperOf(layout, WallSide.Left);
            result.wallpaperRight = WallpaperOf(layout, WallSide.Right);
            for (int i = 0; i < result.waiting.Length; i++)
                result.waiting[i] = new CellRecord { x = layout.WaitingSpots[i].X, y = layout.WaitingSpots[i].Y };
            for (int i = 0; i < result.furniture.Length; i++)
            {
                var f = layout.Furniture[i];
                result.furniture[i] = new FurnitureRecord { id = f.Def.Id, x = f.Origin.X, y = f.Origin.Y, facing = (int)f.Facing, usageCount = f.UsageCount };
            }
            for (int y = 0; y < layout.Grid.Height; y++)
            for (int x = 0; x < layout.Grid.Width; x++)
                result.floors[y * layout.Grid.Width + x] = layout.FloorAt(new GridPos(x, y));
            return result;
        }

        private static void EmptyToNull(string[] cells)
        {
            if (cells == null) return;
            for (int i = 0; i < cells.Length; i++) if (cells[i] == "") cells[i] = null;
        }

        // Older saves (and rooms that have since grown) simply keep the default wall where nothing was saved.
        private static void RestoreWallpaper(RestaurantLayout target, WallSide side, string[] cells)
        {
            if (cells == null) return;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] != null) target.SetWallpaper(side, i, cells[i]);
        }

        private static string[] WallpaperOf(RestaurantLayout layout, WallSide side)
        {
            var cells = new string[layout.WallLength(side)];
            for (int i = 0; i < cells.Length; i++) cells[i] = layout.WallpaperAt(side, i);
            return cells;
        }

        /// <summary>Stage and validate the entire save before changing the target.</summary>
        public bool TryRestore(RestaurantLayout target, ContentPack pack)
        {
            // Unity's JsonUtility writes a null string in an array as "": an empty wall cell, not a bad id.
            EmptyToNull(wallpaperLeft);
            EmptyToNull(wallpaperRight);
            // The door and waiting area: saved ones, or the target's for saves from before the door could move.
            var entrance = HasEntrance ? new GridPos(entranceX, entranceY) : target.Entrance;
            var spots = new List<GridPos>();
            if (HasEntrance)
            {
                foreach (var w in waiting)
                {
                    if (w == null) return false;
                    spots.Add(new GridPos(w.x, w.y));
                }
            }
            else spots.AddRange(target.WaitingSpots);

            if ((version != 1 && version != 2) || width <= 0 || height <= 0 || width > 128 || height > 128 ||
                entrance.X < 0 || entrance.Y < 0 || entrance.X >= width || entrance.Y >= height ||
                (version == 1 && (width != target.Grid.Width || height != target.Grid.Height)) ||
                terraceWidth < 0 || terraceDepth < 0 || terraceWidth > 128 || terraceDepth > 128 ||
                furniture == null || floors == null || floors.Length != GridWidth * GridHeight || furniture.Length > GridWidth * GridHeight)
                return false;
            var staged = new RestaurantLayout(width, height, entrance);
            staged.SetTerrace(terraceWidth, terraceDepth);
            foreach (var spot in spots) if (!staged.Grid.InBounds(spot) || spot == entrance) return false;
            staged.SetWaitingArea(spots);
            try
            {
                foreach (var record in furniture)
                {
                    if (record == null) return false;
                    // Pieces retired from the catalog are dropped; everything else must still fit.
                    if (!pack.HasFurniture(record.id)) continue;
                    // A piece that now hangs on the wall (e.g. the menu holder) was saved on the floor: drop it there.
                    if (pack.GetFurniture(record.id).Kind == FurnitureKind.WallDecor) continue;
                    if (!staged.TryPlace(pack.GetFurniture(record.id), new GridPos(record.x, record.y),
                        (Facing)record.facing, out var placed)) return false;
                    placed.UsageCount = Math.Max(0, record.usageCount);
                }
            }
            catch (ContentException) { return false; }
            if (wall != null)
                foreach (var record in wall)
                {
                    if (record == null || (record.side != 0 && record.side != 1)) return false;
                    if (!pack.HasFurniture(record.id)) continue; // retired from the catalog
                    if (!staged.TryHang(pack.GetFurniture(record.id), (WallSide)record.side, record.index, out _)) return false;
                }
            foreach (var floor in floors)
                if (floor != null && string.IsNullOrWhiteSpace(floor)) return false;
            foreach (var cells in new[] { wallpaperLeft, wallpaperRight })
                if (cells != null)
                    foreach (var id in cells)
                        if (id != null && string.IsNullOrWhiteSpace(id)) return false;

            while (target.Furniture.Count > 0) target.Remove(target.Furniture[target.Furniture.Count - 1]);
            target.Reshape(width, height, terraceWidth, terraceDepth);
            target.SetEntranceUnchecked(entrance, spots);
            foreach (var f in staged.Furniture)
                if (target.TryPlace(f.Def, f.Origin, f.Facing, out var restored)) restored.UsageCount = f.UsageCount;
            target.ClearWallItems();
            foreach (var w in staged.WallItems) target.TryHang(w.Def, w.Side, w.Index, out _);
            for (int y = 0; y < GridHeight; y++)
            for (int x = 0; x < GridWidth; x++) target.SetFloor(new GridPos(x, y), floors[y * GridWidth + x]);
            target.ClearWallpaper();
            RestoreWallpaper(target, WallSide.Left, wallpaperLeft);
            RestoreWallpaper(target, WallSide.Right, wallpaperRight);
            return true;
        }
    }
}
