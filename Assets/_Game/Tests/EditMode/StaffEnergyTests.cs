using System;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class StaffEnergyTests
    {
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);
        private static readonly FurnitureDef Stove = new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1f, 0f, 1);
        private static readonly FurnitureDef Pass = new FurnitureDef("pass", "p", FurnitureKind.ServingCounter, 1, 1, 1f, 0f, 1);
        private static readonly DishDef Laksa = new DishDef("laksa", "dish.laksa", 13, cookSeconds: 4f, eatSeconds: 2f);
        private static readonly CustomerTypeDef Patient = new CustomerTypeDef("p", "p", 4f, 999f, 1, foodPatienceSeconds: 999f);
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef", "staff.chef", StaffRole.Chef, 4f);

        private static RestaurantSim Sim(out RestaurantLayout layout)
        {
            layout = new RestaurantLayout(12, 10, new GridPos(11, 9));
            layout.TryPlace(Stove, new GridPos(0, 0), Facing.SouthWest, out _);
            layout.TryPlace(Pass, new GridPos(0, 3), Facing.SouthWest, out _);
            layout.TryPlace(Chair, new GridPos(0, 7), Facing.NorthEast, out _);
            return new RestaurantSim(layout, new RestaurantMenu(new[] { Laksa }, 3), seed: 3);
        }

        private static void Run(RestaurantSim sim, float seconds)
        {
            for (float t = 0; t < seconds; t += 0.05f) sim.Tick(0.05f);
        }

        [TestCase(100f, 1f)]
        [TestCase(80f, 1f)]
        [TestCase(79.9f, 0.8f)]
        [TestCase(60f, 0.8f)]
        [TestCase(59f, 0.5f)]
        [TestCase(40f, 0.5f)]
        [TestCase(39f, 0.3f)]
        [TestCase(20f, 0.3f)]
        [TestCase(19f, 0.05f)]
        [TestCase(0f, 0.05f)]
        public void SpeedFollowsEnergyBands(float energy, float expected)
        {
            Assert.AreEqual(expected, new StaffEnergyRules().SpeedMultiplier(energy), 1e-5f);
        }

        [Test]
        public void SpeedBands_RunAllCases()
        {
            // The plain test runner used outside Unity ignores [TestCase]; this keeps the bands covered there too.
            var rules = new StaffEnergyRules();
            float[,] cases = { { 100, 1 }, { 80, 1 }, { 79.9f, 0.8f }, { 60, 0.8f }, { 59, 0.5f }, { 40, 0.5f }, { 39, 0.3f }, { 20, 0.3f }, { 19, 0.05f }, { 0, 0.05f } };
            for (int i = 0; i < cases.GetLength(0); i++)
                Assert.AreEqual(cases[i, 1], rules.SpeedMultiplier(cases[i, 0]), 1e-5f, $"energy {cases[i, 0]}");
        }

        [Test]
        public void OnDuty_DrainsEnergy_EvenWhenWaitingForWork()
        {
            var sim = Sim(out _);
            var chef = sim.Hire(Chef, new GridPos(1, 1));

            Run(sim, 600f); // 10 minutes at 2 per minute, no customers at all

            Assert.AreEqual(80f, chef.Energy, 0.1f);
        }

        [Test]
        public void TiredChef_CooksAndWalksSlower()
        {
            float CookTime(float energy)
            {
                var sim = Sim(out _);
                var chef = sim.Hire(Chef, new GridPos(0, 1));
                sim.SetEnergy(chef, energy, resting: false);
                sim.SpawnCustomer(Patient);
                for (int i = 0; i < 4000 && chef.State != StaffState.Cooking; i++) sim.Tick(0.05f);
                float t = 0;
                while (chef.State == StaffState.Cooking && t < 200f) { sim.Tick(0.05f); t += 0.05f; }
                return t;
            }

            Assert.AreEqual(4f, CookTime(100f), 0.15f, "full energy: base 4 s");
            Assert.AreEqual(8f, CookTime(50f), 0.25f, "energy 50 -> half speed");

            var s2 = Sim(out _);
            var tired = s2.Hire(Chef, new GridPos(1, 1));
            s2.SetEnergy(tired, 30f, resting: false);
            Assert.AreEqual(4f * 0.3f, tired.WalkSpeed, 1e-4f);
        }

        [Test]
        public void SendToRest_WhileCooking_FinishesFirst_ThenRecovers_AndReturnsWhenFull()
        {
            var sim = Sim(out _);
            var chef = sim.Hire(Chef, new GridPos(0, 1));
            sim.SetEnergy(chef, 70f, resting: false);
            sim.SpawnCustomer(Patient);
            for (int i = 0; i < 4000 && chef.State != StaffState.Cooking; i++) sim.Tick(0.05f);

            sim.SendToRest(chef);
            Assert.IsTrue(chef.RestRequested);
            Assert.IsFalse(chef.IsResting, "still cooking");

            for (int i = 0; i < 4000 && !chef.IsResting; i++) sim.Tick(0.05f);
            Assert.IsTrue(chef.IsResting);
            Assert.IsNull(chef.Ticket);

            float before = chef.Energy;
            Run(sim, 60f);
            Assert.AreEqual(before + 12f, chef.Energy, 0.2f, "recovers 12 per minute");

            Run(sim, 200f);
            Assert.IsFalse(chef.IsResting, "back to work at full energy");
            Assert.Greater(chef.Energy, 95f);
        }

        [Test]
        public void RestingStaff_TakeNoJobs()
        {
            var sim = Sim(out _);
            var chef = sim.Hire(Chef, new GridPos(0, 1));
            sim.SetEnergy(chef, 10f, resting: true);
            var customer = sim.SpawnCustomer(Patient);

            Run(sim, 30f);

            Assert.AreEqual(TicketState.Queued, customer.Ticket.State);
            Assert.AreEqual(StaffState.Idle, chef.State);
        }
    }

    public class StaffShopTests
    {
        private static readonly FurnitureDef Stove = new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1f, 0f, 1);
        private static readonly StaffTypeDef Junior = new StaffTypeDef("chef", "c", StaffRole.Chef, 2.5f, 1f);
        private static readonly ItemDef Kopi = new ItemDef("kopi", "item.kopi", energyRestore: 40f, price: 30);

        private static RestaurantSession Session(long coins, int level = 1)
        {
            var rules = TestContent.Rules();
            rules.StartingItems = new[] { new System.Collections.Generic.KeyValuePair<ItemDef, int>(Kopi, 1) };
            var pack = new ContentPack("test", TestContent.Dishes, new[] { Stove }, new CustomerTypeDef[0],
                new[] { Junior }, TestContent.AllIngredients, rules, new[] { Kopi }).WithEconomy();
            var layout = new RestaurantLayout(8, 8, new GridPos(7, 7));
            layout.TryPlace(Stove, new GridPos(0, 0), Facing.SouthWest, out _);
            var s = new RestaurantSession(pack, layout, seed: 1, startingCoins: coins);
            if (level > 1) s.Progress.Restore(level, 0);
            return s;
        }

        [Test]
        public void Hiring_CostsCoins_ByTeamSize()
        {
            var s = Session(coins: 99);
            Assert.AreEqual(HireResult.NotEnoughCoins, s.Hire(Junior, new GridPos(1, 1), out _));

            s = Session(coins: 300);
            Assert.AreEqual(HireResult.Hired, s.Hire(Junior, new GridPos(1, 1), out _));
            Assert.AreEqual(200, s.Wallet.DisplayCoins, "first hire: 100");
            Assert.AreEqual(HireResult.Hired, s.Hire(Junior, new GridPos(2, 1), out _));
            Assert.AreEqual(40, s.Wallet.DisplayCoins, "second hire: 100 + 60");
        }

        [Test]
        public void Items_AreBoughtWithCoins_AndRestoreEnergy()
        {
            var s = Session(coins: 50);
            s.Hire(Junior, new GridPos(1, 1), out var chef, free: true);
            s.Sim.SetEnergy(chef, 30f, resting: false);

            Assert.AreEqual(1, s.Items.Count(Kopi), "starting item");
            Assert.IsTrue(s.UseItem(Kopi, chef));
            Assert.AreEqual(70f, chef.Energy, 1e-3f);
            Assert.AreEqual(0, s.Items.Count(Kopi));
            Assert.IsFalse(s.UseItem(Kopi, chef), "none left");

            Assert.IsTrue(s.BuyItem(Kopi));
            Assert.AreEqual(20, s.Wallet.DisplayCoins);
            Assert.IsFalse(s.BuyItem(Kopi), "can't afford a second");

            s.Sim.SetEnergy(chef, 100f, resting: false);
            Assert.IsFalse(s.UseItem(Kopi, chef), "not wasted on a full-energy staff member");
            Assert.AreEqual(1, s.Items.Count(Kopi));
        }

        [Test]
        public void RefillAll_GivesItemsToTheMostTiredFirst_UntilFullOrOut()
        {
            var s = Session(coins: 1000);
            s.Hire(Junior, new GridPos(1, 1), out var a, free: true);
            s.Hire(Junior, new GridPos(2, 1), out var b, free: true);
            s.Sim.SetEnergy(a, 90f, resting: false);
            s.Sim.SetEnergy(b, 20f, resting: false);
            for (int i = 0; i < 4; i++) s.BuyItem(Kopi);
            Assert.AreEqual(5, s.Items.Count(Kopi));

            Assert.AreEqual(3, s.RefillAll(Kopi), "b: 20 -> 60 -> 100, then a: 90 -> 100 (each +40)");
            Assert.AreEqual(100f, a.Energy, 1e-3f);
            Assert.AreEqual(100f, b.Energy, 1e-3f);
            Assert.AreEqual(2, s.Items.Count(Kopi), "nothing used once everyone is full");
            Assert.AreEqual(0, s.RefillAll(Kopi));

            s.Sim.SetEnergy(a, 0f, resting: false);
            s.Sim.SetEnergy(b, 0f, resting: false);
            Assert.AreEqual(2, s.RefillAll(Kopi), "stops when the items run out");
            Assert.AreEqual(0, s.Items.Count(Kopi));
        }

        [Test]
        public void OldSaves_WithStaffIds_LoadAsTheRoleType()
        {
            var save = Session(coins: 0).CaptureSave();
            save.version = 1;
            save.staff.Add(new Save.StaffSave { typeId = "chef_junior", homeX = 1, homeY = 1 });

            var loaded = Session(coins: 0);
            loaded.ApplySave(save);

            Assert.AreEqual(1, loaded.Sim.Staff.Count);
            Assert.AreSame(Junior, loaded.Sim.Staff[0].Type);
            Assert.AreEqual(100f, loaded.Sim.Staff[0].Energy);
        }

        [Test]
        public void EnergyRestAndItems_SurviveSaveAndLoad()
        {
            var s = Session(coins: 0);
            s.Hire(Junior, new GridPos(1, 1), out var chef, free: true);
            s.Sim.SetEnergy(chef, 42f, resting: true);
            var save = s.CaptureSave();

            var loaded = Session(coins: 0);
            loaded.Items.Restore(new System.Collections.Generic.KeyValuePair<ItemDef, int>[0]);
            loaded.ApplySave(save);

            Assert.AreEqual(42f, loaded.Sim.Staff[0].Energy, 1e-3f);
            Assert.IsTrue(loaded.Sim.Staff[0].IsResting);
            Assert.AreEqual(1, loaded.Items.Count(Kopi));
        }
    }
}
