using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;

namespace DemoRestaurant.Logic.Simulation
{
    public sealed class SimSettings
    {
        public float OrderSeconds = 1.5f;
        public StaffEnergyRules StaffEnergy = new StaffEnergyRules();
        /// <summary>Scales each guest type's food patience (published game rules); read when a guest arrives.</summary>
        public FoodPatienceRules FoodPatience = new FoodPatienceRules();
        /// <summary>Current ambience points (each adds the published seconds of food patience); null = none.</summary>
        public Func<int> Ambience;
        /// <summary>Extra food patience from placed furniture effects (Phase 3); null = none.</summary>
        public Func<int> PatienceBonusSeconds;
        /// <summary>Seconds to clear a table after a customer ate (at work speed 1). 0 = no dirty dishes.</summary>
        public float CleanSeconds;
        /// <summary>Duration of each player-requested cleaning task; tasks run one at a time.</summary>
        public float PlayerCleanSeconds = 5f;
        /// <summary>
        /// Guests walk in along the street to the door and leave along it (the game). Off: they appear and vanish
        /// at the entrance (tests and the stress test).
        /// </summary>
        public bool StreetWalk;
        /// <summary>
        /// A guest who sits on a chair with no table beside it complains, waits this long, then leaves unhappy
        /// (<see cref="RestaurantSim.CustomerNoTable"/>). 0 = chairs work alone (tests and the stress test).
        /// </summary>
        public float NoTableWaitSeconds;
        /// <summary>Share of guests who use the toilet after their meal (set from the level; 0 = nobody).</summary>
        public float ToiletChance;
        public float ToiletSeconds = 10f;
        public float SinkSeconds = 4f;
        public float FixSeconds = 6f;
        public float EntertainmentSeconds = 30f;
        public TrashRules Trash = new TrashRules();
    }

    /// <summary>
    /// Customer flow for one restaurant: arrive, wait in the waiting area if every seat is taken, sit,
    /// order, wait for food (with limited patience), eat, pay, leave.
    /// Pure logic driven by <see cref="Tick"/>; the engine only reads state and listens to events.
    ///
    /// Payments raised here are client-side predictions for display. The server awards the real coins.
    /// </summary>
    public sealed partial class RestaurantSim
    {
        private readonly RestaurantLayout _layout;
        private readonly RestaurantMenu _menu;
        private readonly SimSettings _settings;
        private readonly Random _random;
        private readonly Pathfinder _pathfinder;
        private readonly List<Customer> _customers = new List<Customer>();
        private readonly HashSet<int> _reservedSeats = new HashSet<int>();
        private readonly HashSet<int> _reservedTables = new HashSet<int>();
        private readonly List<Customer> _leaving = new List<Customer>();
        private readonly List<Customer> _queue = new List<Customer>();
        private readonly List<PlacedFurniture> _seatCandidates = new List<PlacedFurniture>();
        private readonly List<GridPos> _pathCandidate = new List<GridPos>();
        private int _nextCustomerId = 1;

        public IReadOnlyList<Customer> Customers => _customers;
        /// <summary>Customers in the waiting area, first in line first.</summary>
        public IReadOnlyList<Customer> Queue => _queue;
        public RestaurantLayout Layout => _layout;
        /// <summary>What customers can order. Changes apply to the next order; existing orders are kept.</summary>
        public RestaurantMenu Menu => _menu;

        public event Action<Customer> CustomerSpawned;
        public event Action<Customer> CustomerLeft;
        public event Action<Customer, DishDef> CustomerOrdered;
        /// <summary>Raised when a customer pays. Amount is a client-side prediction; the server is authoritative.</summary>
        public event Action<Customer, int> CustomerPaid;
        /// <summary>Food took longer than the customer's patience; they leave without paying.</summary>
        public event Action<Customer> CustomerAngry;
        /// <summary>Every seat and every waiting spot was taken when they arrived.</summary>
        public event Action<Customer> CustomerTurnedAway;
        /// <summary>Waited in the waiting area longer than their patience and left.</summary>
        public event Action<Customer> CustomerGaveUpWaiting;
        /// <summary>A guest sat where there is no table and left unhappy.</summary>
        public event Action<Customer> CustomerNoTable;

