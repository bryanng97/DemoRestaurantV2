using System.Collections.Generic;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Simulation
{
    public sealed partial class RestaurantSim
    {
        /// <summary>Apply a validated renovation. Keep staff, restart current visits without sales or penalties.</summary>
        public void ApplyLayout(LayoutSnapshot snapshot, Content.ContentPack pack)
        {
            // Validate first; no customer or ticket may change when the snapshot is rejected.
            var probe = new RestaurantLayout(_layout.RoomWidth, _layout.RoomHeight, _layout.Entrance);
            probe.SetWaitingArea(_layout.WaitingSpots);
            if (!snapshot.TryRestore(probe, pack)) throw new System.ArgumentException("Invalid layout snapshot.", nameof(snapshot));
            var types = new List<Content.CustomerTypeDef>();
            foreach (var c in _customers)
                if (c.State != CustomerState.Left && c.State != CustomerState.WalkingToExit && c.State != CustomerState.LeavingOnStreet)
                    types.Add(c.Type);
            while (_customers.Count > 0) Despawn(_customers[_customers.Count - 1]);
            _leaving.Clear();
            _queue.Clear();
            _reservedSeats.Clear();
            _reservedTables.Clear();
            _busyStoves.Clear();
            _busyMachines.Clear();
            ClearToiletVisits();
            snapshot.TryRestore(_layout, pack);

            var homes = new HashSet<GridPos>();
            var path = new List<GridPos>();
            foreach (var s in _staff)
            {
                DropWork(s, requeue: false);
                var home = s.Home;
                if (!_layout.Grid.IsWalkable(home) || homes.Contains(home) || !_pathfinder.TryFindPath(_layout.Entrance, home, path))
                {
                    home = _layout.Entrance;
                    bool found = false;
                    for (int y = 0; y < _layout.Grid.Height && !found; y++)
                    for (int x = 0; x < _layout.Grid.Width && !found; x++)
                    {
                        var cell = new GridPos(x, y);
                        if (cell != _layout.Entrance && _layout.Grid.IsWalkable(cell) && !homes.Contains(cell) &&
                            _pathfinder.TryFindPath(_layout.Entrance, cell, path)) { home = cell; found = true; }
                    }
                }
                homes.Add(home);
                s.Home = home;
                s.Position = Vec2.CellCentre(home);
                s.Path.Clear();
                s.PathIndex = 0;
                s.State = StaffState.Idle;
                s.Timer = 0;
            }
            ClearDishes();
            foreach (var type in types) SpawnCustomer(type);
        }
    }
}
