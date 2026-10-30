using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class BedTests
    {
        private static readonly FurnitureDef Stove = new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1f, 0f, 1);
        private static readonly FurnitureDef Bed = new FurnitureDef("bed", "b", FurnitureKind.Bed, 2, 1, 0.6f, 0.3f, 1, restRecoveryMultiplier: 1.5f);
        private static readonly FurnitureDef BigBed = new FurnitureDef("big_bed", "B", FurnitureKind.Bed, 2, 1, 0.6f, 0.3f, 1, restRecoveryMultiplier: 3f);
        private static readonly DishDef Dish = new DishDef("d", "d", 9, 1f, 1f);
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef", "c", StaffRole.Chef, 4f);
        private static readonly StaffTypeDef Waiter = new StaffTypeDef("waiter", "w", StaffRole.Waiter, 4f);

        private static (RestaurantSim sim, RestaurantLayout room) Restaurant(params (FurnitureDef def, GridPos at)[] beds)
        {
            var room = new RestaurantLayout(10, 8, new GridPos(0, 7));
            room.TryPlace(Stove, new GridPos(2, 0), Facing.SouthWest, out _);
            foreach (var (def, at) in beds) Assert.IsTrue(room.TryPlace(def, at, Facing.SouthWest, out _));
            var settings = new SimSettings();
            settings.StaffEnergy.RecoverPerMinute = 60f; // one energy a second
            settings.StaffEnergy.DrainPerMinute = 0f;
            var sim = new RestaurantSim(room, new RestaurantMenu(new[] { Dish }, 1), seed: 3, settings);
            return (sim, room);
        }

        private static Staff Resting(RestaurantSim sim, StaffTypeDef type, GridPos home, float energy = 10f)
        {
            var s = sim.Hire(type, home);
            sim.SetEnergy(s, energy, resting: true);
            return s;
        }

        [Test]
        public void ARestingEmployee_SleepsOnAFreeBed_AndRecoversFaster()
        {
            var (sim, room) = Restaurant((Bed, new GridPos(6, 2)));
            var chef = Resting(sim, Chef, new GridPos(1, 2));
            sim.Tick(1f);
            var bed = room.FindAt(new GridPos(6, 2));
            Assert.AreSame(bed, chef.Bed);
            Assert.AreEqual(7f, chef.Position.X, 0.001f, "lies in the middle of the 2 x 1 bed");
            Assert.AreEqual(Bed.SeatHeight, chef.Z, 0.001f);
            Assert.AreEqual(1.5f, RestaurantSim.RestMultiplier(chef));
            Assert.AreEqual(11.5f, chef.Energy, 0.01f, "1 energy a second × 1.5 on the bed");
        }

        [Test]
        public void WithoutABed_RestingRecoversAtTheNormalRate()
        {
            var (sim, _) = Restaurant();
            var chef = Resting(sim, Chef, new GridPos(1, 2));
            sim.Tick(1f);
            Assert.IsNull(chef.Bed);
            Assert.AreEqual(11f, chef.Energy, 0.01f);
        }

        [Test]
        public void OneEmployeePerBed_InHiringOrder_StrongestBedFirst()
        {
            var (sim, room) = Restaurant((Bed, new GridPos(6, 2)), (BigBed, new GridPos(6, 4)));
            var first = Resting(sim, Chef, new GridPos(1, 2));
            var second = Resting(sim, Waiter, new GridPos(1, 4));
            var third = Resting(sim, Waiter, new GridPos(1, 5));
            sim.Tick(0.1f);
            Assert.AreSame(room.FindAt(new GridPos(6, 4)), first.Bed, "the first hired gets the strongest bed");
            Assert.AreSame(room.FindAt(new GridPos(6, 2)), second.Bed);
            Assert.IsNull(third.Bed, "no bed left: rests without one");
        }

        [Test]
        public void BackToWork_GetsOutOfBed_AndTheBedIsFreeAgain()
        {
            var (sim, _) = Restaurant((Bed, new GridPos(6, 2)));
            var chef = Resting(sim, Chef, new GridPos(1, 2));
            var waiter = Resting(sim, Waiter, new GridPos(1, 4));
            sim.Tick(0.1f);
            Assert.IsNotNull(chef.Bed);
            Assert.IsNull(waiter.Bed);

            sim.ReturnToWork(chef);
            sim.Tick(0.1f);
            Assert.IsNull(chef.Bed);
            Assert.AreEqual(0f, chef.Z);
            Assert.IsNotNull(waiter.Bed, "the next resting employee takes the free bed");
        }

        [Test]
        public void FullEnergy_WakesUp_AndLeavesTheBed()
        {
            var (sim, _) = Restaurant((Bed, new GridPos(6, 2)));
            var chef = Resting(sim, Chef, new GridPos(1, 2), energy: 99f);
            for (int i = 0; i < 10; i++) sim.Tick(0.2f);
            Assert.IsFalse(chef.IsResting);
            Assert.IsNull(chef.Bed);
        }

        [Test]
        public void StoringTheBed_WakesNobody_ButTheyRestWithoutIt()
        {
            var (sim, room) = Restaurant((Bed, new GridPos(6, 2)));
            var chef = Resting(sim, Chef, new GridPos(1, 2));
            sim.Tick(0.1f);
            room.Remove(room.FindAt(new GridPos(6, 2)));
            sim.Tick(0.1f);
            Assert.IsNull(chef.Bed);
            Assert.IsTrue(chef.IsResting);
            Assert.AreEqual(0f, chef.Z);
        }

        [Test]
        public void ARecoveryMultiplierCountsAsAFurnitureEffect()
        {
            Assert.IsFalse(Bed.Effects.IsNeutral);
            Assert.IsTrue(new FurnitureDef("plain", "p", FurnitureKind.Bed, 2, 1, 0.6f, 0.3f, 1).Effects.IsNeutral);
        }
    }
}
