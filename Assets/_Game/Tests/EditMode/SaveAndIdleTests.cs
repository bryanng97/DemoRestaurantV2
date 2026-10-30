using System;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Progression;
using DemoRestaurant.Logic.Save;
using DemoRestaurant.Logic.Session;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class SaveAndIdleTests
    {
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);
        private static readonly FurnitureDef Stove = new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1f, 0f, 1);
        private static readonly FurnitureDef Pass = new FurnitureDef("pass", "p", FurnitureKind.ServingCounter, 2, 1, 1f, 0f, 1);
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef", "staff.chef", StaffRole.Chef, 4f);
        private static readonly StaffTypeDef Waiter = new StaffTypeDef("waiter", "staff.waiter", StaffRole.Waiter, 4f);
        private static readonly StaffTypeDef Cleaner = new StaffTypeDef("cleaner", "staff.cleaner", StaffRole.Cleaner, 4f);
        private static readonly CustomerTypeDef Local = new CustomerTypeDef("local", "c", 3f, 60f, 1, foodPatienceSeconds: 120f);
        private static readonly DateTime Start = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

        private DateTime _now;

        private ContentPack Pack()
        {
            var rules = TestContent.Rules();
            rules.StartingIngredients = TestContent.R(TestContent.Rice, TestContent.Coconut, TestContent.Anchovy, TestContent.Chili);
            rules.MarketBaskets = 2;
            rules.MarketRestockSeconds = 60f;
            return new ContentPack("test", TestContent.Dishes, new[] { Chair, Stove, Pass }, new[] { Local },
                new[] { Chef, Waiter, Cleaner }, TestContent.AllIngredients, rules).WithEconomy();
        }

        private RestaurantSession Session(ContentPack pack)
        {
            var layout = new RestaurantLayout(12, 10, new GridPos(11, 9));
            layout.TryPlace(Stove, new GridPos(0, 0), Facing.SouthWest, out _);
            layout.TryPlace(Pass, new GridPos(2, 3), Facing.SouthWest, out _);
            for (int i = 0; i < 4; i++)
                layout.TryPlace(Chair, new GridPos(i * 2, 7), Facing.NorthEast, out _);
            return new RestaurantSession(pack, layout, seed: 5, utcNow: () => _now);
        }

        [SetUp]
        public void SetUp() => _now = Start;

        private static void Crew(RestaurantSession s)
        {
            s.Hire(Chef, new GridPos(1, 1), out _, free: true);
            s.Hire(Waiter, new GridPos(6, 5), out _, free: true);
        }

        [Test]
        public void SaveAndLoad_RestoresProgress()
        {
            var pack = Pack();
            var s = Session(pack);
            Crew(s);
            s.TrainRecipe(TestContent.NasiLemak);
            s.CollectMarket();
            for (int i = 0; i < 1200; i++) s.Tick(0.05f); // 60 s of play
            var save = s.CaptureSave();

            var loaded = Session(pack);
            var savedAt = loaded.ApplySave(save);

            Assert.AreEqual(Start, savedAt);
            Assert.AreEqual(s.Wallet.DisplayCoins, loaded.Wallet.DisplayCoins);
            Assert.AreEqual(s.Progress.Level, loaded.Progress.Level);
            Assert.AreEqual(s.Progress.Xp, loaded.Progress.Xp);
            Assert.AreEqual(s.Demand.Value, loaded.Demand.Value, 1e-3);
            Assert.IsTrue(loaded.Recipes.IsKnown(TestContent.NasiLemak));
            CollectionAssert.AreEquivalent(s.Menu.Active, loaded.Menu.Active);
            foreach (var i in TestContent.AllIngredients)
                Assert.AreEqual(s.Inventory.Count(i), loaded.Inventory.Count(i), i.Id);
            Assert.AreEqual(2, loaded.Sim.Staff.Count);
            Assert.AreEqual(s.Ledger.Entries.Count, loaded.Ledger.Entries.Count);
        }

        [Test]
        public void Load_StaffWhoseSpotIsNowFurnished_AreMovedNotLost()
        {
            var s = Session(Pack());
            Crew(s); // chef saved at (1,1), waiter at (6,5)
            var save = s.CaptureSave();

            // A renovation put a chair on the chef's old spot.
            var loaded = Session(Pack());
            loaded.Layout.TryPlace(Chair, new GridPos(1, 1), Facing.SouthWest, out _);
            loaded.ApplySave(save);

            Assert.AreEqual(2, loaded.Sim.Staff.Count);
            foreach (var staff in loaded.Sim.Staff)
                Assert.IsTrue(loaded.Layout.Grid.IsWalkable(staff.Home), $"{staff.Type.Id} home {staff.Home}");
            Assert.AreNotEqual(loaded.Sim.Staff[0].Home, loaded.Sim.Staff[1].Home);
        }

        [Test]
        public void Load_SkipsIdsMissingFromNewerContent()
        {
            var s = Session(Pack());
            var save = s.CaptureSave();
            save.knownDishes.Add("deleted_dish");
            save.menuActive.Insert(0, "deleted_dish");
            save.inventory.Add(new IngredientCount { id = "unobtainium", count = 5 });
            save.staff.Add(new StaffSave { typeId = "robot", homeX = 1, homeY = 1 });

            var loaded = Session(Pack());
            Assert.DoesNotThrow(() => loaded.ApplySave(save));
            CollectionAssert.AreEqual(new[] { TestContent.TehTarik }, loaded.Menu.Active);
            Assert.AreEqual(0, loaded.Sim.Staff.Count);
        }

        [Test]
        public void Idle_WithoutStaff_EarnsNothing_AndSaysWhy()
        {
            var s = Session(Pack());
            var report = s.ApplyOfflineProgress(Start - TimeSpan.FromHours(1));

            Assert.AreEqual(0, report.CustomersServed);
            Assert.AreEqual(OfflineProgress.NoChef, report.BlockedReason);
            Assert.AreEqual(0, s.Wallet.DisplayCoins);
        }

        [Test]
        public void Idle_EarnsFromStaffedRestaurant_AndRecordsLedger()
        {
            var s = Session(Pack());
            Crew(s);
            s.Sim.Hire(Cleaner, new GridPos(8, 5)); // clears the tables, so the waiter only serves
            var report = s.ApplyOfflineProgress(Start - TimeSpan.FromMinutes(10));

            Assert.Greater(report.CustomersServed, 0);
            Assert.AreEqual(report.CustomersServed * 4, report.Coins, "teh tarik at 4 coins");
            Assert.AreEqual(report.Coins, s.Wallet.DisplayCoins);
            Assert.AreEqual(report.CustomersServed * 8, report.Xp, "8 XP per served dish");
            var last = s.Ledger.Entries[s.Ledger.Entries.Count - 1];
            Assert.AreEqual(nameof(LedgerKind.IdleIncome), last.kind);
            Assert.AreEqual(report.Coins, last.amount);
        }

        [Test]
        public void Idle_RunsAtTheSheetsOfflineRate()
        {
            var pack = Pack();
            var quarter = Session(pack);
            Crew(quarter);
            var quarterReport = quarter.ApplyOfflineProgress(Start - TimeSpan.FromMinutes(10));

            var halfPack = Pack();
            halfPack.Economy.settings.offlineRate = 0.5f;
            var half = Session(halfPack);
            Crew(half);
            var halfReport = half.ApplyOfflineProgress(Start - TimeSpan.FromMinutes(10));

            Assert.AreEqual(quarterReport.CustomersServed * 2, halfReport.CustomersServed, 1);
        }

        [Test]
        public void Idle_IsCappedAtMaxOfflineHours()
        {
            var s = Session(Pack());
            Crew(s);
            var report = s.ApplyOfflineProgress(Start - TimeSpan.FromDays(3));

            Assert.AreEqual(3 * 24 * 3600, report.AwaySeconds, 1);
            Assert.AreEqual(4 * 3600, report.CountedSeconds, 1, "economy sheet: 4 hours");
        }

        [Test]
        public void Idle_ClockMovedBackwards_CountsAsNoTime()
        {
            var s = Session(Pack());
            Crew(s);
            var report = s.ApplyOfflineProgress(Start + TimeSpan.FromHours(5));

            Assert.AreEqual(0, report.CountedSeconds);
            Assert.AreEqual(0, s.Wallet.DisplayCoins);
        }

        [Test]
        public void Idle_RestocksMarketBaskets()
        {
            var s = Session(Pack());
            s.CollectMarket();
            Assert.AreEqual(0, s.Market.ReadyBaskets);

            s.ApplyOfflineProgress(Start - TimeSpan.FromMinutes(5));

            Assert.AreEqual(2, s.Market.ReadyBaskets);
        }

        [Test]
        public void Idle_CountsAsWorking_StaffComeBackTired()
        {
            var s = Session(Pack());
            Crew(s);
            var report = s.ApplyOfflineProgress(Start - TimeSpan.FromMinutes(30));

            foreach (var staff in s.Sim.Staff)
                Assert.AreEqual(40f, staff.Energy, 0.5f, "30 min on duty at 2 per minute");
            Assert.AreEqual(0, report.ExhaustedStaff);

            var longer = Session(Pack());
            Crew(longer);
            var tiredReport = longer.ApplyOfflineProgress(Start - TimeSpan.FromHours(2));
            Assert.AreEqual(2, tiredReport.ExhaustedStaff);
        }

        [Test]
        public void Idle_TiredStaffEarnLess()
        {
            var fresh = Session(Pack());
            Crew(fresh);
            var freshReport = fresh.ApplyOfflineProgress(Start - TimeSpan.FromMinutes(20));

            var tired = Session(Pack());
            Crew(tired);
            foreach (var staff in tired.Sim.Staff)
                tired.Sim.SetEnergy(staff, 15f, resting: false);
            var tiredReport = tired.ApplyOfflineProgress(Start - TimeSpan.FromMinutes(20));

            Assert.Greater(freshReport.CustomersServed, 0);
            Assert.Less(tiredReport.CustomersServed, freshReport.CustomersServed);
        }

        [Test]
        public void Idle_RestingStaffRecover_AndGoBackToWork()
        {
            var s = Session(Pack());
            Crew(s);
            foreach (var staff in s.Sim.Staff)
                s.Sim.SetEnergy(staff, 10f, resting: true);

            var report = s.ApplyOfflineProgress(Start - TimeSpan.FromMinutes(10));

            foreach (var staff in s.Sim.Staff)
            {
                Assert.IsFalse(staff.IsResting, "full after 7.5 min of rest, then back on duty");
                Assert.Greater(staff.Energy, 90f);
            }
            Assert.Greater(report.CustomersServed, 0, "served customers once back at work");
        }

        [Test]
        public void Idle_ABedSpeedsUpTheRestOfTheFirstRestingEmployee()
        {
            var bed = new FurnitureDef("bed", "b", FurnitureKind.Bed, 2, 1, 0.6f, 0.3f, 1, restRecoveryMultiplier: 2f);
            var s = Session(Pack());
            Assert.IsTrue(s.Layout.TryPlace(bed, new GridPos(8, 0), Facing.SouthWest, out _));
            Crew(s);
            foreach (var staff in s.Sim.Staff)
                s.Sim.SetEnergy(staff, 10f, resting: true);

            s.ApplyOfflineProgress(Start - TimeSpan.FromMinutes(2));

            var chef = s.Sim.Staff[0];
            var waiter = s.Sim.Staff[1];
            float normal = waiter.Energy - 10f;
            Assert.Greater(normal, 0f);
            Assert.AreEqual(normal * 2f, chef.Energy - 10f, 0.5f, "the chef, hired first, had the bed: twice the recovery");
        }

        [Test]
        public void Idle_XpCanLevelUp()
        {
            var s = Session(Pack());
            Crew(s);
            int levelUps = 0;
            s.LevelUp += (_, __) => levelUps++;

            s.ApplyOfflineProgress(Start - TimeSpan.FromHours(2));

            Assert.Greater(levelUps, 0);
            Assert.Greater(s.Progress.Level, 1);
        }

        [Test]
        public void Load_OldSave_KeepsPlayerLevel_AndMapsTieredStaff()
        {
            var save = Session(Pack()).CaptureSave();
            save.version = 3;
            save.playerLevel = 4;
            save.staff.Add(new StaffSave { typeId = "chef_master", homeX = 1, homeY = 1 });

            var loaded = Session(Pack());
            loaded.ApplySave(save);

            Assert.AreEqual(4, loaded.Progress.Level);
            Assert.AreEqual(0, loaded.Progress.Xp);
            Assert.AreEqual(120.0, loaded.Demand.Value, "demand starts fresh");
            Assert.AreEqual(1, loaded.Sim.Staff.Count);
            Assert.AreSame(Chef, loaded.Sim.Staff[0].Type);
        }
    }

    public class OfflineLedgerTests
    {
        private static readonly DateTime T = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void ConsecutiveSalesOfTheSameDish_AreMerged()
        {
            var ledger = new OfflineLedger();
            ledger.Record(LedgerKind.Sale, 4, "teh_tarik", T);
            ledger.Record(LedgerKind.Sale, 4, "teh_tarik", T.AddSeconds(5));
            ledger.Record(LedgerKind.Sale, 12, "nasi_lemak", T.AddSeconds(9));

            Assert.AreEqual(2, ledger.Entries.Count);
            Assert.AreEqual(8, ledger.Entries[0].amount);
        }

        [Test]
        public void FullLedger_DropsOldestAndCountsThem()
        {
            var ledger = new OfflineLedger();
            for (int i = 0; i < OfflineLedger.MaxEntries + 5; i++)
                ledger.Record(LedgerKind.MarketCollect, 1, "rice", T);

            Assert.AreEqual(OfflineLedger.MaxEntries, ledger.Entries.Count);
            Assert.AreEqual(5, ledger.Dropped);
        }
    }
}
