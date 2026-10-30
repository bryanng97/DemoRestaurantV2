using System.Collections.Generic;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Content;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public sealed class ExteriorLayoutTests
    {
        private static ExteriorItemDef Def(string id, ExteriorKind kind, float w, float h, int price = 0, int owned = 1) =>
            new ExteriorItemDef { id = id, kind = kind, width = w, height = h, price = price, starterOwned = owned };
        private static ExteriorPiece Piece(int instance, string id, float x, float y) =>
            new ExteriorPiece { instance = instance, itemId = id, x = x, y = y };
        private static ExteriorCatalog Catalog() => new ExteriorCatalog(new[] {
            Def("wall", ExteriorKind.Wall,800,360), Def("roof", ExteriorKind.Roof,860,272),
            Def("door", ExteriorKind.Door,100,180), Def("window", ExteriorKind.Window,100,150,150),
            Def("plant", ExteriorKind.Ground,60,100,250), Def("fancy", ExteriorKind.Window,100,150,300,0),
        }, new[] { Piece(1,"wall",500,620), Piece(2,"roof",500,272), Piece(3,"door",500,620),
            Piece(4,"window",280,560), Piece(5,"plant",150,650) });

        [Test] public void WallPiecesHangAnywhereAboveTheGround_TheDoorStaysOnTheBase()
        {
            // As the reference game: windows, signs and wall decor may overlap and hang over the roof.
            var c = Catalog(); var state = c.Starter();
            Assert.IsTrue(c.Validate(state.pieces));
            Assert.IsTrue(c.CanPlace(state.pieces, Piece(6,"window",300,560)), "overlapping another window");
            Assert.IsTrue(c.CanPlace(state.pieces, Piece(6,"window",500,300)), "over the roof");
            Assert.IsTrue(c.CanPlace(state.pieces, Piece(6,"plant",500,650)), "in front of the door");
            Assert.IsFalse(c.CanPlace(state.pieces, Piece(6,"window",300,640)), "below the ground line");
            Assert.IsFalse(c.CanPlace(state.pieces, Piece(6,"window",20,560)), "off the street stage");
            Assert.IsFalse(c.CanPlace(state.pieces, Piece(3,"door",150,620)), "the door stays on the building");
            Assert.IsFalse(c.CanPlace(state.pieces, Piece(6,"window",float.NaN,500)));
        }
        [Test] public void TheFrontGrowsWithLevel_AndEarlierPiecesStayValid()
        {
            var c = new ExteriorCatalog(new[] {
                Def("wall", ExteriorKind.Wall,800,360), Def("roof", ExteriorKind.Roof,860,272),
                Def("door", ExteriorKind.Door,100,180), Def("window", ExteriorKind.Window,100,150,150),
            }, new[] { Piece(1,"wall",500,620), Piece(2,"roof",500,272), Piece(3,"door",500,620), Piece(4,"window",280,560) },
                new[] { new ExteriorStep { level = 5, width = 900, height = 360 }, new ExteriorStep { level = 10, width = 2000, height = 450 } });
            Assert.AreEqual(800f, c.BoxFor(1).Width);
            Assert.AreEqual(900f, c.BoxFor(7).Width);
            Assert.AreEqual(ExteriorCatalog.MaxWidth, c.BoxFor(10).Width, "never wider than the street stage allows");
            Assert.AreEqual(450f, c.BoxFor(10).Height);
            Assert.AreEqual(ExteriorCatalog.Ground, c.BoxFor(10).Bottom, "it grows upwards from the pavement");
            Assert.AreEqual(0, c.StepFor(1)); Assert.AreEqual(1, c.StepFor(9)); Assert.AreEqual(2, c.StepFor(40), "the building base tier");
            // A front saved at a larger size: an unmoved piece outside today's wall still saves; moving it must fit.
            var big = c.Starter(); big.pieces.Find(p => p.instance == 3).x = 140;
            Assert.IsFalse(c.Validate(big.pieces, 1), "outside the level-1 wall");
            Assert.IsTrue(c.TryQuote(big, big.pieces, 1, out _, out _), "left where it was");
            var moved = big.Copy(); moved.pieces.Find(p => p.instance == 3).x = 135;
            Assert.IsFalse(c.TryQuote(big, moved.pieces, 1, out _, out _), "moved, so it must fit");
            var state = c.Starter();
            var wideDoor = Piece(3, "door", 120, 620);
            Assert.IsFalse(c.CanPlace(state.pieces, wideDoor, 1), "the door outside the level-1 front");
            Assert.IsTrue(c.CanPlace(state.pieces, wideDoor, 10), "inside the grown front");
            var wide = Piece(6, "window", 120, 560);
            Assert.IsTrue(c.Validate(state.pieces, 10), "the starter front is still valid at a higher level");
            state.pieces.Add(wide);
            state.owned.Find(o => o.itemId == "window").count = 2;
            Assert.AreEqual(state.pieces.Count, c.Restore(state).pieces.Count, "a front saved at a high level loads again");
        }

        [Test] public void RequiredSlotsDuplicateIdsAndExtraDoorsCannotBeSaved()
        {
            var c = Catalog(); var state = c.Starter();
            state.pieces.Add(Piece(6,"door",720,620)); Assert.IsFalse(c.Validate(state.pieces));
            state = c.Starter(); state.pieces.RemoveAt(2); Assert.IsFalse(c.Validate(state.pieces));
            state = c.Starter(); state.pieces.Add(Piece(4,"window",720,560)); Assert.IsFalse(c.Validate(state.pieces));
        }
        [Test] public void MovingAndStorageAreFreeExtraCopiesCostCatalogPrice()
        {
            var c = Catalog(); var current = c.Starter(); var draft = current.Copy();
            draft.pieces[3].x = 720;
            Assert.IsTrue(c.TryQuote(current,draft.pieces,1,out long cost,out _)); Assert.AreEqual(0,cost);
            draft.pieces.Add(Piece(6,"window",280,560));
            Assert.IsTrue(c.TryQuote(current,draft.pieces,1,out cost,out var owned)); Assert.AreEqual(150,cost);
            Assert.AreEqual(2,owned.Find(o => o.itemId == "window").count);
            draft.pieces.RemoveAt(3);
            Assert.IsTrue(c.TryQuote(current,draft.pieces,1,out cost,out _)); Assert.AreEqual(0,cost);
        }
        private static RestaurantSession Session(long coins = 1000)
        {
            var pack = new ContentPack("test", TestContent.Dishes, new FurnitureDef[0],
                new[] { new CustomerTypeDef("local","c",3,60,1) }, new StaffTypeDef[0],
                TestContent.AllIngredients, TestContent.Rules()).WithEconomy();
            var session = new RestaurantSession(pack,
                new RestaurantLayout(12,10,new GridPos(11,9)),5,coins);
            session.ConfigureExterior(Catalog());
            return session;
        }
        [Test] public void PurchasePersistsOwnershipAndBalanceTogetherWithoutTrustingDraftOwned()
        {
            var s = Session(); var draft = s.CaptureExterior();
            draft.pieces.Add(Piece(6,"window",720,560));
            draft.owned.Add(new ExteriorOwned { itemId = "window", count = 999 });
            Assert.AreEqual(LandPurchaseResult.Success,s.CommitExterior(draft, save => {
                Assert.AreEqual(850,save.coins);
                Assert.AreEqual(2,save.exterior.owned.Find(o => o.itemId == "window").count);
                Assert.AreEqual("ExteriorSaved",save.ledger[save.ledger.Count-1].kind);
                return true;
            }));
            Assert.AreEqual(850,s.Wallet.ConfirmedCoins);
            Assert.AreEqual(6,s.CaptureExterior().pieces.Count);
            var reload = Session(); reload.ApplySave(s.CaptureSave());
            Assert.AreEqual(850,reload.Wallet.ConfirmedCoins);
            Assert.AreEqual(6,reload.CaptureExterior().pieces.Count);
            Assert.AreEqual(LandPurchaseResult.Success,reload.CommitExterior(reload.CaptureExterior(), _ => true));
            Assert.AreEqual(850,reload.Wallet.ConfirmedCoins); // repeated Save never charges twice
        }
        [Test] public void FailedOrUnaffordablePurchaseChangesNothing()
        {
            var s = Session(); var draft = s.CaptureExterior(); draft.pieces.Add(Piece(6,"window",720,560));
            Assert.AreEqual(LandPurchaseResult.SaveFailed,s.CommitExterior(draft,_ => false));
            Assert.AreEqual(1000,s.Wallet.ConfirmedCoins); Assert.AreEqual(5,s.CaptureExterior().pieces.Count);
            var poor = Session(1);
            Assert.AreEqual(LandPurchaseResult.InsufficientCoins,poor.CommitExterior(draft,_ => { Assert.Fail("Must not persist");return true; }));
            Assert.AreEqual(1,poor.Wallet.ConfirmedCoins);
        }
        [Test] public void OldSavesGetStarterFacadeAndSnapshotsDoNotAliasLiveState()
        {
            var s = Session(); var old = s.CaptureSave(); old.version = 4; old.exterior = null;
            s.ApplySave(old); Assert.AreEqual(5,s.CaptureExterior().pieces.Count);
            var draft = s.CaptureExterior(); draft.pieces[3].x = 800;
            Assert.AreEqual(280,s.CaptureExterior().pieces[3].x);
            var catalog = Catalog(); var malformed = catalog.Starter(); malformed.pieces.Add(Piece(6,"window",720,560));
            Assert.AreEqual(5,catalog.Restore(malformed).pieces.Count);
        }
    }
}
