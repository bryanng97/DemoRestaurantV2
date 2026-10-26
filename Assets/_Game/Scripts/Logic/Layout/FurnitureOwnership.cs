using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Logic.Layout
{
    /// <summary>
    /// Furniture is owned, not free: a new restaurant owns only its starter pieces. Placing more copies of a piece than
    /// you own buys the difference when the renovation is saved (at the server's catalog price). Pieces taken out of
    /// the room go to storage and can be placed again for free; nothing is sold back.
    /// </summary>
    public static class FurnitureOwnership
    {
        public static Dictionary<string, int> CountPlaced(RestaurantLayout layout)
        {
            var counts = new Dictionary<string, int>();
            foreach (var f in layout.Furniture)
                counts[f.Def.Id] = (counts.TryGetValue(f.Def.Id, out int n) ? n : 0) + 1;
            foreach (var w in layout.WallItems)
                counts[w.Def.Id] = (counts.TryGetValue(w.Def.Id, out int m) ? m : 0) + 1;
            return counts;
        }

        public static int Count(IReadOnlyDictionary<string, int> counts, string id) =>
            counts != null && id != null && counts.TryGetValue(id, out int n) ? n : 0;

        /// <summary>Owned copies not placed in <paramref name="placed"/> (in storage).</summary>
        public static int InStorage(IReadOnlyDictionary<string, int> owned, IReadOnlyDictionary<string, int> placed, string id) =>
            Math.Max(0, Count(owned, id) - Count(placed, id));

        /// <summary>Coins to buy every placed copy beyond what is owned. Unknown ids throw <see cref="ContentException"/>.</summary>
        public static long PurchaseCost(ContentPack catalog, IReadOnlyDictionary<string, int> owned, IReadOnlyDictionary<string, int> placed)
        {
            long cost = 0;
            if (placed == null) return 0;
            foreach (var pair in placed)
            {
                int extra = pair.Value - Count(owned, pair.Key);
                if (extra > 0) cost += (long)extra * catalog.GetFurniture(pair.Key).Price;
            }
            return cost;
        }

        /// <summary>What is owned after paying for <paramref name="placed"/>: the larger count per piece.</summary>
        public static Dictionary<string, int> AfterPurchase(IReadOnlyDictionary<string, int> owned, IReadOnlyDictionary<string, int> placed)
        {
            var result = new Dictionary<string, int>();
            if (owned != null)
                foreach (var pair in owned) if (pair.Value > 0) result[pair.Key] = pair.Value;
            if (placed != null)
                foreach (var pair in placed) result[pair.Key] = Math.Max(Count(result, pair.Key), pair.Value);
            return result;
        }
    }
}
