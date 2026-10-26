using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class KitchenTests
    {
        private static readonly FurnitureDef Chair =
            new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);
        private static readonly FurnitureDef Stove =
            new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1f, 0f, 1);
        private static readonly FurnitureDef Pass =
            new FurnitureDef("pass", "p", FurnitureKind.ServingCounter, 1, 1, 1f, 0f, 1);
        private static readonly DishDef Laksa = new DishDef("laksa", "dish.laksa", 13, cookSeconds: 4f, eatSeconds: 2f);
        private static readonly CustomerTypeDef Patient = new CustomerTypeDef("p", "p", 4f, 999f, 1);
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef", "staff.chef", StaffRole.Chef, 4f);
        private static readonly StaffTypeDef FastChef = new StaffTypeDef("fast", "staff.fast", StaffRole.Chef, 4f, cookSpeed: 2f);
        private static readonly StaffTypeDef Waiter = new StaffTypeDef("waiter", "staff.waiter", StaffRole.Waiter, 4f);

        /// <summary>12x10 room: stoves along the back wall, passes in a row, chairs at the front.</summary>
        private static RestaurantLayout Room(int stoves, int passes, int chairs)
        {
            var layout = new RestaurantLayout(12, 10, new GridPos(11, 9));
            for (int i = 0; i < stoves; i++) layout.TryPlace(Stove, new GridPos(i * 2, 0), Facing.SouthWest, out _);
            for (int i = 0; i < passes; i++) layout.TryPlace(Pass, new GridPos(i * 2, 3), Facing.SouthWest, out _);
            for (int i = 0; i < chairs; i++) layout.TryPlace(Chair, new GridPos(i * 2, 7), Facing.NorthEast, out _);
            return layout;
        }

        private static RestaurantSim Sim(RestaurantLayout layout) =>
            new RestaurantSim(layout, new RestaurantMenu(new[] { Laksa }, 3), seed: 3);

        private static void Run(RestaurantSim sim, float seconds)
        {
            for (float t = 0; t < seconds; t += 0.05f) sim.Tick(0.05f);
        }

        private static void RunUntil(RestaurantSim sim, System.Func<bool> done, float maxSeconds = 120f)
        {
            for (float t = 0; t < maxSeconds && !done(); t += 0.05f) sim.Tick(0.05f);
        }

        [Test]
        public void Ticket_MovesThroughEveryKitchenStage_InOrder()
        {
            var sim = Sim(Room(1, 1, 1));
            sim.Hire(Chef, new GridPos(1, 1));
            sim.Hire(Waiter, new GridPos(6, 5));
            var c = sim.SpawnCustomer(Patient);

            var seen = new List<TicketState>();
            int paid = 0;
            sim.CustomerPaid += (_, coins) => paid += coins;
            sim.TicketStateChanged += t => seen.Add(t.State);
            RunUntil(sim, () => paid > 0);

            CollectionAssert.AreEqual(new[]
            {
                TicketState.Queued, TicketState.Cooking, TicketState.Plating, TicketState.Ready,
                TicketState.Delivering, TicketState.Served,
            }, seen);
            Assert.AreEqual(13, paid);
            Assert.IsEmpty(sim.Tickets);
        }

        [Test]
        public void Chef_CooksForTheDishPreparationTime_ScaledByCookSpeed()
        {
            foreach (var (type, expected) in new[] { (Chef, 4f), (FastChef, 2f) })
            {
                var sim = Sim(Room(1, 1, 1));
                var chef = sim.Hire(type, new GridPos(0, 1)); // already in front of the stove
                sim.SpawnCustomer(Patient);

                RunUntil(sim, () => chef.State == StaffState.Cooking);
                float cooked = 0f;
                while (chef.State == StaffState.Cooking && cooked < 20f)
                {
                    sim.Tick(0.05f);
                    cooked += 0.05f;
                }

                Assert.AreEqual(expected, cooked, 0.11f, $"{type.Id} cook time");
            }
        }

        [Test]
        public void WithoutChef_CustomerWaitsForever_AndNeverPays()
        {
            var sim = Sim(Room(1, 1, 1));
            sim.Hire(Waiter, new GridPos(6, 5));
            var c = sim.SpawnCustomer(Patient);
            int paid = 0;
            sim.CustomerPaid += (_, __) => paid++;

            Run(sim, 60f);

            Assert.AreEqual(CustomerState.WaitingForFood, c.State);
            Assert.AreEqual(TicketState.Queued, c.Ticket.State);
            Assert.AreEqual(0, paid);
        }

        [Test]
        public void WithoutWaiter_PlateWaitsOnTheStove()
        {
            var sim = Sim(Room(1, 1, 1));
            sim.Hire(Chef, new GridPos(1, 1));
            var c = sim.SpawnCustomer(Patient);

            Run(sim, 30f);

            Assert.AreEqual(TicketState.Ready, c.Ticket.State);
            Assert.IsTrue(c.Ticket.IsOnCounter);
            Assert.AreEqual(new GridPos(0, 0), c.Ticket.CounterSpot, "the stove's cell");
            Assert.AreEqual(CustomerState.WaitingForFood, c.State);
        }

        [Test]
        public void OneStove_CooksOneDishAtATime_EvenWithTwoChefs()
        {
            var sim = Sim(Room(1, 2, 2));
            sim.Hire(Chef, new GridPos(1, 1));
            sim.Hire(Chef, new GridPos(3, 1));
            sim.SpawnCustomer(Patient);
            sim.SpawnCustomer(Patient);

            int maxCooking = 0;
            for (int i = 0; i < 600; i++)
            {
                sim.Tick(0.05f);
                int cooking = 0;
                foreach (var s in sim.Staff) if (s.State == StaffState.Cooking) cooking++;
                if (cooking > maxCooking) maxCooking = cooking;
            }
            Assert.AreEqual(1, maxCooking);
        }

        [Test]
        public void TwoStoves_TwoChefs_CookInParallel()
        {
            var sim = Sim(Room(2, 2, 2));
            sim.Hire(Chef, new GridPos(1, 1));
            sim.Hire(Chef, new GridPos(3, 1));
            sim.SpawnCustomer(Patient);
            sim.SpawnCustomer(Patient);

            int maxCooking = 0;
            for (int i = 0; i < 600; i++)
            {
                sim.Tick(0.05f);
                int cooking = 0;
                foreach (var s in sim.Staff) if (s.State == StaffState.Cooking) cooking++;
                if (cooking > maxCooking) maxCooking = cooking;
            }
            Assert.AreEqual(2, maxCooking);
        }

        [Test]
        public void PlateBlocksItsStove_UntilAWaiterCollectsIt()
        {
            var sim = Sim(Room(1, 0, 2));
            sim.Hire(Chef, new GridPos(1, 1));
            var first = sim.SpawnCustomer(Patient);
            var second = sim.SpawnCustomer(Patient);

            Run(sim, 40f);
            var ready = first.Ticket?.State == TicketState.Ready ? first : second;
            var waiting = ready == first ? second : first;
            Assert.AreEqual(TicketState.Ready, ready.Ticket.State);
            Assert.AreEqual(TicketState.Queued, waiting.Ticket.State, "the only stove still holds the first plate");

            // A waiter collects the plate, the stove frees up, and both customers end up paying.
            sim.Hire(Waiter, new GridPos(6, 5));
            int paid = 0;
            sim.CustomerPaid += (_, __) => paid++;
            RunUntil(sim, () => paid == 2);
            Assert.AreEqual(2, paid);
        }

        [Test]
        public void IdleChefs_EachWaitInFrontOfTheirOwnStove()
        {
            var sim = Sim(Room(2, 0, 1));
            var a = sim.Hire(Chef, new GridPos(6, 5));
            var b = sim.Hire(Chef, new GridPos(7, 5));

            Run(sim, 15f);

            Assert.IsNotNull(a.Station);
            Assert.IsNotNull(b.Station);
            Assert.AreNotSame(a.Station, b.Station, "one chef per stove");
            Assert.AreEqual(new GridPos(a.Station.Origin.X, a.Station.Origin.Y + 1), a.Position.Cell, "on the stove's front cell");
            Assert.AreEqual(new GridPos(b.Station.Origin.X, b.Station.Origin.Y + 1), b.Position.Cell);
        }

        [Test]
        public void ChefAppearsAtAWalledInStove_ButNobodyIsServed()
        {
            var layout = Room(1, 0, 1);
            // Seal the stove at (0,0) and its front cell (0,1) off from the rest of the room.
            layout.Grid.Block(new GridPos(1, 0), 1, 3);
            layout.Grid.Block(new GridPos(0, 2), 1, 1);
            var sim = Sim(layout);
            var chef = sim.Hire(Chef, new GridPos(6, 5));
            sim.SpawnCustomer(Patient);

            Run(sim, 10f);

            Assert.AreEqual(new GridPos(0, 1), chef.Position.Cell, "placed on the stove's front cell, no walking");
            Assert.IsFalse(sim.KitchenReachable, "no waiter could fetch plates from it");
            Assert.IsEmpty(sim.Tickets, "so guests don't sit down to order (as in the reference game)");
        }

        [Test]
        public void ChefWithoutReachableStove_StaysHome()
        {
            var layout = Room(1, 0, 1);
            layout.Grid.Block(new GridPos(0, 1), 3, 1); // wall the stove's front off
            layout.Grid.Block(new GridPos(1, 0), 1, 1);
            var sim = Sim(layout);
            var chef = sim.Hire(Chef, new GridPos(6, 5));

            Run(sim, 10f);

            Assert.IsNull(chef.Station);
            Assert.AreEqual(new GridPos(6, 5), chef.Position.Cell);
        }

        [Test]
        public void CustomerLeaving_CancelsTicket_AndFreesChefAndStove()
        {
            var sim = Sim(Room(1, 1, 2));
            var chef = sim.Hire(Chef, new GridPos(1, 1));
            sim.Hire(Waiter, new GridPos(6, 5));
            var first = sim.SpawnCustomer(Patient);
            var second = sim.SpawnCustomer(Patient);

            RunUntil(sim, () => chef.State == StaffState.Cooking);
            var cooking = chef.Ticket;
            sim.Despawn(cooking.Customer);

            Assert.AreEqual(TicketState.Cancelled, cooking.State);
            Assert.AreNotEqual(StaffState.Cooking, chef.State);

            var remaining = cooking.Customer == first ? second : first;
            int paid = 0;
            sim.CustomerPaid += (_, __) => paid++;
            RunUntil(sim, () => paid == 1);
            Assert.AreEqual(1, paid, "the stove was freed for the other customer's dish");
            Assert.AreEqual(CustomerState.WalkingToExit, remaining.State);
        }

        [Test]
        public void DismissingChefMidCook_RequeuesTheTicket()
        {
            var sim = Sim(Room(1, 1, 1));
            var chef = sim.Hire(Chef, new GridPos(1, 1));
            var c = sim.SpawnCustomer(Patient);
            RunUntil(sim, () => chef.State == StaffState.Cooking);

            sim.Dismiss(chef);

            Assert.AreEqual(TicketState.Queued, c.Ticket.State);
            Assert.IsNull(c.Ticket.Chef);
            sim.Hire(Chef, new GridPos(1, 1));
            sim.Hire(Waiter, new GridPos(6, 5));
            int paid = 0;
            sim.CustomerPaid += (_, __) => paid++;
            RunUntil(sim, () => paid == 1);
            Assert.AreEqual(1, paid);
        }
    }
}
