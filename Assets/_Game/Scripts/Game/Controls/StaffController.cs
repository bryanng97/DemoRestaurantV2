using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;

namespace DemoRestaurant.Game.Controls
{
    /// <summary>
    /// Staff actions for the UI: hire a role (staff slots, role unlock level and hire cost apply, unless the stress
    /// test turns limits off), pick a free spot for them to wait at, dismiss, rest, and use energy items. Also puts
    /// the player's own avatar (the owner) to work and switches their job between chef, waiter and cleaner.
    /// </summary>
    public sealed class StaffController
    {
        private readonly RestaurantSession _session;
        private readonly IReadOnlyList<GridPos> _chefHomes;
        private readonly IReadOnlyList<GridPos> _waiterHomes;

        /// <summary>Stress test only: hire beyond the staff slots, for free.</summary>
        public bool IgnoreCaps { get; set; }

        public StaffController(RestaurantSession session, IReadOnlyList<GridPos> chefHomes, IReadOnlyList<GridPos> waiterHomes)
        {
            _session = session;
            _chefHomes = chefHomes;
            _waiterHomes = waiterHomes;
        }

        /// <summary>The hireable staff type of a role (one per role), or null if the pack has none.</summary>
        public StaffTypeDef TypeFor(StaffRole role)
        {
            foreach (var t in _session.Content.StaffTypes)
                if (t.Role == role && !t.IsOwner) return t;
            return null;
        }

        public HireResult Check(StaffRole role)
        {
            var type = TypeFor(role);
            if (type == null || !FreeHome(role).HasValue) return HireResult.NoSlot;
            return IgnoreCaps ? HireResult.Hired : _session.CheckHire(type);
        }

        public HireResult Hire(StaffRole role, bool free = false)
        {
            var type = TypeFor(role);
            var home = FreeHome(role);
            if (type == null || !home.HasValue)
                return HireResult.NoSlot;
            if (IgnoreCaps)
            {
                _session.Sim.Hire(type, home.Value);
                return HireResult.Hired;
            }
            return _session.Hire(type, home.Value, out _, free);
        }

        public void Dismiss(Staff staff) => _session.Dismiss(staff);

        /// <summary>Gives the owner a job (placing them if they aren't working yet). False if there's no room or no art.</summary>
        public bool AssignOwner(StaffRole role)
        {
            var owner = _session.Owner;
            if (owner != null && owner.Role == role)
                return true;
            var home = FreeHome(role);
            return home.HasValue && _session.AssignOwner(role, home.Value);
        }

        /// <summary>New games and saves from before the owner existed: the player starts as a waiter.</summary>
        public void EnsureOwner()
        {
            if (_session.Owner == null)
                AssignOwner(StaffRole.Waiter);
        }

        /// <summary>Dismisses the most recently hired staff member of a role.</summary>
        public void DismissLatest(StaffRole role)
        {
            var staff = _session.Sim.Staff;
            for (int i = staff.Count - 1; i >= 0; i--)
            {
                if (staff[i].Role == role && !staff[i].IsOwner)
                {
                    _session.Dismiss(staff[i]);
                    return;
                }
            }
        }

        public void ToggleRest(Staff staff)
        {
            if (staff.IsResting || staff.RestRequested) _session.ReturnToWork(staff);
            else _session.SendToRest(staff);
        }

        private GridPos? FreeHome(StaffRole role)
        {
            var grid = _session.Layout.Grid;
            var finder = new DemoRestaurant.Logic.Grid.Pathfinder(grid);
            var path = new List<GridPos>();
            // Cleaners wait in the dining room with the waiters. A home boxed in by furniture is skipped.
            foreach (var home in role == StaffRole.Chef ? _chefHomes : _waiterHomes)
            {
                bool taken = false;
                foreach (var s in _session.Sim.Staff)
                    if (s.Home == home) { taken = true; break; }
                if (!taken && grid.IsWalkable(home) && finder.TryFindPath(_session.Layout.Entrance, home, path))
                    return home;
            }
            // Renovations may occupy the authored homes. Find a reachable alternative.
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var home = new GridPos(x, y);
                if (home == _session.Layout.Entrance || !grid.IsWalkable(home)) continue;
                bool taken = false;
                foreach (var s in _session.Sim.Staff) if (s.Home == home) { taken = true; break; }
                if (!taken && finder.TryFindPath(_session.Layout.Entrance, home, path)) return home;
            }
            return null;
        }
    }
}
