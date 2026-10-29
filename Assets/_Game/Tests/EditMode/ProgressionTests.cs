using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Progression;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    internal static class TestContent
    {
        public static readonly IngredientDef Rice = new IngredientDef("rice", "ing.rice", 10);
        public static readonly IngredientDef Coconut = new IngredientDef("coconut_milk", "ing.coconut_milk", 5);
        public static readonly IngredientDef Anchovy = new IngredientDef("anchovy", "ing.anchovy", 5);
        public static readonly IngredientDef Chili = new IngredientDef("chili", "ing.chili", 10);
        public static readonly IngredientDef Tea = new IngredientDef("tea_leaves", "ing.tea_leaves", 10);
        public static readonly IngredientDef Milk = new IngredientDef("condensed_milk", "ing.condensed_milk", 10);
        public static readonly IngredientDef Egg = new IngredientDef("egg", "ing.egg", 10);
        public static readonly IngredientDef[] AllIngredients = { Rice, Coconut, Anchovy, Chili, Tea, Milk, Egg };

        public static IngredientAmount[] R(params IngredientDef[] items)
        {
            var counts = new Dictionary<IngredientDef, int>();
            foreach (var i in items) { counts.TryGetValue(i, out int n); counts[i] = n + 1; }
            var list = new List<IngredientAmount>();
            foreach (var kv in counts) list.Add(new IngredientAmount(kv.Key, kv.Value));
            return list.ToArray();
        }

        // One dish per menu category, so each fits its own level-1 slot.
        public static readonly DishDef TehTarik = new DishDef("teh_tarik", "dish.teh_tarik", 4, 2, 4, stars: 1,
            recipe: R(Tea, Milk), isStarter: true, category: DishCategory.Dessert);
        public static readonly DishDef NasiLemak = new DishDef("nasi_lemak", "dish.nasi_lemak", 12, 6, 8, stars: 2,
            recipe: R(Rice, Coconut, Anchovy, Chili), category: DishCategory.Main);
        public static readonly DishDef EggRice = new DishDef("egg_rice", "dish.egg_rice", 6, 3, 4, stars: 1,
            recipe: R(Rice, Egg, Egg), category: DishCategory.Starter);
        public static readonly DishDef[] Dishes = { TehTarik, NasiLemak, EggRice };

        public static GameRules Rules() => new GameRules();

        /// <summary>
        /// Economy table with the v0.3 sheet's numbers for the rules under test, capped at
        /// <paramref name="maxLevel"/> levels: XP to next = 80 + 60 x level, 100 coins per level-up,
        /// 3 menu categories with 1 dish each (2 from level 10), staff slots 3 (+1 at levels 2 and 5),
        /// cleaners from level 6.
        /// </summary>
        public static EconomyTable Economy(int maxLevel = 10)
        {
            var levels = new EconomyLevel[maxLevel];
            int staff = 3;
            for (int i = 0; i < maxLevel; i++)
            {
                int level = i + 1;
                if (level == 2 || level == 5) staff++;
                levels[i] = new EconomyLevel
                {
                    level = level, xpToNext = 80 + 60 * level, coinsReward = level == 1 ? 0 : 100,
                    menuCategories = 3, dishesPerCategory = level >= 10 ? 2 : 1, staffSlots = staff, inPhase1 = true,
                };
            }
            return new EconomyTable
            {
                levels = levels,
                staffRoles = new[]
                {
                    new EconomyStaffRole { role = "chef", unlockLevel = 1 },
                    new EconomyStaffRole { role = "waiter", unlockLevel = 1 },
                    new EconomyStaffRole { role = "cleaner", unlockLevel = 6 },
                },
                settings = new EconomySettings
                {
                    startingRating = 3, xpCurveBase = 80, xpCurvePerLevel = 60, maxLevel = 65, levelUpCoins = 100,
                    dishPriceStepPerLevel = 0.2f, maxDishLevel = 10,
                    xpServeBase = 6, xpServePerDishLevel = 2, xpUpgradeBase = 20, xpUpgradePerLevel = 10,
                    arrivalBaseSeconds = 9, arrivalLessPerStar = 1.1f, arrivalLessPerLevel = 0.25f, arrivalMinSeconds = 2.2f,
                    ratingGainFast = 0.06f, ratingGainSlow = 0.03f, fastServiceThreshold = 0.5f, ratingLossAngry = 0.15f,
                    hireBase = 100, hirePerMember = 60, tipCap = 0.4f, sellBackFraction = 1f / 3f,
                    phase1MaxLevel = maxLevel, offlineCapHours = 4, offlineRate = 0.25f,
                },
            };
        }

        /// <summary>A pack ready for a <c>RestaurantSession</c>: the given pack with the test economy attached.</summary>
        public static ContentPack WithEconomy(this ContentPack pack, int maxLevel = 10)
        {
            pack.Economy = Economy(maxLevel);
            return pack;
        }
    }

    public class InventoryTests
    {
        [Test]
        public void TryConsume_IsAllOrNothing()
        {
            var inv = new Inventory();
            inv.Add(TestContent.Rice, 2);
            inv.Add(TestContent.Egg, 1);

            Assert.IsFalse(inv.TryConsume(TestContent.R(TestContent.Rice, TestContent.Egg, TestContent.Egg)));
            Assert.AreEqual(2, inv.Count(TestContent.Rice), "nothing taken when short");

            Assert.IsTrue(inv.TryConsume(TestContent.R(TestContent.Rice, TestContent.Egg)));
            Assert.AreEqual(1, inv.Count(TestContent.Rice));
            Assert.AreEqual(0, inv.Count(TestContent.Egg));
        }
    }

    public class RecipeBookTests
    {
        private static Inventory Stocked()
        {
            var inv = new Inventory();
            foreach (var i in TestContent.AllIngredients) inv.Add(i, 3);
            return inv;
        }

        [Test]
        public void StartsKnowingOnlyStarterDishes()
        {
            var book = new RecipeBook(TestContent.Dishes);
            CollectionAssert.AreEqual(new[] { TestContent.TehTarik }, book.Known);
        }

        [Test]
        public void Learning_FromTheList_UsesOneIngredientSet()
        {
            var book = new RecipeBook(TestContent.Dishes);
            var inv = Stocked();
            DishDef learned = null;
            book.Discovered += d => learned = d;

            Assert.AreEqual(RecipeTrainResult.Learned, book.Train(TestContent.NasiLemak, inv, 99, 10, 1));

            Assert.AreSame(TestContent.NasiLemak, learned);
            Assert.IsTrue(book.IsKnown(TestContent.NasiLemak));
            Assert.AreEqual(1, book.LevelOf(TestContent.NasiLemak));
            Assert.AreEqual(2, inv.Count(TestContent.Rice));
            Assert.AreEqual(2, inv.Count(TestContent.Chili));
        }

        [Test]
        public void ContentPack_RejectsTwoDishesWithTheSameRecipe()
        {
            var clone = new DishDef("teh_clone", "dish.x", 1, 1, 1, recipe: TestContent.R(TestContent.Milk, TestContent.Tea));
            Assert.Throws<ContentException>(() => new ContentPack("x", new[] { TestContent.TehTarik, clone },
                new FurnitureDef[0], new CustomerTypeDef[0]));
        }
    }

    public class IngredientMarketTests
    {
        [Test]
        public void Collect_EmptiesBaskets_ThenTheyRestockOneAtATime()
        {
            var market = new IngredientMarket(TestContent.AllIngredients, baskets: 3, restockSeconds: 10f, new Random(1));
            var inv = new Inventory();

            Assert.AreEqual(3, market.Collect(inv).Count);
            Assert.AreEqual(3, inv.TotalItems);
            Assert.AreEqual(0, market.ReadyBaskets);
            Assert.IsEmpty(market.Collect(inv));

            market.Tick(9f);
            Assert.AreEqual(0, market.ReadyBaskets);
            market.Tick(2f);
            Assert.AreEqual(1, market.ReadyBaskets);
            market.Tick(100f);
            Assert.AreEqual(3, market.ReadyBaskets, "never more than capacity");
        }

        [Test]
        public void Roll_FollowsDropWeights()
        {
            var common = new IngredientDef("common", "c", 9);
            var rare = new IngredientDef("rare", "r", 1);
            var market = new IngredientMarket(new[] { common, rare }, 1, 1f, new Random(42));
            int rares = 0;
            for (int i = 0; i < 10000; i++)
                if (market.Roll() == rare) rares++;
            Assert.That(rares, Is.InRange(800, 1200));
        }
    }

    public class RestaurantProgressTests
    {
        [Test]
        public void XpCurve_FollowsTheSheet()
        {
            var p = new RestaurantProgress(TestContent.Economy());
            Assert.AreEqual(1, p.Level);
            Assert.AreEqual(140, p.XpToNext);
            Assert.AreEqual(8, p.XpForServe(1));
            Assert.AreEqual(30, p.XpForRecipeLevel(1));
        }

        [Test]
        public void LevelUp_CarriesOverXp_AndPaysCoins()
        {
            var p = new RestaurantProgress(TestContent.Economy());
            var ups = new List<(int, int)>();
            p.LevelUp += (level, coins) => ups.Add((level, coins));

            p.Add(150); // 140 to level 2, 10 left over

            Assert.AreEqual(2, p.Level);
            Assert.AreEqual(10, p.Xp);
            Assert.AreEqual(200, p.XpToNext);
            CollectionAssert.AreEqual(new[] { (2, 100) }, ups);
        }

        [Test]
        public void SeveralLevelsAtOnce()
        {
            var p = new RestaurantProgress(TestContent.Economy());
            int ups = 0;
            p.LevelUp += (_, __) => ups++;
            p.Add(140 + 200 + 5);
            Assert.AreEqual(3, p.Level);
            Assert.AreEqual(5, p.Xp);
            Assert.AreEqual(2, ups);
        }

        [Test]
        public void StopsAtThePlayableCap_WithAFullBar()
        {
            var p = new RestaurantProgress(TestContent.Economy(maxLevel: 3));
            p.Add(100000);
            Assert.AreEqual(3, p.Level);
            Assert.IsTrue(p.IsMaxLevel);
            Assert.AreEqual(0, p.XpToNext);
            Assert.AreEqual(1f, p.Progress);
        }

        [Test]
        public void TestingSwitch_GoesPastThePlayableCap_ToTheEndOfTheTable()
        {
            var economy = TestContent.Economy(maxLevel: 10);
            economy.settings.phase1MaxLevel = 3; // the Levels tab goes to 10, play stops at 3
            var p = new RestaurantProgress(economy);
            p.Add(100000);
            Assert.AreEqual(3, p.Level);
            p.IgnorePlayableCap = true;
            Assert.IsFalse(p.IsMaxLevel);
            p.Add(p.XpToNext);
            Assert.AreEqual(4, p.Level);
        }

        [Test]
        public void Restore_ClampsToTheCap()
        {
            var p = new RestaurantProgress(TestContent.Economy(maxLevel: 10));
            p.Restore(28, 50);
            Assert.AreEqual(10, p.Level);
            Assert.AreEqual(0, p.Xp);
        }

        [Test]
        public void LevelRow_GivesSlots()
        {
            var e = TestContent.Economy();
            var p = new RestaurantProgress(e, level: 5);
            Assert.AreEqual(5, p.Row.staffSlots);
            Assert.AreEqual(1, e.MenuSlots(5, DishCategory.Starter));
            Assert.AreEqual(0, e.MenuSlots(5, DishCategory.Drink), "drinks are the 4th category");
            Assert.AreEqual(2, e.MenuSlots(10, DishCategory.Main));
            Assert.AreEqual(6, e.RoleUnlockLevel("Cleaner"));
        }
    }

    public class RestaurantDemandTests
    {
        private static readonly DemandRules Rules = new DemandRules
        {
            Start = 120, Min = 40, Max = 500, PerHappyGuest = 1, PerUnhappyGuest = 2, ClosedDecayPerHour = 10, GuestsPerMinutePerDemand = 0.05,
        };

        [Test]
        public void HappyGuestsRaise_UnhappyGuestsLower()
        {
            var d = new RestaurantDemand(Rules);
            double change = 0;
            d.Changed += c => change = c;
            d.OnHappyGuest();
            Assert.AreEqual(121, d.Value, 1e-9);
            Assert.AreEqual(1, change, 1e-9);
            d.OnUnhappyGuest();
            Assert.AreEqual(119, d.Value, 1e-9);
            Assert.AreEqual(-2, change, 1e-9);
        }

        [Test]
        public void SetsTheGuestRate()
        {
            var d = new RestaurantDemand(Rules);
            Assert.AreEqual(6, d.GuestsPerMinute, 1e-9); // 120 x 0.05
            Assert.AreEqual(0.24f, d.Fill, 1e-6f);
        }

        [Test]
        public void FadesWhileClosed()
        {
            var d = new RestaurantDemand(Rules);
            d.OnClosed(TimeSpan.FromHours(3));
            Assert.AreEqual(90, d.Value, 1e-9);
            d.OnClosed(TimeSpan.FromHours(-5));
            Assert.AreEqual(90, d.Value, 1e-9, "negative time does nothing");
        }

        [Test]
        public void StaysBetweenMinAndMax()
        {
            var d = new RestaurantDemand(Rules);
            d.OnClosed(TimeSpan.FromDays(30));
            Assert.AreEqual(40, d.Value);
            d.Restore(9999);
            Assert.AreEqual(500, d.Value);
            Assert.AreEqual(40, new RestaurantDemand(Rules, 1).Value);
        }
    }

    public class CustomerArrivalsTests
    {
        private static readonly CustomerTypeDef Local = new CustomerTypeDef("local", "c", 2, 10, 1, minLevel: 1);
        private static readonly CustomerTypeDef Tourist = new CustomerTypeDef("tourist", "c", 2, 10, 1, minLevel: 2);

        private static CustomerArrivals Arrivals(int level, double perMinute, params CustomerTypeDef[] types) =>
            new CustomerArrivals(types, () => level, () => perMinute, new Random(3));

        [Test]
        public void Interval_FollowsTheGuestRate()
        {
            Assert.AreEqual(10, Arrivals(1, 6, Local).IntervalSeconds, 1e-9);
            Assert.IsTrue(double.IsPositiveInfinity(Arrivals(1, 0, Local).IntervalSeconds));
        }

        [Test]
        public void GuestsKeepComing_NoDailyLimit()
        {
            var arrivals = Arrivals(1, 6, Local);
            var batch = new List<CustomerTypeDef>();
            int total = 0;
            for (int i = 0; i < 6000; i++) { arrivals.Tick(0.1f, batch); total += batch.Count; } // 10 minutes
            Assert.AreEqual(60, total, 1);
        }

        [Test]
        public void DailyCap_StopsArrivalsUntilTheNextDay()
        {
            long day = 100;
            int cap = 3;
            var arrivals = new CustomerArrivals(new[] { Local }, () => 1, () => 60, new Random(3), () => cap, () => day);
            var batch = new List<CustomerTypeDef>();
            int total = 0;
            for (int i = 0; i < 600; i++) { arrivals.Tick(0.1f, batch); total += batch.Count; } // 60 guests' worth
            Assert.AreEqual(3, total);
            Assert.IsTrue(arrivals.DailyCapReached);

            day = 101;
            Assert.IsFalse(arrivals.DailyCapReached);
            for (int i = 0; i < 600; i++) { arrivals.Tick(0.1f, batch); total += batch.Count; }
            Assert.AreEqual(6, total);

            cap = 0; // the BO removed the limit
            for (int i = 0; i < 600; i++) { arrivals.Tick(0.1f, batch); total += batch.Count; }
            Assert.Greater(total, 6);
        }

        [Test]
        public void DailyCap_RestoresTodaysCountAndIgnoresAnotherDay()
        {
            long day = 7;
            var arrivals = new CustomerArrivals(new[] { Local }, () => 1, () => 60, new Random(3), () => 2, () => day);
            arrivals.Restore(7, 2);
            Assert.IsTrue(arrivals.DailyCapReached);
            arrivals.Restore(6, 2);
            Assert.IsFalse(arrivals.DailyCapReached);
            Assert.AreEqual(0, arrivals.ArrivedToday);
        }

        [Test]
        public void NoRate_NoGuests()
        {
            var arrivals = Arrivals(1, 0, Local);
            var batch = new List<CustomerTypeDef>();
            for (int i = 0; i < 100; i++) { arrivals.Tick(1f, batch); Assert.AreEqual(0, batch.Count); }
        }

        [Test]
        public void CustomerTypes_UnlockWithLevel()
        {
            var arrivals = Arrivals(1, 6, Local, Tourist);
            for (int i = 0; i < 50; i++)
                Assert.AreSame(Local, arrivals.PickType());

            var arrivalsL2 = Arrivals(2, 6, Local, Tourist);
            bool sawTourist = false;
            for (int i = 0; i < 50; i++)
                sawTourist |= arrivalsL2.PickType() == Tourist;
            Assert.IsTrue(sawTourist);
        }
    }
}
