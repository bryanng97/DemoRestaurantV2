using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class ToiletTests
    {
        private static readonly FurnitureDef Table = new FurnitureDef("table", "t", FurnitureKind.Table, 1, 1, 0.7f, 0f, 1);
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);
        private static readonly FurnitureDef Stove = new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1f, 0f, 1);
        private static readonly FurnitureDef Toilet = new FurnitureDef("toilet", "w", FurnitureKind.Toilet, 1, 1, 0.9f, 0.35f, 1, breakCount: 2);
        private static readonly FurnitureDef Sink = new FurnitureDef("sink", "k", FurnitureKind.Sink, 1, 1, 1f, 0f, 1);
        private static readonly DishDef Dish = new DishDef("d", "d", 9, 1f, 1f);
        private static readonly CustomerTypeDef Guest = new CustomerTypeDef("g", "g", 4f, 999f, 1, foodPatienceSeconds: 999f);
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef", "c", StaffRole.Chef, 4f);
        private static readonly StaffTypeDef Waiter = new StaffTypeDef("waiter", "w", StaffRole.Waiter, 4f);
        private static readonly StaffTypeDef Cleaner = new StaffTypeDef("cleaner", "x", StaffRole.Cleaner, 4f);

        private static (RestaurantSim sim, RestaurantLayout room) Restaurant(bool toilet = true, bool sink = true)
        {
            var room = new RestaurantLayout(10, 8, new GridPos(0, 7));
            room.TryPlace(Stove, new GridPos(2, 0), Facing.SouthWest, out _);
            room.TryPlace(Table, new GridPos(5, 4), Facing.SouthWest, out _);
            room.TryPlace(Chair, new GridPos(5, 3), Facing.SouthWest, out _);
            if (toilet) room.TryPlace(Toilet, new GridPos(9, 0), Facing.SouthWest, out _);
            if (sink) room.TryPlace(Sink, new GridPos(7, 0), Facing.SouthWest, out _);
            var sim = new RestaurantSim(room, new RestaurantMenu(new[] { Dish }, 1), seed: 2,
                new SimSettings { ToiletChance = 1f, ToiletSeconds = 2f, SinkSeconds = 1f, FixSeconds = 1f });
            sim.Hire(Chef, new GridPos(1, 2));
            sim.Hire(Waiter, new GridPos(4, 6));
            return (sim, room);
        }

        private static void Serve(RestaurantSim sim, System.Func<bool> until, float max = 120f)
        {
            for (float t = 0; t < max && !until(); t += 0.05f) sim.Tick(0.05f);
        }

        [Test]
        public void BathroomProgress_AdvancesPerActivity_AndClearsAfterLeaving()
        {
            var (sim,room)=Restaurant();var guest=sim.SpawnCustomer(Guest);
            Assert.AreEqual(-1,sim.BathroomProgress(guest));Assert.IsNull(guest.BathroomFixture);
            Serve(sim,()=>guest.State==CustomerState.OnToilet);
            Assert.AreSame(room.FindAt(new GridPos(9,0)),guest.BathroomFixture);
            sim.Tick(.5f);Assert.AreEqual(.25f,sim.BathroomProgress(guest),.001f);
            Serve(sim,()=>guest.State==CustomerState.UsingSink);
            Assert.AreSame(room.FindAt(new GridPos(7,0)),guest.BathroomFixture);
            sim.Tick(.5f);Assert.AreEqual(.5f,sim.BathroomProgress(guest),.001f);
            Serve(sim,()=>guest.State==CustomerState.Left);
            Assert.AreEqual(-1,sim.BathroomProgress(guest));Assert.IsNull(guest.BathroomFixture);
        }

        [Test]
        public void WashingHands_FacesTheSinkInsteadOfTheStandingCell()
        {
            foreach (Facing facing in System.Enum.GetValues(typeof(Facing)))
            {
                var (sim, room) = Restaurant();
                var sink = room.FindAt(new GridPos(7, 0));
                Assert.IsTrue(room.TryMove(sink, new GridPos(7, 2), facing));
                var guest = sim.SpawnCustomer(Guest);
                Serve(sim, () => guest.State == CustomerState.UsingSink);
                Assert.AreEqual(CustomerState.UsingSink, guest.State);
                Assert.AreEqual(FacingUtil.FromDelta(sink.Origin.X+.5f-guest.Position.X, sink.Origin.Y+.5f-guest.Position.Y), guest.Facing, facing.ToString());
                Assert.IsFalse(guest.IsSeated);
            }
        }

        [Test]
        public void AfterTheMeal_GuestUsesTheToiletThenTheSink_AndLeaves()
        {
            var (sim, room) = Restaurant();
            var c = sim.SpawnCustomer(Guest);
            bool onToilet = false, atSink = false, paid = false;
            sim.CustomerPaid += (_, __) => paid = true;
            Serve(sim, () =>
            {
                onToilet |= c.State == CustomerState.OnToilet;
                atSink |= c.State == CustomerState.UsingSink;
                return c.State == CustomerState.Left || c.State == CustomerState.LeavingOnStreet;
            });
            Assert.IsTrue(paid);
            Assert.IsTrue(onToilet, "sat on the toilet");
            Assert.IsTrue(atSink, "then washed hands");
            Assert.AreEqual(1, sim.UsesOf(room.FindAt(new GridPos(9, 0))));
        }

        [Test]
        public void WithoutAToilet_TheGuestLeavesUnhappy()
        {
            var (sim, _) = Restaurant(toilet: false);
            int noToilet = 0;
            sim.CustomerNoToilet += _ => noToilet++;
            var c = sim.SpawnCustomer(Guest);
            Serve(sim, () => noToilet > 0);
            Assert.AreEqual(1, noToilet);
        }

        [Test]
        public void ToiletBreaksAfterItsUses_AndATapFixesIt()
        {
            var (sim, room) = Restaurant(sink: false);
            var toilet = room.FindAt(new GridPos(9, 0));
            int broke = 0, used = 0;
            sim.ToiletBroke += _ => broke++;
            sim.ToiletUsed += t => { Assert.AreSame(toilet, t); used++; };
            sim.SetUses(toilet, 1);
            var c = sim.SpawnCustomer(Guest);
            Serve(sim, () => broke > 0);
            Assert.IsTrue(sim.IsBroken(toilet), "two uses for a breakCount of 2");
            Assert.AreEqual(1, used, "each finished use is reported once, for the server's wear count");

            int noToilet = 0;
            sim.CustomerNoToilet += _ => noToilet++;
            sim.SpawnCustomer(Guest);
            Serve(sim, () => noToilet > 0);
            Assert.AreEqual(1, noToilet, "a broken toilet can't be used");

            Assert.IsTrue(sim.Fix(toilet));
            Assert.IsFalse(sim.IsBroken(toilet));
            Assert.IsFalse(sim.Fix(toilet), "nothing to fix");
        }

        [Test]
        public void ACleanerFixesABrokenToilet()
        {
            var (sim, room) = Restaurant();
            var toilet = room.FindAt(new GridPos(9, 0));
            sim.SetUses(toilet, 2);
            var cleaner = sim.Hire(Cleaner, new GridPos(8, 6));
            bool fixing = false;
            Serve(sim, () => { fixing |= cleaner.State == StaffState.Fixing; return !sim.IsBroken(toilet); }, 30f);
            Assert.IsTrue(fixing);
            Assert.IsFalse(sim.IsBroken(toilet));
        }

        [Test]
        public void NobodyGoesBeforeTheRulesLevel()
        {
            var (sim, _) = Restaurant(toilet: false);
            sim.ToiletChance = 0f;
            int noToilet = 0, paid = 0;
            sim.CustomerNoToilet += _ => noToilet++;
            sim.CustomerPaid += (_, __) => paid++;
            sim.SpawnCustomer(Guest);
            Serve(sim, () => paid > 0);
            for (int i = 0; i < 100; i++) sim.Tick(0.05f);
            Assert.AreEqual(0, noToilet);
        }
    }
}
