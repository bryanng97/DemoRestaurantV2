using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Layout
{
    /// <summary>
    /// Chairs face the table they're placed next to (a chair's facing is the way a seated guest looks), so the
    /// player doesn't have to rotate every chair by hand. Away from tables the chosen facing is kept.
    /// </summary>
    public static class SeatFacing
    {
        private static readonly Facing[] Order = { Facing.SouthWest, Facing.SouthEast, Facing.NorthEast, Facing.NorthWest };

        /// <summary>
        /// The facing for a 1-cell chair at <paramref name="cell"/>: <paramref name="current"/> if it already looks
        /// at a table, otherwise the first side with a table, otherwise <paramref name="current"/>.
        /// </summary>
        public static Facing TowardTable(RestaurantLayout layout, GridPos cell, Facing current)
        {
            if (HasTable(layout, cell + FacingUtil.ToDelta(current)))
                return current;
            foreach (var f in Order)
                if (HasTable(layout, cell + FacingUtil.ToDelta(f)))
                    return f;
            return current;
        }

        private static bool HasTable(RestaurantLayout layout, GridPos p)
        {
            var item = layout.FindAt(p);
            return item != null && item.Def.Kind == FurnitureKind.Table;
        }

        /// <summary>The table served from this chair, preferring the direction the chair faces.</summary>
        public static PlacedFurniture TableFor(RestaurantLayout layout, PlacedFurniture chair)
        {
            if (chair == null) return null;
            var front = layout.FindAt(chair.Origin + FacingUtil.ToDelta(chair.Facing));
            if (front != null && front.Def.Kind == FurnitureKind.Table) return front;
            foreach (var facing in Order)
            {
                var item = layout.FindAt(chair.Origin + FacingUtil.ToDelta(facing));
                if (item != null && item.Def.Kind == FurnitureKind.Table) return item;
            }
            return null;
        }

        /// <summary>One dining place per shared table. Lone chairs only count in packs that allow them.</summary>
        public static int DiningCapacity(RestaurantLayout layout, bool allowLoneChairs)
        {
            var tables = new System.Collections.Generic.HashSet<int>();
            int loneChairs = 0;
            foreach (var item in layout.Furniture)
            {
                if (item.Def.Kind != FurnitureKind.Chair) continue;
                var table = TableFor(layout,item);
                if (table != null) tables.Add(table.InstanceId);
                else if (allowLoneChairs) loneChairs++;
            }
            return tables.Count + loneChairs;
        }
    }
}
