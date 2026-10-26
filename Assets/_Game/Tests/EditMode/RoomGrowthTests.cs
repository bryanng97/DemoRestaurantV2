using System;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class RoomGrowthTests
    {
        private static RoomGrowth Growth() => new RoomGrowth(new[]
        {
            new RoomGrowthStep { level = 5, width = 13, height = 12 },
            new RoomGrowthStep { level = 1, width = 12, height = 11 },
            new RoomGrowthStep { level = 10, width = 15, height = 15 },
        });

        [Test]
        public void SizeFollowsTheLevel()
        {
            var g = Growth();
            Assert.AreEqual((12, 11), g.SizeFor(1));
            Assert.AreEqual((12, 11), g.SizeFor(4));
            Assert.AreEqual((13, 12), g.SizeFor(5));
            Assert.AreEqual((15, 15), g.SizeFor(99));
        }

        [Test]
        public void RoomsNeverShrink()
        {
            var g = Growth();
            Assert.AreEqual((21, 21), g.Grow(21, 21, 1), "a big room from an older save stays big");
            Assert.AreEqual((13, 14), g.Grow(12, 14, 5));
        }

        [Test]
        public void SavedSize_MayBeWhatTheLevelAllows_OrWhatItWas()
        {
            var g = Growth();
            Assert.IsTrue(g.Allows(12, 11, 13, 12, 5));
            Assert.IsFalse(g.Allows(12, 11, 13, 12, 4), "not yet");
            Assert.IsTrue(g.Allows(21, 21, 21, 21, 1));
            Assert.IsFalse(g.Allows(12, 11, 30, 30, 10));
        }

        [Test]
        public void StepsThatShrink_AreRejected()
        {
            Assert.Throws<ArgumentException>(() => new RoomGrowth(new[]
            {
                new RoomGrowthStep { level = 1, width = 12, height = 12 },
                new RoomGrowthStep { level = 2, width = 11, height = 13 },
            }));
            Assert.Throws<ArgumentException>(() => new RoomGrowth(new RoomGrowthStep[0]));
        }

        [Test]
        public void Server_SavesTheGrownRoomForFree_ButNotMore()
        {
            var pack = new ContentPack("test", new[] { new DishDef("d", "d", 1, 1, 1, isStarter: true) }, new FurnitureDef[0],
                new[] { new CustomerTypeDef("c", "c", 2, 10, 1) }, rules: new GameRules { RoomGrowth = Growth() });
            int level = 1;
            var server = new LocalEconomyServer(pack, startingCoins: 100, ownedWidth: 12, ownedHeight: 11);
            server.SetLevelSource(() => level);
            Assert.AreEqual(LandPurchaseResult.InvalidSize, server.CommitLayout(13, 12, null, _ => true));
            level = 5;
            Assert.AreEqual(LandPurchaseResult.Success, server.CommitLayout(13, 12, null, _ => true));
            Assert.AreEqual(100, server.Balance, "growing with the level is free");
        }
    }
}
