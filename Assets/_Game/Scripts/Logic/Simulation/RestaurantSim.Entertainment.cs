using System;
using System.Linq;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Simulation
{
    public sealed partial class RestaurantSim
    {
        private readonly HashSet<int> _busyEntertainment = new HashSet<int>();
        private readonly List<PlacedFurniture> _entertainmentCandidates = new List<PlacedFurniture>();
        /// <summary>One completed use. The economy authority decides the payout, never the actor.</summary>
        public event Action<Customer, PlacedFurniture> EntertainmentCompleted;

        public bool IsEntertainmentBusy(PlacedFurniture item) => item != null && _busyEntertainment.Contains(item.InstanceId);
        public bool IsEntertainmentPlaying(PlacedFurniture item)
        {
            foreach (var customer in _customers)
                if (customer.Entertainment == item && customer.State == CustomerState.PlayingEntertainment) return true;
            return false;
        }
        public float EntertainmentProgress(Customer customer) => customer?.State != CustomerState.PlayingEntertainment ? -1f :
            _settings.EntertainmentSeconds <= 0 ? 1f : Math.Max(0f, Math.Min(1f, 1f - customer.TimeRemaining / _settings.EntertainmentSeconds));

        private bool TryStartEntertainment(Customer c)
        {
            _entertainmentCandidates.Clear();
            foreach (var item in _layout.Furniture)
                if (item.Def.Kind == FurnitureKind.Entertainment && !IsEntertainmentBusy(item) && !IsBroken(item))
                    _entertainmentCandidates.Add(item);
            while (_entertainmentCandidates.Count > 0)
            {
                int index = _random.Next(_entertainmentCandidates.Count);
                var item = _entertainmentCandidates[index];
                _entertainmentCandidates.RemoveAt(index);
                // Machines are operated from the front. No path through their footprint or use from behind.
                var cell = FrontCell(item) + FacingUtil.ToDelta(item.Facing);
                if (!_layout.Grid.IsWalkable(cell) || !StartPath(c, cell)) continue;
                c.Entertainment = item;
                _busyEntertainment.Add(item.InstanceId);
                c.State = CustomerState.WalkingToEntertainment;
                return true;
            }
            return false;
        }

        private void UpdateEntertainment(Customer c, float dt)
        {
            var item = c.Entertainment;
            if (item == null || !_layout.Furniture.Contains(item) || IsBroken(item)) { Leave(c); return; }
            if (c.State == CustomerState.WalkingToEntertainment)
            {
                if (!FollowPath(c, dt)) return;
                Face(c, FrontCell(item));
                c.State = CustomerState.PlayingEntertainment;
                c.Timer = Math.Max(0f, _settings.EntertainmentSeconds);
                return;
            }
            if (!CountDown(c, dt)) return;
            SetUses(item, UsesOf(item) + 1);
            // Transition before notifying subscribers so a frame or callback cannot pay twice.
            Leave(c);
            EntertainmentCompleted?.Invoke(c, item);
        }

        private void ReleaseEntertainment(Customer c)
        {
            if (c.Entertainment != null) _busyEntertainment.Remove(c.Entertainment.InstanceId);
            c.Entertainment = null;
        }
    }
}
