using System;
using System.Collections.Generic;
using System.Reflection;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public sealed class ObstacleMovementTests
    {
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, .9f, .35f, 1);
        private static readonly FurnitureDef Stove = new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1, 0, 1);
        private static readonly FurnitureDef Counter = new FurnitureDef("counter", "p", FurnitureKind.ServingCounter, 2, 1, 1, 0, 1);
        private static readonly FurnitureDef Block = new FurnitureDef("block", "b", FurnitureKind.Decor, 1, 1, 1, 0, 1);
        private static readonly DishDef Dish = new DishDef("dish", "d", 10, .5f, 1);
        private static readonly CustomerTypeDef Patient = new CustomerTypeDef("customer", "c", 3, 999, 1, 999);
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef", "c", StaffRole.Chef, 3);
        private static readonly StaffTypeDef Waiter = new StaffTypeDef("waiter", "w", StaffRole.Waiter, 3);

        private static RestaurantSim Sim(RestaurantLayout room) => new RestaurantSim(room, new RestaurantMenu(new[] { Dish }, 3), 7);
        private static PlacedFurniture Place(RestaurantLayout room, FurnitureDef def, int x, int y, Facing facing = Facing.SouthWest)
        {
            Assert.That(room.TryPlace(def, new GridPos(x, y), facing, out var placed), Is.True);
            return placed;
        }
        private static void Until(RestaurantSim sim, Func<bool> done)
        {
            for (int i = 0; i < 3000 && !done(); i++) sim.Tick(.02f);
            Assert.That(done(), Is.True, "Activity did not complete within a minute.");
        }
        private static void Set(object actor, string property, object value) =>
            actor.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public).SetValue(actor, value);

        [Test]
        public void Chef_StandsInFrontOfTheStove_EvenAcrossASealedWall()
        {
            var room = new RestaurantLayout(9, 9, new GridPos(0, 0));
            var stove = Place(room, Stove, 4, 4);
            room.Grid.Block(new GridPos(0, 4), 4, 1);
            room.Grid.Block(new GridPos(5, 4), 4, 1);
            Place(room, Chair, 7, 1);
            var sim = Sim(room);
            var chef = sim.Hire(Chef, new GridPos(1, 2));
            sim.SpawnCustomer(Patient);
            Until(sim, () => chef.State == StaffState.Cooking);
            Assert.That(chef.Stove, Is.SameAs(stove));
            // Chefs are placed at their stove (as in the reference game), so the front cell counts even though no
            // path from the chef's home reaches it.
            Assert.That(chef.Position.Cell, Is.EqualTo(new GridPos(4, 5)));
            Assert.That(room.Grid.IsWalkable(chef.Position.Cell), Is.True);
        }

        [Test]
        public void Chef_SkipsInaccessibleNearestStove_AndUsesReachableEquipment()
        {
            var room = new RestaurantLayout(10, 9, new GridPos(0, 0));
            Place(room, Stove, 2, 2);
            foreach (var delta in GridPos.Neighbours4) Place(room, Block, 2 + delta.X, 2 + delta.Y);
            var accessible = Place(room, Stove, 7, 2);
            Place(room, Chair, 1, 6);
            var sim = Sim(room);
            var chef = sim.Hire(Chef, new GridPos(0, 2));
            sim.SpawnCustomer(Patient);
            Until(sim, () => chef.State == StaffState.Cooking);
            Assert.That(chef.Stove, Is.SameAs(accessible));
            Assert.That(room.Grid.IsWalkable(chef.Position.Cell), Is.True);
        }

        [Test]
        public void LargeRotatedStove_CanBeUsedFromItsOtherFootprintEdge()
        {
            var room = new RestaurantLayout(10, 9, new GridPos(0, 0));
            var stove = Place(room, new FurnitureDef("big", "b", FurnitureKind.Stove, 2, 3, 1, 0, 1), 3, 3, Facing.SouthEast);
            // Isolate the preferred front cell, leaving the other edge accessible.
            Place(room, Block, 6, 3); Place(room, Block, 5, 2);
            Place(room, Chair, 1, 6);
            var sim = Sim(room);
            var chef = sim.Hire(Chef, new GridPos(0, 2));
            sim.SpawnCustomer(Patient);
            Until(sim, () => chef.State == StaffState.Cooking);
            Assert.That(chef.Stove, Is.SameAs(stove));
            Assert.That(stove.Covers(chef.Position.Cell), Is.False);
        }

        [Test]
        public void UnreachableCustomer_IsNotServedRemotelyThroughAWall()
        {
            var room = new RestaurantLayout(10, 9, new GridPos(0, 8));
            Place(room, Stove, 2, 0);
            Place(room, Counter, 2, 3);
            Place(room, Chair, 7, 6);
            var sim = Sim(room);
            sim.Hire(Chef, new GridPos(2, 1));
            var waiter = sim.Hire(Waiter, new GridPos(2, 4));
            var customer = sim.SpawnCustomer(Patient);
            Until(sim, () => waiter.State == StaffState.WalkingToPickup);
            // Insert a sealed barrier after pickup was assigned, to exercise the failure route.
            room.Grid.Block(new GridPos(0, 5), 10, 1);
            for (int i = 0; i < 250; i++) sim.Tick(.02f);
            Assert.That(customer.State, Is.EqualTo(CustomerState.WaitingForFood));
            Assert.That(customer.Ticket.IsOnCounter, Is.True);
            Assert.That(waiter.Position.Y, Is.LessThan(5));
        }

        [TestCase(Facing.SouthWest)]
        [TestCase(Facing.SouthEast)]
        public void CompleteService_RoutesAllCharactersAroundTablesShelvesAndChairs(Facing rotation)
        {
            var room = new RestaurantLayout(15, 12, new GridPos(0, 10));
            Place(room, Stove, 4, 0); Place(room, Counter, 4, 3);
            Place(room, new FurnitureDef("shelf", "s", FurnitureKind.Decor, 2, 1, 2, 0, 1), 1, 2, rotation);
            foreach (int x in new[] { 3, 8 })
            {
                Place(room, new FurnitureDef("table", "t", FurnitureKind.Table, 2, 2, 1, 0, 1), x, 6);
                Place(room, Chair, x, 5); Place(room, Chair, x + 1, 5);
                Place(room, Chair, x, 8, Facing.NorthEast); Place(room, Chair, x + 1, 8, Facing.NorthEast);
            }
            Place(room, Block, 6, 5); Place(room, Block, 6, 8);
            room.SetWaitingArea(new[] { new GridPos(1, 10), new GridPos(1, 9), new GridPos(1, 8) });
            var sim = Sim(room);
            sim.Hire(Chef, new GridPos(4, 1)); sim.Hire(Waiter, new GridPos(6, 4));
            var seats = new Dictionary<Customer, PlacedFurniture>();
            int paid = 0, other = 0; sim.CustomerPaid += (_, __) => paid++;
            sim.CustomerTurnedAway += _ => other++; sim.CustomerGaveUpWaiting += _ => other++; sim.CustomerAngry += _ => other++;
            // Only two tables can serve guests now, regardless of their eight chairs. Give those
            // dining places time to turn over; this test checks routing, not oversubscribed arrivals.
            for (int i = 0; i < 20000; i++)
            {
                if (i % 500 == 0 && i < 12500) sim.SpawnCustomer(Patient);
                foreach (var c in sim.Customers) if (c.Seat != null) seats[c] = c.Seat;
                sim.Tick(.02f);
                foreach (var staff in sim.Staff) AssertClear(room, staff, null);
                foreach (var c in sim.Customers)
                {
                    seats.TryGetValue(c, out var ownSeat);
                    AssertClear(room, c, ownSeat);
                }
            }
            Assert.That(sim.Customers, Is.Empty, "Nobody gets stuck on furniture.");
            Assert.That(paid + other, Is.EqualTo(25), "Every guest either eats and pays or leaves for a normal reason.");
            Assert.That(paid, Is.GreaterThanOrEqualTo(22), "Customers must still enter their chairs, eat and leave.");
        }

        private static void AssertClear(RestaurantLayout room, Agent agent, PlacedFurniture ownSeat)
        {
            // A small foot radius must clear furniture, including the inside of a turn. Only the
            // customer's own chair is exempt during entry, sitting and departure.
            const float radius = .2f;
            foreach (var f in room.Furniture)
            {
                if (f == ownSeat) continue;
                float nearX = Math.Max(f.Origin.X, Math.Min(agent.Position.X, f.Origin.X + f.SizeX));
                float nearY = Math.Max(f.Origin.Y, Math.Min(agent.Position.Y, f.Origin.Y + f.SizeY));
                float dx = agent.Position.X - nearX, dy = agent.Position.Y - nearY;
                Assert.That(dx * dx + dy * dy, Is.GreaterThanOrEqualTo(radius * radius - .00001f),
                    $"{agent.GetType().Name} at {agent.Position} overlaps {f.Def.Id} at {f.Origin}");
            }
        }
    }
}
