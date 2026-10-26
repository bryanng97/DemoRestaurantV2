using System;

namespace DemoRestaurant.Logic.Progression
{
    /// <summary>
    /// After paying, some guests use the toilet and then a sink (as in the reference game). With no working toilet
    /// they leave unhappy. Toilets break after their BreakCount uses; a cleaner or the player fixes them.
    /// </summary>
    [Serializable]
    public sealed class ToiletRules
    {
        /// <summary>Restaurant level from which guests need the toilet.</summary>
        public int fromLevel = 8;
        /// <summary>Share of guests who go after their meal (0..1).</summary>
        public float chance = 1f / 3f;
        public float toiletSeconds = 10f;
        public float sinkSeconds = 4f;
        /// <summary>Time a cleaner takes to fix a broken toilet (at work speed 1).</summary>
        public float fixSeconds = 6f;
    }
}
