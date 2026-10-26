using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class RestaurantSimTests
    {
        private static readonly FurnitureDef Chair =
            new FurnitureDef("chair", "furniture.chair", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 10);
        private static readonly FurnitureDef Table =
            new FurnitureDef("table", "furniture.table", FurnitureKind.Table, 2, 1, 0.7f, 0f, 20);
        private static readonly DishDef NasiLemak =
            new DishDef("nasi_lemak", "dish.nasi_lemak", 12, cookSeconds: 2f, eatSeconds: 3f);
        private static readonly CustomerTypeDef Walker =
            new CustomerTypeDef("office_worker", "customer.office_worker", walkSpeed: 2f, patienceSeconds: 5f, spawnWeight: 1);

        private static RestaurantMenu MenuOf(params DishDef[] dishes) => new RestaurantMenu(dishes, dishes.Length);

        private static RestaurantLayout EmptyRoom() => new RestaurantLayout(8, 8, entrance: new GridPos(7, 7));

        private static readonly FurnitureDef DrinkMachine =
            new FurnitureDef("soda", "furniture.soda", FurnitureKind.DrinkMachine, 1, 1, 1.5f, 0f, 50);

        internal static readonly FurnitureDef Stove =
            new FurnitureDef("stove", "furniture.stove", FurnitureKind.Stove, 1, 1, 1f, 0f, 50);
        internal static readonly FurnitureDef Pass =
            new FurnitureDef("pass", "furniture.pass", FurnitureKind.ServingCounter, 2, 1, 1f, 0f, 50);
        internal static readonly StaffTypeDef ChefType = new StaffTypeDef("chef", "staff.chef", StaffRole.Chef, 3f);
        internal static readonly StaffTypeDef WaiterType = new StaffTypeDef("waiter", "staff.waiter", StaffRole.Waiter, 3f);

        /// <summary>One stove and a two-spot serving counter in the back-right corner of EmptyRoom.</summary>
        private static void AddKitchen(RestaurantLayout layout)
        {
            layout.TryPlace(Stove, new GridPos(6, 0), Facing.SouthWest, out _);
            layout.TryPlace(Pass, new GridPos(5, 2), Facing.SouthWest, out _);
        }

        private static void HireCrew(RestaurantSim sim)
        {
            sim.Hire(ChefType, new GridPos(5, 1));
            sim.Hire(WaiterType, new GridPos(4, 4));
        }

        private static void Run(RestaurantSim sim, float seconds, float step = 0.05f)
        {
            for (float t = 0; t < seconds; t += step)
                sim.Tick(step);
        }

        [Test]
        public void Guests_PickACourseFirst_ThenADish()
        {
            var mains = new[]
            {
                new DishDef("m1", "d", 5, 1f, 1f, category: DishCategory.Main),
                new DishDef("m2", "d", 5, 1f, 1f, category: DishCategory.Main),
                new DishDef("m3", "d", 5, 1f, 1f, category: DishCategory.Main),
            };
            var drink = new DishDef("drink", "d", 5, 1f, 1f, category: DishCategory.Drink);
            int drinks = 0, orders = 200;
            for (int i = 0; i < orders; i++)
            {
                var layout = EmptyRoom();
                layout.TryPlace(Table, new GridPos(2, 2), Facing.SouthWest, out _);
                layout.TryPlace(Chair, new GridPos(2, 3), Facing.NorthEast, out _);
                layout.TryPlace(DrinkMachine, new GridPos(5, 5), Facing.SouthWest, out _);
                var sim = new RestaurantSim(layout, MenuOf(mains[0], mains[1], mains[2], drink), seed: i);
                var c = sim.SpawnCustomer(Walker);
                for (int t = 0; t < 400 && c.Order == null; t++) sim.Tick(0.05f);
                Assert.IsNotNull(c.Order);
                if (c.Order == drink) drinks++;
            }
            // Dish-first would give the one drink a 1-in-4 share; course-first gives it half.
            Assert.That(drinks, Is.InRange(75, 125));
        }

        [Test]
        public void WithoutADrinkMachine_GuestsDontOrderDrinks()
        {
            var main = new DishDef("m", "d", 5, 1f, 1f, category: DishCategory.Main);
            var drink = new DishDef("drink", "d", 5, 1f, 1f, category: DishCategory.Drink);
            for (int i = 0; i < 30; i++)
            {
                var layout = EmptyRoom();
                layout.TryPlace(Table, new GridPos(2, 2), Facing.SouthWest, out _);
                layout.TryPlace(Chair, new GridPos(2, 3), Facing.NorthEast, out _);
                var sim = new RestaurantSim(layout, MenuOf(main, drink), seed: i);
                var c = sim.SpawnCustomer(Walker);
                for (int t = 0; t < 400 && c.Order == null; t++) sim.Tick(0.05f);
                Assert.AreSame(main, c.Order);
            }
        }

        [Test]
        public void Drinks_ArePouredByAWaiter_NotCookedByAChef()
        {
            var drink = new DishDef("teh", "d", 7, 2f, 1f, category: DishCategory.Drink);
            var layout = EmptyRoom();
            layout.TryPlace(Table, new GridPos(2, 2), Facing.SouthWest, out _);
            layout.TryPlace(Chair, new GridPos(2, 3), Facing.NorthEast, out _);
            layout.TryPlace(DrinkMachine, new GridPos(5, 5), Facing.SouthWest, out _);
            AddKitchen(layout);
            var sim = new RestaurantSim(layout, MenuOf(drink), seed: 1);
            var chef = sim.Hire(ChefType, new GridPos(5, 1));
            var waiter = sim.Hire(WaiterType, new GridPos(4, 4));
            int paid = 0;
            bool chefCooked = false, poured = false;
            sim.CustomerPaid += (_, coins) => paid += coins;
            sim.SpawnCustomer(Walker);
            for (int i = 0; i < 2000 && paid == 0; i++)
            {
                sim.Tick(0.05f);
                chefCooked |= chef.State == StaffState.Cooking;
                poured |= waiter.State == StaffState.Pouring;
            }
            Assert.AreEqual(7, paid);
            Assert.IsTrue(poured, "the waiter poured it at the machine");
            Assert.IsFalse(chefCooked, "chefs don't make drinks");
        }

        [Test]
        public void ChairWithoutTable_GuestComplains_AndLeaves()
        {
            var layout = EmptyRoom();
            layout.TryPlace(Chair, new GridPos(2, 3), Facing.NorthEast, out _);
            var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 1, new SimSettings { NoTableWaitSeconds = 2f });
            int noTable = 0, left = 0;
            sim.CustomerNoTable += _ => noTable++;
            sim.CustomerLeft += _ => left++;

            var c = sim.SpawnCustomer(Walker);
            bool sawNoTable = false;
            for (int i = 0; i < 1000 && left == 0; i++) { sim.Tick(0.05f); sawNoTable |= c.State == CustomerState.NoTable; }

            Assert.IsTrue(sawNoTable);
            Assert.AreEqual(1, noTable);
            Assert.AreEqual(1, left);
            Assert.IsNull(c.Order, "never ordered");
        }

        [Test]
        public void ChairNextToTable_GuestOrders()
        {
            var layout = EmptyRoom();
            layout.TryPlace(Table, new GridPos(2, 2), Facing.SouthWest, out _);
            layout.TryPlace(Chair, new GridPos(2, 3), Facing.NorthEast, out _);
            var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 1, new SimSettings { NoTableWaitSeconds = 2f });
            int noTable = 0;
            sim.CustomerNoTable += _ => noTable++;

            var c = sim.SpawnCustomer(Walker);
            for (int i = 0; i < 400 && c.Order == null; i++) sim.Tick(0.05f);

            Assert.AreSame(NasiLemak, c.Order);
            Assert.AreEqual(0, noTable);
        }
        [Test]
        public void Customer_CompletesFullLoop_AndPaysOnce()
        {
            var layout = EmptyRoom();
            layout.TryPlace(Table, new GridPos(2, 2), Facing.SouthWest, out _);
            layout.TryPlace(Chair, new GridPos(2, 3), Facing.NorthEast, out _);
            AddKitchen(layout);
            var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 1);
            HireCrew(sim);

            var states = new List<CustomerState>();
            int paid = 0, payments = 0, left = 0;
            sim.CustomerPaid += (_, amount) => { paid += amount; payments++; };
            sim.CustomerLeft += _ => left++;

            var c = sim.SpawnCustomer(Walker);
            for (int i = 0; i < 1000 && left == 0; i++)
            {
                sim.Tick(0.05f);
                if (states.Count == 0 || states[states.Count - 1] != c.State)
                    states.Add(c.State);
            }

            CollectionAssert.AreEqual(new[]
            {
                CustomerState.WalkingToSeat, CustomerState.Ordering, CustomerState.WaitingForFood,
                CustomerState.Eating, CustomerState.WalkingToExit, CustomerState.Left,
            }, states);
            Assert.AreEqual(1, payments);
            Assert.AreEqual(12, paid);
            Assert.AreEqual(0, sim.Customers.Count);
        }

        [Test]
        public void SeatedCustomer_SitsOnChair_FacingTheChairDirection()
        {
            var layout = EmptyRoom();
            layout.TryPlace(Chair, new GridPos(1, 1), Facing.NorthEast, out var chair);
            var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 1);
            var c = sim.SpawnCustomer(Walker);

            for (int i = 0; i < 400 && c.State == CustomerState.WalkingToSeat; i++)
                sim.Tick(0.05f);

            Assert.AreEqual(CustomerState.Ordering, c.State);
            Assert.AreEqual(chair.Origin, c.Position.Cell);
            Assert.AreEqual(Facing.NorthEast, c.Facing);
            Assert.AreEqual(Chair.SeatHeight, c.Z);
        }

        private static readonly GridPos[] WaitingArea = { new GridPos(7, 6), new GridPos(7, 5), new GridPos(7, 4) };

        [Test]
        public void TwoCustomers_NeverShareASeat_SecondQueuesInWaitingArea()
        {
            var layout = EmptyRoom();
            layout.TryPlace(Chair, new GridPos(1, 1), Facing.NorthEast, out _);
            layout.SetWaitingArea(WaitingArea);
            var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 3);

            var first = sim.SpawnCustomer(Walker);
            var second = sim.SpawnCustomer(Walker);

            Assert.IsNotNull(first.Seat);
            Assert.IsNull(second.Seat);
            Assert.AreEqual(CustomerState.WalkingToQueue, second.State);
            CollectionAssert.AreEqual(new[] { second }, sim.Queue);

            Run(sim, 2f);
            Assert.AreEqual(CustomerState.WaitingInQueue, second.State);
            Assert.AreEqual(WaitingArea[0], second.Position.Cell);
        }

        [Test]
        public void NoSeatAndNoWaitingArea_CustomerIsTurnedAway()
        {
            var layout = EmptyRoom();
            layout.TryPlace(Chair, new GridPos(1, 1), Facing.NorthEast, out _);
            var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 3);
            int turnedAway = 0;
            sim.CustomerTurnedAway += _ => turnedAway++;

            sim.SpawnCustomer(Walker);
            var second = sim.SpawnCustomer(Walker);
            sim.Tick(0.05f);

            Assert.AreEqual(1, turnedAway);
            Assert.AreEqual(CustomerState.Left, second.State);
            Assert.AreEqual(1, sim.Customers.Count);
        }

        [Test]
        public void FullWaitingArea_TurnsAwayExtraCustomers()
        {
            var layout = EmptyRoom();
            layout.SetWaitingArea(WaitingArea); // no chairs at all
            var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 3);
            int turnedAway = 0;
            sim.CustomerTurnedAway += _ => turnedAway++;

            for (int i = 0; i < 5; i++)
                sim.SpawnCustomer(Walker);

            Assert.AreEqual(3, sim.Queue.Count);
            Assert.AreEqual(2, turnedAway);
        }

        [Test]
        public void QueuingCustomer_GivesUpAfterPatienceRunsOut()
        {
            var layout = EmptyRoom();
            layout.SetWaitingArea(WaitingArea);
            var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 7);
            int gaveUp = 0, left = 0;
            sim.CustomerGaveUpWaiting += _ => gaveUp++;
            sim.CustomerLeft += _ => left++;

            sim.SpawnCustomer(Walker); // patience 5 s
            Run(sim, 4f);
            Assert.AreEqual(0, gaveUp);
            Run(sim, 10f);

            Assert.AreEqual(1, gaveUp);
            Assert.AreEqual(1, left);
            Assert.IsEmpty(sim.Queue);
        }

        [Test]
        public void QueuedCustomers_TakeFreedSeatsInArrivalOrder_AndStepForward()
        {
            var layout = EmptyRoom();
            layout.TryPlace(Chair, new GridPos(1, 1), Facing.NorthEast, out _);
            layout.SetWaitingArea(WaitingArea);
            var patient = new CustomerTypeDef("patient", "customer.patient", 2f, patienceSeconds: 120f, spawnWeight: 1);
            AddKitchen(layout);
            var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 11);
            HireCrew(sim);
            var seatedOrder = new List<Customer>();
            sim.CustomerOrdered += (c, _) => seatedOrder.Add(c);

            var a = sim.SpawnCustomer(patient);
            var b = sim.SpawnCustomer(patient);
            var c3 = sim.SpawnCustomer(patient);
            Assert.AreEqual(new[] { b, c3 }, sim.Queue);

            int payments = 0;
            sim.CustomerPaid += (_, __) => payments++;
            for (int i = 0; i < 2000 && payments < 1; i++) sim.Tick(0.05f);
            Run(sim, 0.5f);
            Assert.AreEqual(CustomerState.WalkingToSeat, b.State, "first in line goes in first");
            Assert.AreEqual(0, c3.QueuePosition, "next customer steps to the front");

            Run(sim, 120f);
            CollectionAssert.AreEqual(new[] { a, b, c3 }, seatedOrder);
            Assert.AreEqual(3, payments);
        }

        [Test]
        public void SlowFood_MakesCustomerAngry_LeavesWithoutPaying_AndCancelsTicket()
        {
            var layout = EmptyRoom();
            layout.TryPlace(Chair, new GridPos(1, 1), Facing.NorthEast, out _);
            var impatient = new CustomerTypeDef("impatient", "c", 2f, 30f, 1, foodPatienceSeconds: 5f);
            var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 1); // no kitchen: food never comes
            Customer angry = null;
            int paid = 0;
            sim.CustomerAngry += c => angry = c;
            sim.CustomerPaid += (_, __) => paid++;

            var customer = sim.SpawnCustomer(impatient);
            for (int i = 0; i < 400 && customer.State != CustomerState.WaitingForFood; i++) sim.Tick(0.05f);
            var ticket = customer.Ticket;
            Assert.Greater(customer.PatienceLeft, 0.9f);
            Run(sim, 3f);
            Assert.That(customer.PatienceLeft, Is.InRange(0.2f, 0.6f));
            Run(sim, 3f);

            Assert.AreSame(customer, angry);
            Assert.IsTrue(customer.IsAngry);
            Assert.AreEqual(TicketState.Cancelled, ticket.State);
            Assert.AreEqual(0, paid);
            Assert.AreEqual(CustomerState.WalkingToExit, customer.State);
        }
        [Test]
        public void Despawn_ReleasesSeat()
        {
            var layout = EmptyRoom();
            layout.TryPlace(Chair, new GridPos(1, 1), Facing.NorthEast, out var chair);
            var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 1);
            var c = sim.SpawnCustomer(Walker);
            Assert.IsTrue(sim.IsSeatReserved(chair));

            sim.Despawn(c);

            Assert.IsFalse(sim.IsSeatReserved(chair));
            Assert.AreEqual(0, sim.Customers.Count);
        }

        [Test]
        public void SameSeed_GivesIdenticalSimulation()
        {
            string RunOnce()
            {
                var layout = EmptyRoom();
                layout.TryPlace(Table, new GridPos(2, 2), Facing.SouthWest, out _);
                layout.TryPlace(Chair, new GridPos(2, 3), Facing.NorthEast, out _);
                layout.TryPlace(Chair, new GridPos(3, 3), Facing.NorthEast, out _);
                var sim = new RestaurantSim(layout, MenuOf(NasiLemak), seed: 42);
                for (int i = 0; i < 5; i++)
                    sim.SpawnCustomer(Walker);
                Run(sim, 7.3f);
                var sb = new System.Text.StringBuilder();
                foreach (var c in sim.Customers)
                    sb.Append(c.Id).Append(c.State).Append(c.Position).Append(';');
                return sb.ToString();
            }

            Assert.AreEqual(RunOnce(), RunOnce());
        }
    }

    public class RestaurantLayoutTests
    {
        private static readonly FurnitureDef Counter =
            new FurnitureDef("counter", "furniture.counter", FurnitureKind.Counter, 3, 1, 1f, 0f, 50);

        [Test]
        public void Rotation_SwapsFootprint()
        {
            var layout = new RestaurantLayout(6, 6, new GridPos(5, 5));
            Assert.IsTrue(layout.TryPlace(Counter, new GridPos(0, 0), Facing.SouthEast, out var placed));

            Assert.AreEqual(1, placed.SizeX);
            Assert.AreEqual(3, placed.SizeY);
            Assert.IsFalse(layout.Grid.IsWalkable(new GridPos(0, 2)));
            Assert.IsTrue(layout.Grid.IsWalkable(new GridPos(1, 0)));
        }

        [Test]
        public void CannotOverlapFurniture_OrBlockEntrance_OrLeaveGrid()
        {
            var layout = new RestaurantLayout(6, 6, new GridPos(5, 5));
            layout.TryPlace(Counter, new GridPos(0, 0), Facing.SouthWest, out _);

            Assert.IsFalse(layout.CanPlace(Counter, new GridPos(2, 0), Facing.SouthWest), "overlap");
            Assert.IsFalse(layout.CanPlace(Counter, new GridPos(3, 5), Facing.SouthWest), "covers entrance");
            Assert.IsFalse(layout.CanPlace(Counter, new GridPos(4, 2), Facing.SouthWest), "out of bounds");
            Assert.IsTrue(layout.CanPlace(Counter, new GridPos(3, 0), Facing.SouthWest));
        }

        [Test]
        public void Remove_FreesCells()
        {
            var layout = new RestaurantLayout(6, 6, new GridPos(5, 5));
            layout.TryPlace(Counter, new GridPos(0, 0), Facing.SouthWest, out var placed);

            Assert.IsTrue(layout.Remove(placed));
            Assert.IsTrue(layout.Grid.IsAreaFree(new GridPos(0, 0), 3, 1));
        }
    }
}
