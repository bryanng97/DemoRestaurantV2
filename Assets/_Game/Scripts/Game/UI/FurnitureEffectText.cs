using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Game.UI
{
    /// <summary>Player-facing text for a piece's permanent effects (Phase 3), e.g. on the build status line.</summary>
    public static class FurnitureEffectText
    {
        /// <summary>A short line such as "Cooks in 80% of the time · +5.0 max popularity", or "" for none.</summary>
        public static string For(FurnitureDef def)
        {
            if (def == null || def.Effects.IsNeutral) return "";
            var e = def.Effects;
            var parts = new List<string>();
            if (e.OperationTimePercent != 100) parts.Add(Loc.Format("ui.effects.work_time", e.OperationTimePercent));
            // Popularity is shown to players as points / 10.
            if (e.DemandCapBonus > 0) parts.Add(Loc.Format("ui.effects.popularity_cap", (e.DemandCapBonus / 10f).ToString("0.0")));
            if (e.PatienceSeconds > 0) parts.Add(Loc.Format("ui.effects.patience", e.PatienceSeconds));
            return string.Join(" · ", parts);
        }
    }
}
