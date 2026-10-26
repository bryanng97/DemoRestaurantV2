using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Online;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class ApiErrorPolicyTests
    {
        [TestCase(0, null, ApiAction.Backoff)]
        [TestCase(500, "INTERNAL", ApiAction.Backoff)]
        [TestCase(503, null, ApiAction.Backoff)]
        [TestCase(429, "RATE_LIMITED", ApiAction.RetryAfter)]
        [TestCase(401, "UNAUTHENTICATED", ApiAction.RefreshSession)]
        [TestCase(403, "EMAIL_NOT_VERIFIED", ApiAction.VerifyEmail)]
        [TestCase(403, "OTHER", ApiAction.Fail)]
        [TestCase(404, "NOT_FOUND", ApiAction.NotFound)]
        [TestCase(409, "PLAYER_NOT_INITIALIZED", ApiAction.Bootstrap)]
        [TestCase(409, "LAYOUT_VERSION_CONFLICT", ApiAction.Reload)]
        [TestCase(409, "INSUFFICIENT_FUNDS", ApiAction.Reload)]
        [TestCase(409, "REWARD_NOT_READY", ApiAction.Reload)]
        [TestCase(409, "IDEMPOTENCY_KEY_REUSED", ApiAction.Fail)]
        [TestCase(409, "SELF_VISIT_NOT_ALLOWED", ApiAction.Fail)]
        [TestCase(400, "VALIDATION_ERROR", ApiAction.Fail)]
        [TestCase(413, null, ApiAction.Fail)]
        public void Classify_UsesStatusAndCodeOnly(int status, string code, ApiAction expected)
        {
            Assert.AreEqual(expected, ApiErrorPolicy.Classify(status, code));
        }

        [Test]
        public void Backoff_GrowsAndIsCapped()
        {
            Assert.AreEqual(1, RetryBackoff.DelaySeconds(1, 0.999), 0.01);
            Assert.AreEqual(4, RetryBackoff.DelaySeconds(3, 0.999), 0.01);
            Assert.AreEqual(RetryBackoff.CapSeconds, RetryBackoff.DelaySeconds(30, 0.9999), 0.01);
            Assert.AreEqual(0, RetryBackoff.DelaySeconds(2, 0));
            Assert.IsFalse(RetryBackoff.ShouldRetry(RetryBackoff.MaxAttempts));
        }

        [Test]
        public void RetryAfter_ParsesSecondsOrFallsBack()
        {
            Assert.AreEqual(7, RetryBackoff.ParseRetryAfter("7"));
            Assert.AreEqual(5, RetryBackoff.ParseRetryAfter(null));
            Assert.AreEqual(5, RetryBackoff.ParseRetryAfter("soon"));
        }
    }

    public class PendingOperationTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

        [Test]
        public void SameRequest_ReusesKeyUntilCompleted()
        {
            int n = 0;
            var store = new PendingOperationStore(() => "key" + ++n);

            var a = store.Begin("POST", "/purchases", "{\"itemId\":\"x\"}", Now);
            var b = store.Begin("POST", "/purchases", "{\"itemId\":\"x\"}", Now);
            Assert.AreEqual(a.key, b.key);

            store.Complete(a.key);
            var c = store.Begin("POST", "/purchases", "{\"itemId\":\"x\"}", Now);
            Assert.AreNotEqual(a.key, c.key);
        }

        [Test]
        public void DifferentBody_GetsOwnKey()
        {
            var store = new PendingOperationStore();
            var a = store.Begin("POST", "/purchases", "{\"itemId\":\"x\"}", Now);
            var b = store.Begin("POST", "/purchases", "{\"itemId\":\"y\"}", Now);
            Assert.AreNotEqual(a.key, b.key);
            Assert.AreEqual(2, store.Items.Count);
        }

        [Test]
        public void SurvivesRestart()
        {
            var store = new PendingOperationStore();
            var a = store.Begin("POST", "/rewards/daily/claims", "{}", Now);

            var restored = new PendingOperationStore();
            restored.Restore(store.ToList());

            Assert.AreEqual(a.key, restored.Begin("POST", "/rewards/daily/claims", "{}", Now).key);
        }
    }

    public class ServerLayoutTests
    {
        private static LayoutSnapshot Snapshot(params LayoutSnapshot.FurnitureRecord[] pieces) => new LayoutSnapshot
        {
            width = 10, height = 10, furniture = pieces,
            floors = new string[100]
        };

        private static LayoutSnapshot.FurnitureRecord Piece(string id, int x, int y, Facing f) =>
            new LayoutSnapshot.FurnitureRecord { id = id, x = x, y = y, facing = (int)f };

        [Test]
        public void Rotation_RoundTripsAndMatchesRotatedFootprints()
        {
            foreach (Facing f in Enum.GetValues(typeof(Facing)))
            {
                int r = ServerLayoutMapper.ToRotation(f);
                Assert.IsTrue(ServerLayoutMapper.TryFromRotation(r, out var back));
                Assert.AreEqual(f, back);
                bool swapped = r == 90 || r == 270;
                Assert.AreEqual(f == Facing.SouthEast || f == Facing.NorthWest, swapped);
            }
            Assert.IsFalse(ServerLayoutMapper.TryFromRotation(45, out _));
        }

        [Test]
        public void ToServer_MapsIdsAndKeepsInstanceIdsOfUnmovedPieces()
        {
            var map = new ItemIdMap(new[] { new ItemIdMap.Entry { local = "dining_table", server = "dining_table_01" } });
            int n = 0;
            var first = ServerLayoutMapper.ToServer(Snapshot(Piece("dining_table", 2, 3, Facing.SouthWest)), map,
                "wall_a", null, () => "id" + ++n, out var error);
            Assert.IsNull(error);
            Assert.AreEqual("dining_table_01", first.furniture[0].itemId);

            var second = ServerLayoutMapper.ToServer(Snapshot(
                Piece("dining_table", 2, 3, Facing.SouthWest), Piece("dining_table", 5, 5, Facing.SouthEast)),
                map, null, first, () => "id" + ++n, out error);
            Assert.AreEqual("id1", second.furniture[0].instanceId);
            Assert.AreEqual("id2", second.furniture[1].instanceId);
            Assert.AreEqual(270, second.furniture[1].rotation);
            Assert.AreEqual("wall_a", second.wallId);
        }

        [Test]
        public void ToServer_RejectsRoomsLargerThanServerGrid()
        {
            var big = new LayoutSnapshot { width = 21, height = 10, furniture = new LayoutSnapshot.FurnitureRecord[0] };
            Assert.IsNull(ServerLayoutMapper.ToServer(big, new ItemIdMap(), null, null, null, out var error));
            Assert.AreEqual("ROOM_TOO_LARGE", error);
        }

        [Test]
        public void ToServer_PicksMostCommonFloor()
        {
            var s = Snapshot();
            for (int i = 0; i < 60; i++) s.floors[i] = "wood";
            for (int i = 60; i < 100; i++) s.floors[i] = "tile";
            Assert.AreEqual("wood", ServerLayoutMapper.ToServer(s, new ItemIdMap(), null, null, null, out _).floorId);
        }

        [Test]
        public void ToLocal_SkipsUnknownRotations()
        {
            var server = new ServerLayout { furniture = new[]
            {
                new ServerPlacement { instanceId = "a", itemId = "sink", x = 1, y = 1, rotation = 90 },
                new ServerPlacement { instanceId = "b", itemId = "sink", x = 3, y = 1, rotation = 45 },
            } };
            var skipped = new List<string>();
            var local = ServerLayoutMapper.ToLocalFurniture(server, new ItemIdMap(), skipped);
            Assert.AreEqual(1, local.Count);
            Assert.AreEqual((int)Facing.NorthWest, local[0].facing);
            Assert.AreEqual(1, skipped.Count);
        }

        [Test]
        public void ToLocalOwned_MergesMappedIds()
        {
            var map = new ItemIdMap(new[] { new ItemIdMap.Entry { local = "dining_table", server = "dining_table_01" } });
            var owned = ServerLayoutMapper.ToLocalOwned(new Dictionary<string, int> { { "dining_table_01", 2 }, { "sink", 0 } }, map);
            Assert.AreEqual(2, owned["dining_table"]);
            Assert.IsFalse(owned.ContainsKey("sink"));
        }

        [Test]
        public void ItemMap_RejectsDuplicates()
        {
            Assert.Throws<ContentException>(() => new ItemIdMap(new[]
            {
                new ItemIdMap.Entry { local = "a", server = "x" },
                new ItemIdMap.Entry { local = "b", server = "x" },
            }));
        }

        [Test]
        public void Normalize_IgnoresOrderAndInstanceIdCase()
        {
            var a = new ServerLayout { furniture = new[]
            {
                new ServerPlacement { instanceId = "B", itemId = "t", x = 1 },
                new ServerPlacement { instanceId = "a", itemId = "t", x = 2 },
            } };
            var b = new ServerLayout { furniture = new[]
            {
                new ServerPlacement { instanceId = "a", itemId = "t", x = 2 },
                new ServerPlacement { instanceId = "b", itemId = "t", x = 1 },
            } };
            Assert.AreEqual(ServerLayoutMapper.Normalize(a), ServerLayoutMapper.Normalize(b));
            b.furniture[0].x = 3;
            Assert.AreNotEqual(ServerLayoutMapper.Normalize(a), ServerLayoutMapper.Normalize(b));
        }
    }

    public class LayoutSyncTests
    {
        private static ServerLayout Layout(int x) => new ServerLayout
        {
            furniture = new[] { new ServerPlacement { instanceId = "a", itemId = "t", x = x } }
        };

        [Test]
        public void Save_SendsLoadedVersion_AndAdoptsNewOne()
        {
            var sync = new LayoutSync();
            sync.Loaded(3, Layout(1));
            Assert.AreEqual(3, sync.BeginSave(Layout(2)));
            sync.Saved(4);
            Assert.AreEqual(4, sync.Version);
            Assert.AreEqual(LayoutSyncState.Synced, sync.State);
            Assert.AreEqual(2, sync.Confirmed.furniture[0].x);
        }

        [Test]
        public void LostResponse_AdoptsVersionWhenContentLanded()
        {
            var sync = new LayoutSync();
            sync.Loaded(0, Layout(1));
            sync.BeginSave(Layout(2));
            sync.SaveLost();
            Assert.IsTrue(sync.Recover(1, Layout(2)));
            Assert.AreEqual(1, sync.Version);
            Assert.AreEqual(LayoutSyncState.Synced, sync.State);
        }

        [Test]
        public void LostResponse_WithOtherContent_IsAConflict()
        {
            var sync = new LayoutSync();
            sync.Loaded(0, Layout(1));
            sync.BeginSave(Layout(2));
            sync.SaveLost();
            Assert.IsFalse(sync.Recover(5, Layout(9)));
            Assert.AreEqual(LayoutSyncState.Conflict, sync.State);
            Assert.Throws<InvalidOperationException>(() => sync.BeginSave(Layout(2)));

            sync.KeepMine();
            Assert.AreEqual(5, sync.BeginSave(sync.Pending ?? Layout(2)));
        }

        [Test]
        public void CannotSaveTwiceAtOnce()
        {
            var sync = new LayoutSync();
            sync.Loaded(0, Layout(1));
            sync.BeginSave(Layout(2));
            Assert.Throws<InvalidOperationException>(() => sync.BeginSave(Layout(3)));
        }
    }

    public class OnlineEconomyTests
    {
        [Test]
        public void PurchasePlan_ListsOnlyCopiesBeyondOwned()
        {
            var layout = new ServerLayout { furniture = new[]
            {
                new ServerPlacement { itemId = "table" }, new ServerPlacement { itemId = "table" },
                new ServerPlacement { itemId = "table" }, new ServerPlacement { itemId = "chair" },
            } };
            var buy = PurchasePlan.MissingCopies(layout, new Dictionary<string, int> { { "table", 2 }, { "chair", 4 } });
            CollectionAssert.AreEqual(new[] { "table" }, buy);
        }

        [Test]
        public void OnlineCoins_ServerIsTruth_UnsyncedIsDisplayOnly()
        {
            var coins = new OnlineCoins();
            Assert.IsFalse(coins.CanSpend(1));

            coins.ApplyServer(1000, "2026-10-22T10:00:00.000Z");
            coins.AddUnsynced(200);
            Assert.AreEqual(1000, coins.ServerCoins);
            Assert.AreEqual(200, coins.UnsyncedCoins);
            Assert.IsTrue(coins.CanSpend(1000));
            Assert.IsFalse(coins.CanSpend(1100));
        }

        [Test]
        public void OnlineCoins_IgnoresStaleReplayWallet()
        {
            var coins = new OnlineCoins();
            coins.ApplyServer(900, "2026-10-22T10:05:00.000Z");
            coins.ApplyServer(1000, "2026-10-22T10:00:00.000Z");
            Assert.AreEqual(900, coins.ServerCoins);
        }
    }
}
