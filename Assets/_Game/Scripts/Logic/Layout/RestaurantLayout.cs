using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Layout
{
    public sealed class PlacedFurniture
    {
        /// <summary>Functional machine wear; follows the piece through a renovation.</summary>
        public int UsageCount { get; internal set; }
        public int InstanceId { get; }
        public FurnitureDef Def { get; }
        public GridPos Origin { get; internal set; }
        /// <summary>Direction the piece faces. For chairs this is the direction a seated customer looks.</summary>
        public Facing Facing { get; internal set; }

        /// <summary>Footprint after rotation. Defs are authored facing SouthWest (front towards +Y, long side
        /// along X), matching the art; facing SouthEast/NorthWest turns the piece 90 degrees and swaps X and Y.</summary>
        public int SizeX => IsRotated ? Def.SizeY : Def.SizeX;
        public int SizeY => IsRotated ? Def.SizeX : Def.SizeY;

        /// <summary>True when the footprint is turned 90 degrees; the renderer mirrors the sprite.</summary>
        public bool IsRotated => Facing == Facing.SouthEast || Facing == Facing.NorthWest;

        internal PlacedFurniture(int instanceId, FurnitureDef def, GridPos origin, Facing facing)
        {
            InstanceId = instanceId;
            Def = def;
            Origin = origin;
            Facing = facing;
        }

        /// <summary>The surface piece (a table, a counter) this one stands on, or null when it stands on the floor.</summary>
        public PlacedFurniture StackedOn { get; internal set; }

        public bool Covers(GridPos p) =>
            p.X >= Origin.X && p.Y >= Origin.Y && p.X < Origin.X + SizeX && p.Y < Origin.Y + SizeY;
    }

    /// <summary>Furniture placement on the single floor. Owns the walkability grid.</summary>
    public sealed partial class RestaurantLayout
    {
        private readonly List<PlacedFurniture> _furniture = new List<PlacedFurniture>();
        private int _nextInstanceId = 1;
        private string[] _floorTiles;

        public FloorGrid Grid { get; }
        /// <summary>
        /// Cell customers spawn on and leave from (the door). Must stay walkable. It sits against a back wall and
        /// can be moved in the build editor together with the waiting area (<see cref="TryMoveEntrance"/>).
        /// </summary>
        public GridPos Entrance { get; private set; }
        public IReadOnlyList<PlacedFurniture> Furniture => _furniture;
        /// <summary>
        /// Waiting-area cells by the entrance, front of the line first. Customers queue here when every seat
        /// is taken; with no spots, they are turned away instead. Furniture can't be placed on them.
        /// </summary>
        public IReadOnlyList<GridPos> WaitingSpots => _waitingSpots;
        private readonly List<GridPos> _waitingSpots = new List<GridPos>();

        public event Action<PlacedFurniture> FurniturePlaced;
        public event Action<PlacedFurniture> FurnitureRemoved;
        public event Action<GridPos> FloorChanged;
        public event Action BoundsChanged;
        /// <summary>The door (and its waiting area) moved.</summary>
        public event Action EntranceChanged;

        // Resize is internal: only validated snapshots, room growth and the draft expansion command call it.
        /// <summary>Resizes the room; the terrace in front of it (if any) moves with its front edge.</summary>
        internal void Resize(int width, int height) => Reshape(width, height, TerraceWidth, TerraceDepth);

        public RestaurantLayout(int width, int height, GridPos entrance)
        {
            Grid = new FloorGrid(width, height);
            _floorTiles = new string[width * height];
            RoomWidth = width; RoomHeight = height;
            if (!Grid.InBounds(entrance))
                throw new ArgumentOutOfRangeException(nameof(entrance));
            Entrance = entrance;
        }

        /// <summary>
        /// True when <paramref name="entrance"/> is a free cell against the back-left wall (X = 0, the street side;
        /// the back-right wall faces the farm), the street cell outside it is clear, and every waiting spot is a free
        /// cell other than the entrance.
        /// </summary>
        public bool CanMoveEntrance(GridPos entrance, IReadOnlyList<GridPos> waitingSpots)
        {
            if (!Grid.InBounds(entrance) || entrance.X != 0 || !Grid.IsWalkable(entrance) || WallItemBlocksDoor(entrance))
                return false;
            for (int x = -1; x >= StreetLaneX; x--)
                if (FindAt(new GridPos(x, entrance.Y)) != null)
                    return false; // decoration on the new door's path to the sidewalk
            var seen = new HashSet<GridPos>();
            foreach (var s in waitingSpots)
                if (s == entrance || !Grid.IsWalkable(s) || !seen.Add(s)) return false;
            return true;
        }

        /// <summary>Moves the door and its waiting area. False (and nothing changes) when the cells aren't free.</summary>
        public bool TryMoveEntrance(GridPos entrance, IReadOnlyList<GridPos> waitingSpots)
        {
            if (!CanMoveEntrance(entrance, waitingSpots))
                return false;
            if (entrance == Entrance && SameSpots(waitingSpots))
                return true;
            Entrance = entrance;
            _waitingSpots.Clear();
            _waitingSpots.AddRange(waitingSpots);
            EntranceChanged?.Invoke();
            return true;
        }

        /// <summary>Restoring a validated snapshot: the target is empty, so the cells are free.</summary>
        internal void SetEntranceUnchecked(GridPos entrance, IReadOnlyList<GridPos> waitingSpots)
        {
            bool changed = entrance != Entrance || !SameSpots(waitingSpots);
            Entrance = entrance;
            _waitingSpots.Clear();
            _waitingSpots.AddRange(waitingSpots);
            if (changed) EntranceChanged?.Invoke();
        }

        private bool SameSpots(IReadOnlyList<GridPos> spots)
        {
            if (spots.Count != _waitingSpots.Count) return false;
            for (int i = 0; i < spots.Count; i++) if (spots[i] != _waitingSpots[i]) return false;
            return true;
        }

        /// <summary>Sets the waiting area. Every spot must be a free, walkable cell.</summary>
        public void SetWaitingArea(IEnumerable<GridPos> spotsFrontFirst)
        {
            var spots = new List<GridPos>(spotsFrontFirst);
            foreach (var s in spots)
                if (!Grid.IsWalkable(s))
                    throw new ArgumentException($"Waiting spot {s} is not a free walkable cell.", nameof(spotsFrontFirst));
            _waitingSpots.Clear();
            _waitingSpots.AddRange(spots);
        }

        // ------------------------------------------------------------------ outside the door
        // Beyond the back-left wall (x < 0): an optional strip of lawn (StreetGap, currently none: the sidewalk runs right
        // along the wall), then the sidewalk, StreetWidth cells wide. (The road
        // beyond the sidewalk is scenery only.) The sidewalk runs StreetOverhang cells past each end of the room.
        // Guests walk along its middle lane, turn onto the path across the lawn on the door's row, and come in; they
        // leave the same way and walk off along the sidewalk. Decoration (decor and plants) goes on the sidewalk's
        // side lanes and on the lawn, never on the walking lane or the door path. A later land purchase (the sheet's
        // terraces) adds more outdoor space. Nothing outside touches the walkable grid inside the room.

        /// <summary>Lawn between the wall and the sidewalk, in cells (0: the sidewalk runs along the wall).</summary>
        public const int StreetGap = 0;
        public const int StreetWidth = 3;
        public const int StreetOverhang = 3;
        /// <summary>The sidewalk's middle lane, where guests walk.</summary>
        public const int StreetLaneX = -StreetGap - 2;
        /// <summary>Outermost column of the sidewalk (the road starts beyond it).</summary>
        public const int StreetOuterX = -StreetGap - StreetWidth;

        /// <summary>First and last row of the sidewalk.</summary>
        public int StreetMinY => -StreetOverhang;
        public int StreetMaxY => Grid.Height - 1 + StreetOverhang;

        /// <summary>True for any outside cell along the door wall: the lawn strip or the sidewalk.</summary>
        public bool IsStreetCell(GridPos p) => p.X < 0 && p.X >= StreetOuterX && p.Y >= StreetMinY && p.Y <= StreetMaxY;

        /// <summary>The sidewalk itself (not the lawn strip).</summary>
        public bool IsSidewalkCell(GridPos p) => IsStreetCell(p) && p.X < -StreetGap;

        /// <summary>True for any outside cell the editor can use.</summary>
        public bool IsOutdoor(GridPos p) => IsStreetCell(p);

        /// <summary>The lawn cell right outside the door.</summary>
        public GridPos DoorStep => new GridPos(-1, Entrance.Y);

        /// <summary>The path across the lawn on the door's row, from the sidewalk lane to the door.</summary>
        public bool IsDoorPath(GridPos p) => p.Y == Entrance.Y && p.X < 0 && p.X >= StreetLaneX;

        /// <summary>Outside cells where decoration can stand: not the walking lane, not the door path.</summary>
        public bool IsStreetDecorCell(GridPos p) => IsStreetCell(p) && p.X != StreetLaneX && !IsDoorPath(p);

        /// <summary>Indoor floor or outside: anywhere the build editor can put something.</summary>
        public bool InPlacementBounds(GridPos p) => Grid.InBounds(p) || IsOutdoor(p);

        /// <summary>Only decoration can stand on the street (no seats, stoves or counters outside).</summary>
        public static bool AllowedOutdoors(FurnitureDef def) =>
            def != null && def.OutdoorDecor && (def.Kind == FurnitureKind.Decor || def.Kind == FurnitureKind.Plant);

        public bool IsOutdoorItem(PlacedFurniture f) => f != null && IsOutdoor(f.Origin);

        /// <summary>
        /// The walk from the front end of the sidewalk to the door (ending on the entrance cell): along the middle
        /// lane to the door's row, then across the lawn path and in.
        /// </summary>
        public List<GridPos> StreetRouteIn()
        {
            var route = new List<GridPos>();
            for (int y = StreetMaxY; y >= Entrance.Y; y--) route.Add(new GridPos(StreetLaneX, y));
            for (int x = StreetLaneX + 1; x <= 0; x++) route.Add(new GridPos(x, Entrance.Y));
            return route;
        }

        /// <summary>From the door, across the lawn path and away along the sidewalk to its back end.</summary>
        public List<GridPos> StreetRouteOut()
        {
            var route = new List<GridPos>();
            for (int x = -1; x >= StreetLaneX; x--) route.Add(new GridPos(x, Entrance.Y));
            for (int y = Entrance.Y - 1; y >= StreetMinY; y--) route.Add(new GridPos(StreetLaneX, y));
            return route;
        }

        private bool CanStandOutdoors(PlacedFurniture probe, PlacedFurniture ignore)
        {
            if (!AllowedOutdoors(probe.Def)) return false;
            for (int y = probe.Origin.Y; y < probe.Origin.Y + probe.SizeY; y++)
            for (int x = probe.Origin.X; x < probe.Origin.X + probe.SizeX; x++)
                if (!IsStreetDecorCell(new GridPos(x, y))) return false;
            foreach (var f in _furniture)
            {
                if (f == ignore || !IsOutdoorItem(f)) continue;
                if (probe.Origin.X < f.Origin.X + f.SizeX && f.Origin.X < probe.Origin.X + probe.SizeX &&
                    probe.Origin.Y < f.Origin.Y + f.SizeY && f.Origin.Y < probe.Origin.Y + probe.SizeY) return false;
            }
            return true;
        }

        // A piece standing on a surface blocks nothing: the surface under it already does.
        private void BlockIfIndoor(PlacedFurniture f)
        {
            if (!IsOutdoorItem(f) && f.StackedOn == null) Grid.Block(f.Origin, f.SizeX, f.SizeY);
        }

        private void UnblockIfIndoor(PlacedFurniture f)
        {
            if (!IsOutdoorItem(f) && f.StackedOn == null) Grid.Unblock(f.Origin, f.SizeX, f.SizeY);
        }

        /// <summary>
        /// The surface a stackable piece could stand on at this spot (as in the reference game: a TV on a table), or
        /// null. Its whole footprint must lie on one surface piece, with nothing else stacked there.
        /// </summary>
        public PlacedFurniture SurfaceFor(FurnitureDef def, GridPos origin, Facing facing, PlacedFurniture ignore = null)
        {
            if (def == null || !def.Stackable || !Enum.IsDefined(typeof(Facing), facing)) return null;
            var probe = new PlacedFurniture(0, def, origin, facing);
            var surface = FindAt(origin);
            if (surface == null || surface == ignore || !surface.Def.Surface) return null;
            for (int y = origin.Y; y < origin.Y + probe.SizeY; y++)
            for (int x = origin.X; x < origin.X + probe.SizeX; x++)
                if (!surface.Covers(new GridPos(x, y))) return null;
            foreach (var f in _furniture)
            {
                if (f == ignore || f.StackedOn != surface) continue;
                if (probe.Origin.X < f.Origin.X + f.SizeX && f.Origin.X < probe.Origin.X + probe.SizeX &&
                    probe.Origin.Y < f.Origin.Y + f.SizeY && f.Origin.Y < probe.Origin.Y + probe.SizeY) return null;
            }
            return surface;
        }

        public bool CanStack(FurnitureDef def, GridPos origin, Facing facing) => SurfaceFor(def, origin, facing) != null;

        /// <summary>Pieces standing on <paramref name="surface"/>.</summary>
        public List<PlacedFurniture> StackedOn(PlacedFurniture surface)
        {
            var list = new List<PlacedFurniture>();
            foreach (var f in _furniture) if (surface != null && f.StackedOn == surface) list.Add(f);
            return list;
        }

        public bool CanPlace(FurnitureDef def, GridPos origin, Facing facing)
        {
            if (def == null || def.Kind == FurnitureKind.WallDecor || !Enum.IsDefined(typeof(Facing), facing)) return false; // wall pieces hang, see TryHang
            var probe = new PlacedFurniture(0, def, origin, facing);
            if (IsOutdoor(origin)) return CanStandOutdoors(probe, null);
            if (!Grid.InBounds(origin) || probe.SizeX > Grid.Width - origin.X || probe.SizeY > Grid.Height - origin.Y) return false;
            if (!Grid.IsAreaFree(origin, probe.SizeX, probe.SizeY))
                return false;
            foreach (var spot in _waitingSpots)
                if (probe.Covers(spot))
                    return false;
            return !probe.Covers(Entrance);
        }

        public bool TryPlace(FurnitureDef def, GridPos origin, Facing facing, out PlacedFurniture placed)
        {
            placed = null;
            PlacedFurniture surface = null;
            if (!CanPlace(def, origin, facing))
            {
                // A stackable piece can stand on a table or counter instead.
                surface = SurfaceFor(def, origin, facing);
                if (surface == null) return false;
            }
            placed = new PlacedFurniture(_nextInstanceId++, def, origin, facing) { StackedOn = surface };
            BlockIfIndoor(placed);
            _furniture.Add(placed);
            FurniturePlaced?.Invoke(placed);
            return true;
        }

        public bool Remove(PlacedFurniture item)
        {
            if (!_furniture.Contains(item))
                return false;
            foreach (var child in StackedOn(item)) Remove(child); // what stood on it goes with it
            UnblockIfIndoor(item);
            _furniture.Remove(item);
            FurnitureRemoved?.Invoke(item);
            return true;
        }

        public PlacedFurniture FindAt(GridPos p)
        {
            // The piece on the floor (what blocks the cell); pieces stacked on it are found with StackedOn.
            foreach (var f in _furniture)
            {
                if (f.StackedOn == null && f.Covers(p))
                    return f;
            }
            return null;
        }

        /// <summary>Validate a move without freeing the original footprint or firing renderer events.</summary>
        public bool CanMove(PlacedFurniture item, GridPos origin, Facing facing) =>
            CanMoveOnFloor(item, origin, facing) || (item != null && SurfaceFor(item.Def, origin, facing, ignore: item) != null);

        private bool CanMoveOnFloor(PlacedFurniture item, GridPos origin, Facing facing)
        {
            if (item == null || !_furniture.Contains(item) || !Enum.IsDefined(typeof(Facing), facing)) return false;
            var probe = new PlacedFurniture(0, item.Def, origin, facing);
            if (IsOutdoor(origin)) return CanStandOutdoors(probe, item);
            if (!Grid.InBounds(origin) || probe.SizeX > Grid.Width - origin.X || probe.SizeY > Grid.Height - origin.Y) return false;
            // Its own cells count as free only when it blocks them itself (not when it stands on a table there).
            bool fromIndoors = !IsOutdoorItem(item) && item.StackedOn == null;
            for (int y = origin.Y; y < origin.Y + probe.SizeY; y++)
            for (int x = origin.X; x < origin.X + probe.SizeX; x++)
            {
                var cell = new GridPos(x, y);
                if (!Grid.InBounds(cell) || (!Grid.IsWalkable(cell) && !(fromIndoors && item.Covers(cell)))) return false;
            }
            if (probe.Covers(Entrance)) return false;
            foreach (var spot in _waitingSpots) if (probe.Covers(spot)) return false;
            return true;
        }

        public bool TryMove(PlacedFurniture item, GridPos origin, Facing facing)
        {
            if (!CanMove(item, origin, facing)) return false;
            // A stackable piece goes on the floor when it can, otherwise onto the surface there.
            var surface = CanMoveOnFloor(item, origin, facing) ? null : SurfaceFor(item.Def, origin, facing, ignore: item);
            var children = StackedOn(item);
            var from = item.Origin;
            bool turned = item.Facing != facing;
            UnblockIfIndoor(item);
            FurnitureRemoved?.Invoke(item);
            item.StackedOn = surface;
            item.Origin = origin;
            item.Facing = facing;
            BlockIfIndoor(item);
            FurniturePlaced?.Invoke(item);
            // What stood on it moves with it; if the piece was turned, it is taken off (back to storage).
            foreach (var child in children)
            {
                if (turned) { Remove(child); continue; }
                FurnitureRemoved?.Invoke(child);
                child.Origin = new GridPos(child.Origin.X + origin.X - from.X, child.Origin.Y + origin.Y - from.Y);
                FurniturePlaced?.Invoke(child);
            }
            return true;
        }

        /// <summary>Null means the content pack's default floor. Flooring never changes walkability.</summary>
        public string FloorAt(GridPos cell) => Grid.InBounds(cell) ? _floorTiles[cell.Y * Grid.Width + cell.X] : null;

        public bool SetFloor(GridPos cell, string tileId)
        {
            if (!Grid.InBounds(cell) || (tileId != null && string.IsNullOrWhiteSpace(tileId))) return false;
            int index = cell.Y * Grid.Width + cell.X;
            if (_floorTiles[index] == tileId) return false;
            _floorTiles[index] = tileId;
            FloorChanged?.Invoke(cell);
            return true;
        }
    }
}
