using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Progression;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    /// <summary>The whole loop wired together: arrivals, kitchen, coins, level, rating, ingredients, recipes, staff slots.</summary>
    public class RestaurantSessionTests
    {
        private const long StartCoins = 1000;
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);
        private static readonly FurnitureDef Stove = new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1f, 0f, 1);
        private static readonly FurnitureDef Pass = new FurnitureDef("pass", "p", FurnitureKind.ServingCounter, 2, 1, 1f, 0f, 1);
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef", "staff.chef", StaffRole.Chef, 4f);
        private static readonly StaffTypeDef Waiter = new StaffTypeDef("waiter", "staff.waiter", StaffRole.Waiter, 4f);
        private static readonly StaffTypeDef Cleaner = new StaffTypeDef("cleaner", "staff.cleaner", StaffRole.Cleaner, 4f);
        private static readonly CustomerTypeDef Local = new CustomerTypeDef("local", "c", 3f, 60f, 1, foodPatienceSeconds: 120f);

        private static ContentPack Pack(GameRules rules = null)
        {
            rules ??= TestContent.Rules();
            rules.StartingIngredients = TestContent.R(TestContent.Rice, TestContent.Coconut, TestContent.Anchovy, TestContent.Chili);
            return new ContentPack("test", TestContent.Dishes, new[] { Chair, Stove, Pass }, new[] { Local },
                new[] { Chef, Waiter, Cleaner }, TestContent.AllIngredients, rules).WithEconomy();
        }

        private static RestaurantSession Session(ContentPack pack)
        {
            var layout = new RestaurantLayout(12, 10, new GridPos(11, 9));
            layout.TryPlace(Stove, new GridPos(0, 0), Facing.SouthWest, out _);
            layout.TryPlace(Pass, new GridPos(2, 3), Facing.SouthWest, out _);
            for (int i = 0; i < 4; i++)
                layout.TryPlace(Chair, new GridPos(i * 2, 7), Facing.NorthEast, out _);
            layout.SetWaitingArea(new[] { new GridPos(11, 8), new GridPos(11, 7), new GridPos(11, 6) });
            return new RestaurantSession(pack, layout, seed: 5, startingCoins: StartCoins);
        }

        private static void Run(RestaurantSession s, float seconds)
        {
            for (float t = 0; t < seconds; t += 0.05f) s.Tick(0.05f);
        }

        [Test]
        public void StarterMenu_AndStartingIngredients_AreSetUp()
        {
            var s = Session(Pack());
            CollectionAssert.AreEqual(new[] { TestContent.TehTarik }, s.Menu.Available);
            Assert.AreEqual(1, s.Inventory.Count(TestContent.Rice));
        }

        [Test]
        public void LearningARecipe_AddsItToTheMenu()
        {
            var s = Session(Pack());
            DishDef discovered = null;
            s.RecipeDiscovered += d => discovered = d;

            var result = s.TrainRecipe(TestContent.NasiLemak);

            Assert.AreEqual(RecipeTrainResult.Learned, result);
            Assert.AreSame(TestContent.NasiLemak, discovered);
            Assert.IsTrue(s.Menu.IsAvailable(TestContent.NasiLemak));
            Assert.IsTrue(s.Menu.IsActive(TestContent.NasiLemak), "free menu slot: goes straight on");
        }

        [Test]
        public void LearningAboveTheLevel_IsRefused_AndUsesNothing()
        {
            var pack = new ContentPack("test", new[]
                {
                    TestContent.TehTarik,
                    new DishDef("laksa", "d", 17, 7, 9, recipe: TestContent.R(TestContent.Rice, TestContent.Chili), unlockLevel: 9),
                }, new[] { Chair, Stove, Pass }, new[] { Local }, new[] { Chef, Waiter, Cleaner }, TestContent.AllIngredients,
                new GameRules { StartingIngredients = TestContent.R(TestContent.Rice, TestContent.Chili) }).WithEconomy();
            var s = Session(pack);

            var result = s.TrainRecipe(pack.GetDish("laksa"));

            Assert.AreEqual(RecipeTrainResult.LevelTooLow, result);
            Assert.AreEqual(1, s.Inventory.Count(TestContent.Rice));
        }

        [Test]
        public void StaffSlots_AreSharedByAllRoles_AndCostMoreAsTheTeamGrows()
        {
            var s = Session(Pack());
            Assert.AreEqual(3, s.StaffSlots);
            Assert.AreEqual(100, s.NextHireCost);
            Assert.IsNotNull(s.TryHire(Chef, new GridPos(1, 1)));
            Assert.AreEqual(160, s.NextHireCost);
            Assert.IsNotNull(s.TryHire(Waiter, new GridPos(6, 5)));
            Assert.IsNotNull(s.TryHire(Chef, new GridPos(3, 1)));
            Assert.AreEqual(StartCoins - 100 - 160 - 220, s.Wallet.DisplayCoins);

            Assert.AreEqual(HireResult.NoSlot, s.CheckHire(Waiter), "level 1 has 3 slots");
        }

        [Test]
        public void Cleaners_UnlockAtTheirLevel()
        {
            var s = Session(Pack());
            Assert.AreEqual(HireResult.RoleLocked, s.CheckHire(Cleaner));
            s.Progress.Restore(6, 0);
            Assert.AreEqual(HireResult.Hired, s.CheckHire(Cleaner));
        }

        [Test]
        public void ArrivingCustomers_AreServed_EarnCoinsXpAndDemand()
        {
            var s = Session(Pack());
            s.Hire(Chef, new GridPos(1, 1), out _, free: true);
            s.Hire(Waiter, new GridPos(6, 5), out _, free: true);
            int served = 0, xp = 0, levelCoins = 0;
            s.CustomerServed += (_, __, gained) => { served++; xp += gained; };
            s.LevelUp += (_, coins) => levelCoins += coins;

            Run(s, 120f); // a guest every 10 s at the starting demand; no daily limit

            Assert.Greater(served, 5);
            Assert.AreEqual(served * 8, xp, "8 XP per served dish");
            Assert.AreEqual(StartCoins + served * 4 + levelCoins, s.Wallet.DisplayCoins, "teh tarik costs 4, plus level-up coins");
            Assert.Greater(s.Demand.Value, 120.0, "every happy guest raises demand");
        }

        [Test]
        public void BuyingTheNextTerrace_EnlargesTheFloor_AndIsSavedWithTheNewBalance()
        {
            var rules = TestContent.Rules();
            rules.TerraceSteps = new[] { new TerraceStep { width = 5, depth = 3, level = 2, price = 40 } };
            var s = Session(Pack(rules));
            Assert.IsFalse(s.CanBuyNextTerrace, "level 1");
            s.Progress.Restore(2, 0);
            long coins = s.Wallet.ConfirmedCoins;

            Assert.AreEqual(Logic.Economy.LandPurchaseResult.SaveFailed, s.BuyNextTerrace((snapshot, balance) => false));
            Assert.IsFalse(s.Layout.HasTerrace, "a failed save changes nothing");

            long saved = -1;
            Assert.AreEqual(Logic.Economy.LandPurchaseResult.Success, s.BuyNextTerrace((snapshot, balance) =>
            {
                Assert.AreEqual(5, snapshot.terraceWidth);
                saved = balance;
                return true;
            }));
            Assert.AreEqual(coins - 40, saved);
            Assert.AreEqual(coins - 40, s.Wallet.ConfirmedCoins);
            Assert.AreEqual(13, s.Layout.Grid.Height);
            Assert.IsNull(s.NextTerrace, "the only step is owned");
        }

        [Test]
        public void LevelUp_GrowsTheRoom_ForFree()
        {
            var rules = TestContent.Rules();
            rules.RoomGrowth = new RoomGrowth(new[]
            {
                new RoomGrowthStep { level = 1, width = 12, height = 10 },
                new RoomGrowthStep { level = 2, width = 13, height = 11 },
            });
            var s = Session(Pack(rules));
            Assert.IsFalse(s.GrowRoom(), "already the level-1 size");
            long coins = s.Wallet.DisplayCoins;

            s.Progress.Add(140); // level 2

            Assert.AreEqual(13, s.Layout.Grid.Width);
            Assert.AreEqual(11, s.Layout.Grid.Height);
            Assert.AreEqual(coins + 100, s.Wallet.DisplayCoins, "only the level-up reward; the room costs nothing");
        }

        [Test]
        public void LevelUp_PaysCoins_AndGrowsStaffSlots()
        {
            var s = Session(Pack());
            int coinsBefore = (int)s.Wallet.DisplayCoins;

            s.Progress.Add(140);

            Assert.AreEqual(2, s.Progress.Level);
            Assert.AreEqual(coinsBefore + 100, s.Wallet.DisplayCoins);
            Assert.AreEqual(4, s.StaffSlots);
        }

        [Test]
        public void AngryCustomers_LowerDemand()
        {
            var impatient = new CustomerTypeDef("impatient", "c", 3f, 60f, 1, foodPatienceSeconds: 3f);
            var pack = new ContentPack("test", TestContent.Dishes, new[] { Chair, Stove, Pass }, new[] { impatient },
                new[] { Chef, Waiter }, TestContent.AllIngredients, TestContent.Rules()).WithEconomy();
            var s = Session(pack);
            int angered = 0;
            s.CustomerAngered += (_, lost) => angered++;

            Run(s, 30f); // no staff: nobody gets food

            Assert.Greater(angered, 0);
            Assert.Less(s.Demand.Value, 120.0);
        }

        [Test]
        public void Menu_HasOneSlotPerCategory_AtLevelOne()
        {
            var s = Session(Pack());
            s.TrainRecipe(TestContent.NasiLemak);
            Assert.AreEqual(3, s.Menu.Capacity);
            Assert.IsTrue(s.Menu.IsActive(TestContent.TehTarik), "dessert slot");
            Assert.IsTrue(s.Menu.IsActive(TestContent.NasiLemak), "main slot");
        }

        [Test]
        public void Market_GrantsIngredients_ThatCanBeUsedForRecipes()
        {
            var s = Session(Pack());
            var gained = new List<IngredientDef>();
            s.IngredientsGained += (items, source) => gained.AddRange(items);
            int before = s.Inventory.TotalItems;

            var got = s.CollectMarket();

            Assert.AreEqual(s.Rules.MarketBaskets, got.Count);
            Assert.AreEqual(before + got.Count, s.Inventory.TotalItems);
            CollectionAssert.AreEqual(got, gained);
        }
    }
}
