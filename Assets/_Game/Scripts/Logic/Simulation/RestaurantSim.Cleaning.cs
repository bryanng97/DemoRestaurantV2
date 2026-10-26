using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Simulation
{
    /// <summary>
    /// Dirty tables: a customer who ate leaves their dishes, and nobody can sit there until they are cleared.
    /// Cleaners clear them (<see cref="SimSettings.CleanSeconds"/> divided by their work speed). While no cleaner is
    /// on duty, waiters clear tables when they have nothing to serve, so a restaurant without cleaners never jams.
    /// </summary>
    public sealed partial class RestaurantSim
    {
        /// <summary>Seats with dishes on them, by furniture instance id.</summary>
        private readonly Dictionary<int, PlacedFurniture> _dirtySeats = new Dictionary<int, PlacedFurniture>();
        private readonly Dictionary<int, DishDef> _dirtySeatMeals = new Dictionary<int, DishDef>();
        /// <summary>Dirty seats someone is walking to or clearing.</summary>
        private readonly HashSet<int> _seatsBeingCleaned = new HashSet<int>();

        /// <summary>Seats waiting to be cleared (the renderer draws dirty dishes there).</summary>
        public IEnumerable<PlacedFurniture> DirtySeats => _dirtySeats.Values;
        public int DirtySeatCount => _dirtySeats.Count;
        public bool IsSeatDirty(PlacedFurniture seat) => seat != null && _dirtySeats.ContainsKey(seat.InstanceId);
        /// <summary>The meal whose empty container remains, even after its customer has left.</summary>
        public DishDef DirtyDishFor(PlacedFurniture seat) => seat != null && _dirtySeatMeals.TryGetValue(seat.InstanceId,out var dish) ? dish : null;

        public event Action<PlacedFurniture> SeatCleaned;

        private readonly List<PlacedFurniture> _playerCleaning = new List<PlacedFurniture>();
        private float _playerCleanElapsed;
        public int PlayerCleaningCount => _playerCleaning.Count;
        public bool IsPlayerCleaning(PlacedFurniture seat) => seat != null && _playerCleaning.Contains(seat);
        /// <summary>-1 = not queued, 0 = waiting, 0..1 = active task progress.</summary>
        public float PlayerCleaningProgress(PlacedFurniture seat) => !IsPlayerCleaning(seat) ? -1f :
            _playerCleaning[0] == seat ? Math.Min(1f, _playerCleanElapsed / Math.Max(.1f, _settings.PlayerCleanSeconds)) : 0f;

        /// <summary>Tap a dirty plate to queue it; tap a waiting task again to cancel. Active tasks cannot be cancelled.</summary>
        public bool TogglePlayerCleaning(PlacedFurniture seat)
        {
            if (seat == null || !_dirtySeats.TryGetValue(seat.InstanceId, out var actual) || actual != seat) return false;
            int index = _playerCleaning.IndexOf(seat);
            if (index == 0) return false;
            if (index > 0) { _playerCleaning.RemoveAt(index); return true; }
            // The player takes over a walking/working cleaner without leaving a stale staff assignment.
            foreach (var worker in _staff)
                if (worker.CleaningSeat == seat) { ReleaseCleaning(worker); GoHome(worker); }
            _playerCleaning.Add(seat);
            return true;
        }

        private void UpdatePlayerCleaning(float dt)
        {
            float duration = Math.Max(.1f, _settings.PlayerCleanSeconds);
            while (_playerCleaning.Count > 0)
            {
                var seat = _playerCleaning[0];
                if (!IsSeatDirty(seat)) { _playerCleaning.RemoveAt(0); _playerCleanElapsed = 0f; continue; }
                float used = Math.Min(dt, duration - _playerCleanElapsed);
                _playerCleanElapsed += used; dt -= used;
                if (_playerCleanElapsed < duration) break;
                _playerCleaning.RemoveAt(0); _playerCleanElapsed = 0f;
                _dirtySeats.Remove(seat.InstanceId); _dirtySeatMeals.Remove(seat.InstanceId);
                SeatCleaned?.Invoke(seat);
                if (dt <= 0f) break;
            }
        }

        private void LeaveDishes(PlacedFurniture seat, DishDef dish)
        {
            if (seat != null && _settings.CleanSeconds > 0f)
            {
                _dirtySeats[seat.InstanceId] = seat;
                _dirtySeatMeals[seat.InstanceId] = dish;
            }
        }

        /// <summary>Is any cleaner working (not resting)? If not, waiters clear tables.</summary>
        private bool CleanerOnDuty()
        {
            foreach (var s in _staff)
                if (s.Role == StaffRole.Cleaner && !s.IsResting) return true;
            return false;
        }

        /// <summary>Sends an idle staff member to the nearest dirty seat nobody is clearing yet.</summary>
        private bool TryStartCleaning(Staff s)
        {
            if (_dirtySeats.Count == 0)
                return false;
            GridPos from = s.Position.Cell;
            PlacedFurniture best = null;
            int bestDistance = int.MaxValue;
            foreach (var seat in _dirtySeats.Values)
            {
                if (_seatsBeingCleaned.Contains(seat.InstanceId) || IsPlayerCleaning(seat))
                    continue;
                int d = from.ManhattanDistance(seat.Origin);
                if (d < bestDistance)
                {
                    best = seat;
                    bestDistance = d;
                }
            }
            if (best == null || !TryStartAccessPath(s, best.Origin))
                return false;
            _seatsBeingCleaned.Add(best.InstanceId);
            s.CleaningSeat = best;
            s.CookProgress = 0f;
            s.State = StaffState.WalkingToClean;
            return true;
        }

        private void UpdateCleaning(Staff s, float dt)
        {
            var seat = s.CleaningSeat;
            if (seat == null || !_dirtySeats.ContainsKey(seat.InstanceId))
            {
                ReleaseCleaning(s);
                GoHome(s);
                return;
            }
            if (s.State == StaffState.WalkingToClean)
            {
                if (FollowPath(s, dt))
                {
                    Face(s, seat.Origin);
                    s.Timer = _settings.CleanSeconds; // work left at work speed 1
                    s.State = StaffState.Cleaning;
                }
                return;
            }

            s.Timer -= dt * s.EffectiveCookSpeed;
            s.CookProgress = Math.Min(1f, 1f - s.Timer / _settings.CleanSeconds);
            if (s.Timer <= 0f)
            {
                _dirtySeats.Remove(seat.InstanceId);
                _dirtySeatMeals.Remove(seat.InstanceId);
                ReleaseCleaning(s);
                SeatCleaned?.Invoke(seat);
                GoHome(s);
            }
        }

        private void ReleaseCleaning(Staff s)
        {
            if (s.CleaningSeat != null)
                _seatsBeingCleaned.Remove(s.CleaningSeat.InstanceId);
            s.CleaningSeat = null;
            s.CookProgress = 0f;
        }

        /// <summary>A renovation rebuilt the room: furniture instances are new, so old dishes go.</summary>
        private void ClearDishes()
        {
            _dirtySeats.Clear();
            _dirtySeatMeals.Clear();
            _seatsBeingCleaned.Clear();
            _playerCleaning.Clear(); _playerCleanElapsed = 0f;
            foreach (var s in _staff) s.CleaningSeat = null;
        }
    }
}
