using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Economy;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class WalletTests
    {
        [Test]
        public void Prediction_ShowsImmediately_AndConfirmsOnSettle()
        {
            var wallet = new Wallet(100);
            long receipt = wallet.Predict(12);

            Assert.AreEqual(112, wallet.DisplayCoins);
            Assert.AreEqual(100, wallet.ConfirmedCoins);

            wallet.Settle(new SaleResult(receipt, true, 12));

            Assert.AreEqual(112, wallet.ConfirmedCoins);
            Assert.AreEqual(0, wallet.PendingCoins);
        }

        [Test]
        public void ServerAmount_WinsOverPrediction()
        {
            var wallet = new Wallet();
            long delta = 0;
            wallet.Corrected += (_, d) => delta = d;

            long receipt = wallet.Predict(999); // tampered client price
            wallet.Settle(new SaleResult(receipt, true, 12));

            Assert.AreEqual(12, wallet.DisplayCoins);
            Assert.AreEqual(12 - 999, delta);
        }

        [Test]
        public void Rejected_RemovesPrediction()
        {
            var wallet = new Wallet(50);
            long receipt = wallet.Predict(10);

            wallet.Settle(new SaleResult(receipt, false, 0));

            Assert.AreEqual(50, wallet.DisplayCoins);
            Assert.AreEqual(0, wallet.PendingCount);
        }

        [Test]
        public void SettlingTwice_CountsOnce()
        {
            var wallet = new Wallet();
            long receipt = wallet.Predict(10);

            wallet.Settle(new SaleResult(receipt, true, 10));
            wallet.Settle(new SaleResult(receipt, true, 10));

            Assert.AreEqual(10, wallet.ConfirmedCoins);
        }
    }

    public class LocalEconomyServerTests
    {
        private static ContentPack Catalog() => new ContentPack("test",
            new[] { new DishDef("laksa", "dish.laksa", 13, 1, 1) },
            new FurnitureDef[0], new CustomerTypeDef[0]);

        private static List<SaleResult> Collect(LocalEconomyServer server)
        {
            var results = new List<SaleResult>();
            server.SaleSettled += results.Add;
            return results;
        }

        [Test]
        public void AwardsCatalogPrice_NotClaimedPrice_AfterLatency()
        {
            var server = new LocalEconomyServer(Catalog(), latencySeconds: 0.5f);
            var results = Collect(server);

            server.SubmitSale(new SaleReport(1, "laksa", 500));
            server.Tick(0.3f);
            Assert.IsEmpty(results);
            server.Tick(0.3f);

            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].Accepted);
            Assert.AreEqual(13, results[0].AwardedCoins);
        }

        [Test]
        public void RejectsUnknownDish_AndReplayedReceipt()
        {
            var server = new LocalEconomyServer(Catalog(), latencySeconds: 0f);
            var results = Collect(server);

            server.SubmitSale(new SaleReport(1, "caviar", 13));
            server.SubmitSale(new SaleReport(2, "laksa", 13));
            server.SubmitSale(new SaleReport(2, "laksa", 13));
            server.Tick(0.01f);

            Assert.IsFalse(results[0].Accepted);
            Assert.IsTrue(results[1].Accepted);
            Assert.IsFalse(results[2].Accepted, "replayed receipt");
        }
    }

    public class EarningsTrackerTests
    {
        [Test]
        public void Rate_UsesSlidingWindow()
        {
            var tracker = new EarningsTracker(windowSeconds: 60f);
            for (int t = 0; t < 120; t += 10)
                tracker.Record(t, 10);

            // At t=120 only the sales at 70..110 (5 x 10 coins) are inside the last 60 s.
            Assert.AreEqual(50f, tracker.CoinsPerMinute(120f), 0.001f);
            Assert.AreEqual(120, tracker.SessionCoins);
            Assert.AreEqual(12, tracker.CustomersServed);
        }

        [Test]
        public void Rate_BeforeFullWindow_ScalesToElapsedTime()
        {
            var tracker = new EarningsTracker(60f);
            tracker.Record(0f, 10);
            tracker.Record(15f, 10);

            Assert.AreEqual(20f * 60f / 30f, tracker.CoinsPerMinute(30f), 0.001f);
        }

        [Test]
        public void Rate_IsZero_WithNoSales()
        {
            Assert.AreEqual(0f, new EarningsTracker().CoinsPerMinute(100f));
        }
    }
}
