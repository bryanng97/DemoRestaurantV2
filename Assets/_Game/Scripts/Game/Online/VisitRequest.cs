using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Online;
using UnityEngine;

namespace DemoRestaurant.Game.Online
{
    /// <summary>
    /// Phase 5, as in the reference game: tapping a shop on the street opens that restaurant. The scene reloads into
    /// visit mode (the bootstrap reads <see cref="Current"/>): the visited layout, read-only, with no save, sync,
    /// guests or editor. Going home clears it and reloads the player's own restaurant from their saves.
    /// </summary>
    public sealed class VisitRequest
    {
        public StreetRestaurant Restaurant;
        /// <summary>The visited restaurant's layout, already decoded to local ids.</summary>
        public LayoutSnapshot Layout;
        /// <summary>The first-visit ingredient, shown once the visit has loaded (null on later visits).</summary>
        public FirstVisitReward Reward;

        /// <summary>The visit in progress, or null at home. Survives the scene reload; cleared on Play start.</summary>
        public static VisitRequest Current { get; set; }

        // Play mode starts with domain reload off, so a visit would otherwise outlive the previous session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
