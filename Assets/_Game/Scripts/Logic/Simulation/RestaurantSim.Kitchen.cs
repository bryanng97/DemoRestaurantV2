using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Simulation
{
    /// <summary>
    /// Kitchen flow (as in the reference game): order ticket -> chef cooks at a free stove for the dish's preparation
    /// time -> the plate waits on that stove -> a waiter collects it there and carries it to the customer. A stove holding
    /// a plate stays busy until it is collected, so throughput is limited by chefs, stoves and waiters.
    /// </summary>
    public sealed partial class RestaurantSim
    {
        private readonly List<Staff> _staff = new List<Staff>();
        private readonly List<OrderTicket> _tickets = new List<OrderTicket>();
        private readonly HashSet<int> _busyStoves = new HashSet<int>();
        /// <summary>Drink machines a waiter is pouring at (one at a time each).</summary>
        private readonly HashSet<int> _busyMachines = new HashSet<int>();
        private int _nextStaffId = 1;
        private int _nextTicketId = 1;

        public IReadOnlyList<Staff> Staff => _staff;
        /// <summary>Open tickets (queued, cooking, ready or being delivered), oldest first.</summary>
        public IReadOnlyList<OrderTicket> Tickets => _tickets;

        public event Action<Staff> StaffHired;
        public event Action<Staff> StaffDismissed;
        public event Action<OrderTicket> DishReady;
        public event Action<OrderTicket> DishServed;
        /// <summary>Raised on every ticket transition, including ones that last zero ticks.</summary>
        public event Action<OrderTicket> TicketStateChanged;

        private void SetState(OrderTicket t, TicketState state)
        {
            t.State = state;
            TicketStateChanged?.Invoke(t);
        }

        public Staff Hire(StaffTypeDef type, GridPos home)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!_layout.Grid.IsWalkable(home))
                throw new ArgumentException($"Staff home {home} must be a walkable cell.", nameof(home));
            var s = new Staff(_nextStaffId++, type, home, _settings.StaffEnergy);
            _staff.Add(s);
            StaffHired?.Invoke(s);
            return s;
        }

        /// <summary>
        /// Sends a staff member off duty to recover energy. If they are in the middle of a job they finish it first.
        /// They return to work by themselves at <see cref="StaffEnergyRules.AutoReturnAt"/>, or via <see cref="ReturnToWork"/>.
        /// </summary>
        public void SendToRest(Staff s)
        {
            if (s == null || s.IsResting)
                return;
            if (s.IsBusy)
            {
                s.RestRequested = true;
                return;
            }
            StartResting(s);
        }

        public void ReturnToWork(Staff s)
        {
            if (s == null)
                return;
            s.RestRequested = false;
            s.IsResting = false;
        }

        /// <summary>Instantly adds energy (energy items).</summary>
        public void RestoreEnergy(Staff s, float amount)
        {
            if (s != null && amount > 0)
                s.Energy += amount;
        }

        /// <summary>Sets energy and rest state directly (loading a save, idle progress).</summary>
        public void SetEnergy(Staff s, float energy, bool resting)
        {
            if (s == null)
                return;
            s.Energy = energy;
            if (resting && !s.IsBusy) StartResting(s);
            else if (!resting) ReturnToWork(s);
        }

        private void StartResting(Staff s)
        {
            s.RestRequested = false;
            s.IsResting = true;
            GoHome(s);
        }

        /// <summary>On duty (working or waiting for work) drains energy; resting recovers it.</summary>
        private void UpdateEnergy(Staff s, float dt)
        {
            var rules = _settings.StaffEnergy;
            if (s.IsResting)
            {
                s.Energy += rules.RecoverPerMinute * dt / 60f;
                if (s.Energy >= rules.AutoReturnAt)
                    s.IsResting = false;
            }
            else
            {
                s.Energy -= rules.DrainPerMinute * dt / 60f;
            }
        }

        /// <summary>
        /// Gives the owner (the player's avatar) another job, e.g. chef to cleaner. Work in progress goes back to the
        /// queue; energy and rest state are kept. <paramref name="home"/> is where they wait in the new role.
        /// </summary>
        public void ChangeType(Staff s, StaffTypeDef type, GridPos home)
        {
            if (s == null || type == null) throw new ArgumentNullException(s == null ? nameof(s) : nameof(type));
            if (!_staff.Contains(s)) throw new ArgumentException("Not on the staff.", nameof(s));
            if (!_layout.Grid.IsWalkable(home))
                throw new ArgumentException($"Staff home {home} must be a walkable cell.", nameof(home));
            DropWork(s, requeue: true);
            s.Type = type;
            s.Station = null;
            s.Home = home;
            GoHome(s);
        }

        /// <summary>Removes a staff member. Work in progress goes back to the queue (or stays on the counter).</summary>
        public void Dismiss(Staff s)
        {
            if (!_staff.Remove(s))
                return;
            DropWork(s, requeue: true);
            StaffDismissed?.Invoke(s);
        }

        private void PlaceOrder(Customer c)
        {
            var ticket = new OrderTicket(_nextTicketId++, c, c.Order);
            c.Ticket = ticket;
            _tickets.Add(ticket);
            SetState(ticket, TicketState.Queued);
        }

        private void CancelTicket(Customer c)
        {
            var t = c.Ticket;
            if (t == null)
                return;
            c.Ticket = null;
            SetState(t, TicketState.Cancelled);
            _tickets.Remove(t);
            ReleasePlate(t);
            if (t.Chef != null && t.Chef.Ticket == t) DropWork(t.Chef, requeue: false);
            if (t.Waiter != null && t.Waiter.Ticket == t) DropWork(t.Waiter, requeue: false);
        }

        private void UpdateKitchen(float dt)
        {
            for (int i = 0; i < _staff.Count; i++)
            {
                UpdateEnergy(_staff[i], dt);
                UpdateStaff(_staff[i], dt);
            }
            Dispatch(dt);
        }

        private void UpdateStaff(Staff s, float dt)
        {
            var t = s.Ticket;
            switch (s.State)
            {
                case StaffState.Idle:
                    FollowPath(s, dt); // walking home, if not there yet
                    break;

                case StaffState.WalkingToStove:
                    if (FollowPath(s, dt))
                    {
                        Face(s, FrontCell(s.Stove));
                        s.Timer = t.Dish.CookSeconds; // work left, in seconds at cook speed 1
                        s.CookProgress = 0f;
                        s.State = StaffState.Cooking;
                    }
                    break;

                case StaffState.Cooking:
                    // Tired chefs cook slower: work done scales with cook speed and the energy multiplier.
                    s.Timer -= dt * s.EffectiveCookSpeed;
                    s.CookProgress = t.Dish.CookSeconds > 0 ? Math.Min(1f, 1f - s.Timer / t.Dish.CookSeconds) : 1f;
                    if (s.Timer <= 0f)
                    {
                        // The plate stays on the stove (still busy) until a waiter collects it; the chef stays put.
                        t.Stove = s.Stove;
                        t.CounterSpot = s.Stove.Origin;
                        s.Stove = null;
                        s.CookProgress = 1f;
                        SetState(t, TicketState.Plating);
                        SetState(t, TicketState.Ready);
                        s.Ticket = null;
                        s.State = StaffState.Idle;
                        DishReady?.Invoke(t);
                    }
                    break;

                case StaffState.WalkingToDrinks:
                    if (FollowPath(s, dt))
                    {
                        Face(s, FrontCell(s.Stove));
                        s.Timer = t.Dish.CookSeconds; // pouring time, at work speed 1
                        s.CookProgress = 0f;
                        s.State = StaffState.Pouring;
                    }
                    break;

                case StaffState.Pouring:
                    s.Timer -= dt * s.EffectiveCookSpeed;
                    s.CookProgress = t.Dish.CookSeconds > 0 ? Math.Min(1f, 1f - s.Timer / t.Dish.CookSeconds) : 1f;
                    if (s.Timer <= 0f)
                    {
                        _busyMachines.Remove(s.Stove.InstanceId);
                        s.Stove = null;
                        s.CookProgress = 0f;
                        if (TryStartAccessPath(s, t.Customer.Seat?.Origin ?? t.Customer.Position.Cell))
                            s.State = StaffState.WalkingToCustomer;
                        else
                            DropWork(s, requeue: true); // the guest can't be reached: someone tries again later
                    }
                    break;

                case StaffState.WalkingToPickup:
                    if (FollowPath(s, dt))
                    {
                        Face(s, t.CounterSpot.Value);
                        if (TryStartAccessPath(s, t.Customer.Seat?.Origin ?? t.Customer.Position.Cell))
                        {
                            ReleasePlate(t);
                            s.State = StaffState.WalkingToCustomer;
                        }
                        else
                        {
                            // Leave the plate on the stove until the customer can be reached.
                            t.Waiter = null;
                            s.Ticket = null;
                            SetState(t, TicketState.Ready);
                            GoHome(s);
                        }
                    }
                    break;

                case StaffState.WalkingToCustomer:
                    if (FollowPath(s, dt))
                    {
                        Face(s, t.Customer.Position.Cell);
                        Serve(s, t);
                    }
                    break;

                case StaffState.WalkingToClean:
                case StaffState.Cleaning:
                    UpdateCleaning(s, dt);
                    break;

                case StaffState.WalkingToFix:
                case StaffState.Fixing:
                    UpdateFixing(s, dt);
                    break;
            }
        }

        /// <summary>
        /// Matches idle chefs with queued tickets and free stoves, idle waiters with ready plates, and idle cleaners
        /// with dirty tables (waiters clear tables too when they have nothing to serve and no cleaner is working).
        /// </summary>
        private void Dispatch(float dt)
        {
            bool waitersClean = _dirtySeats.Count > 0 && !CleanerOnDuty();
            foreach (var s in _staff)
            {
                if (s.State != StaffState.Idle || s.IsBusy || s.IsResting)
                    continue;
                if (s.RestRequested)
                {
                    StartResting(s); // finished the last job; now off duty
                    continue;
                }

                if (s.Role == StaffRole.Chef)
                {
                    var ticket = OldestTicket(TicketState.Queued, drinks: false);
                    if (ticket != null && TryStartCooking(s, ticket))
                        continue;
                    GoToStation(s, dt);
                }
                else if (s.Role == StaffRole.Cleaner)
                {
                    if (!TryStartFixing(s)) TryStartCleaning(s);
                }
                else
                {
                    var ticket = OldestTicket(TicketState.Ready);
                    if (ticket != null && ticket.Stove != null && TryStartFurnitureAccessPath(s, ticket.Stove))
                    {
                        SetState(ticket, TicketState.Delivering);
                        ticket.Waiter = s;
                        s.Ticket = ticket;
                        s.State = StaffState.WalkingToPickup;
                    }
                    else if (OldestTicket(TicketState.Queued, drinks: true) is OrderTicket drink && TryStartDrink(s, drink))
                    {
                        // fetching a drink from a machine
                    }
                    else if (waitersClean)
                    {
                        TryStartCleaning(s);
                    }
                    if (s.State == StaffState.Idle && !s.IsBusy) GoToPost(s);
                }
            }
        }

        private bool TryStartCooking(Staff chef, OrderTicket ticket)
        {
            GridPos from = chef.Position.Cell;
            PlacedFurniture best = null;
            int bestDistance = int.MaxValue;
            // A chef cooks at their own stove when it is free; otherwise at the nearest free one nobody else stands at.
            var own = ValidStation(chef);
            if (own != null && !_busyStoves.Contains(own.InstanceId) && StandCell(own).HasValue)
                best = own;
            else foreach (var f in _layout.Furniture)
            {
                if (f.Def.Kind != FurnitureKind.Stove || _busyStoves.Contains(f.InstanceId) || ClaimedByOtherChef(f, chef))
                    continue;
                int d = from.ManhattanDistance(FrontCell(f));
                if (d < bestDistance && StandCell(f).HasValue)
                {
                    best = f;
                    bestDistance = d;
                }
            }
            if (best == null)
                return false;
            if (chef.Station == null) chef.Station = best;
            PlaceAtStove(chef, best); // chefs don't walk to stoves (as in the reference game)

            _busyStoves.Add(best.InstanceId);
            chef.Stove = best;
            chef.Ticket = ticket;
            chef.State = StaffState.WalkingToStove;
            ticket.Chef = chef;
            SetState(ticket, TicketState.Cooking);
            return true;
        }

        /// <summary>Frees the stove a collected (or cancelled) plate was waiting on.</summary>
        private void ReleasePlate(OrderTicket t)
        {
            if (t.Stove != null) _busyStoves.Remove(t.Stove.InstanceId);
            t.Stove = null;
            t.CounterSpot = null;
        }

        private void Serve(Staff waiter, OrderTicket t)
        {
            SetState(t, TicketState.Served);
            _tickets.Remove(t);
            waiter.Ticket = null;
            var c = t.Customer;
            if (c.Ticket == t)
                c.Ticket = null;
            if (c.State == CustomerState.WaitingForFood)
            {
                c.ServedPatienceLeft = c.PatienceLeft; // fast or slow service, for the rating
                c.State = CustomerState.Eating;
                c.Timer = t.Dish.EatSeconds;
            }
            DishServed?.Invoke(t);
            GoHome(waiter);
        }

        /// <summary>Stops whatever a staff member is doing. Optionally returns the ticket to the right queue.</summary>
        private void DropWork(Staff s, bool requeue)
        {
            if (s.Stove != null)
            {
                _busyStoves.Remove(s.Stove.InstanceId);
                _busyMachines.Remove(s.Stove.InstanceId);
                s.Stove = null;
            }
            ReleaseCleaning(s); // the dishes stay on the table for someone else
            ReleaseFixing(s);   // the toilet stays broken for someone else
            var t = s.Ticket;
            s.Ticket = null;
            s.CookProgress = 0f;
            if (t != null && requeue && t.State != TicketState.Cancelled)
            {
                bool plateOnCounter = s.State == StaffState.WalkingToPickup; // still waiting on its stove
                t.Chef = null;
                t.Waiter = null;
                SetState(t, plateOnCounter ? TicketState.Ready : TicketState.Queued);
            }
            GoHome(s);
        }

        private void GoHome(Staff s)
        {
            s.State = StaffState.Idle;
            // On duty, a chef stands in front of their own stove rather than at home.
            var station = s.Role == StaffRole.Chef && !s.IsResting ? ValidStation(s) : null;
            if (station != null && PlaceAtStove(s, station))
                return;
            // On duty, a waiter waits at their post beside a stove.
            var post = s.Role == StaffRole.Waiter && !s.IsResting ? WaiterPost(s) : null;
            if (post.HasValue && StartPath(s, post.Value))
                return;
            if (!StartPath(s, s.Home))
                s.Path.Clear();
        }

        /// <summary>
        /// An idle waiter goes to their post beside a stove reachable from the door. The first time (hired, or the game
        /// starts) they simply appear there, as in the reference game; afterwards they walk back to it.
        /// </summary>
        private void GoToPost(Staff waiter)
        {
            if (waiter.Path.Count > waiter.PathIndex) return; // walking somewhere already
            var post = WaiterPost(waiter);
            if (!post.HasValue || waiter.Position.Cell == post.Value) return;
            if (!waiter.Posted)
            {
                waiter.Posted = true;
                waiter.Path.Clear();
                waiter.PathIndex = 0;
                waiter.Position = Vec2.CellCentre(post.Value);
                return;
            }
            StartPath(waiter, post.Value);
        }

        /// <summary>The chef's station if it is still in the room, else null (and forgotten).</summary>
        private PlacedFurniture ValidStation(Staff chef)
        {
            if (chef.Station == null) return null;
            foreach (var f in _layout.Furniture) if (f == chef.Station) return f;
            chef.Station = null;
            return null;
        }

        /// <summary>
        /// An idle chef claims a stove no other chef has claimed (the nearest) and appears on its front cell: chefs are
        /// placed at stoves, they don't walk there, so a stove walled in by tables still gets a chef (as in the
        /// reference game). With no free stove the chef stays home. Looks again every few seconds.
        /// </summary>
        private void GoToStation(Staff chef, float dt)
        {
            var station = ValidStation(chef);
            if (station != null)
            {
                var stand = StandCell(station);
                if (stand.HasValue && chef.Position.Cell == stand.Value && chef.Path.Count == 0) return;
                if (PlaceAtStove(chef, station)) return;
                chef.Station = null; // its front got blocked: look for another
            }
            chef.StationRetry -= dt;
            if (chef.StationRetry > 0f) return;
            chef.StationRetry = 3f;
            PlacedFurniture best = null;
            int bestDistance = int.MaxValue;
            foreach (var f in _layout.Furniture)
            {
                if (f.Def.Kind != FurnitureKind.Stove || ClaimedByOtherChef(f, chef) || !StandCell(f).HasValue) continue;
                int d = chef.Position.Cell.ManhattanDistance(FrontCell(f));
                if (d < bestDistance) { best = f; bestDistance = d; }
            }
            chef.Station = best;
            if (best != null) PlaceAtStove(chef, best);
        }

        /// <summary>Where a chef stands to use a stove: the free cell it faces, else any free cell beside it.</summary>
        private GridPos? StandCell(PlacedFurniture stove)
        {
            var front = FrontCell(stove) + FacingUtil.ToDelta(stove.Facing);
            if (_layout.Grid.IsWalkable(front)) return front;
            for (int y = stove.Origin.Y - 1; y <= stove.Origin.Y + stove.SizeY; y++)
            for (int x = stove.Origin.X - 1; x <= stove.Origin.X + stove.SizeX; x++)
            {
                var cell = new GridPos(x, y);
                bool corner = (x < stove.Origin.X || x >= stove.Origin.X + stove.SizeX) && (y < stove.Origin.Y || y >= stove.Origin.Y + stove.SizeY);
                if (!corner && !stove.Covers(cell) && _layout.Grid.IsWalkable(cell)) return cell;
            }
            return null;
        }

        /// <summary>Puts a chef straight onto the stove's stand cell, facing it. False when nowhere to stand.</summary>
        private bool PlaceAtStove(Staff chef, PlacedFurniture stove)
        {
            var stand = StandCell(stove);
            if (!stand.HasValue) return false;
            chef.Path.Clear();
            chef.PathIndex = 0;
            chef.Position = Vec2.CellCentre(stand.Value);
            Face(chef, FrontCell(stove));
            return true;
        }

        private bool ClaimedByOtherChef(PlacedFurniture stove, Staff chef)
        {
            foreach (var s in _staff)
                if (s != chef && s.Station == stove) return true;
            return false;
        }



        /// <summary>The oldest ticket in a state; <paramref name="drinks"/> picks drinks only (true), food only (false) or both.</summary>
        private OrderTicket OldestTicket(TicketState state, bool? drinks = null)
        {
            foreach (var t in _tickets)
            {
                if (t.State != state || (state == TicketState.Ready && t.Waiter != null)) continue;
                if (drinks.HasValue && (t.Dish.Category == DishCategory.Drink) != drinks.Value) continue;
                return t;
            }
            return null;
        }

        /// <summary>A waiter walks to the nearest free drink machine to pour a drink order (no chef, as in the reference game).</summary>
        private bool TryStartDrink(Staff waiter, OrderTicket ticket)
        {
            GridPos from = waiter.Position.Cell;
            PlacedFurniture best = null;
            int bestDistance = int.MaxValue;
            foreach (var f in _layout.Furniture)
            {
                if (f.Def.Kind != FurnitureKind.DrinkMachine || _busyMachines.Contains(f.InstanceId)) continue;
                int d = from.ManhattanDistance(FrontCell(f));
                if (d < bestDistance && TryStartFurnitureAccessPath(waiter, f)) { best = f; bestDistance = d; }
            }
            if (best == null) return false;
            TryStartFurnitureAccessPath(waiter, best);
            _busyMachines.Add(best.InstanceId);
            waiter.Stove = best;
            waiter.Ticket = ticket;
            waiter.State = StaffState.WalkingToDrinks;
            ticket.Waiter = waiter;
            SetState(ticket, TicketState.Delivering);
            return true;
        }

        /// <summary>The footprint cell on the side a piece faces (where you'd stand to use it).</summary>
        private static GridPos FrontCell(PlacedFurniture f)
        {
            switch (f.Facing)
            {
                case Facing.SouthWest: return new GridPos(f.Origin.X, f.Origin.Y + f.SizeY - 1);
                case Facing.SouthEast: return new GridPos(f.Origin.X + f.SizeX - 1, f.Origin.Y);
                default: return f.Origin;
            }
        }

        /// <summary>Routes to a reachable free cell beside the target, never into the obstacle itself.</summary>
        private bool TryStartAccessPath(Agent agent, GridPos target, Facing? preferred = null)
        {
            if (preferred.HasValue)
            {
                var front = target + FacingUtil.ToDelta(preferred.Value);
                if (_layout.Grid.IsWalkable(front) && StartPath(agent, front))
                    return true;
            }
            if (_layout.Grid.IsWalkable(target) && StartPath(agent, target))
                return true;
            // Prefer the viewer side (+Y, then +X) so staff stand in front of things, not behind them.
            foreach (var d in AccessOrder)
            {
                var p = target + d;
                if (_layout.Grid.IsWalkable(p) && StartPath(agent, p))
                    return true;
            }
            return false;
        }

        private bool TryStartFurnitureAccessPath(Agent agent, PlacedFurniture furniture)
        {
            if (TryStartAccessPath(agent, FrontCell(furniture), furniture.Facing)) return true;
            // Large or rotated equipment may only be accessible from another edge of its footprint.
            for (int x = furniture.Origin.X; x < furniture.Origin.X + furniture.SizeX; x++)
            {
                if (StartPath(agent, new GridPos(x, furniture.Origin.Y - 1)) ||
                    StartPath(agent, new GridPos(x, furniture.Origin.Y + furniture.SizeY))) return true;
            }
            for (int y = furniture.Origin.Y; y < furniture.Origin.Y + furniture.SizeY; y++)
            {
                if (StartPath(agent, new GridPos(furniture.Origin.X - 1, y)) ||
                    StartPath(agent, new GridPos(furniture.Origin.X + furniture.SizeX, y))) return true;
            }
            return false;
        }

        private static readonly GridPos[] AccessOrder = { GridPos.Down, GridPos.Right, GridPos.Left, GridPos.Up };

        private static void Face(Agent a, GridPos target)
        {
            var here = a.Position.Cell;
            if (here != target)
                a.Facing = FacingUtil.FromDelta(target.X - here.X, target.Y - here.Y);
        }
    }
}
