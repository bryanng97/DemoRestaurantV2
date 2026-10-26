using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Economy
{
    public enum LandPurchaseResult { Success, InvalidSize, InsufficientCoins, SaveFailed, Unavailable }

    /// <summary>Offline authority transaction; persist the approved balance and room together before debit.
    /// Production replaces this local adapter with an asynchronous Nakama purchase RPC.</summary>
    public interface ILandPurchaseServer
    {
        long Balance { get; }

        /// <summary>Furniture the player owns (placed or in storage), by furniture id.</summary>
        IReadOnlyDictionary<string, int> OwnedFurniture { get; }

        /// <summary>Loading a save: what the player already owns.</summary>
        void RestoreOwnedFurniture(IReadOnlyDictionary<string, int> owned);

        LandPurchaseResult CommitExpansion(int width, int height, Func<long, bool> persist);

        /// <summary>
        /// Saves a renovation: pays for new land and for every placed piece beyond what is owned (server prices),
        /// then records the new ownership. <paramref name="placed"/> null = furniture unchanged.
        /// </summary>
        LandPurchaseResult CommitLayout(int width, int height, IReadOnlyDictionary<string, int> placed, Func<long, bool> persist);

        /// <summary>Terrace steps bought so far (0 = none).</summary>
        int OwnedTerraceStep { get; }

        /// <summary>Loading a save: the terrace steps already bought.</summary>
        void RestoreTerraceStep(int step);

        /// <summary>
        /// Buys terrace step <paramref name="step"/> (1-based; only the next one, from its level, at the catalog price).
        /// The terrace and the new balance are saved by <paramref name="persist"/> before the coins are taken.
        /// </summary>
        LandPurchaseResult CommitTerrace(int step, Func<long, bool> persist);
    }
}
