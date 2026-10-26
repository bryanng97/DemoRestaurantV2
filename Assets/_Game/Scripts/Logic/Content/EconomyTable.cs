using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Content
{
    /// <summary>
    /// The designers' economy sheet (Docs/Design/Global_FnB_City_Economy_*.xlsx) as data: economy.json, written by
    /// DemoRestaurant > Content > Import Economy Sheet. Never edit economy.json by hand; change the sheet and import.
    /// Field names are the JSON keys. Art, names and recipes stay in dishes.json / furniture.json; the rows here
    /// carry only the numbers, matched by id.
    /// </summary>
    [Serializable]
    public sealed class EconomyTable
    {
        public string sourceFile;
        public EconomySettings settings = new EconomySettings();
        public EconomyLevel[] levels = new EconomyLevel[0];
        public EconomyDish[] dishes = new EconomyDish[0];
        public EconomyIngredient[] ingredients = new EconomyIngredient[0];
        public EconomyFurniture[] furniture = new EconomyFurniture[0];
        public EconomyExpansion[] expansions = new EconomyExpansion[0];
        public EconomyStaffRole[] staffRoles = new EconomyStaffRole[0];

        /// <summary>Highest level playable now (Phase 1 caps the 65 sheet levels).</summary>
        public int MaxPlayableLevel => Math.Max(1, Math.Min(settings.phase1MaxLevel > 0 ? settings.phase1MaxLevel : int.MaxValue,
            levels.Length > 0 ? levels[levels.Length - 1].level : 1));

        /// <summary>The last level in the Levels tab (beyond the playable cap).</summary>
        public int MaxTableLevel => levels.Length > 0 ? levels[levels.Length - 1].level : 1;

        public EconomyLevel Level(int level)
        {
            if (levels.Length == 0) throw new ContentException("economy: no levels");
            int i = Math.Max(1, Math.Min(level, levels.Length)) - 1;
            return levels[i];
        }

        /// <summary>
        /// Menu slots for a category at a level: the first <c>menuCategories</c> categories in sheet order
        /// (starter, main, dessert, then drink) each get <c>dishesPerCategory</c> slots; the rest get none.
        /// </summary>
        public int MenuSlots(int level, DishCategory category)
        {
            var row = Level(level);
            return (int)category < row.menuCategories ? Math.Max(0, row.dishesPerCategory) : 0;
        }

        /// <summary>Level a staff role can first be hired at (Staff tab); 1 when the role isn't listed.</summary>
        public int RoleUnlockLevel(string role)
        {
            foreach (var r in staffRoles)
                if (string.Equals(r.role, role, StringComparison.OrdinalIgnoreCase)) return Math.Max(1, r.unlockLevel);
            return 1;
        }

        public EconomyDish Dish(string id) => Find(dishes, d => d.id == id);
        public EconomyIngredient Ingredient(string id) => Find(ingredients, d => d.id == id);
        public EconomyFurniture Furniture(string id) => Find(furniture, d => d.id == id);

        /// <summary>Problems that would break the game; the importer refuses to write a table with any.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var s = settings;
            if (s.xpCurveBase <= 0) errors.Add("settings: XP curve base must be positive");
            if (s.maxDishLevel < 1) errors.Add("settings: max dish level must be at least 1");
            if (s.arrivalMinSeconds <= 0) errors.Add("settings: minimum arrival interval must be positive");
            if (s.tipCap < 0 || s.tipCap > 1) errors.Add("settings: tip cap must be between 0 and 1");
            if (s.sellBackFraction < 0 || s.sellBackFraction > 1) errors.Add("settings: sell-back must be between 0 and 1");
            for (int i = 0; i < levels.Length; i++)
                if (levels[i].level != i + 1) errors.Add($"levels: row {i + 1} is level {levels[i].level}, expected {i + 1}");
            if (levels.Length == 0) errors.Add("levels: none");
            CheckUnique(dishes, d => d.id, "dishes", errors);
            CheckUnique(ingredients, d => d.id, "ingredients", errors);
            CheckUnique(furniture, d => d.id, "furniture", errors);
            CheckUnique(expansions, d => d.id, "expansions", errors);
            foreach (var d in dishes)
            {
                if (d.basePrice <= 0) errors.Add($"dish {d.id}: base price must be positive");
                if (d.cookSeconds <= 0) errors.Add($"dish {d.id}: cook time must be positive");
                foreach (var ing in d.ingredients)
                    if (Ingredient(ing) == null) errors.Add($"dish {d.id}: unknown ingredient '{ing}'");
            }
            foreach (var i in ingredients)
                if (i.rarity < 1 || i.rarity > 5) errors.Add($"ingredient {i.id}: rarity must be 1 to 5");
            return errors;
        }

        private static T Find<T>(T[] rows, Predicate<T> match) where T : class
        {
            foreach (var r in rows) if (match(r)) return r;
            return null;
        }

        private static void CheckUnique<T>(T[] rows, Func<T, string> id, string what, List<string> errors)
        {
            var seen = new HashSet<string>();
            foreach (var r in rows)
            {
                string key = id(r);
                if (string.IsNullOrEmpty(key)) errors.Add($"{what}: a row has no id");
                else if (!seen.Add(key)) errors.Add($"{what}: duplicate id '{key}'");
            }
        }
    }

    /// <summary>Global tuning from the Settings sheet.</summary>
    [Serializable]
    public sealed class EconomySettings
    {
        // Start
        public int startingCoins, startingTables, startingStaff;
        public float startingRating;
        // Progression
        public int xpCurveBase, xpCurvePerLevel, maxLevel, levelUpCoins;
        // Menu
        public int menuCategoriesAtStart, drinksUnlockLevel, dishesPerCategoryAtStart, twoDishesFromLevel, threeDishesFromLevel;
        // Dishes
        public float dishPriceStepPerLevel;
        public int maxDishLevel, ingredientSetsPerUpgrade, xpServeBase, xpServePerDishLevel, xpUpgradeBase, xpUpgradePerLevel;
        // Guests
        public float patienceBaseSeconds, patiencePerAmbience, eatSeconds, tipPerAmbience, tipCap;
        public int toiletsFromLevel;
        public float arrivalBaseSeconds, arrivalLessPerStar, arrivalLessPerLevel, arrivalMinSeconds;
        // Rating (0 to 5 stars)
        public float ratingGainFast, ratingGainSlow, fastServiceThreshold, ambienceBoostDivisor, ratingLossAngry;
        // Staff
        public int staffSlotsAtStart;
        public int[] extraStaffSlotLevels = new int[0];
        public float staffEnergyHours, snackRefill;
        public int snackCost, mealCost, breakMinutes, hireBase, hirePerMember;
        // Ingredients and market
        public float crateCooldownSeconds, crateMenuChance, guestDropChance, dailyDealDiscount;
        public int crateItems, dailyDeals;
        // Daily and social
        public int streakFirstDay, streakMaxPerDay, streakStepsLost, quizReward, quizSeconds;
        public int[] visitRewards = new int[0];
        public int freeGiftsPerDay, trashCoins, surpriseVisitsPerDay, surpriseChoices;
        // Garden
        public int[] gardenPlotLevels = new int[0];
        public float defaultGrowHours, wateringHours;
        public int seedCost;
        // Fun machines
        public int funCoinsPerUse, funUsesBeforeRepair, funRepairCost;
        // Furniture
        public float sellBackFraction;
        // Phase 1
        public int phase1MaxLevel, themedSetBonus;
        // Offline income
        public float offlineCapHours, offlineRate;
    }

    [Serializable]
    public sealed class EconomyLevel
    {
        public int level, xpToNext, totalXp, coinsReward, menuCategories, dishesPerCategory, menuSlots, staffSlots, gardenPlots;
        public bool inPhase1;
    }

    public enum DishCategory { Starter, Main, Dessert, Drink }

    [Serializable]
    public sealed class EconomyDish
    {
        public string id, cuisine;
        /// <summary>starter, main, dessert or drink.</summary>
        public string category;
        public string[] ingredients = new string[0];
        public int basePrice, unlockLevel;
        public float cookSeconds;
        public bool inPhase1;

        public DishCategory Category
        {
            get
            {
                switch (category)
                {
                    case "starter": return DishCategory.Starter;
                    case "dessert": return DishCategory.Dessert;
                    case "drink": return DishCategory.Drink;
                    default: return DishCategory.Main;
                }
            }
        }
    }

    [Serializable]
    public sealed class EconomyIngredient
    {
        public string id;
        /// <summary>market or garden.</summary>
        public string source;
        public int rarity;
        /// <summary>Garden grow time in hours; 0 for market-only ingredients.</summary>
        public float growHours;
        public bool inPhase1;

        /// <summary>Crate weight = 12 - 2 x stars (minimum 1): a 1-star item is 5 times as common as a 5-star one.</summary>
        public int CrateWeight => Math.Max(1, 12 - 2 * rarity);
    }

    [Serializable]
    public sealed class EconomyFurniture
    {
        public string id;
        /// <summary>function, decor, fun, floor or wallpaper.</summary>
        public string category;
        public int cost, ambience, unlockLevel;
        /// <summary>Theme name ("Malaysia") or empty.</summary>
        public string themedSet;
        public bool inPhase1;
    }

    [Serializable]
    public sealed class EconomyExpansion
    {
        public string id, type;
        public int width, height, unlockLevel, cost;
        public bool inPhase1;
    }

    [Serializable]
    public sealed class EconomyStaffRole
    {
        /// <summary>chef, waiter or cleaner.</summary>
        public string role;
        public int unlockLevel;
        public bool inPhase1;
    }

    /// <summary>The sheet's formulas, in one place so the game and the tests use the same maths.</summary>
    public static class EconomyFormulas
    {
        /// <summary>XP to go from <paramref name="level"/> to the next: base + per level x level.</summary>
        public static int XpToNext(EconomySettings s, int level) => s.xpCurveBase + s.xpCurvePerLevel * Math.Max(1, level);

        /// <summary>Price at a dish level: base x (1 + step x (level - 1)), rounded half away from zero.</summary>
        public static int DishPrice(EconomySettings s, int basePrice, int dishLevel)
        {
            int lvl = Math.Max(1, Math.Min(dishLevel, Math.Max(1, s.maxDishLevel)));
            return (int)Math.Round(basePrice * (1.0 + s.dishPriceStepPerLevel * (lvl - 1)), MidpointRounding.AwayFromZero);
        }

        public static int XpPerServe(EconomySettings s, int dishLevel) => s.xpServeBase + s.xpServePerDishLevel * Math.Max(1, dishLevel);

        /// <summary>XP for taking a dish to <paramref name="newLevel"/> (level 1 = discovering the recipe).</summary>
        public static int XpForUpgrade(EconomySettings s, int newLevel) => s.xpUpgradeBase + s.xpUpgradePerLevel * Math.Max(1, newLevel);

        /// <summary>Seconds between guests: base - per star x rating - per level x level, never below the minimum.</summary>
        public static double ArrivalSeconds(EconomySettings s, double rating, int level) =>
            Math.Max(s.arrivalMinSeconds, s.arrivalBaseSeconds - s.arrivalLessPerStar * rating - s.arrivalLessPerLevel * level);

        /// <summary>Guest patience in seconds: base + per ambience point.</summary>
        public static double PatienceSeconds(EconomySettings s, int ambience) =>
            s.patienceBaseSeconds + s.patiencePerAmbience * Math.Max(0, ambience);

        /// <summary>Tip as a fraction of the bill: per ambience point, capped.</summary>
        public static double TipFraction(EconomySettings s, int ambience) =>
            Math.Min(s.tipCap, s.tipPerAmbience * Math.Max(0, ambience));

        /// <summary>Hire cost = base + per member x current team size.</summary>
        public static int HireCost(EconomySettings s, int teamSize) => s.hireBase + s.hirePerMember * Math.Max(0, teamSize);

        public static int SellBack(EconomySettings s, int cost) =>
            (int)Math.Round(cost * s.sellBackFraction, MidpointRounding.AwayFromZero);
    }
}
