using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class EconomyFormulaTests
    {
        // Values from Global_FnB_City_Economy_v0.3.xlsx.
        private static readonly EconomySettings S = new EconomySettings
        {
            xpCurveBase = 80, xpCurvePerLevel = 60, dishPriceStepPerLevel = 0.2f, maxDishLevel = 10,
            xpServeBase = 6, xpServePerDishLevel = 2, xpUpgradeBase = 20, xpUpgradePerLevel = 10,
            arrivalBaseSeconds = 9, arrivalLessPerStar = 1.1f, arrivalLessPerLevel = 0.25f, arrivalMinSeconds = 2.2f,
            patienceBaseSeconds = 40, patiencePerAmbience = 1.5f, tipPerAmbience = 0.03f, tipCap = 0.4f,
            hireBase = 100, hirePerMember = 60, sellBackFraction = 1f / 3f,
        };

        [TestCase(1, 140)]
        [TestCase(10, 680)]
        [TestCase(65, 3980)]
        public void XpToNext_MatchesLevelsSheet(int level, int xp) => Assert.AreEqual(xp, EconomyFormulas.XpToNext(S, level));

        [TestCase(12, 2, 14)]
        [TestCase(12, 3, 17)]
        [TestCase(12, 10, 34)]
        [TestCase(14, 4, 22)]
        [TestCase(8, 2, 10)]
        [TestCase(12, 99, 34)] // capped at max dish level
        public void DishPrice_MatchesDishesSheet(int basePrice, int level, int price) =>
            Assert.AreEqual(price, EconomyFormulas.DishPrice(S, basePrice, level));

        [Test]
        public void ServeAndUpgradeXp()
        {
            Assert.AreEqual(8, EconomyFormulas.XpPerServe(S, 1));
            Assert.AreEqual(26, EconomyFormulas.XpPerServe(S, 10));
            Assert.AreEqual(30, EconomyFormulas.XpForUpgrade(S, 1));
        }

        [Test]
        public void ArrivalInterval_MatchesBalanceCheck()
        {
            // Balance check: 1 star, level 1 → 7.84 guests per minute.
            Assert.AreEqual(7.84, 60 / EconomyFormulas.ArrivalSeconds(S, 1, 1), 0.01);
            Assert.AreEqual(2.2, EconomyFormulas.ArrivalSeconds(S, 5, 50), 0.0001);
        }

        [Test]
        public void PatienceTipHireSellBack()
        {
            Assert.AreEqual(55, EconomyFormulas.PatienceSeconds(S, 10), 0.001);
            Assert.AreEqual(0.3, EconomyFormulas.TipFraction(S, 10), 0.0001);
            Assert.AreEqual(0.4, EconomyFormulas.TipFraction(S, 100), 0.0001);
            Assert.AreEqual(220, EconomyFormulas.HireCost(S, 2));
            Assert.AreEqual(27, EconomyFormulas.SellBack(S, 80));
        }

        [Test]
        public void CrateWeight_FromRarity()
        {
            Assert.AreEqual(10, new EconomyIngredient { rarity = 1 }.CrateWeight);
            Assert.AreEqual(2, new EconomyIngredient { rarity = 5 }.CrateWeight);
        }
    }

    public class EconomySheetParserTests
    {
        private static Dictionary<string, List<string[]>> Workbook()
        {
            var settings = new List<string[]> { new[] { "Setting", "Value" } };
            foreach (var label in new[]
            {
                "Starting coins", "Starting star rating", "Starting tables", "Starting staff", "XP curve: base",
                "XP curve: added per level", "Highest restaurant level", "Coins reward per level-up",
                "Menu categories at level 1", "Drinks category unlocks at level", "Dishes per category at level 1",
                "2 dishes per category from level", "3 dishes per category from level", "Price increase per dish level",
                "Max dish level", "Ingredients of each type per upgrade", "XP per dish served: base",
                "XP per dish served: per dish level", "XP per dish upgrade: base", "XP per dish upgrade: per level",
                "Guest patience: base", "Guest patience: added per ambience point", "Eating time", "Tip per ambience point",
                "Guests need toilets from level", "Arrival interval: base", "Arrival interval: less per star",
                "Arrival interval: less per level", "Arrival interval: minimum", "Rating gain: fast service",
                "Rating gain: slow service", "Fast service threshold", "Ambience boost divisor",
                "Rating loss: guest leaves angry", "Staff slots at level 1", "Staff energy lasts", "Snack cost (small refill)",
                "Snack refill", "Meal cost (full refill)", "Free break length", "Hire cost: base",
                "Hire cost: added per team member", "Market crate cooldown", "Ingredients per crate",
                "Chance a crate slot matches your menu", "Chance a happy guest leaves an ingredient", "Daily market deals",
                "Daily deal discount", "Login streak: ingredients on day 1", "Login streak: most ingredients per day",
                "Login streak: steps lost for a missed day", "Daily quiz reward", "Daily quiz time limit",
                "Visit reward: first friend each day", "Visit reward: second friend", "Visit reward: third to fifth friend",
                "Visit reward: after that", "Free gifts per day", "Coins per piece of trash cleaned",
                "Surprise visitor appearances per day", "Surprise visitor reward choices", "Default grow time",
                "One watering lasts", "Seed cost", "Coins per use", "Uses before repair", "Repair cost",
                "Highest restaurant level in Phase 1", "Themed set bonus when all pieces are placed", "Offline income cap",
                "Offline income rate",
            })
                settings.Add(new[] { label, "5" });
            settings.Add(new[] { "XP curve: base", "80" }); // later row wins
            settings.Add(new[] { "Tip cap", "0.4" });
            settings.Add(new[] { "Sell-back value of furniture", "0.333333333333333" });
            settings.Add(new[] { "Extra staff slot at level", "2" });
            settings.Add(new[] { "Extra staff slot at level", "5" });
            settings.Add(new[] { "Garden plot 1 at level", "3" });
            settings.Add(new[] { "Garden plot 2 at level", "6" });

            return new Dictionary<string, List<string[]>>
            {
                ["Settings"] = settings,
                ["Levels"] = new List<string[]>
                {
                    new[] { "Restaurant levels" },
                    new[] { "Level", "XP to next level", "Total XP to reach", "Coins reward on reaching", "Menu categories",
                        "Dishes per category", "Menu slots", "Staff slots", "Garden plots", "In Phase 1" },
                    new[] { "1", "140", "0", "0", "3", "1", "3", "3", "0", "Yes" },
                    new[] { "2", "200", "140", "100", "3", "1", "3", "4", "0", "No" },
                },
                ["Ingredients"] = new List<string[]>
                {
                    new[] { "ID", "Ingredient", "Source", "Rarity (stars)", "Garden grow time (hours)", "In Phase 1" },
                    new[] { "rice", "Rice", "Market", "1", null, "Yes" },
                    new[] { "chili", "Chili", "Garden", "1", "24", "Yes" },
                    new[] { "Crate weight = 12 - 2 x stars" },
                },
                ["Dishes"] = new List<string[]>
                {
                    new[] { "ID", "Dish", "Cuisine", "Category", "Ingredient 1", "Ingredient 2", "Ingredient 3",
                        "Base price (coins)", "Cook time (s)", "Unlocks at level", "In Phase 1" },
                    new[] { "nasi_lemak", "Nasi Lemak", "Malaysia", "Main", "rice", "chili", null, "12", "5", "1", "Yes" },
                    new[] { "Each ingredient needed to take one dish to max level", "9" },
                },
                ["Furniture"] = new List<string[]>
                {
                    new[] { "ID", "Item", "Category", "Cost (coins)", "Ambience points", "Unlocks at level", "Themed set", "In Phase 1" },
                    new[] { "batik_wall_art", "Batik", "Decor", "110", "2", "3", "Malaysia", "Yes" },
                },
                ["Expansions"] = new List<string[]>
                {
                    new[] { "ID", "Expansion", "Type", "Size (tiles)", "Unlocks at level", "Cost (coins)", "In Phase 1" },
                    new[] { "indoor_1", "First", "Indoor", "10 x 12", "6", "1500", "Yes" },
                },
                ["Staff"] = new List<string[]>
                {
                    new[] { "Role", "Unlocks at level", "What they do", "Source", "In Phase 1" },
                    new[] { "Chef", "1", "Cooks", "Prototype", "Yes" },
                    new string[0],
                    new[] { "Hire costs" },
                },
            };
        }

        [Test]
        public void Parses_AllTables()
        {
            var r = EconomySheetParser.Parse(Workbook(), "test.xlsx");
            Assert.IsTrue(r.Ok, string.Join("\n", r.Errors));
            var t = r.Table;
            Assert.AreEqual(80, t.settings.xpCurveBase);
            CollectionAssert.AreEqual(new[] { 2, 5 }, t.settings.extraStaffSlotLevels);
            CollectionAssert.AreEqual(new[] { 3, 6 }, t.settings.gardenPlotLevels);
            CollectionAssert.AreEqual(new[] { 5, 5, 5, 5 }, t.settings.visitRewards);
            Assert.AreEqual(2, t.levels.Length);
            Assert.IsTrue(t.levels[0].inPhase1);
            Assert.AreEqual(2, t.ingredients.Length);
            Assert.AreEqual("garden", t.Ingredient("chili").source);
            Assert.AreEqual(24, t.Ingredient("chili").growHours);
            Assert.AreEqual(1, t.dishes.Length);
            Assert.AreEqual(DishCategory.Main, t.Dish("nasi_lemak").Category);
            CollectionAssert.AreEqual(new[] { "rice", "chili" }, t.Dish("nasi_lemak").ingredients);
            Assert.AreEqual("Malaysia", t.Furniture("batik_wall_art").themedSet);
            Assert.AreEqual(10, t.expansions[0].width);
            Assert.AreEqual(12, t.expansions[0].height);
            Assert.AreEqual("chef", t.staffRoles[0].role);
            Assert.AreEqual(1, t.staffRoles.Length);
        }

        [Test]
        public void MissingSetting_IsAnError()
        {
            var book = Workbook();
            book["Settings"].RemoveAll(row => row[0] == "Tip cap");
            var r = EconomySheetParser.Parse(book, "test.xlsx");
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("Tip cap", string.Join("\n", r.Errors));
        }

        [Test]
        public void UnknownIngredientInDish_IsAnError()
        {
            var book = Workbook();
            book["Dishes"][1][5] = "unicorn";
            var r = EconomySheetParser.Parse(book, "test.xlsx");
            StringAssert.Contains("unknown ingredient 'unicorn'", string.Join("\n", r.Errors));
        }

        [Test]
        public void UppercaseId_IsAnError()
        {
            var book = Workbook();
            book["Furniture"][1][0] = "Batik_Wall_Art";
            Assert.IsFalse(EconomySheetParser.Parse(book, "test.xlsx").Ok);
        }

        [Test]
        public void BadNumber_IsAnError()
        {
            var book = Workbook();
            book["Dishes"][1][7] = "twelve";
            StringAssert.Contains("not a number", string.Join("\n", EconomySheetParser.Parse(book, "test.xlsx").Errors));
        }
    }
}
