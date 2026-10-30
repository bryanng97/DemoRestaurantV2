using System;
using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Game.UI
{
    /// <summary>Player-facing text for a perk's timed effects (Phase 4C), e.g. "+20% work speed · 30 min".</summary>
    public static class PerkText
    {
        public static string Effects(PerkEffects e)
        {
            if (e == null || !e.IsTimed) return "";
            var parts = new List<string>();
            if (e.WorkSpeedPercent > 0) parts.Add(Loc.Format("ui.perk.work_speed", e.WorkSpeedPercent));
            if (e.EnergyDrainPercent > 0) parts.Add(Loc.Format("ui.perk.energy_drain", e.EnergyDrainPercent));
            if (e.XpPercent > 0) parts.Add(Loc.Format("ui.perk.xp", e.XpPercent));
            if (e.PopularityPercent > 0) parts.Add(Loc.Format("ui.perk.popularity", e.PopularityPercent));
            return string.Join(" · ", parts);
        }

        /// <summary>The item's perk with its duration, or "" for a plain energy item.</summary>
        public static string For(ItemDef item) =>
            item != null && item.IsPerk ? Effects(item.Effects) + " · " + Loc.Format("ui.perk.minutes", item.DurationMinutes) : "";

        /// <summary>Time left as "12m" or "45s".</summary>
        public static string Remaining(TimeSpan left) =>
            left.TotalMinutes >= 1 ? Loc.Format("ui.perk.left_minutes", (int)Math.Ceiling(left.TotalMinutes))
                : Loc.Format("ui.perk.left_seconds", Math.Max(0, (int)Math.Ceiling(left.TotalSeconds)));
    }
}
