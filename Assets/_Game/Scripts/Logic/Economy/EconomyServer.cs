using System;

namespace DemoRestaurant.Logic.Economy
{
    /// <summary>What the client claims happened: a customer paid for a dish.</summary>
    public readonly struct SaleReport
    {
        public readonly long ReceiptId;
        public readonly string DishId;
        public readonly int ClaimedCoins;

        public SaleReport(long receiptId, string dishId, int claimedCoins)
        {
            ReceiptId = receiptId;
            DishId = dishId;
            ClaimedCoins = claimedCoins;
        }
    }

    /// <summary>What the server decided. AwardedCoins comes from the server's own price list, never the claim.</summary>
    public readonly struct SaleResult
    {
        public readonly long ReceiptId;
        public readonly bool Accepted;
        public readonly int AwardedCoins;

        public SaleResult(long receiptId, bool accepted, int awardedCoins)
        {
            ReceiptId = receiptId;
            Accepted = accepted;
            AwardedCoins = awardedCoins;
        }
    }

    /// <summary>
    /// The authority that awards coins. Today a local stand-in; later a Nakama RPC. Results arrive
    /// asynchronously through <see cref="SaleSettled"/>, always on the thread that calls the owner's Tick/update.
    /// </summary>
    public interface IEntertainmentEconomyServer
    {
        void SubmitEntertainment(long receiptId, string furnitureId);
    }

    public interface IEconomyServer
    {
        void SubmitSale(SaleReport sale);
        event Action<SaleResult> SaleSettled;
    }
}
