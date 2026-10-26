using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Simulation
{
    /// <summary>
    /// What can be reached from the door, as in the reference game: waiters start beside a stove they can walk to from
    /// the door and serve from there, and guests only sit where food can come from. A layout that walls the stoves in
    /// can still be saved; it just serves nobody until a path opens.
    /// </summary>
    public sealed partial class RestaurantSim
    {
        private readonly HashSet<GridPos> _doorRegion = new HashSet<GridPos>();
        private readonly Queue<GridPos> _flood = new Queue<GridPos>();
        private bool _reachDirty = true;

        private void WatchReach()
        {
            _layout.FurniturePlaced += _ => _reachDirty = true;
            _layout.FurnitureRemoved += _ => _reachDirty = true;
            _layout.BoundsChanged += () => _reachDirty = true;
            _layout.EntranceChanged += () => _reachDirty = true;
        }

        /// <summary>Walkable cells connected to the door (recomputed after the layout changes).</summary>
        private HashSet<GridPos> DoorRegion()
        {
            if (!_reachDirty) return _doorRegion;
            _reachDirty = false;
            _doorRegion.Clear();
            _flood.Clear();
            var grid = _layout.Grid;
            if (!grid.IsWalkable(_layout.Entrance)) return _doorRegion;
            _doorRegion.Add(_layout.Entrance);
            _flood.Enqueue(_layout.Entrance);
            while (_flood.Count > 0)
            {
                var cell = _flood.Dequeue();
                foreach (var d in GridPos.Neighbours4)
                {
                    var next = cell + d;
                    if (grid.InBounds(next) && grid.IsWalkable(next) && _doorRegion.Add(next)) _flood.Enqueue(next);
                }
            }
            return _doorRegion;
        }

        /// <summary>A cell beside the piece that is connected to the door, or null.</summary>
        private GridPos? DoorSideCell(PlacedFurniture f, GridPos? avoid = null)
        {
            var region = DoorRegion();
            for (int y = f.Origin.Y - 1; y <= f.Origin.Y + f.SizeY; y++)
            for (int x = f.Origin.X - 1; x <= f.Origin.X + f.SizeX; x++)
            {
                var cell = new GridPos(x, y);
                bool corner = (x < f.Origin.X || x >= f.Origin.X + f.SizeX) && (y < f.Origin.Y || y >= f.Origin.Y + f.SizeY);
                if (!corner && !f.Covers(cell) && region.Contains(cell) && cell != avoid) return cell;
            }
            return null;
        }

        private bool HasStove()
        {
            foreach (var f in _layout.Furniture) if (f.Def.Kind == FurnitureKind.Stove) return true;
            return false;
        }

        /// <summary>True when some drink machine can be reached from the door, so drinks can be served.</summary>
        public bool DrinksReachable
        {
            get
            {
                foreach (var f in _layout.Furniture)
                    if (f.Def.Kind == FurnitureKind.DrinkMachine && DoorSideCell(f).HasValue) return true;
                return false;
            }
        }

        /// <summary>True when some stove can be reached from the door, so a waiter can fetch plates.</summary>
        public bool KitchenReachable
        {
            get
            {
                foreach (var f in _layout.Furniture)
                    if (f.Def.Kind == FurnitureKind.Stove && DoorSideCell(f).HasValue) return true;
                return false;
            }
        }

        /// <summary>
        /// Where a waiter waits for work (the reference game puts waiters beside a stove): a free cell next to a stove
        /// reachable from the door, spread so waiters don't stack. Null when no stove can be reached.
        /// </summary>
        private GridPos? WaiterPost(Staff waiter)
        {
            int index = 0;
            foreach (var s in _staff)
            {
                if (s == waiter) break;
                if (s.Role == StaffRole.Waiter) index++;
            }
            var posts = new List<GridPos>();
            foreach (var f in _layout.Furniture)
            {
                if (f.Def.Kind != FurnitureKind.Stove) continue;
                var stand = StandCell(f);
                var cell = DoorSideCell(f, avoid: stand);
                if (cell.HasValue && !posts.Contains(cell.Value)) posts.Add(cell.Value);
            }
            if (posts.Count == 0) return null;
            return posts[index % posts.Count];
        }
    }
}
