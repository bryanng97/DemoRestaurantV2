using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Layout
{
    public enum WallSide { Left, Right }

    /// <summary>
    /// A piece hung on a back wall (<see cref="FurnitureKind.WallDecor"/>): it covers <see cref="Width"/> wall cells from
    /// <see cref="Index"/>, counted from the back corner. The back-left wall runs along X = 0 (index = Y), the back-right
    /// wall along Y = 0 (index = X). It never takes floor cells.
    /// </summary>
    public sealed class PlacedWallItem
    {
        public int InstanceId { get; }
        public FurnitureDef Def { get; }
        public WallSide Side { get; internal set; }
        public int Index { get; internal set; }
        public int Width => Math.Max(1, Def.SizeX);

        internal PlacedWallItem(int instanceId, FurnitureDef def, WallSide side, int index)
        {
            InstanceId = instanceId; Def = def; Side = side; Index = index;
        }

        public bool Covers(WallSide side, int index) => side == Side && index >= Index && index < Index + Width;
    }

    public sealed partial class RestaurantLayout
    {
        private readonly List<PlacedWallItem> _wallItems = new List<PlacedWallItem>();

        public IReadOnlyList<PlacedWallItem> WallItems => _wallItems;

        /// <summary>A wall item was hung, moved or taken down.</summary>
        public event Action WallItemsChanged;

        public int WallLength(WallSide side) => side == WallSide.Left ? RoomHeight : RoomWidth;

        // Wallpaper per wall cell, counted from the back corner like wall items (null = the theme's default wall).
        private readonly List<string> _wallpaperLeft = new List<string>(), _wallpaperRight = new List<string>();

        /// <summary>A wall cell's wallpaper changed.</summary>
        public event Action WallpaperChanged;

        public string WallpaperAt(WallSide side, int index)
        {
            var cells = side == WallSide.Left ? _wallpaperLeft : _wallpaperRight;
            return index >= 0 && index < cells.Count ? cells[index] : null;
        }

        /// <summary>Sets one wall cell's wallpaper (null = default). False when off the wall or unchanged.</summary>
        public bool SetWallpaper(WallSide side, int index, string wallpaperId)
        {
            if (index < 0 || index >= WallLength(side) || (wallpaperId != null && string.IsNullOrWhiteSpace(wallpaperId))) return false;
            var cells = side == WallSide.Left ? _wallpaperLeft : _wallpaperRight;
            while (cells.Count <= index) cells.Add(null);
            if (cells[index] == wallpaperId) return false;
            cells[index] = wallpaperId;
            WallpaperChanged?.Invoke();
            return true;
        }

        internal void ClearWallpaper()
        {
            if (_wallpaperLeft.Count == 0 && _wallpaperRight.Count == 0) return;
            _wallpaperLeft.Clear(); _wallpaperRight.Clear();
            WallpaperChanged?.Invoke();
        }

        /// <summary>The wall cell the door is in, or -1 when the door isn't on that wall.</summary>
        public int DoorIndexOn(WallSide side) =>
            side == WallSide.Left ? (Entrance.X == 0 ? Entrance.Y : -1) : (Entrance.Y == 0 && Entrance.X > 0 ? Entrance.X : -1);

        public bool CanHang(FurnitureDef def, WallSide side, int index, PlacedWallItem moving = null)
        {
            if (def == null || def.Kind != FurnitureKind.WallDecor) return false;
            int width = Math.Max(1, def.SizeX);
            if (index < 0 || index + width > WallLength(side)) return false;
            int door = DoorIndexOn(side);
            if (door >= index && door < index + width) return false;
            foreach (var w in _wallItems)
            {
                if (w == moving || w.Side != side) continue;
                if (index < w.Index + w.Width && w.Index < index + width) return false;
            }
            return true;
        }

        public bool TryHang(FurnitureDef def, WallSide side, int index, out PlacedWallItem placed)
        {
            placed = null;
            if (!CanHang(def, side, index)) return false;
            placed = new PlacedWallItem(_nextInstanceId++, def, side, index);
            _wallItems.Add(placed);
            WallItemsChanged?.Invoke();
            return true;
        }

        public bool TryMoveOnWall(PlacedWallItem item, WallSide side, int index)
        {
            if (item == null || !_wallItems.Contains(item) || !CanHang(item.Def, side, index, item)) return false;
            item.Side = side; item.Index = index;
            WallItemsChanged?.Invoke();
            return true;
        }

        public bool TakeDown(PlacedWallItem item)
        {
            if (!_wallItems.Remove(item)) return false;
            WallItemsChanged?.Invoke();
            return true;
        }

        public PlacedWallItem WallItemAt(WallSide side, int index)
        {
            foreach (var w in _wallItems) if (w.Covers(side, index)) return w;
            return null;
        }

        /// <summary>The first wall item of a kind of piece, e.g. the menu holder.</summary>
        public PlacedWallItem FindWallItem(string defId)
        {
            foreach (var w in _wallItems) if (w.Def.Id == defId) return w;
            return null;
        }

        /// <summary>True when a hung piece covers the wall cell of <paramref name="door"/>.</summary>
        private bool WallItemBlocksDoor(GridPos door)
        {
            foreach (var w in _wallItems)
                if ((door.X == 0 && w.Covers(WallSide.Left, door.Y)) || (door.Y == 0 && door.X > 0 && w.Covers(WallSide.Right, door.X)))
                    return true;
            return false;
        }

        internal void ClearWallItems()
        {
            if (_wallItems.Count == 0) return;
            _wallItems.Clear();
            WallItemsChanged?.Invoke();
        }
    }
}
