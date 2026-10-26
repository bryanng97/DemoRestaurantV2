using System;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Session
{
    /// <summary>The terrace bought in front of the room from the for-sale sign (the reference game's outside area).</summary>
    public sealed partial class RestaurantSession
    {
        private ILandPurchaseServer LandServer => Economy as ILandPurchaseServer;

        /// <summary>Terrace steps owned (0 = none).</summary>
        public int TerraceStep => LandServer?.OwnedTerraceStep ?? 0;

        /// <summary>The next terrace size for sale, or null when the last one is owned (or none are sold).</summary>
        public TerraceStep NextTerrace
        {
            get
            {
                int next = TerraceStep;
                return next < Rules.TerraceSteps.Count ? Rules.TerraceSteps[next] : null;
            }
        }

        /// <summary>True when the next terrace can be bought at the current level (coins aside).</summary>
        public bool CanBuyNextTerrace => NextTerrace != null && Progress.Level >= NextTerrace.level;

        /// <summary>
        /// Buys the next terrace size. The server checks order, level and price; <paramref name="persist"/> saves the
        /// enlarged layout with the approved balance before the coins are taken. On any failure the room is unchanged.
        /// </summary>
        public LandPurchaseResult BuyNextTerrace(Func<LayoutSnapshot, long, bool> persist)
        {
            var server = LandServer;
            var next = NextTerrace;
            if (server == null || next == null || persist == null) return LandPurchaseResult.Unavailable;
            int oldWidth = Layout.TerraceWidth, oldDepth = Layout.TerraceDepth;
            var result = server.CommitTerrace(TerraceStep + 1, balance =>
            {
                Layout.SetTerrace(next.width, next.depth);
                var snapshot = LayoutSnapshot.Capture(Layout);
                snapshot.SetOwned(server.OwnedFurniture);
                if (persist(snapshot, balance)) return true;
                Layout.SetTerrace(oldWidth, oldDepth);
                return false;
            });
            if (result == LandPurchaseResult.Success) Wallet.SetConfirmed(server.Balance);
            return result;
        }

        /// <summary>Which terrace step a loaded room has (its size matches a step), so the server knows what is owned.</summary>
        private void RestoreTerraceFromLayout()
        {
            var server = LandServer;
            if (server == null || !Layout.HasTerrace) return;
            var steps = Rules.TerraceSteps;
            for (int i = 0; i < steps.Count; i++)
                if (steps[i].width == Layout.TerraceWidth && steps[i].depth == Layout.TerraceDepth)
                    server.RestoreTerraceStep(i + 1);
        }
    }
}