        public RestaurantSim(RestaurantLayout layout, RestaurantMenu menu, int seed, SimSettings settings = null)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));
            _settings = settings ?? new SimSettings();
            _random = new Random(seed);
            _pathfinder = new Pathfinder(layout.Grid);
            WatchReach();
        }

        /// <summary>Food patience of a guest arriving now: the type's time scaled by the published base, plus ambience.</summary>
        public float FoodPatienceFor(CustomerTypeDef type)
        {
            var rules = _settings.FoodPatience;
            int ambience = Math.Max(0, _settings.Ambience?.Invoke() ?? 0);
            int furniture = Math.Max(0, _settings.PatienceBonusSeconds?.Invoke() ?? 0);
            return type.FoodPatienceSeconds * rules.Scale + Math.Max(0f, rules.perAmbienceSeconds) * ambience + furniture;
        }

        public Customer SpawnCustomer(CustomerTypeDef type)
        {
            if (_settings.StreetWalk)
            {
                // Appear at the front end of the street and walk to the door; decide on a seat on arrival.
                var route = _layout.StreetRouteIn();
                var walker = new Customer(_nextCustomerId++, type, Vec2.CellCentre(route[0]));
                walker.FoodPatienceSeconds = FoodPatienceFor(type);
                _customers.Add(walker);
                walker.Path.Clear();
                walker.Path.AddRange(route);
                walker.PathIndex = 0;
                walker.State = CustomerState.ArrivingOnStreet;
                CustomerSpawned?.Invoke(walker);
                return walker;
            }

            var c = new Customer(_nextCustomerId++, type, Vec2.CellCentre(_layout.Entrance));
            c.FoodPatienceSeconds = FoodPatienceFor(type);
            _customers.Add(c);
            CustomerSpawned?.Invoke(c);
            ArriveAtDoor(c);
            return c;
        }

        /// <summary>At the entrance: a free seat, a place in the waiting area, or turned away.</summary>
        private void ArriveAtDoor(Customer c)
        {
            if (TrashCount > 0 && _random.NextDouble() < TrashTurnAwayChance)
            {
                WalkAway(c);
                CustomerTurnedAway?.Invoke(c);
                return;
            }
            // Seats go to whoever has waited longest, so newcomers only walk straight in when nobody is queuing.
            if (_queue.Count == 0 && TryReserveSeat(c))
                c.State = CustomerState.WalkingToSeat;
            else if (TryStartEntertainment(c)) { }
            else if (_queue.Count < _layout.WaitingSpots.Count)
                JoinQueue(c);
            else
            {
                WalkAway(c);
                CustomerTurnedAway?.Invoke(c);
            }
        }

        /// <summary>Gone from the entrance: down the street (street walk) or straight away.</summary>
        private void WalkAway(Customer c)
        {
            if (_settings.StreetWalk)
            {
                c.Path.Clear();
                c.Path.AddRange(_layout.StreetRouteOut());
                c.PathIndex = 0;
                c.State = CustomerState.LeavingOnStreet;
                return;
            }
            c.State = CustomerState.Left;
            _leaving.Add(c);
        }

        /// <summary>Removes a customer immediately, wherever they are (used by the stress test to scale down).</summary>
        public void Despawn(Customer c)
        {
            if (!_customers.Remove(c))
                return;
            _queue.Remove(c);
            ReleaseEntertainment(c);
            ReleaseSeat(c);
            CancelTicket(c);
            c.State = CustomerState.Left;
            CustomerLeft?.Invoke(c);
        }

        public bool IsSeatReserved(PlacedFurniture seat) => _reservedSeats.Contains(seat.InstanceId);

        public void Tick(float dt)
        {
            if (dt <= 0)
                return;

            UpdatePlayerCleaning(dt);
            TickTrash(dt);

            AdmitFromQueue();
            for (int i = 0; i < _customers.Count; i++)
                Update(_customers[i], dt);

            UpdateKitchen(dt);

            if (_leaving.Count > 0)
            {
                foreach (var c in _leaving)
                {
                    _customers.Remove(c);
                    CustomerLeft?.Invoke(c);
                }
                _leaving.Clear();
            }
        }

        private void Update(Customer c, float dt)
        {
            switch (c.State)
            {
                case CustomerState.ArrivingOnStreet:
                    if (FollowPath(c, dt))
                        ArriveAtDoor(c);
                    break;

                case CustomerState.LeavingOnStreet:
                    if (FollowPath(c, dt))
                    {
                        c.State = CustomerState.Left;
                        _leaving.Add(c);
                    }
                    break;

                case CustomerState.WalkingToQueue:
                    c.WaitedForSeat += dt;
                    if (FollowPath(c, dt))
                    {
                        c.State = CustomerState.WaitingInQueue;
                        c.Facing = FacingUtil.FromDelta(_layout.Grid.Width * 0.5f - c.Position.X,
                            _layout.Grid.Height * 0.5f - c.Position.Y); // look inward from the doorway queue
                    }
                    CheckQueuePatience(c);
                    break;

                case CustomerState.WaitingInQueue:
                    c.WaitedForSeat += dt;
                    CheckQueuePatience(c);
                    break;

                case CustomerState.WalkingToSeat:
                    if (FollowPath(c, dt))
                        SitDown(c);
                    break;

                case CustomerState.NoTable:
                    if (CountDown(c, dt))
                    {
                        CustomerNoTable?.Invoke(c);
                        Leave(c);
                    }
                    break;

                case CustomerState.Ordering:
                    if (CountDown(c, dt))
                    {
                        c.Order = PickDish();
                        c.State = CustomerState.WaitingForFood;
                        c.WaitedForFood = 0f;
                        PlaceOrder(c);
                        CustomerOrdered?.Invoke(c, c.Order);
                    }
                    break;

                case CustomerState.WaitingForFood:
                    // A waiter delivers the dish (see Serve). Until then, patience runs down.
                    c.WaitedForFood += dt;
                    if (c.WaitedForFood >= c.FoodPatienceSeconds)
                    {
                        c.IsAngry = true;
                        CancelTicket(c);
                        CustomerAngry?.Invoke(c);
                        Leave(c);
                    }
                    break;

                case CustomerState.Eating:
                    if (CountDown(c, dt))
                    {
                        CustomerPaid?.Invoke(c, c.Order.Price);
                        LeaveDishes(c.Seat,c.Order);
                        AfterMeal(c);
                    }
                    break;

                case CustomerState.WalkingToEntertainment:
                case CustomerState.PlayingEntertainment:
                    UpdateEntertainment(c, dt);
                    break;

                case CustomerState.WalkingToToilet:
                case CustomerState.OnToilet:
                case CustomerState.WalkingToSink:
                case CustomerState.UsingSink:
                    UpdateToiletVisit(c, dt);
                    break;

                case CustomerState.WalkingToExit:
                    if (FollowPath(c, dt))
                        WalkAway(c);
                    break;
            }
        }

        private readonly List<DishCategory> _categoryCandidates = new List<DishCategory>();
        private readonly List<DishDef> _dishCandidates = new List<DishDef>();

        /// <summary>
        /// A guest first picks a course (starter, main, dessert, drink) among those on the menu, then a dish of that
        /// course, so every course gets ordered however many dishes each one has.
        /// </summary>
        private DishDef PickDish()
        {
            _categoryCandidates.Clear();
            // Drinks only when a waiter can get to a drink machine (as in the reference game).
            bool drinks = DrinksReachable;
            foreach (var d in _menu.Active)
                if (!_categoryCandidates.Contains(d.Category) && (drinks || d.Category != DishCategory.Drink)) _categoryCandidates.Add(d.Category);
            if (_categoryCandidates.Count == 0)
                foreach (var d in _menu.Active)
                    if (!_categoryCandidates.Contains(d.Category)) _categoryCandidates.Add(d.Category);
            var course = _categoryCandidates[_random.Next(_categoryCandidates.Count)];
            _dishCandidates.Clear();
            foreach (var d in _menu.Active)
                if (d.Category == course) _dishCandidates.Add(d);
            return _dishCandidates[_random.Next(_dishCandidates.Count)];
        }

        // ---------------------------------------------------------------- waiting area

        private void JoinQueue(Customer c)
        {
            _queue.Add(c);
            c.WaitedForSeat = 0f;
            WalkToQueueSpot(c, _queue.Count - 1);
        }

        private void WalkToQueueSpot(Customer c, int index)
        {
            c.QueueSpot = index;
            var spot = _layout.WaitingSpots[index];
            if (StartPath(c, spot))
                c.State = CustomerState.WalkingToQueue;
            else
            {
                c.Path.Clear();
                c.State = CustomerState.WaitingInQueue;
            }
        }

        /// <summary>First in line takes any free seat; everyone behind steps forward.</summary>
        private void AdmitFromQueue()
        {
            bool moved = false;
            while (_queue.Count > 0 && TryReserveSeat(_queue[0]))
            {
                var c = _queue[0];
                _queue.RemoveAt(0);
                c.QueueSpot = -1;
                c.State = CustomerState.WalkingToSeat;
                moved = true;
            }
            if (moved)
                ShuffleQueueForward();
        }

        private void ShuffleQueueForward()
        {
            for (int i = 0; i < _queue.Count; i++)
                if (_queue[i].QueueSpot != i)
                    WalkToQueueSpot(_queue[i], i);
        }

        private void CheckQueuePatience(Customer c)
        {
            if (c.WaitedForSeat < c.Type.PatienceSeconds)
                return;
            _queue.Remove(c);
            c.QueueSpot = -1;
            CustomerGaveUpWaiting?.Invoke(c);
            Leave(c);
            ShuffleQueueForward();
        }

        // ---------------------------------------------------------------- seats

        private bool TryReserveSeat(Customer c)
        {
            // No waiter can fetch plates when every stove is walled off from the door: nowhere to be served.
            if (HasStove() && !KitchenReachable) return false;
            _seatCandidates.Clear();
            foreach (var f in _layout.Furniture)
            {
                // A seat with dishes on it waits for a cleaner (or a waiter) first.
                if (f.Def.Kind == FurnitureKind.Chair && !_reservedSeats.Contains(f.InstanceId) && !_dirtySeats.ContainsKey(f.InstanceId) && !DiningTableUnavailable(f))
                    _seatCandidates.Add(f);
            }

            // Try free seats in random order so customers spread around the room.
            while (_seatCandidates.Count > 0)
            {
                int k = _random.Next(_seatCandidates.Count);
                var seat = _seatCandidates[k];
                _seatCandidates[k] = _seatCandidates[_seatCandidates.Count - 1];
                _seatCandidates.RemoveAt(_seatCandidates.Count - 1);

                if (StartPath(c, seat.Origin, allowBlockedGoal: true))
                {
                    c.Seat = seat;
                    c.Table = SeatFacing.TableFor(_layout,seat);
                    _reservedSeats.Add(seat.InstanceId);
                    if (c.Table != null) _reservedTables.Add(c.Table.InstanceId);
                    return true;
                }
            }
            return false;
        }

        private void SitDown(Customer c)
        {
            c.Z = c.Seat.Def.SeatHeight;
            c.Facing = c.Seat.Facing;
            if (_settings.NoTableWaitSeconds > 0f && !HasTableBeside(c.Seat))
            {
                c.State = CustomerState.NoTable;
                c.Timer = _settings.NoTableWaitSeconds;
                return;
            }
            c.State = CustomerState.Ordering;
            c.Timer = _settings.OrderSeconds;
        }

        /// <summary>A table on one of the chair's four sides.</summary>
        private bool HasTableBeside(PlacedFurniture chair)
            => SeatFacing.TableFor(_layout,chair) != null;

        private bool DiningTableUnavailable(PlacedFurniture chair)
        {
            var table = SeatFacing.TableFor(_layout,chair);
            if (table == null) return false; // lone chairs still exercise the existing no-table complaint flow
            if (_reservedTables.Contains(table.InstanceId)) return true;
            foreach (var dirty in _dirtySeats.Values)
                if (SeatFacing.TableFor(_layout,dirty) == table) return true;
            return false;
        }

        private void Leave(Customer c)
        {
            ReleaseEntertainment(c);
            ReleaseSeat(c);
            c.Z = 0;
            if (StartPath(c, _layout.Entrance))
            {
                c.State = CustomerState.WalkingToExit;
            }
            else
            {
                // Walled in by furniture placed after they arrived; leave on the spot.
                c.State = CustomerState.Left;
                _leaving.Add(c);
            }
        }

        private void ReleaseSeat(Customer c)
        {
            if (c.Table != null) { _reservedTables.Remove(c.Table.InstanceId); c.Table = null; }
            if (c.Seat != null)
            {
                _reservedSeats.Remove(c.Seat.InstanceId);
                c.Seat = null;
            }
        }

        // ---------------------------------------------------------------- movement

        /// <param name="allowBlockedGoal">Only used when a customer enters their reserved chair.</param>
        private bool StartPath(Agent c, GridPos goal, bool allowBlockedGoal = false)
        {
            var start = c.Position.Cell;
            if (!_pathfinder.TryFindPath(start, goal, _pathCandidate, allowBlockedGoal))
                return false;
            // A replacement route starts at this cell's centre. Turning directly from a fractional
            // position to the next cell cuts diagonally across the corner of neighbouring furniture.
            c.Path.Clear();
            if ((c.Position - Vec2.CellCentre(start)).Length > 1e-5f)
                c.Path.Add(start);
            c.Path.AddRange(_pathCandidate);
            c.PathIndex = 0;
            return true;
        }

        /// <summary>Advances along the current path. Returns true on arrival.</summary>
        private static bool FollowPath(Agent c, float dt)
        {
            float budget = c.WalkSpeed * dt;
            while (c.PathIndex < c.Path.Count)
            {
                Vec2 target = Vec2.CellCentre(c.Path[c.PathIndex]);
                Vec2 delta = target - c.Position;
                if (delta.Length > 1e-5f)
                    c.Facing = FacingUtil.FromDelta(delta.X, delta.Y);

                float before = delta.Length;
                bool reached = Vec2.MoveTowards(c.Position, target, budget, out Vec2 next);
                c.Position = next;
                if (!reached)
                    return false;
                budget -= before;
                c.PathIndex++;
            }
            return true;
        }

        private static bool CountDown(Agent c, float dt)
        {
            c.Timer -= dt;
            return c.Timer <= 0;
        }
    }
}
