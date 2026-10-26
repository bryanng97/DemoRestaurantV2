using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoRestaurant.Logic.Content
{
    /// <summary>
    /// Turns the economy workbook's sheets (rows of cell text, column A = index 0) into an <see cref="EconomyTable"/>.
    /// Tables are found by their header row and read by column name, so designers can add or move columns.
    /// Settings are matched by the label in column A. Anything missing or unreadable is reported, never guessed.
    /// </summary>
    public static class EconomySheetParser
    {
        public sealed class Result
        {
            public EconomyTable Table;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public bool Ok => Errors.Count == 0;
        }

        // Settings sheet label → setter. Repeated labels ("Extra staff slot at level") collect into lists.
        private static readonly Dictionary<string, Action<EconomySettings, double>> SettingSetters =
            new Dictionary<string, Action<EconomySettings, double>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Starting coins"] = (s, v) => s.startingCoins = I(v),
                ["Starting star rating"] = (s, v) => s.startingRating = F(v),
                ["Starting tables"] = (s, v) => s.startingTables = I(v),
                ["Starting staff"] = (s, v) => s.startingStaff = I(v),
                ["XP curve: base"] = (s, v) => s.xpCurveBase = I(v),
                ["XP curve: added per level"] = (s, v) => s.xpCurvePerLevel = I(v),
                ["Highest restaurant level"] = (s, v) => s.maxLevel = I(v),
                ["Coins reward per level-up"] = (s, v) => s.levelUpCoins = I(v),
                ["Menu categories at level 1"] = (s, v) => s.menuCategoriesAtStart = I(v),
                ["Drinks category unlocks at level"] = (s, v) => s.drinksUnlockLevel = I(v),
                ["Dishes per category at level 1"] = (s, v) => s.dishesPerCategoryAtStart = I(v),
                ["2 dishes per category from level"] = (s, v) => s.twoDishesFromLevel = I(v),
                ["3 dishes per category from level"] = (s, v) => s.threeDishesFromLevel = I(v),
                ["Price increase per dish level"] = (s, v) => s.dishPriceStepPerLevel = F(v),
                ["Max dish level"] = (s, v) => s.maxDishLevel = I(v),
                ["Ingredients of each type per upgrade"] = (s, v) => s.ingredientSetsPerUpgrade = I(v),
                ["XP per dish served: base"] = (s, v) => s.xpServeBase = I(v),
                ["XP per dish served: per dish level"] = (s, v) => s.xpServePerDishLevel = I(v),
                ["XP per dish upgrade: base"] = (s, v) => s.xpUpgradeBase = I(v),
                ["XP per dish upgrade: per level"] = (s, v) => s.xpUpgradePerLevel = I(v),
                ["Guest patience: base"] = (s, v) => s.patienceBaseSeconds = F(v),
                ["Guest patience: added per ambience point"] = (s, v) => s.patiencePerAmbience = F(v),
                ["Eating time"] = (s, v) => s.eatSeconds = F(v),
                ["Tip per ambience point"] = (s, v) => s.tipPerAmbience = F(v),
                ["Tip cap"] = (s, v) => s.tipCap = F(v),
                ["Guests need toilets from level"] = (s, v) => s.toiletsFromLevel = I(v),
                ["Arrival interval: base"] = (s, v) => s.arrivalBaseSeconds = F(v),
                ["Arrival interval: less per star"] = (s, v) => s.arrivalLessPerStar = F(v),
                ["Arrival interval: less per level"] = (s, v) => s.arrivalLessPerLevel = F(v),
                ["Arrival interval: minimum"] = (s, v) => s.arrivalMinSeconds = F(v),
                ["Rating gain: fast service"] = (s, v) => s.ratingGainFast = F(v),
                ["Rating gain: slow service"] = (s, v) => s.ratingGainSlow = F(v),
                ["Fast service threshold"] = (s, v) => s.fastServiceThreshold = F(v),
                ["Ambience boost divisor"] = (s, v) => s.ambienceBoostDivisor = F(v),
                ["Rating loss: guest leaves angry"] = (s, v) => s.ratingLossAngry = F(v),
                ["Staff slots at level 1"] = (s, v) => s.staffSlotsAtStart = I(v),
                ["Extra staff slot at level"] = (s, v) => s.extraStaffSlotLevels = Append(s.extraStaffSlotLevels, I(v)),
                ["Staff energy lasts"] = (s, v) => s.staffEnergyHours = F(v),
                ["Snack cost (small refill)"] = (s, v) => s.snackCost = I(v),
                ["Snack refill"] = (s, v) => s.snackRefill = F(v),
                ["Meal cost (full refill)"] = (s, v) => s.mealCost = I(v),
                ["Free break length"] = (s, v) => s.breakMinutes = I(v),
                ["Hire cost: base"] = (s, v) => s.hireBase = I(v),
                ["Hire cost: added per team member"] = (s, v) => s.hirePerMember = I(v),
                ["Market crate cooldown"] = (s, v) => s.crateCooldownSeconds = F(v),
                ["Ingredients per crate"] = (s, v) => s.crateItems = I(v),
                ["Chance a crate slot matches your menu"] = (s, v) => s.crateMenuChance = F(v),
                ["Chance a happy guest leaves an ingredient"] = (s, v) => s.guestDropChance = F(v),
                ["Daily market deals"] = (s, v) => s.dailyDeals = I(v),
                ["Daily deal discount"] = (s, v) => s.dailyDealDiscount = F(v),
                ["Login streak: ingredients on day 1"] = (s, v) => s.streakFirstDay = I(v),
                ["Login streak: most ingredients per day"] = (s, v) => s.streakMaxPerDay = I(v),
                ["Login streak: steps lost for a missed day"] = (s, v) => s.streakStepsLost = I(v),
                ["Daily quiz reward"] = (s, v) => s.quizReward = I(v),
                ["Daily quiz time limit"] = (s, v) => s.quizSeconds = I(v),
                ["Visit reward: first friend each day"] = (s, v) => s.visitRewards = Append(s.visitRewards, I(v)),
                ["Visit reward: second friend"] = (s, v) => s.visitRewards = Append(s.visitRewards, I(v)),
                ["Visit reward: third to fifth friend"] = (s, v) => s.visitRewards = Append(s.visitRewards, I(v)),
                ["Visit reward: after that"] = (s, v) => s.visitRewards = Append(s.visitRewards, I(v)),
                ["Free gifts per day"] = (s, v) => s.freeGiftsPerDay = I(v),
                ["Coins per piece of trash cleaned"] = (s, v) => s.trashCoins = I(v),
                ["Surprise visitor appearances per day"] = (s, v) => s.surpriseVisitsPerDay = I(v),
                ["Surprise visitor reward choices"] = (s, v) => s.surpriseChoices = I(v),
                ["Default grow time"] = (s, v) => s.defaultGrowHours = F(v),
                ["One watering lasts"] = (s, v) => s.wateringHours = F(v),
                ["Seed cost"] = (s, v) => s.seedCost = I(v),
                ["Coins per use"] = (s, v) => s.funCoinsPerUse = I(v),
                ["Uses before repair"] = (s, v) => s.funUsesBeforeRepair = I(v),
                ["Repair cost"] = (s, v) => s.funRepairCost = I(v),
                ["Sell-back value of furniture"] = (s, v) => s.sellBackFraction = F(v),
                ["Highest restaurant level in Phase 1"] = (s, v) => s.phase1MaxLevel = I(v),
                ["Themed set bonus when all pieces are placed"] = (s, v) => s.themedSetBonus = I(v),
                ["Offline income cap"] = (s, v) => s.offlineCapHours = F(v),
                ["Offline income rate"] = (s, v) => s.offlineRate = F(v),
            };

        public static Result Parse(IReadOnlyDictionary<string, List<string[]>> sheets, string sourceFile)
        {
            var result = new Result();
            var table = new EconomyTable { sourceFile = sourceFile };
            result.Table = table;

            ParseSettings(Sheet(sheets, "Settings", result), table.settings, result);
            table.levels = ReadTable(Sheet(sheets, "Levels", result), "Level", "Levels", result, (r, row) => new EconomyLevel
            {
                level = r.Int(row, "Level"),
                xpToNext = r.Int(row, "XP to next level"),
                totalXp = r.Int(row, "Total XP to reach"),
                coinsReward = r.Int(row, "Coins reward on reaching"),
                menuCategories = r.Int(row, "Menu categories"),
                dishesPerCategory = r.Int(row, "Dishes per category"),
                menuSlots = r.Int(row, "Menu slots"),
                staffSlots = r.Int(row, "Staff slots"),
                gardenPlots = r.Int(row, "Garden plots"),
                inPhase1 = r.Bool(row, "In Phase 1"),
            });
            table.ingredients = ReadTable(Sheet(sheets, "Ingredients", result), "ID", "Ingredients", result, (r, row) => new EconomyIngredient
            {
                id = r.Id(row),
                source = r.Text(row, "Source").ToLowerInvariant(),
                rarity = r.Int(row, "Rarity (stars)"),
                growHours = r.Float(row, "Garden grow time (hours)", optional: true),
                inPhase1 = r.Bool(row, "In Phase 1"),
            });
            table.dishes = ReadTable(Sheet(sheets, "Dishes", result), "ID", "Dishes", result, (r, row) =>
            {
                var ingredients = new List<string>();
                for (int i = 1; r.Has("Ingredient " + i); i++)
                {
                    string ing = r.Text(row, "Ingredient " + i, optional: true);
                    if (ing.Length > 0) ingredients.Add(ing);
                }
                return new EconomyDish
                {
                    id = r.Id(row),
                    cuisine = r.Text(row, "Cuisine"),
                    category = r.Text(row, "Category").ToLowerInvariant(),
                    ingredients = ingredients.ToArray(),
                    basePrice = r.Int(row, "Base price (coins)"),
                    cookSeconds = r.Float(row, "Cook time (s)"),
                    unlockLevel = r.Int(row, "Unlocks at level"),
                    inPhase1 = r.Bool(row, "In Phase 1"),
                };
            });
            table.furniture = ReadTable(Sheet(sheets, "Furniture", result), "ID", "Furniture", result, (r, row) => new EconomyFurniture
            {
                id = r.Id(row),
                category = r.Text(row, "Category").ToLowerInvariant(),
                cost = r.Int(row, "Cost (coins)"),
                ambience = r.Int(row, "Ambience points"),
                unlockLevel = r.Int(row, "Unlocks at level"),
                themedSet = r.Text(row, "Themed set", optional: true),
                inPhase1 = r.Bool(row, "In Phase 1"),
            });
            table.expansions = ReadTable(Sheet(sheets, "Expansions", result), "ID", "Expansions", result, (r, row) =>
            {
                ParseSize(r.Text(row, "Size (tiles)"), out int w, out int h, row, result);
                return new EconomyExpansion
                {
                    id = r.Id(row),
                    type = r.Text(row, "Type").ToLowerInvariant(),
                    width = w,
                    height = h,
                    unlockLevel = r.Int(row, "Unlocks at level"),
                    cost = r.Int(row, "Cost (coins)"),
                    inPhase1 = r.Bool(row, "In Phase 1"),
                };
            });
            table.staffRoles = ReadTable(Sheet(sheets, "Staff", result), "Role", "Staff", result, (r, row) => new EconomyStaffRole
            {
                role = r.Text(row, "Role").ToLowerInvariant(),
                unlockLevel = r.Int(row, "Unlocks at level"),
                inPhase1 = r.Bool(row, "In Phase 1"),
            }, stopAtBlankRow: true);

            result.Errors.AddRange(table.Validate());
            return result;
        }

        // ---------------------------------------------------------------------------------------------------------

        private static List<string[]> Sheet(IReadOnlyDictionary<string, List<string[]>> sheets, string name, Result result)
        {
            if (sheets.TryGetValue(name, out var rows)) return rows;
            result.Errors.Add($"Sheet '{name}' is missing");
            return new List<string[]>();
        }

        private static void ParseSettings(List<string[]> rows, EconomySettings s, Result result)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                string label = Cell(row, 0);
                if (label.Length == 0 || !SettingSetters.TryGetValue(label, out var set)) continue;
                if (!TryNumber(Cell(row, 1), out double value))
                {
                    result.Errors.Add($"Settings: '{label}' has no number in column B");
                    continue;
                }
                set(s, value);
                found.Add(label);
            }
            foreach (var label in SettingSetters.Keys)
                if (!found.Contains(label)) result.Errors.Add($"Settings: row '{label}' not found");

            // Garden plot rows are numbered ("Garden plot 1 at level" ...).
            var plots = new List<int>();
            foreach (var row in rows)
            {
                string label = Cell(row, 0);
                if (label.StartsWith("Garden plot ", StringComparison.OrdinalIgnoreCase) &&
                    label.EndsWith(" at level", StringComparison.OrdinalIgnoreCase) && TryNumber(Cell(row, 1), out double v))
                    plots.Add(I(v));
            }
            if (plots.Count == 0) result.Warnings.Add("Settings: no 'Garden plot N at level' rows");
            s.gardenPlotLevels = plots.ToArray();
        }

        private static T[] ReadTable<T>(List<string[]> rows, string firstHeader, string sheet, Result result,
            Func<Columns, string[], T> read, bool stopAtBlankRow = false)
        {
            int header = rows.FindIndex(r => Cell(r, 0) == firstHeader);
            if (header < 0)
            {
                if (rows.Count > 0) result.Errors.Add($"{sheet}: header row starting with '{firstHeader}' not found");
                return new T[0];
            }
            var columns = new Columns(rows[header], sheet, result);
            var list = new List<T>();
            for (int i = header + 1; i < rows.Count; i++)
            {
                var row = rows[i];
                if (Cell(row, 0).Length == 0)
                {
                    if (stopAtBlankRow) break;
                    continue;
                }
                if (!columns.IsDataRow(row)) break; // notes under the table
                columns.Row = i + 1;
                list.Add(read(columns, row));
            }
            return list.ToArray();
        }

        private static void ParseSize(string text, out int w, out int h, string[] row, Result result)
        {
            w = h = 0;
            var parts = text.ToLowerInvariant().Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out w) && int.TryParse(parts[1].Trim(), out h)) return;
            result.Errors.Add($"Expansions: size '{text}' for '{Cell(row, 0)}' is not 'W x H'");
        }

        /// <summary>Column lookup by header text for one table, with error reporting per row.</summary>
        private sealed class Columns
        {
            private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private readonly string _sheet;
            private readonly Result _result;
            private readonly HashSet<string> _missingReported = new HashSet<string>();
            public int Row;

            public Columns(string[] header, string sheet, Result result)
            {
                _sheet = sheet;
                _result = result;
                for (int c = 0; c < header.Length; c++)
                {
                    string name = Cell(header, c);
                    if (name.Length > 0 && !_index.ContainsKey(name)) _index[name] = c;
                }
            }

            public bool Has(string name) => _index.ContainsKey(name);

            /// <summary>Data rows have a lowercase id or a number in column A; notes are sentences.</summary>
            public bool IsDataRow(string[] row)
            {
                string first = Cell(row, 0);
                return first.IndexOf(' ') < 0 || TryNumber(first, out _);
            }

            public string Id(string[] row)
            {
                string id = Cell(row, 0);
                if (id != id.ToLowerInvariant()) _result.Errors.Add($"{_sheet} row {Row}: id '{id}' must be lowercase");
                return id;
            }

            public string Text(string[] row, string name, bool optional = false)
            {
                if (!_index.TryGetValue(name, out int c))
                {
                    if (!optional && _missingReported.Add(name)) _result.Errors.Add($"{_sheet}: column '{name}' not found");
                    return "";
                }
                string v = Cell(row, c);
                if (v.Length == 0 && !optional) _result.Errors.Add($"{_sheet} row {Row}: '{name}' is empty");
                return v;
            }

            public float Float(string[] row, string name, bool optional = false)
            {
                string text = Text(row, name, optional);
                if (text.Length == 0) return 0;
                if (TryNumber(text, out double v)) return (float)v;
                _result.Errors.Add($"{_sheet} row {Row}: '{name}' = '{text}' is not a number");
                return 0;
            }

            public int Int(string[] row, string name, bool optional = false) => I(Float(row, name, optional));

            public bool Bool(string[] row, string name) => string.Equals(Text(row, name, optional: true), "Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string Cell(string[] row, int c) => row != null && c < row.Length && row[c] != null ? row[c].Trim() : "";

        private static bool TryNumber(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static int I(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        private static float F(double v) => (float)v;

        private static int[] Append(int[] list, int v)
        {
            var copy = new int[(list?.Length ?? 0) + 1];
            list?.CopyTo(copy, 0);
            copy[copy.Length - 1] = v;
            return copy;
        }
    }
}
