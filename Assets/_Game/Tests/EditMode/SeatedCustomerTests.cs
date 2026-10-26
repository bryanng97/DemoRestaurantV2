using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public sealed class SeatedCustomerTests
    {
        [TestCase(Facing.SouthWest)]
        [TestCase(Facing.SouthEast)]
        [TestCase(Facing.NorthEast)]
        [TestCase(Facing.NorthWest)]
        public void CompleteVisit_SitsForOrderingWaitingAndEatingThenStandsToLeave(Facing facing)
        {
            var layout = new RestaurantLayout(8, 8, new GridPos(0, 6));
            layout.TryPlace(new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1, 0, 1),
                new GridPos(2, 0), Facing.SouthWest, out _);
            layout.TryPlace(new FurnitureDef("counter", "p", FurnitureKind.ServingCounter, 1, 1, 1, 0, 1),
                new GridPos(2, 3), Facing.SouthWest, out _);
            layout.TryPlace(new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 1, .35f, 1),
                new GridPos(4, 5), facing, out var seat);
            var dish = new DishDef("dish", "d", 10, 4, 5);
            var sim = new RestaurantSim(layout, new RestaurantMenu(new[] { dish }, 3), 7);
            sim.Hire(new StaffTypeDef("chef", "c", StaffRole.Chef, 3), new GridPos(1, 1));
            sim.Hire(new StaffTypeDef("waiter", "w", StaffRole.Waiter, 3), new GridPos(3, 3));
            var customer = sim.SpawnCustomer(new CustomerTypeDef("customer", "c", 2, 60, 1, 90));
            int coins = 0;
            sim.CustomerPaid += (_, paid) => coins += paid;
            Assert.That(customer.Seat, Is.SameAs(seat));
            Assert.That(customer.IsSeated, Is.False, "Reserving a chair while walking must not show sitting art.");
            var states = new HashSet<CustomerState>();
            for (int i = 0; i < 2500 && customer.State != CustomerState.Left; i++)
            {
                sim.Tick(.05f); states.Add(customer.State);
                bool sitting = customer.State == CustomerState.Ordering || customer.State == CustomerState.WaitingForFood || customer.State == CustomerState.Eating;
                Assert.That(customer.IsSeated, Is.EqualTo(sitting));
                if (sitting)
                {
                    Assert.That(customer.Z, Is.EqualTo(seat.Def.SeatHeight));
                    Assert.That(customer.Facing, Is.EqualTo(facing));
                    Assert.That(customer.Position, Is.EqualTo(Vec2.CellCentre(seat.Origin)));
                }
                else Assert.That(customer.Z, Is.Zero, "Leaving customers must return to floor height.");
            }
            Assert.That(states, Does.Contain(CustomerState.Ordering));
            Assert.That(states, Does.Contain(CustomerState.WaitingForFood));
            Assert.That(states, Does.Contain(CustomerState.Eating));
            Assert.That(states, Does.Contain(CustomerState.WalkingToExit));
            Assert.That(customer.State, Is.EqualTo(CustomerState.Left));
            Assert.That(customer.Seat, Is.Null); Assert.That(coins, Is.EqualTo(dish.Price));
        }
    }
}
