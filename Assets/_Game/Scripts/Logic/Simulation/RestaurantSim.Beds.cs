using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Simulation
{
    /// <summary>
    /// Beds, as in the reference game: every resting staff member takes a free bed (one each) and sleeps on it; the
    /// bed's <see cref="FurnitureEffects.RestRecoveryMultiplier"/> speeds up their recovery. Without a bed they rest
    /// at the normal rate. Beds go to resting staff in hiring order, the strongest bed first, the same order the
    /// server uses when it settles energy.
    /// </summary>
    public sealed partial class RestaurantSim
    {
        private readonly List<PlacedFurniture> _beds = new List<PlacedFurniture>();

        /// <summary>The recovery multiplier of the bed this staff member sleeps on (1 without one).</summary>
        public static float RestMultiplier(Staff s) =>
            s?.Bed != null ? System.Math.Max(1f, s.Bed.Def.Effects.RestRecoveryMultiplier) : 1f;

        /// <summary>Beds in the order they are handed out: strongest first, then the oldest piece.</summary>
        public static List<PlacedFurniture> BedsInOrder(IEnumerable<PlacedFurniture> furniture, List<PlacedFurniture> into = null)
        {
            into ??= new List<PlacedFurniture>();
            into.Clear();
            foreach (var f in furniture)
                if (f.Def.Kind == FurnitureKind.Bed && f.StackedOn == null) into.Add(f);
            into.Sort((a, b) =>
            {
                int c = b.Def.Effects.RestRecoveryMultiplier.CompareTo(a.Def.Effects.RestRecoveryMultiplier);
                return c != 0 ? c : a.InstanceId.CompareTo(b.InstanceId);
            });
            return into;
        }

        private void AssignBeds()
        {
            BedsInOrder(_layout.Furniture, _beds);
            int next = 0;
            foreach (var s in _staff)
            {
                var bed = s.IsResting && next < _beds.Count ? _beds[next++] : null;
                if (bed == s.Bed) continue;
                if (s.Bed != null) LeaveBed(s);
                if (bed != null) LieDown(s, bed);
            }
        }

        private static void LieDown(Staff s, PlacedFurniture bed)
        {
            s.Bed = bed;
            s.Path.Clear();
            s.PathIndex = 0;
            s.State = StaffState.Idle;
            s.Position = new Vec2(bed.Origin.X + bed.SizeX * 0.5f, bed.Origin.Y + bed.SizeY * 0.5f);
            s.Z = bed.Def.SeatHeight;
            s.Facing = bed.Facing;
        }

        /// <summary>Gets up beside the bed (or at home if that cell is blocked) and goes back to their place.</summary>
        private void LeaveBed(Staff s)
        {
            var bed = s.Bed;
            s.Bed = null;
            s.Z = 0;
            var cell = FrontCell(bed) + FacingUtil.ToDelta(bed.Facing);
            s.Position = Vec2.CellCentre(_layout.Grid.IsWalkable(cell) ? cell : s.Home);
            GoHome(s);
        }
    }
}
