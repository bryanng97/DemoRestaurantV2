using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Logic.Progression
{
    /// <summary>The player's ingredient stock.</summary>
    public sealed class Inventory
    {
        private readonly Dictionary<IngredientDef, int> _counts = new Dictionary<IngredientDef, int>();

        public event Action Changed;

        public int Count(IngredientDef ingredient) =>
            ingredient != null && _counts.TryGetValue(ingredient, out int n) ? n : 0;

        public int TotalItems
        {
            get
            {
                int total = 0;
                foreach (var n in _counts.Values) total += n;
                return total;
            }
        }

        public void Add(IngredientDef ingredient, int count = 1)
        {
            if (ingredient == null) throw new ArgumentNullException(nameof(ingredient));
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            _counts[ingredient] = Count(ingredient) + count;
            Changed?.Invoke();
        }

        /// <summary>All ingredients with a non-zero count (for saving).</summary>
        public IEnumerable<IngredientAmount> Contents()
        {
            foreach (var pair in _counts)
                if (pair.Value > 0)
                    yield return new IngredientAmount(pair.Key, pair.Value);
        }

        /// <summary>Replaces the whole stock (loading a save).</summary>
        public void Restore(IEnumerable<IngredientAmount> amounts)
        {
            _counts.Clear();
            foreach (var a in amounts)
                _counts[a.Ingredient] = Count(a.Ingredient) + a.Count;
            Changed?.Invoke();
        }

        public void AddAll(IEnumerable<IngredientAmount> amounts)
        {
            foreach (var a in amounts)
                _counts[a.Ingredient] = Count(a.Ingredient) + a.Count;
            Changed?.Invoke();
        }

        public bool Has(IReadOnlyList<IngredientAmount> amounts)
        {
            foreach (var a in amounts)
                if (Count(a.Ingredient) < a.Count)
                    return false;
            return true;
        }

        /// <summary>Removes all the amounts, or nothing if any is short.</summary>
        public bool TryConsume(IReadOnlyList<IngredientAmount> amounts)
        {
            if (!Has(amounts))
                return false;
            foreach (var a in amounts)
                _counts[a.Ingredient] -= a.Count;
            Changed?.Invoke();
            return true;
        }
    }
}
