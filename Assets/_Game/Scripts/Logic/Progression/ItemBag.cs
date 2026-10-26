using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Logic.Progression
{
    /// <summary>The player's consumable items (energy drinks and the like).</summary>
    public sealed class ItemBag
    {
        private readonly Dictionary<ItemDef, int> _counts = new Dictionary<ItemDef, int>();

        public event Action Changed;

        public int Count(ItemDef item) => item != null && _counts.TryGetValue(item, out int n) ? n : 0;

        public void Add(ItemDef item, int count = 1)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (count <= 0) return;
            _counts[item] = Count(item) + count;
            Changed?.Invoke();
        }

        public bool TryUse(ItemDef item)
        {
            if (Count(item) <= 0)
                return false;
            _counts[item]--;
            Changed?.Invoke();
            return true;
        }

        public IEnumerable<KeyValuePair<ItemDef, int>> Contents()
        {
            foreach (var pair in _counts)
                if (pair.Value > 0)
                    yield return pair;
        }

        public void Restore(IEnumerable<KeyValuePair<ItemDef, int>> items)
        {
            _counts.Clear();
            foreach (var pair in items)
                if (pair.Value > 0)
                    _counts[pair.Key] = Count(pair.Key) + pair.Value;
            Changed?.Invoke();
        }
    }
}
