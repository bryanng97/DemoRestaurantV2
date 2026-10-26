using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class FurnitureShopTests
    {
        private static readonly FurnitureDef Table = new FurnitureDef("table", "t", FurnitureKind.Table, 2, 2, 0.75f, 0f, 80);
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.9f, 0.35f, 20);
        private static readonly FurnitureDef Oven = new FurnitureDef("oven", "o", FurnitureKind.Stove, 2, 2, 1f, 0f, 250, unlockLevel: 3);

        private static ContentPack Pack() => new ContentPack("test", TestContent.Dishes, new[] { Table, Chair, Oven },
            new CustomerTypeDef[0], null, TestContent.AllIngredients, TestContent.Rules());

        private static Dictionary<string, int> Counts(params (string id, int n)[] items)
        {
            var d = new Dictionary<string, int>();
            foreach (var (id, n) in items) d[id] = n;
            return d;
        }

        [Test]
        public void OnlyPiecesBeyondWhatIsOwned_AreBought()
        {
            var owned = Counts(("table", 2), ("chair", 8));
            Assert.AreEqual(0, FurnitureOwnership.PurchaseCost(Pack(), owned, Counts(("table", 2), ("chair", 8))));
            Assert.AreEqual(80 + 2 * 20, FurnitureOwnership.PurchaseCost(Pack(), owned, Counts(("table", 3), ("chair", 10))));
            Assert.AreEqual(0, FurnitureOwnership.PurchaseCost(Pack(), owned, Counts(("table", 1))), "fewer placed: the rest is in storage");
            Assert.AreEqual(1, FurnitureOwnership.InStorage(owned, Counts(("table", 1)), "table"));
        }

        [Test]
        public void Server_ChargesItsOwnPrices_AndRemembersOwnership()
        {
            var server = new LocalEconomyServer(Pack(), startingCoins: 200, ownedWidth: 10, ownedHeight: 8);
            server.RestoreOwnedFurniture(Counts(("table", 2), ("chair", 8)));
            long persisted = -1;

            var result = server.CommitLayout(10, 8, Counts(("table", 3), ("chair", 8)), balance => { persisted = balance; return true; });

            Assert.AreEqual(LandPurchaseResult.Success, result);
            Assert.AreEqual(120, server.Balance);
            Assert.AreEqual(120, persisted, "the save is written with the balance after paying");
            Assert.AreEqual(3, server.OwnedFurniture["table"]);

            // Take one out (storage) and put it back: free.
            Assert.AreEqual(LandPurchaseResult.Success, server.CommitLayout(10, 8, Counts(("table", 2), ("chair", 8)), _ => true));
            Assert.AreEqual(LandPurchaseResult.Success, server.CommitLayout(10, 8, Counts(("table", 3), ("chair", 8)), _ => true));
            Assert.AreEqual(120, server.Balance);
        }

        [Test]
        public void Server_RefusesWhatThePlayerCantAfford_AndChangesNothing()
        {
            var server = new LocalEconomyServer(Pack(), startingCoins: 100, ownedWidth: 10, ownedHeight: 8);
            server.RestoreOwnedFurniture(Counts(("table", 1)));
            bool saved = false;

            var result = server.CommitLayout(10, 8, Counts(("table", 1), ("oven", 1)), _ => saved = true);

            Assert.AreEqual(LandPurchaseResult.InsufficientCoins, result);
            Assert.IsFalse(saved);
            Assert.AreEqual(100, server.Balance);
            Assert.IsFalse(server.OwnedFurniture.ContainsKey("oven"));
        }

        [Test]
        public void Ownership_RoundTripsThroughTheLayoutSave()
        {
            var layout = new RestaurantLayout(10, 8, new GridPos(9, 7));
            layout.TryPlace(Table, new GridPos(2, 2), Facing.SouthWest, out _);
            var snapshot = LayoutSnapshot.Capture(layout);
            Assert.IsNull(snapshot.OwnedCounts(), "older saves have no ownership list");

            snapshot.SetOwned(Counts(("table", 2), ("chair", 0)));
            var json = UnityEngineFreeJson.RoundTrip(snapshot);

            Assert.AreEqual(2, json.OwnedCounts()["table"]);
            Assert.IsFalse(json.OwnedCounts().ContainsKey("chair"));
            Assert.AreEqual(1, FurnitureOwnership.CountPlaced(layout)["table"]);
        }

        /// <summary>The logic assembly has no JsonUtility; copying the fields is what a round trip preserves.</summary>
        private static class UnityEngineFreeJson
        {
            public static LayoutSnapshot RoundTrip(LayoutSnapshot s) => new LayoutSnapshot
            {
                width = s.width, height = s.height, furniture = s.furniture, floors = s.floors, owned = s.owned,
            };
        }
    }
}
