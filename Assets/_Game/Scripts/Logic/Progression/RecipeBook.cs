using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Logic.Progression
{
    public enum RecipeTrainResult { Learned, Upgraded, NotEnoughIngredients, LevelTooLow, MaxLevel, UnknownDish }

    /// <summary>
    /// Which dishes the restaurant knows and at what level. Recipes are learned and upgraded from the list
    /// (<see cref="Train"/>); there is no ingredient experimenting.
    /// </summary>
    public sealed class RecipeBook
    {
        private readonly List<DishDef> _all;
        private readonly HashSet<DishDef> _known = new HashSet<DishDef>();
        private readonly List<DishDef> _knownOrdered = new List<DishDef>();
        private readonly Dictionary<DishDef, int> _levels = new Dictionary<DishDef, int>();

        public IReadOnlyList<DishDef> AllDishes => _all;
        public IReadOnlyList<DishDef> Known => _knownOrdered;

        public event Action<DishDef> Discovered;
        public event Action<DishDef, int> Upgraded;
        public int LevelOf(DishDef dish) => dish != null && _levels.TryGetValue(dish, out int level) ? level : 0;

        /// <summary>Restaurant City recipe flow: one catalog recipe set is consumed each time it is learned/upgraded.</summary>
        public RecipeTrainResult Train(DishDef dish, Inventory inventory, int restaurantLevel, int maxLevel, int setsPerUpgrade)
        {
            if (dish == null || !dish.Enabled || !_all.Contains(dish)) return RecipeTrainResult.UnknownDish;
            int level = LevelOf(dish);
            if (level >= maxLevel) return RecipeTrainResult.MaxLevel;
            if (dish.UnlockLevel > restaurantLevel) return RecipeTrainResult.LevelTooLow;
            var amounts = new List<IngredientAmount>();
            foreach (var a in dish.Recipe) amounts.Add(new IngredientAmount(a.Ingredient, checked(a.Count * Math.Max(1, setsPerUpgrade))));
            if (!inventory.TryConsume(amounts)) return RecipeTrainResult.NotEnoughIngredients;
            if (level == 0)
            {
                Learn(dish); Discovered?.Invoke(dish); return RecipeTrainResult.Learned;
            }
            _levels[dish] = level + 1;
            Upgraded?.Invoke(dish, level + 1);
            return RecipeTrainResult.Upgraded;
        }

        public void RestoreLevel(DishDef dish, int level, int maxLevel)
        {
            if (_known.Contains(dish)) _levels[dish] = Math.Max(1, Math.Min(level, Math.Max(1, maxLevel)));
        }

        public RecipeBook(IReadOnlyList<DishDef> allDishes, IEnumerable<DishDef> initiallyKnown = null)
        {
            if (allDishes == null) throw new ArgumentNullException(nameof(allDishes));
            _all = new List<DishDef>(allDishes);
            foreach (var d in initiallyKnown ?? StartersOf(allDishes))
                Learn(d);
        }

        public bool IsKnown(DishDef dish) => _known.Contains(dish);
        public void RefreshCatalog(IReadOnlyList<DishDef> dishes)
        {
            foreach (var dish in dishes) if (!_all.Contains(dish)) _all.Add(dish);
        }

        /// <summary>Marks dishes as known without events (loading a save).</summary>
        public void Restore(IEnumerable<DishDef> known)
        {
            foreach (var d in known)
                Learn(d);
        }

        private void Learn(DishDef dish)
        {
            if (dish != null && _known.Add(dish))
            {
                _knownOrdered.Add(dish);
                _levels[dish] = 1;
            }
        }

        private static IEnumerable<DishDef> StartersOf(IReadOnlyList<DishDef> dishes)
        {
            foreach (var d in dishes)
                if (d.IsStarter)
                    yield return d;
        }
    }
}
