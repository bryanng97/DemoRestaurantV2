using System;
using System.Linq;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Online;
using DemoRestaurant.Logic.Profile;
using DemoRestaurant.Logic.Save;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class WardrobeAndTrashTests
    {
        private static ContentPack Pack()
        {
            var rules = TestContent.Rules();
            rules.Trash = new TrashRules { spawnSeconds = 10, maxPieces = 3, cleanSeconds = .2f, xpPerPiece = 1 };
            var pack = new ContentPack("test", TestContent.Dishes, new FurnitureDef[0], new CustomerTypeDef[0],
                null, TestContent.AllIngredients, rules).WithEconomy();
            pack.Avatar = new AvatarCatalog(new[] {
                new AvatarPartDef("none", AvatarSlot.Hat, "none"),
                new AvatarPartDef("cap", AvatarSlot.Hat, "cap", price: 100),
                new AvatarPartDef("crown", AvatarSlot.Hat, "crown", unlockLevel: 5, price: 200),
                new AvatarPartDef("tee", AvatarSlot.Shirt, "tee"),
            });
            return pack;
        }

        private static RestaurantSession Session(ContentPack pack = null, long coins = 1000) =>
            new RestaurantSession(pack ?? Pack(), new RestaurantLayout(8, 8, new GridPos(0, 1)), 7,
                startingCoins: coins) { ArrivalsEnabled = false };

        [Test]
        public void PurchaseUsesCatalogPriceAndIsIdempotent()
        {
            var s = Session();
            var forged = new AvatarPartDef("cap", AvatarSlot.Hat, "fake", price: 1);
            Assert.AreEqual(WardrobeResult.Success, s.BuyAvatarPart(forged));
            Assert.AreEqual(900, s.Wallet.DisplayCoins);
            Assert.IsTrue(s.OwnsAvatarPart(s.Content.Avatar.Find(AvatarSlot.Hat, "cap")));
            Assert.AreEqual(WardrobeResult.AlreadyOwned, s.BuyAvatarPart(forged));
            Assert.AreEqual(900, s.Wallet.DisplayCoins);
            var sent = s.ServerLog.Items.Where(a => a.type.StartsWith("wardrobe")).ToList();
            Assert.AreEqual(1, sent.Count, "one purchase is sent; the server prices it itself");
            Assert.AreEqual(-100, sent[0].coins);
        }

        [Test]
        public void PurchasesRejectLockedUnknownAndUnaffordableItems()
        {
            var s = Session(coins: 99);
            Assert.AreEqual(WardrobeResult.Locked, s.BuyAvatarPart(s.Content.Avatar.Find(AvatarSlot.Hat,"crown")));
            Assert.AreEqual(WardrobeResult.NotEnoughCoins, s.BuyAvatarPart(s.Content.Avatar.Find(AvatarSlot.Hat,"cap")));
            Assert.AreEqual(WardrobeResult.UnknownPart, s.BuyAvatarPart(new AvatarPartDef("fake",AvatarSlot.Hat,"x")));
            Assert.AreEqual(99, s.Wallet.DisplayCoins);
        }

        [Test]
        public void UnownedCannotBeEquippedAndRandomUsesOnlyOwnedParts()
        {
            var s = Session(); var look = s.Profile.Avatar.Clone();
            look.TrySet(s.Content.Avatar,AvatarSlot.Hat,"cap",99);
            s.SaveAvatar(look);
            Assert.AreEqual("none",s.Profile.Avatar[AvatarSlot.Hat]);
            for (int i=0;i<20;i++) Assert.AreEqual("none",s.RandomOwnedAvatar(new Random(i))[AvatarSlot.Hat]);
        }

        [Test]
        public void OutfitOwnershipAppearanceAndTrashSurviveSaveReload()
        {
            var pack = Pack(); var s = Session(pack);
            s.BuyAvatarPart(pack.Avatar.Find(AvatarSlot.Hat,"cap"));
            var look=s.Profile.Avatar.Clone(); look.TrySet(pack.Avatar,AvatarSlot.Hat,"cap",1); s.SaveAvatar(look);
            s.Sim.TickTrash(25);
            var save=s.CaptureSave(); var loaded=Session(pack); loaded.ApplySave(save);
            Assert.IsTrue(loaded.Profile.HasCustomAvatar);
            Assert.AreEqual(look.Key,loaded.Profile.Avatar.Key);
            Assert.IsTrue(loaded.OwnsAvatarPart(pack.Avatar.Find(AvatarSlot.Hat,"cap")));
            Assert.AreEqual(2,loaded.Sim.TrashCount);
            Assert.AreEqual(5,loaded.Sim.TrashSpawnTimer,.001);
            Assert.AreNotSame(save.trash[0],loaded.Sim.Trash[0]);
            Assert.AreEqual(900,loaded.Wallet.DisplayCoins);
        }

        [Test]
        public void LegacySaveKeepsEquippedPaidItemButDoesNotGrantOtherItems()
        {
            var s=Session(); var save=s.CaptureSave(); save.version=6; save.wardrobe=null;
            save.avatar.First(p=>p.slot=="Hat").id="cap";
            var loaded=Session(); loaded.ApplySave(save);
            Assert.IsTrue(loaded.Profile.HasCustomAvatar);
            Assert.IsTrue(loaded.OwnsAvatarPart(loaded.Content.Avatar.Find(AvatarSlot.Hat,"cap")));
            Assert.IsFalse(loaded.OwnsAvatarPart(loaded.Content.Avatar.Find(AvatarSlot.Hat,"crown")));
        }

        [Test]
        public void OnlyUnequippedOwnedItemsCanBeSoldOnce()
        {
            var s=Session(); var cap=s.Content.Avatar.Find(AvatarSlot.Hat,"cap");
            s.BuyAvatarPart(cap);
            var look=s.Profile.Avatar.Clone();look.TrySet(s.Content.Avatar,AvatarSlot.Hat,"cap",1);s.SaveAvatar(look);
            Assert.AreEqual(WardrobeResult.Equipped,s.SellAvatarPart(cap));
            s.SaveAvatar(AvatarLook.Default(s.Content.Avatar));
            Assert.AreEqual(WardrobeResult.Success,s.SellAvatarPart(cap));
            Assert.AreEqual(950,s.Wallet.DisplayCoins);
            Assert.AreEqual(WardrobeResult.NotOwned,s.SellAvatarPart(cap));
            Assert.AreEqual(950,s.Wallet.DisplayCoins);
        }

        [Test]
        public void OnlineAccountWithoutServerWardrobeDoesNotSpendOrAwardCurrency()
        {
            var s=Session();s.Profile.SetAccount("account",null);s.ServerWardrobe=false;
            Assert.AreEqual(WardrobeResult.OnlineUnavailable,s.BuyAvatarPart(s.Content.Avatar.Find(AvatarSlot.Hat,"cap")));
            s.Sim.TickTrash(10);var piece=s.Sim.Trash[0];
            int xp=s.Progress.Xp;int pending=s.ServerLog.Items.Count;
            Assert.IsTrue(s.Sim.CleanTrash(piece.id));
            Assert.AreEqual(1000,s.Wallet.DisplayCoins);Assert.AreEqual(xp,s.Progress.Xp);
            Assert.AreEqual(pending,s.ServerLog.Items.Count);
        }

        [Test]
        public void TradesAndTrashAreSentToTheServer_SignedInOrNot()
        {
            var s=Session();var cap=s.Content.Avatar.Find(AvatarSlot.Hat,"cap");
            Assert.AreEqual(WardrobeResult.Success,s.BuyAvatarPart(cap));
            var buy=s.ServerLog.Items[s.ServerLog.Count-1];
            Assert.AreEqual("wardrobe_buy",buy.type);Assert.AreEqual(-100,buy.coins);
            Assert.AreEqual("{\"slot\":\"hat\",\"id\":\"cap\"}",buy.payload);
            Assert.AreEqual(WardrobeResult.Success,s.SellAvatarPart(cap));
            Assert.AreEqual("wardrobe_sell",s.ServerLog.Items[s.ServerLog.Count-1].type);
            Assert.AreEqual(50,s.ServerLog.Items[s.ServerLog.Count-1].coins);
            s.Sim.TickTrash(10);Assert.IsTrue(s.Sim.CleanTrash(s.Sim.Trash[0].id));
            var clean=s.ServerLog.Items[s.ServerLog.Count-1];
            Assert.AreEqual("trash_clean",clean.type);Assert.AreEqual(1,clean.xp);
        }

        [Test]
        public void TheAccountsWardrobeReplacesTheDevices_AndUnownedClothesComeOff()
        {
            var s=Session();var cap=s.Content.Avatar.Find(AvatarSlot.Hat,"cap");
            s.BuyAvatarPart(cap);var look=s.Profile.Avatar.Clone();look.TrySet(s.Content.Avatar,AvatarSlot.Hat,"cap",1);s.SaveAvatar(look);
            Assert.AreEqual("cap",s.Profile.Avatar[AvatarSlot.Hat]);
            // The purchase never reached the account: signed out, then the account's state arrives.
            s.ServerLog.Restore(new ServerActionList());
            s.ApplyServerState(JsonValue.Parse("{\"wardrobe\":{\"owned\":[],\"look\":null,\"trash\":null}}"));
            Assert.IsFalse(s.OwnsAvatarPart(cap));
            Assert.AreEqual("none",s.Profile.Avatar[AvatarSlot.Hat],"a paid part the account does not own comes off");
            Assert.AreEqual("wardrobe_look",s.ServerLog.Items[0].type,"the device look is offered to an account with none");

            s.ServerLog.Restore(new ServerActionList());
            s.ApplyServerState(JsonValue.Parse("{\"wardrobe\":{\"owned\":[{\"slot\":\"hat\",\"id\":\"cap\"}],\"look\":{\"hat\":\"cap\"}}}"));
            Assert.IsTrue(s.OwnsAvatarPart(cap));Assert.AreEqual("cap",s.Profile.Avatar[AvatarSlot.Hat]);
            Assert.IsTrue(s.Profile.HasCustomAvatar);
        }

        [Test]
        public void ALookIsSentAsTheNewestOnly_WithPartsTheServerSells()
        {
            var s=Session();var sync=new OnlineSync(s){Catalog=ServerCatalog.FromConfig(JsonValue.Parse(
                "{\"rulesetId\":\"malaysia\",\"version\":3,\"body\":{\"catalog\":{\"furniture\":[],\"items\":[],\"wardrobe\":[" +
                "{\"slot\":\"hat\",\"id\":\"none\",\"price\":0,\"unlockLevel\":1,\"available\":true}," +
                "{\"slot\":\"hat\",\"id\":\"cap\",\"price\":100,\"unlockLevel\":1,\"available\":true}]},\"rules\":{}}}"))};
            s.BuyAvatarPart(s.Content.Avatar.Find(AvatarSlot.Hat,"cap"));
            var look=s.Profile.Avatar.Clone();look.TrySet(s.Content.Avatar,AvatarSlot.Hat,"cap",1);s.SaveAvatar(look);s.SaveAvatar(look);
            CollectionAssert.AreEqual(new[]{"wardrobe_buy","wardrobe_look"},s.ServerLog.Items.Select(a=>a.type).ToArray());
            var body=sync.NextRequest(out _)["entries"][1]["payload"];
            Assert.AreEqual("{\"look\":{\"hat\":\"cap\"}}",body.ToString(),"the shirt is not in the server catalog");
        }

        [Test]
        public void TrashSpawnsOnReachableFreeCellsWithoutBlockingAndCapsLongAbsences()
        {
            var s=Session();s.Sim.TickTrash(9);Assert.AreEqual(0,s.Sim.TrashCount);
            s.Sim.TickTrash(1);Assert.AreEqual(1,s.Sim.TrashCount);
            s.Sim.TickTrash(1000000000);Assert.AreEqual(3,s.Sim.TrashCount);
            Assert.AreEqual(3,s.Sim.Trash.Select(t=>t.Cell).Distinct().Count());
            foreach(var piece in s.Sim.Trash) {Assert.IsTrue(s.Layout.Grid.IsWalkable(piece.Cell));Assert.AreNotEqual(s.Layout.Entrance,piece.Cell);}
            var first=s.Sim.Trash[0];s.Sim.CleanTrash(first.id);s.Sim.TickTrash(.01f);
            Assert.AreEqual(2,s.Sim.TrashCount,"No deferred spawn backlog when the cap is reached.");
            Assert.AreEqual(.1f,s.Sim.TrashTurnAwayChance,.001);
        }

        [Test]
        public void ManualCleanupAwardsExactlyOnceAndInvalidIdsNeverPay()
        {
            var s=Session();s.Sim.TickTrash(10);var piece=s.Sim.Trash[0];int count=0;s.TrashCollected+=(_,__,___)=>count++;
            long before=s.Wallet.DisplayCoins;int xp=s.Progress.Xp;
            Assert.IsTrue(s.Sim.CleanTrash(piece.id));Assert.IsFalse(s.Sim.CleanTrash(piece.id));Assert.IsFalse(s.Sim.CleanTrash(-1));
            Assert.AreEqual(before+s.Settings.trashCoins,s.Wallet.DisplayCoins);Assert.AreEqual(xp+1,s.Progress.Xp);Assert.AreEqual(1,count);
        }

        [Test]
        public void RestoreRejectsBlockedDuplicateAndOutOfBoundsTrash()
        {
            var s=Session();s.Layout.Grid.Block(new GridPos(3,3),1,1);
            s.Sim.RestoreTrash(new[]{new TrashPiece{x=3,y=3},new TrashPiece{x=-1,y=0},new TrashPiece{x=1,y=1},new TrashPiece{x=1,y=1}},float.NaN);
            Assert.AreEqual(1,s.Sim.TrashCount);Assert.AreEqual(0,s.Sim.TrashSpawnTimer);
        }

        [Test]
        public void CleanerPicksUpTrashAndManualTakeoverDoesNotDoubleReward()
        {
            var s=Session();s.Sim.TickTrash(20);
            var cleaner=s.Sim.Hire(new StaffTypeDef("cleaner","c",StaffRole.Cleaner,8),new GridPos(2,2));
            s.Sim.Tick(.01f);Assert.IsNotNull(cleaner.TrashTarget);
            long before=s.Wallet.DisplayCoins;var first=cleaner.TrashTarget;
            Assert.IsTrue(s.Sim.CleanTrash(first.id));Assert.IsNull(cleaner.TrashTarget);Assert.IsFalse(cleaner.IsBusy);
            for(int i=0;i<100 && s.Sim.TrashCount>0;i++)s.Sim.Tick(.05f);
            Assert.AreEqual(0,s.Sim.TrashCount);Assert.AreEqual(before+2*s.Settings.trashCoins,s.Wallet.DisplayCoins);
        }

        [Test]
        public void DismissingCleanerReleasesClaimAndRestingCleanerDoesNotPickUp()
        {
            var s=Session();s.Sim.TickTrash(10);
            var type=new StaffTypeDef("cleaner","c",StaffRole.Cleaner,8);
            var first=s.Sim.Hire(type,new GridPos(2,2));s.Sim.Tick(.01f);Assert.IsNotNull(first.TrashTarget);
            s.Sim.Dismiss(first);Assert.IsNull(first.TrashTarget);
            var second=s.Sim.Hire(type,new GridPos(2,2));s.Sim.SetEnergy(second,0,true);s.Sim.Tick(.01f);
            Assert.IsNull(second.TrashTarget);
            s.Sim.ReturnToWork(second);s.Sim.Tick(.01f);Assert.IsNotNull(second.TrashTarget);
        }

        [Test]
        public void LitterCanTurnAwayGuestEvenWhenASeatIsFreeAndCleaningRestoresAdmission()
        {
            var room=new RestaurantLayout(8,8,new GridPos(0,1));
            room.TryPlace(new FurnitureDef("chair","c",FurnitureKind.Chair,1,1,.8f,.4f,1),new GridPos(4,4),Facing.SouthWest,out _);
            room.TryPlace(new FurnitureDef("stove","s",FurnitureKind.Stove,1,1,1,0,1),new GridPos(1,1),Facing.SouthWest,out _);
            var sim=new RestaurantSim(room,new RestaurantMenu(new[]{TestContent.NasiLemak},1),2,
                new SimSettings{Trash=new TrashRules{spawnSeconds=10,maxPieces=1,turnAwayChancePerPiece=1}});
            sim.TickTrash(10);int rejected=0;sim.CustomerTurnedAway+=_=>rejected++;
            var type=new CustomerTypeDef("guest","g",3,60,1);
            sim.SpawnCustomer(type);Assert.AreEqual(1,rejected);
            sim.CleanTrash(sim.Trash[0].id);
            var admitted=sim.SpawnCustomer(type);
            Assert.AreEqual(1,rejected);Assert.AreEqual(CustomerState.WalkingToSeat,admitted.State);
        }

        [Test]
        public void RenovationDropsCoveredLitterWithoutRewardAndReleasesCleaner()
        {
            var s=Session();s.Sim.TickTrash(10);var piece=s.Sim.Trash[0];
            var cleaner=s.Sim.Hire(new StaffTypeDef("cleaner","c",StaffRole.Cleaner,8),new GridPos(2,2));
            s.Sim.Tick(.01f);long coins=s.Wallet.DisplayCoins;
            var decor=new FurnitureDef("cabinet","cabinet",FurnitureKind.Decor,1,1,1,0,1);
            Assert.IsTrue(s.Layout.TryPlace(decor,piece.Cell,Facing.SouthWest,out _));
            s.Sim.Tick(.01f);
            Assert.AreEqual(0,s.Sim.TrashCount);Assert.IsNull(cleaner.TrashTarget);Assert.AreEqual(coins,s.Wallet.DisplayCoins);
        }
    }
}
