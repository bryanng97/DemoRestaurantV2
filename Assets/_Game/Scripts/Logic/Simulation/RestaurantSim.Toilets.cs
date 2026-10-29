using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Simulation
{
    /// <summary>
    /// Toilets, as in the reference game: after paying, some guests (<see cref="SimSettings.ToiletChance"/>) walk to a
    /// free working toilet, sit for a while, then wash their hands at a free sink (if any) and leave happy. With no
    /// working toilet they leave unhappy. Each use counts; at its BreakCount a toilet breaks until a cleaner (or the
    /// player, <see cref="Fix"/>) fixes it.
    /// </summary>
    public sealed partial class RestaurantSim
    {
        private readonly Dictionary<int, int> _toiletUses = new Dictionary<int, int>();
        private readonly HashSet<int> _busyToilets = new HashSet<int>();
        private readonly HashSet<int> _busySinks = new HashSet<int>();
        private readonly HashSet<int> _fixing = new HashSet<int>();
        private readonly List<PlacedFurniture> _toiletCandidates = new List<PlacedFurniture>();

        /// <summary>A guest wanted the toilet but none was free and working: they leave unhappy.</summary>
        public event Action<Customer> CustomerNoToilet;
        /// <summary>A guest finished using a toilet (one use of wear, recorded for the server).</summary>
        public event Action<PlacedFurniture> ToiletUsed;
        /// <summary>A toilet reached its BreakCount uses.</summary>
        public event Action<PlacedFurniture> ToiletBroke;
        /// <summary>A broken toilet or machine was repaired (by the player or a cleaner).</summary>
        public event Action<PlacedFurniture> FurnitureFixed;

        public float ToiletChance
        {
            get => _settings.ToiletChance;
            set => _settings.ToiletChance = Math.Max(0f, Math.Min(1f, value));
        }

        /// <summary>Completion of the current bathroom activity, or -1 when the customer is not using a fixture.</summary>
        public float BathroomProgress(Customer customer)
        {
            if (customer == null) return -1f;
            float duration = customer.State == CustomerState.OnToilet ? _settings.ToiletSeconds :
                customer.State == CustomerState.UsingSink ? _settings.SinkSeconds : -1f;
            return duration < 0f ? -1f : duration <= 0f ? 1f : Math.Max(0f, Math.Min(1f, 1f - customer.TimeRemaining / duration));
        }

        public int UsesOf(PlacedFurniture toilet) =>
            toilet?.Def.Kind == FurnitureKind.Entertainment ? toilet.UsageCount :
            toilet != null && _toiletUses.TryGetValue(toilet.InstanceId, out int n) ? n : 0;

        public bool IsBroken(PlacedFurniture f) => f != null && f.Def.BreakCount > 0 && UsesOf(f) >= f.Def.BreakCount;

        /// <summary>Fixes a broken toilet at once (the player tapping it). Returns false if it wasn't broken.</summary>
        public bool Fix(PlacedFurniture toilet)
        {
            if (!IsBroken(toilet)) return false;
            toilet.UsageCount = 0;
            _toiletUses.Remove(toilet.InstanceId);
            FurnitureFixed?.Invoke(toilet);
            return true;
        }

        /// <summary>Loading a save: how often a toilet has been used since it was last fixed.</summary>
        public void SetUses(PlacedFurniture toilet, int uses)
        {
            if (toilet == null) return;
            if (toilet.Def.Kind == FurnitureKind.Entertainment) { toilet.UsageCount = Math.Max(0, uses); return; }
            if (uses > 0) _toiletUses[toilet.InstanceId] = uses;
            else _toiletUses.Remove(toilet.InstanceId);
        }

        private void AfterMeal(Customer c)
        {
            if (_settings.ToiletChance > 0f && _random.NextDouble() < _settings.ToiletChance) GoToToilet(c);
            else Leave(c);
        }

        private void GoToToilet(Customer c)
        {
            ReleaseSeat(c);
            c.Z = 0;
            _toiletCandidates.Clear();
            foreach (var f in _layout.Furniture)
                if (f.Def.Kind == FurnitureKind.Toilet && !_busyToilets.Contains(f.InstanceId) && !IsBroken(f))
                    _toiletCandidates.Add(f);
            while (_toiletCandidates.Count > 0)
            {
                int k = _random.Next(_toiletCandidates.Count);
                var toilet = _toiletCandidates[k];
                _toiletCandidates[k] = _toiletCandidates[_toiletCandidates.Count - 1];
                _toiletCandidates.RemoveAt(_toiletCandidates.Count - 1);
                if (!StartPath(c, toilet.Origin, allowBlockedGoal: true)) continue;
                c.Seat = toilet;
                _busyToilets.Add(toilet.InstanceId);
                c.State = CustomerState.WalkingToToilet;
                return;
            }
            CustomerNoToilet?.Invoke(c);
            Leave(c);
        }

        private void UpdateToiletVisit(Customer c, float dt)
        {
            switch (c.State)
            {
                case CustomerState.WalkingToToilet:
                    if (FollowPath(c, dt))
                    {
                        c.Z = c.Seat.Def.SeatHeight;
                        c.Facing = c.Seat.Facing;
                        c.State = CustomerState.OnToilet;
                        c.Timer = _settings.ToiletSeconds;
                    }
                    break;

                case CustomerState.OnToilet:
                    if (CountDown(c, dt)) FinishToilet(c);
                    break;

                case CustomerState.WalkingToSink:
                    if (FollowPath(c, dt))
                    {
                        Face(c, c.Sink.Origin);
                        c.State = CustomerState.UsingSink;
                        c.Timer = _settings.SinkSeconds;
                    }
                    break;

                case CustomerState.UsingSink:
                    if (CountDown(c, dt))
                    {
                        _busySinks.Remove(c.Sink.InstanceId);
                        c.Sink = null;
                        Leave(c);
                    }
                    break;
            }
        }

        private void FinishToilet(Customer c)
        {
            var toilet = c.Seat;
            _busyToilets.Remove(toilet.InstanceId);
            _toiletUses[toilet.InstanceId] = UsesOf(toilet) + 1;
            ToiletUsed?.Invoke(toilet);
            if (IsBroken(toilet)) ToiletBroke?.Invoke(toilet);
            ReleaseSeat(c);
            c.Z = 0;
            foreach (var f in _layout.Furniture)
            {
                if (f.Def.Kind != FurnitureKind.Sink || _busySinks.Contains(f.InstanceId)) continue;
                if (!TryStartFurnitureAccessPath(c, f)) continue;
                c.Sink = f;
                _busySinks.Add(f.InstanceId);
                c.State = CustomerState.WalkingToSink;
                return;
            }
            Leave(c); // no sink: off they go
        }

        /// <summary>A free cleaner walks to a broken toilet nobody is fixing yet.</summary>
        private bool TryStartFixing(Staff s)
        {
            foreach (var f in _layout.Furniture)
            {
                if ((f.Def.Kind != FurnitureKind.Toilet && f.Def.Kind != FurnitureKind.Entertainment) || !IsBroken(f) || _fixing.Contains(f.InstanceId)) continue;
                if (!TryStartFurnitureAccessPath(s, f)) continue;
                _fixing.Add(f.InstanceId);
                s.FixTarget = f;
                s.State = StaffState.WalkingToFix;
                return true;
            }
            return false;
        }

        private void UpdateFixing(Staff s, float dt)
        {
            var toilet = s.FixTarget;
            if (s.State == StaffState.WalkingToFix)
            {
                if (!FollowPath(s, dt)) return;
                Face(s, FrontCell(toilet));
                s.Timer = _settings.FixSeconds;
                s.CookProgress = 0f;
                s.State = StaffState.Fixing;
                return;
            }
            s.Timer -= dt * s.EffectiveCookSpeed;
            s.CookProgress = _settings.FixSeconds > 0 ? Math.Min(1f, 1f - s.Timer / _settings.FixSeconds) : 1f;
            if (s.Timer > 0f) return;
            _toiletUses.Remove(toilet.InstanceId);
            ReleaseFixing(s);
            toilet.UsageCount = 0;
            FurnitureFixed?.Invoke(toilet);
            GoHome(s);
        }

        private void ReleaseFixing(Staff s)
        {
            if (s.FixTarget != null) _fixing.Remove(s.FixTarget.InstanceId);
            s.FixTarget = null;
            s.CookProgress = 0f;
        }

        /// <summary>Clears visits in progress when the layout is replaced (every guest restarts).</summary>
        private void ClearToiletVisits()
        {
            _busyToilets.Clear();
            _busySinks.Clear();
            _fixing.Clear();
        }
    }
}
