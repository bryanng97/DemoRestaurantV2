using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Layout
{
    public enum ServiceLayoutIssue { None, MissingChair, MissingStove, MissingCounter, BlockedAccess }

    /// <summary>Edits an isolated draft. Undo/redo and cancel never mutate the running restaurant.</summary>
    public sealed class LayoutEditor
    {
        private const int HistoryLimit = 60;
        private readonly ContentPack _pack;
        private readonly List<LayoutSnapshot> _undo = new List<LayoutSnapshot>();
        private readonly Stack<LayoutSnapshot> _redo = new Stack<LayoutSnapshot>();
        public RestaurantLayout Draft { get; }
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public LayoutEditor(RestaurantLayout source, ContentPack pack)
        {
            _pack = pack;
            Draft = new RestaurantLayout(source.RoomWidth, source.RoomHeight, source.Entrance);
            Draft.SetWaitingArea(source.WaitingSpots);
            LayoutSnapshot.Capture(source).TryRestore(Draft, pack);
        }

        private void Remember(LayoutSnapshot before)
        {
            _undo.Add(before);
            if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
            _redo.Clear();
        }

        public bool Place(FurnitureDef def, GridPos origin, Facing facing)
        {
            if (!FurnitureWorkArea.CanEdit(Draft, def, origin, facing)) return false;
            if (!Draft.CanPlace(def, origin, facing) && !Draft.CanStack(def, origin, facing)) return false;
            var before = LayoutSnapshot.Capture(Draft);
            if (!Draft.TryPlace(def, origin, facing, out _)) return false;
            Remember(before);
            return true;
        }

        public bool Move(PlacedFurniture item, GridPos origin, Facing facing)
        {
            if (item == null || !FurnitureWorkArea.CanEdit(Draft, item.Def, origin, facing, item)) return false;
            if (!Draft.CanMove(item, origin, facing) || (item.Origin == origin && item.Facing == facing)) return false;
            var before = LayoutSnapshot.Capture(Draft);
            if (!Draft.TryMove(item, origin, facing)) return false;
            Remember(before);
            return true;
        }

        public bool Remove(PlacedFurniture item)
        {
            var before = LayoutSnapshot.Capture(Draft);
            if (!Draft.Remove(item)) return false;
            Remember(before);
            return true;
        }

        public bool Hang(FurnitureDef def, WallSide side, int index)
        {
            if (!Draft.CanHang(def, side, index)) return false;
            var before = LayoutSnapshot.Capture(Draft);
            if (!Draft.TryHang(def, side, index, out _)) return false;
            Remember(before);
            return true;
        }

        public bool MoveOnWall(PlacedWallItem item, WallSide side, int index)
        {
            if (item == null || (item.Side == side && item.Index == index) || !Draft.CanHang(item.Def, side, index, item)) return false;
            var before = LayoutSnapshot.Capture(Draft);
            if (!Draft.TryMoveOnWall(item, side, index)) return false;
            Remember(before);
            return true;
        }

        /// <summary>Takes a wall piece down (to storage).</summary>
        public bool TakeDown(PlacedWallItem item)
        {
            var before = LayoutSnapshot.Capture(Draft);
            if (!Draft.TakeDown(item)) return false;
            Remember(before);
            return true;
        }

        /// <summary>Everything placed goes to storage (the reference game's "store all"); floors and the door stay.</summary>
        public bool StoreAll()
        {
            if (Draft.Furniture.Count == 0 && Draft.WallItems.Count == 0) return false;
            var before = LayoutSnapshot.Capture(Draft);
            while (Draft.Furniture.Count > 0) Draft.Remove(Draft.Furniture[Draft.Furniture.Count - 1]);
            while (Draft.WallItems.Count > 0) Draft.TakeDown(Draft.WallItems[Draft.WallItems.Count - 1]);
            Remember(before);
            return true;
        }

        public bool Paint(GridPos cell, string tileId)
        {
            var before = LayoutSnapshot.Capture(Draft);
            if (!Draft.SetFloor(cell, tileId)) return false;
            Remember(before);
            return true;
        }

        /// <summary>Paints every floor cell in the rectangle between two corners (a drag), as one undo step.</summary>
        public bool PaintArea(GridPos a, GridPos b, string tileId)
        {
            if (tileId != null && string.IsNullOrWhiteSpace(tileId)) return false;
            var before = LayoutSnapshot.Capture(Draft);
            bool changed = false;
            for (int y = System.Math.Min(a.Y, b.Y); y <= System.Math.Max(a.Y, b.Y); y++)
            for (int x = System.Math.Min(a.X, b.X); x <= System.Math.Max(a.X, b.X); x++)
                changed |= Draft.IsFloorCell(new GridPos(x, y)) && Draft.SetFloor(new GridPos(x, y), tileId);
            if (changed) Remember(before);
            return changed;
        }

        public bool FillFloor(string tileId)
        {
            if (tileId != null && string.IsNullOrWhiteSpace(tileId)) return false;
            var before = LayoutSnapshot.Capture(Draft);
            bool changed = false;
            for (int y = 0; y < Draft.Grid.Height; y++)
            for (int x = 0; x < Draft.Grid.Width; x++) changed |= Draft.SetFloor(new GridPos(x, y), tileId);
            if (changed) Remember(before);
            return changed;
        }

        /// <summary>Papers the wall cells between two indexes on one wall (a drag), as one undo step.</summary>
        public bool PaintWall(WallSide side, int from, int to, string wallpaperId)
        {
            if (wallpaperId != null && string.IsNullOrWhiteSpace(wallpaperId)) return false;
            var before = LayoutSnapshot.Capture(Draft);
            bool changed = false;
            for (int i = System.Math.Max(0, System.Math.Min(from, to)); i <= System.Math.Max(from, to); i++)
                changed |= Draft.SetWallpaper(side, i, wallpaperId);
            if (changed) Remember(before);
            return changed;
        }

        /// <summary>Papers both back walls, as one undo step.</summary>
        public bool FillWalls(string wallpaperId)
        {
            if (wallpaperId != null && string.IsNullOrWhiteSpace(wallpaperId)) return false;
            var before = LayoutSnapshot.Capture(Draft);
            bool changed = false;
            foreach (var side in new[] { WallSide.Left, WallSide.Right })
                for (int i = 0; i < Draft.WallLength(side); i++) changed |= Draft.SetWallpaper(side, i, wallpaperId);
            if (changed) Remember(before);
            return changed;
        }

        public enum DoorMove { Moved, SameSpot, Blocked }

        /// <summary>
        /// Moves the door to the wall segment nearest <paramref name="tapped"/>; the waiting area follows it.
        /// Blocked when furniture stands on the door cell or leaves too little room to queue inside it.
        /// </summary>
        public DoorMove MoveEntrance(GridPos tapped, int cellsPerSegment, int waitingSpots = DoorPlanner.MinWaitingSpots)
        {
            var door = DoorPlanner.DoorNear(tapped, cellsPerSegment, Draft.RoomWidth, Draft.RoomHeight);
            if (door == Draft.Entrance) return DoorMove.SameSpot;
            int count = System.Math.Max(0, waitingSpots);
            var spots = DoorPlanner.WaitingSpotsFor(Draft, door, count);
            if (spots.Count < count || !Draft.CanMoveEntrance(door, spots)) return DoorMove.Blocked;
            var before = LayoutSnapshot.Capture(Draft);
            if (!Draft.TryMoveEntrance(door, spots)) return DoorMove.Blocked;
            Remember(before);
            return DoorMove.Moved;
        }

        public bool Undo()
        {
            if (!CanUndo) return false;
            _redo.Push(LayoutSnapshot.Capture(Draft));
            int i = _undo.Count - 1;
            _undo[i].TryRestore(Draft, _pack);
            _undo.RemoveAt(i);
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo) return false;
            _undo.Add(LayoutSnapshot.Capture(Draft));
            return _redo.Pop().TryRestore(Draft, _pack);
        }

        public bool Expand(bool alongX)
        {
            var rules = _pack.Rules.Expansion;
            if (rules == null) return false;
            int width = Draft.Grid.Width + (alongX ? rules.step : 0);
            int height = Draft.Grid.Height + (alongX ? 0 : rules.step);
            if (!rules.TryQuote(Draft.Grid.Width, Draft.Grid.Height, width, height, out _)) return false;
            var before = LayoutSnapshot.Capture(Draft);
            Draft.Resize(width, height);
            Remember(before);
            return true;
        }

        /// <summary>
        /// Every working station/seat and queue spot must be reachable from the entrance. Empty floor that is boxed
        /// in by furniture is allowed: nobody needs to stand there (staff homes skip unreachable cells).
        /// </summary>
        public ServiceLayoutIssue ValidateForService() => ValidateForService(out _);

        /// <param name="blocked">For <see cref="ServiceLayoutIssue.BlockedAccess"/>: the seat or station nobody can
        /// reach (null when it's the waiting area), so the editor can point at it.</param>
        public ServiceLayoutIssue ValidateForService(out PlacedFurniture blocked)
        {
            blocked = null;
            bool chair = false, stove = false;
            var finder = new Pathfinder(Draft.Grid);
            var path = new List<GridPos>();
            foreach (var spot in Draft.WaitingSpots)
                if (!finder.TryFindPath(Draft.Entrance, spot, path)) return ServiceLayoutIssue.BlockedAccess;
            foreach (var f in Draft.Furniture)
            {
                bool working = f.Def.Kind == FurnitureKind.Chair || f.Def.Kind == FurnitureKind.Stove;
                chair |= f.Def.Kind == FurnitureKind.Chair;
                stove |= f.Def.Kind == FurnitureKind.Stove;
                if (!working) continue;
                bool accessible = false;
                for (int y = f.Origin.Y; y < f.Origin.Y + f.SizeY; y++)
                for (int x = f.Origin.X; x < f.Origin.X + f.SizeX; x++)
                {
                    var cell = new GridPos(x, y);
                    foreach (var d in GridPos.Neighbours4)
                        if (Draft.Grid.IsWalkable(cell + d) && finder.TryFindPath(Draft.Entrance, cell + d, path))
                            accessible = true;
                }
                if (!accessible)
                {
                    blocked = f;
                    return ServiceLayoutIssue.BlockedAccess;
                }
            }
            if (!chair) return ServiceLayoutIssue.MissingChair;
            if (!stove) return ServiceLayoutIssue.MissingStove;
            return ServiceLayoutIssue.None;
        }
    }
}
