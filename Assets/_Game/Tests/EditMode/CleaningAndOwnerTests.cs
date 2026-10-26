using System;
using System.Linq;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class CleaningTests
    {
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);
        private static readonly FurnitureDef Stove = new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1f, 0f, 1);
        private static readonly FurnitureDef Pass = new FurnitureDef("pass", "p", FurnitureKind.ServingCounter, 1, 1, 1f, 0f, 1);
        private static readonly DishDef Laksa = new DishDef("laksa", "dish.laksa", 13, cookSeconds: 2f, eatSeconds: 2f);
        private static readonly CustomerTypeDef Patient = new CustomerTypeDef("p", "p", 4f, 999f, 1, foodPatienceSeconds: 999f);
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef", "staff.chef", StaffRole.Chef, 4f);
        private static readonly StaffTypeDef Waiter = new StaffTypeDef("waiter", "staff.waiter", StaffRole.Waiter, 4f);
        private static readonly StaffTypeDef Cleaner = new StaffTypeDef("cleaner", "staff.cleaner", StaffRole.Cleaner, 4f);
        private static readonly StaffTypeDef FastCleaner = new StaffTypeDef("cleaner2", "staff.cleaner", StaffRole.Cleaner, 4f, cookSpeed: 4f);

        private static RestaurantSim Sim(int chairs = 1, float cleanSeconds = 4f)
        {
            var layout = new RestaurantLayout(12, 10, new GridPos(11, 9));
            layout.TryPlace(Stove, new GridPos(0, 0), Facing.SouthWest, out _);
            layout.TryPlace(Pass, new GridPos(0, 3), Facing.SouthWest, out _);
            for (int i = 0; i < chairs; i++) layout.TryPlace(Chair, new GridPos(i * 2, 7), Facing.NorthEast, out _);
            layout.SetWaitingArea(new[] { new GridPos(10, 9), new GridPos(10, 8) });
            var sim = new RestaurantSim(layout, new RestaurantMenu(new[] { Laksa }, 3), seed: 3,
                new SimSettings { CleanSeconds = cleanSeconds });
            sim.Hire(Chef, new GridPos(1, 1));
            sim.Hire(Waiter, new GridPos(6, 5));
            return sim;
        }

        private static void RunUntil(RestaurantSim sim, Func<bool> done, float maxSeconds = 120f)
        {
            for (float t = 0; t < maxSeconds && !done(); t += 0.05f) sim.Tick(0.05f);
        }

        [Test]
        public void PlayerCleaning_TakesFiveSeconds_ReleasesWorker_AndNeverPaysTwice()
        {
            var sim = Sim(cleanSeconds: 30f);
            var cleaner = sim.Hire(Cleaner, new GridPos(8, 5));
            int paid = 0, cleaned = 0;
            sim.CustomerPaid += (_, __) => paid++;
            sim.SeatCleaned += _ => cleaned++;
            sim.SpawnCustomer(Patient);
            RunUntil(sim, () => cleaner.State == StaffState.Cleaning);
            var seat = sim.DirtySeats.Single();
            Assert.IsTrue(sim.TogglePlayerCleaning(seat));
            Assert.IsNull(cleaner.CleaningSeat);
            Assert.AreNotEqual(StaffState.Cleaning, cleaner.State);
            Assert.IsFalse(sim.TogglePlayerCleaning(seat), "An active task cannot be queued twice.");
            sim.Tick(4.9f);
            Assert.IsTrue(sim.IsSeatDirty(seat));
            Assert.AreEqual(.98f, sim.PlayerCleaningProgress(seat), .001f);
            sim.Tick(.11f);
            Assert.IsFalse(sim.IsSeatDirty(seat));
            Assert.AreEqual(0, sim.PlayerCleaningCount);
            Assert.AreEqual(1, cleaned);
            Assert.AreEqual(1, paid, "Cleaning adds no second sale or reward.");
            Assert.IsFalse(sim.TogglePlayerCleaning(seat));
            sim.Tick(30);
            Assert.AreEqual(1, cleaned, "The cleaner's old task must not complete a second time.");
        }

        [Test]
        public void PlayerCleaning_QueuesSerially_CancelsWaitingTask_AndStaffSkipClaimedPlates()
        {
            var sim = Sim(chairs: 2, cleanSeconds: 10000);
            sim.SpawnCustomer(Patient); sim.SpawnCustomer(Patient);
            RunUntil(sim, () => sim.DirtySeatCount == 2);
            var seats = sim.DirtySeats.ToArray();
            Assert.AreEqual(2, seats.Length);
            Assert.IsTrue(sim.TogglePlayerCleaning(seats[0]));
            Assert.IsTrue(sim.TogglePlayerCleaning(seats[1]));
            sim.Tick(3);
            Assert.AreEqual(.6f, sim.PlayerCleaningProgress(seats[0]), .001f);
            Assert.AreEqual(0f, sim.PlayerCleaningProgress(seats[1]));
            Assert.IsTrue(sim.Staff.All(s => s.CleaningSeat == null));
            Assert.IsTrue(sim.TogglePlayerCleaning(seats[1]), "A queued task can be cancelled.");
            Assert.AreEqual(-1, sim.PlayerCleaningProgress(seats[1]));
            Assert.IsTrue(sim.TogglePlayerCleaning(seats[1]));
            sim.Tick(2.1f);
            Assert.IsFalse(sim.IsSeatDirty(seats[0]));
            Assert.IsTrue(sim.IsSeatDirty(seats[1]));
            Assert.AreEqual(.02f, sim.PlayerCleaningProgress(seats[1]), .001f);
            sim.Tick(5);
            Assert.IsFalse(sim.IsSeatDirty(seats[1]));
        }

        [Test]
        public void PlayerCleaning_RejectsNullCleanAndForeignSeats()
        {
            var sim = Sim();
            var seat = sim.Layout.Furniture.First(f => f.Def.Kind == FurnitureKind.Chair);
            Assert.IsFalse(sim.TogglePlayerCleaning(null));
            Assert.IsFalse(sim.TogglePlayerCleaning(seat));
            sim.SpawnCustomer(Patient);
            RunUntil(sim, () => sim.DirtySeatCount > 0);
            var other = Sim().Layout.Furniture.First(f => f.Def.Kind == FurnitureKind.Chair);
            Assert.AreEqual(seat.InstanceId, other.InstanceId);
            Assert.IsFalse(sim.TogglePlayerCleaning(other), "Matching ids from another room are not the same dish.");
        }

        [Test]
        public void PaidCustomer_LeavesDishes_AndTheSeatWaitsUntilCleared()
        {
            var sim = Sim();
            var cleaner = sim.Hire(Cleaner, new GridPos(8, 5));
            sim.SendToRest(cleaner); // off duty, so the table stays dirty for a moment
            int paid = 0;
            sim.CustomerPaid += (_, __) => paid++;
            sim.SpawnCustomer(Patient);
            RunUntil(sim, () => paid == 1);

            var seat = sim.Layout.Furniture.First(f => f.Def.Kind == FurnitureKind.Chair);
            Assert.IsTrue(sim.IsSeatDirty(seat));
            var next = sim.SpawnCustomer(Patient);
            Assert.IsTrue(next.IsQueuing, "the only seat is dirty, so the next guest waits");

            int cleaned = 0;
            sim.SeatCleaned += _ => cleaned++;
            sim.ReturnToWork(cleaner);
            RunUntil(sim, () => next.State == CustomerState.Ordering);

            Assert.AreEqual(1, cleaned);
            Assert.AreEqual(seat, next.Seat);
        }

        [Test]
        public void WithoutCleaner_IdleWaitersClearTables()
        {
            var sim = Sim();
            int paid = 0, cleaned = 0;
            sim.CustomerPaid += (_, __) => paid++;
            sim.SeatCleaned += _ => cleaned++;
            sim.SpawnCustomer(Patient);
            RunUntil(sim, () => paid == 1);
            RunUntil(sim, () => cleaned == 1, 30f);

            Assert.AreEqual(1, cleaned);
            Assert.AreEqual(0, sim.DirtySeatCount);
        }

        [Test]
        public void WithCleanerOnDuty_WaitersLeaveTheTablesToThem()
        {
            var sim = Sim();
            var cleaner = sim.Hire(Cleaner, new GridPos(8, 5));
            var waiter = sim.Staff.First(s => s.Role == StaffRole.Waiter);
            bool waiterCleaned = false;
            int paid = 0;
            sim.CustomerPaid += (_, __) => paid++;
            sim.SpawnCustomer(Patient);
            RunUntil(sim, () =>
            {
                waiterCleaned |= waiter.CleaningSeat != null;
                return paid == 1 && sim.DirtySeatCount == 0;
            });

            Assert.AreEqual(0, sim.DirtySeatCount);
            Assert.IsFalse(waiterCleaned);
        }

        [Test]
        public void CleaningTime_IsCleanSecondsOverWorkSpeed()
        {
            foreach (var (type, expected) in new[] { (Cleaner, 4f), (FastCleaner, 1f) })
            {
                var sim = Sim();
                var cleaner = sim.Hire(type, new GridPos(8, 5));
                int paid = 0;
                sim.CustomerPaid += (_, __) => paid++;
                sim.SpawnCustomer(Patient);
                RunUntil(sim, () => paid == 1);
                RunUntil(sim, () => cleaner.State == StaffState.Cleaning, 30f);
                float t = 0f;
                for (; t < 30f && cleaner.State == StaffState.Cleaning; t += 0.05f) sim.Tick(0.05f);
                Assert.AreEqual(expected, t, 0.11f, type.Id);
            }
        }

        [Test]
        public void CleanSecondsZero_MeansNoDirtyDishes()
        {
            var sim = Sim(cleanSeconds: 0f);
            int paid = 0;
            sim.CustomerPaid += (_, __) => paid++;
            sim.SpawnCustomer(Patient);
            RunUntil(sim, () => paid == 1);
            Assert.AreEqual(0, sim.DirtySeatCount);
        }

        [Test]
        public void RestingMidClean_FinishesFirst_AndDismissLeavesTheDishesForOthers()
        {
            var sim = Sim();
            var cleaner = sim.Hire(Cleaner, new GridPos(8, 5));
            int paid = 0;
            sim.CustomerPaid += (_, __) => paid++;
            sim.SpawnCustomer(Patient);
            RunUntil(sim, () => paid == 1);
            RunUntil(sim, () => cleaner.State == StaffState.Cleaning, 30f);

            sim.SendToRest(cleaner);
            Assert.IsTrue(cleaner.RestRequested, "busy: rests after this table");
            sim.Dismiss(cleaner);
            Assert.AreEqual(1, sim.DirtySeatCount, "the dishes stay");
            RunUntil(sim, () => sim.DirtySeatCount == 0, 30f);
            Assert.AreEqual(0, sim.DirtySeatCount, "the waiter takes over");
        }
    }

    public class OwnerTests
    {
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef_junior", "staff.chef", StaffRole.Chef, 4f);
        private static readonly StaffTypeDef Waiter = new StaffTypeDef("waiter_junior", "staff.waiter", StaffRole.Waiter, 4f);
        private static readonly StaffTypeDef OwnerChef = new StaffTypeDef("owner_chef", "staff.owner", StaffRole.Chef, 3f, 1.5f, isOwner: true);
        private static readonly StaffTypeDef OwnerWaiter = new StaffTypeDef("owner_waiter", "staff.owner", StaffRole.Waiter, 3f, isOwner: true);
        private static readonly StaffTypeDef OwnerCleaner = new StaffTypeDef("owner_cleaner", "staff.owner", StaffRole.Cleaner, 3f, isOwner: true);
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);

        private static RestaurantSession Session()
        {
            var pack = new ContentPack("test", TestContent.Dishes, new[] { Chair }, new CustomerTypeDef[0],
                new[] { Chef, Waiter, OwnerChef, OwnerWaiter, OwnerCleaner }, TestContent.AllIngredients, TestContent.Rules()).WithEconomy();
            return new RestaurantSession(pack, new RestaurantLayout(10, 8, new GridPos(9, 7)), 1);
        }

        [Test]
        public void Owner_IsFree_OutsideTheCaps_AndNeverHiredOrDismissed()
        {
            var s = Session();
            Assert.IsTrue(s.AssignOwner(StaffRole.Waiter, new GridPos(5, 4)));
            var owner = s.Owner;
            Assert.IsNotNull(owner);
            Assert.AreEqual(0, s.StaffCount(StaffRole.Waiter), "you don't take a hire slot");
            Assert.AreEqual(0, s.Wallet.DisplayCoins);
            Assert.AreEqual(HireResult.NoSlot, s.CheckHire(OwnerChef, free: true), "owner types aren't hireable");

            s.Dismiss(owner);
            Assert.AreSame(owner, s.Owner);
        }

        [Test]
        public void Owner_ChangesJob_KeepingEnergy()
        {
            var s = Session();
            s.AssignOwner(StaffRole.Waiter, new GridPos(5, 4));
            var owner = s.Owner;
            s.Sim.SetEnergy(owner, 55f, resting: false);

            s.AssignOwner(StaffRole.Cleaner, new GridPos(6, 4));

            Assert.AreSame(owner, s.Owner, "the same person");
            Assert.AreEqual(StaffRole.Cleaner, owner.Role);
            Assert.AreEqual(new GridPos(6, 4), owner.Home);
            Assert.AreEqual(55f, owner.Energy, 0.001f);
            Assert.AreEqual(1, s.Sim.Staff.Count);
            Assert.IsTrue(s.Ledger.Entries.Any(e => e.kind == "OwnerJobChanged" && e.refId == "owner_cleaner"));
        }

        [Test]
        public void Owner_SurvivesSaveAndLoad_Once()
        {
            var s = Session();
            s.AssignOwner(StaffRole.Chef, new GridPos(2, 2));
            var save = s.CaptureSave();
            save.staff.Add(save.staff[0]); // a corrupt save with two of you

            var loaded = Session();
            loaded.ApplySave(save);

            Assert.AreEqual(1, loaded.Sim.Staff.Count(x => x.IsOwner));
            Assert.AreEqual(StaffRole.Chef, loaded.Owner.Role);
        }
    }
}
