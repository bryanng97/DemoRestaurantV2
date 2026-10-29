using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Online;
using DemoRestaurant.Logic.Profile;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class CatalogControlTests
    {
        private static JsonValue ManagedConfig() => JsonValue.Parse(@"{
          ""rulesetId"":""malaysia"",""version"":3001,""body"":{
            ""catalog"":{""furniture"":[{""id"":""gas_stove"",""kind"":""Stove"",""placement"":""floor"",""category"":""Premium"",""typeId"":""PremiumStove"",""price"":120}],
              ""configuration"":{""schemaVersion"":1,""categories"":[{""code"":""Premium"",""sortOrder"":1,""active"":true},{""code"":""Kitchen"",""sortOrder"":2,""active"":true}]}},
            ""economy"":{""settings"":{""maxDishLevel"":10,""ingredientSetsPerUpgrade"":2,""dishPriceIncreasePerLevel"":0.25,""xpUpgradeBase"":20,""xpUpgradePerLevel"":10},
              ""dishes"":[{""id"":""base"",""name"":""Base recipe"",""ingredients"":[""flour""],""category"":""main"",""basePrice"":25,""cookSeconds"":8,""eatSeconds"":6,""unlockLevel"":2,""enabled"":true},
                {""id"":""premium_recipe"",""assetKey"":""base"",""ingredients"":[""egg""],""category"":""main"",""basePrice"":30,""cookSeconds"":9,""eatSeconds"":7,""unlockLevel"":3,""enabled"":true}]}}}");

        private static ContentPack ManagedPack()
        {
            var flour = new IngredientDef("flour", "flour", 1);
            var egg = new IngredientDef("egg", "egg", 1);
            var dish = new DishDef("base", "base", 10, 4, 5, recipe:new[] {new IngredientAmount(flour,1)}, isStarter:true);
            return new ContentPack("test", new[] {dish}, new[] {new FurnitureDef("gas_stove","stove",FurnitureKind.Stove,1,1,1,0,120)},
                new CustomerTypeDef[0], ingredients:new[] {flour,egg}) {Economy=new EconomyTable()};
        }

        [Test]
        public void ManagedRecipesKeepKnownObjectAndLevelsAndUpdateAuthoritativeSettings()
        {
            var pack=ManagedPack(); var original=pack.GetDish("base");
            var book=new DemoRestaurant.Logic.Progression.RecipeBook(pack.Dishes);
            book.RestoreLevel(original,3,10);
            ServerCatalog.FromConfig(ManagedConfig()).ApplyFurniture(pack);
            book.RefreshCatalog(pack.Dishes);
            Assert.AreSame(original,pack.GetDish("base"));
            Assert.AreEqual(3,book.LevelOf(original));
            Assert.AreEqual(8,original.CookSeconds); Assert.AreEqual(25,original.Price);
            Assert.AreEqual(2,pack.Economy.settings.ingredientSetsPerUpgrade);
            Assert.AreEqual(0.25f,pack.Economy.settings.dishPriceStepPerLevel);
            Assert.AreEqual(2,book.AllDishes.Count);
            Assert.AreEqual("base",pack.GetDish("premium_recipe").AssetKey);
            Assert.AreEqual("Premium",pack.GetFurniture("gas_stove").ShopCategory);
            Assert.AreEqual("PremiumStove",pack.GetFurniture("gas_stove").ConfiguredTypeId);
            Assert.AreEqual("Premium",pack.ShopCategories[0].Code);
        }

        [Test]
        public void CachedManagedConfigIsIdempotentAndRetiredRecipesRemainKnownButCannotTrain()
        {
            var pack=ManagedPack(); var config=ManagedConfig();
            config["body"]["economy"]["dishes"].Items[0].Set("enabled",false);
            var book=new DemoRestaurant.Logic.Progression.RecipeBook(pack.Dishes);
            var catalog=ServerCatalog.FromConfig(JsonValue.Parse(config.ToString()));
            catalog.ApplyFurniture(pack); catalog.ApplyFurniture(pack); book.RefreshCatalog(pack.Dishes);
            Assert.AreEqual(2,pack.Dishes.Count); Assert.AreEqual(2,pack.ShopCategories.Count);
            Assert.IsTrue(book.IsKnown(pack.GetDish("base")));
            Assert.IsFalse(pack.GetDish("base").Enabled);
            Assert.AreEqual(DemoRestaurant.Logic.Progression.RecipeTrainResult.UnknownDish,
                book.Train(pack.GetDish("base"),null,10,10,1));
        }

        private static ContentPack Pack() => new ContentPack("test", new DishDef[0],
            new[] { new FurnitureDef("gas_stove", "stove", FurnitureKind.Stove, 1, 1, 1f, 0f, 120) },
            new CustomerTypeDef[0]);

        private static ServerCatalog Catalog(string records) => ServerCatalog.FromConfig(JsonValue.Parse(
            "{\"rulesetId\":\"malaysia\",\"version\":3001,\"body\":{\"catalog\":{\"furniture\":[" + records + "]}}}"));

        [Test]
        public void PublishedPriceAndAvailabilityUpdateTheExistingDefinition()
        {
            var pack = Pack(); var stove = pack.GetFurniture("gas_stove");
            var layout = new RestaurantLayout(4, 4, new GridPos(0, 1));
            Assert.IsTrue(layout.TryPlace(stove, new GridPos(2, 1), Facing.SouthWest, out var placed));
            Catalog("{\"id\":\"gas_stove\",\"placement\":\"floor\",\"price\":199,\"unlockLevel\":3,\"available\":false}").ApplyFurniture(pack);
            Assert.AreSame(stove, placed.Def);
            Assert.AreEqual(199, placed.Def.Price); Assert.AreEqual(3, placed.Def.UnlockLevel);
            Assert.IsFalse(stove.Available);
            Assert.AreEqual(1, pack.Furniture.Count);
        }

        [Test]
        public void NewFurnitureUsesKnownArtworkAndSurvivesSnapshotRestoreWithoutDuplicateDefinitions()
        {
            var pack = Pack();
            var catalog = Catalog("{\"id\":\"premium_stove\",\"assetKey\":\"gas_stove\",\"kind\":\"Stove\",\"placement\":\"floor\",\"sizeX\":1,\"sizeY\":1,\"price\":250,\"unlockLevel\":2}");
            catalog.ApplyFurniture(pack); catalog.ApplyFurniture(pack);
            Assert.AreEqual(2, pack.Furniture.Count);
            var piece = pack.GetFurniture("premium_stove");
            Assert.AreEqual("gas_stove", piece.AssetKey); Assert.AreEqual(250, piece.Price);
            Assert.AreEqual("catalog.furniture.premium_stove", piece.NameKey);
            var layout = new RestaurantLayout(4, 4, new GridPos(0, 1));
            Assert.IsTrue(layout.TryPlace(piece, new GridPos(2, 1), Facing.SouthWest, out _));
            var restoredPack = Pack(); catalog.ApplyFurniture(restoredPack);
            var restored = new RestaurantLayout(4, 4, new GridPos(0, 1));
            Assert.IsTrue(LayoutSnapshot.Capture(layout).TryRestore(restored, restoredPack));
            Assert.AreEqual("premium_stove", new List<PlacedFurniture>(restored.Furniture)[0].Def.Id);
        }

        [Test]
        public void UnknownArtAndIncompatibleGeometryAreNotInventedByTheClient()
        {
            var pack = Pack();
            Catalog("{\"id\":\"missing\",\"assetKey\":\"absent\",\"kind\":\"Stove\",\"placement\":\"floor\",\"sizeX\":1,\"sizeY\":1}," +
                "{\"id\":\"wrong_size\",\"assetKey\":\"gas_stove\",\"kind\":\"Stove\",\"placement\":\"floor\",\"sizeX\":2,\"sizeY\":1}").ApplyFurniture(pack);
            Assert.AreEqual(1, pack.Furniture.Count);
        }

        [Test]
        public void WithdrawnFurnitureCannotBeBoughtButOwnedCopiesCanStillMove()
        {
            var pack = Pack();
            var authority = new DemoRestaurant.Logic.Economy.LocalEconomyServer(pack, startingCoins: 1000, ownedWidth: 4, ownedHeight: 4);
            Catalog("{\"id\":\"gas_stove\",\"placement\":\"floor\",\"price\":199,\"available\":false}").ApplyFurniture(pack);
            var placed = new Dictionary<string, int> { ["gas_stove"] = 1 };
            Assert.AreEqual(DemoRestaurant.Logic.Economy.LandPurchaseResult.Unavailable,
                authority.CommitLayout(4, 4, placed, _ => true));
            authority.RestoreOwnedFurniture(placed);
            Assert.AreEqual(DemoRestaurant.Logic.Economy.LandPurchaseResult.Success,
                authority.CommitLayout(4, 4, placed, _ => true));
            Assert.AreEqual(1000, authority.Balance);
        }
    }

    public class JsonValueTests
    {
        [Test]
        public void Parse_ReadsNestedValuesAndMissingKeysChain()
        {
            var v = JsonValue.Parse("{\"a\":{\"b\":[1,2.5,\"x\\n\\u00e9\"]},\"t\":true,\"n\":null}");
            Assert.AreEqual(1, v["a"]["b"][0].AsInt());
            Assert.AreEqual(2.5, v["a"]["b"][1].AsDouble());
            Assert.AreEqual("x\né", v["a"]["b"][2].AsString());
            Assert.IsTrue(v["t"].AsBool());
            Assert.IsTrue(v["n"].IsNull);
            Assert.AreEqual(7, v["missing"]["deeper"][3].AsInt(7));
        }

        [Test]
        public void Write_RoundTripsAndKeepsKeyOrder()
        {
            var o = JsonValue.Object().Set("z", 1).Set("a", "q\"uote").Set("list", JsonValue.Array().Add(JsonValue.Of(true)));
            Assert.AreEqual("{\"z\":1,\"a\":\"q\\\"uote\",\"list\":[true]}", o.ToString());
            Assert.AreEqual(o.ToString(), JsonValue.Parse(o.ToString()).ToString());
            Assert.AreEqual("{\"a\":2,\"b\":1}", JsonValue.FromCounts(new Dictionary<string, int> { ["b"] = 1, ["a"] = 2, ["c"] = 0 }).ToString());
        }

        [TestCase("{")]
        [TestCase("[1,]")]
        [TestCase("{\"a\" 1}")]
        [TestCase("tru")]
        [TestCase("\"open")]
        public void Parse_RejectsMalformedText(string text)
        {
            Assert.IsFalse(JsonValue.TryParse(text, out _));
        }
    }

    public class ServerActionLogTests
    {
        private static ServerActionLog Log()
        {
            int n = 0;
            return new ServerActionLog(() => "00000000-0000-4000-8000-" + (++n).ToString("000000000000"),
                () => new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc));
        }

        [Test]
        public void Batch_KeepsOrderKeysAndClientTime()
        {
            var log = Log();
            string sale = log.Record("sale", JsonValue.Object().Set("dishId", "nasi_lemak").Set("recipeLevel", 1).Set("happy", true), coins: 10);
            log.Record("hire", JsonValue.Object().Set("role", "chef"), coins: -200);
            var body = ServerActionLog.BatchBody(log.NextBatch(null));
            var entries = body["entries"];
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(sale, entries[0]["key"].AsString());
            Assert.AreEqual("sale", entries[0]["type"].AsString());
            Assert.AreEqual("nasi_lemak", entries[0]["payload"]["dishId"].AsString());
            Assert.AreEqual("2026-10-26T08:00:00.000Z", entries[0]["clientTime"].AsString());
            Assert.AreEqual(-190, log.PendingCoins);
        }

        [Test]
        public void Apply_DropsAcceptedAndRejected_KeepsRetryForResend()
        {
            var log = Log();
            string a = log.Record("sale", JsonValue.Object(), coins: 10);
            string b = log.Record("sale", JsonValue.Object(), coins: 12);
            string c = log.Record("sale", JsonValue.Object(), coins: 14);
            var first = ServerActionLog.BatchBody(log.NextBatch(null))["entries"][2].ToString();
            var outcome = log.Apply(JsonValue.Parse("[{\"key\":\"" + a + "\",\"outcome\":\"accepted\",\"result\":{\"coins\":410}}," +
                "{\"key\":\"" + b + "\",\"outcome\":\"rejected\",\"reason\":\"SALES_RATE_LIMITED\"}," +
                "{\"key\":\"" + c + "\",\"outcome\":\"retry\",\"reason\":\"TEMPORARILY_UNAVAILABLE\"}]"));
            Assert.AreEqual(1, outcome.Accepted.Count);
            Assert.AreEqual(410, outcome.Results[a]["coins"].AsInt());
            Assert.AreEqual("SALES_RATE_LIMITED", outcome.Rejected[0].Value);
            Assert.AreEqual(1, log.Count);
            Assert.AreEqual(14, log.PendingCoins);
            // The retried entry is resent byte for byte.
            var again = ServerActionLog.BatchBody(log.NextBatch(null))["entries"][0].ToString();
            Assert.AreEqual(first, again);
            Assert.AreEqual(c, JsonValue.Parse(again)["key"].AsString());
        }

        [Test]
        public void Deferred_KeepsOnlyTheNewestUnsentSave_AndBuildsItWhenSent()
        {
            var log = Log();
            log.RecordDeferred("layout", -80);
            log.Record("sale", JsonValue.Object(), coins: 10);
            string second = log.RecordDeferred("layout", -20);
            Assert.AreEqual(2, log.Count);
            Assert.AreEqual("layout", log.Items[1].type, "the newest save follows the actions before it");
            Assert.AreEqual(-100, log.Items[1].coins, "the replaced save's spending carries over");
            int built = 0;
            var batch = log.NextBatch(_ => { built++; return JsonValue.Object().Set("n", built); });
            Assert.AreEqual(1, built);
            Assert.AreEqual(second, batch[1].key);
            // Sent: later edits start a new entry rather than changing what was sent.
            string third = log.RecordDeferred("layout");
            Assert.AreNotEqual(second, third);
            Assert.AreEqual("{\"n\":1}", log.Find(second).payload);
        }

        [Test]
        public void Deferred_ThatCannotBeBuilt_IsDropped()
        {
            var log = Log();
            log.RecordDeferred("exterior");
            Assert.AreEqual(0, log.NextBatch(_ => null).Count);
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void DemandEvents_MergeWhileUnsent()
        {
            var log = Log();
            string k = log.RecordDemandEvent("turned_away", -1);
            Assert.AreEqual(k, log.RecordDemandEvent("angry", -1));
            Assert.AreEqual(2, JsonValue.Parse(log.Items[0].payload)["events"].Count);
            log.NextBatch(null);
            Assert.AreNotEqual(k, log.RecordDemandEvent("gave_up", -1));
            Assert.AreEqual(-3f, log.PendingDemand);
        }

        [Test]
        public void SurvivesRestart()
        {
            var log = Log();
            log.Record("garden", JsonValue.Object().Set("plot", 0).Set("action", "plant"), coins: -300);
            log.NextBatch(null);
            var restored = new ServerActionLog();
            restored.Restore(log.ToList());
            Assert.AreEqual(log.Items[0].key, restored.Items[0].key);
            Assert.IsTrue(restored.Items[0].sent);
            Assert.AreEqual(-300, restored.PendingCoins);
        }
    }

    public class ServerLayoutCodecTests
    {
        private static readonly FurnitureDef Table = new FurnitureDef("dining_table", "t", FurnitureKind.Table, 1, 1, 0.6f, 0f, 80);
        private static readonly FurnitureDef Chair = new FurnitureDef("wooden_chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 20);
        private static readonly FurnitureDef Stove = new FurnitureDef("gas_stove", "s", FurnitureKind.Stove, 1, 1, 1f, 0f, 120);
        private static readonly FurnitureDef Lamp = new FurnitureDef("table_lamp", "l", FurnitureKind.Decor, 1, 1, 0.5f, 0f, 30, stackable: true);
        private static readonly FurnitureDef Proto = new FurnitureDef("ref_secret", "p", FurnitureKind.Decor, 1, 1, 0.5f, 0f, 30);
        private static readonly FurnitureDef Window = new FurnitureDef("window_basic", "w", FurnitureKind.WallDecor, 2, 1, 1f, 0f, 150);

        private static ServerCatalog Catalog() => ServerCatalog.FromConfig(JsonValue.Parse(
            "{\"rulesetId\":\"malaysia\",\"version\":2,\"etag\":\"\\\"malaysia.2\\\"\",\"body\":{\"catalog\":{\"furniture\":[" +
            "{\"id\":\"dining_table\",\"placement\":\"floor\",\"price\":80},{\"id\":\"wooden_chair\",\"placement\":\"floor\",\"price\":20}," +
            "{\"id\":\"gas_stove\",\"placement\":\"floor\",\"price\":120},{\"id\":\"table_lamp\",\"placement\":\"floor\",\"price\":30}," +
            "{\"id\":\"window_basic\",\"placement\":\"wall\",\"price\":150},{\"id\":\"floor_wood\",\"placement\":\"floorTile\",\"price\":0}," +
            "{\"id\":\"floor_oak\",\"placement\":\"floorTile\",\"price\":120},{\"id\":\"terrace_tile\",\"placement\":\"floorTile\",\"price\":0,\"outdoorOnly\":true}," +
            "{\"id\":\"wall_sky\",\"placement\":\"wallpaper\",\"price\":100}]," +
            "\"exterior\":[],\"items\":[]},\"rules\":{\"street\":{\"cells\":[]}}}}"));

        private static readonly ServerIdMap Ids = new ServerIdMap(new[]
        {
            new ServerIdMap.Entry { local = "wood", server = "floor_wood" },
            new ServerIdMap.Entry { local = "classic", server = "wall_sky" },
        });

        private static Func<string> Uuids()
        {
            int n = 0;
            return () => "11111111-1111-4111-8111-" + (++n).ToString("000000000000");
        }

        private static RestaurantLayout Room()
        {
            var layout = new RestaurantLayout(6, 5, new GridPos(0, 3));
            layout.TryPlace(Stove, new GridPos(1, 1), Facing.SouthWest, out _);
            layout.TryPlace(Table, new GridPos(3, 2), Facing.SouthWest, out _);
            layout.TryPlace(Chair, new GridPos(2, 2), Facing.SouthEast, out _);
            layout.TryPlace(Lamp, new GridPos(3, 2), Facing.SouthWest, out _);
            layout.TryHang(Window, WallSide.Right, 2, out _);
            return layout;
        }

        [Test]
        public void Encode_UsesTheClientConventionWithStackingAndWalls()
        {
            var doc = ServerLayoutCodec.Encode(Room(), Catalog(), Ids, new InstanceIdBook(), Uuids());
            Assert.AreEqual(6, doc["room"]["width"].AsInt());
            Assert.AreEqual(0, doc["entrance"]["side"].AsInt());
            Assert.AreEqual(3, doc["entrance"]["index"].AsInt());
            Assert.AreEqual("floor_wood", doc["floor"].AsString(), "unpainted cells are the default floor");
            Assert.IsTrue(doc["terraceFloor"].IsNull);
            var furniture = doc["furniture"];
            Assert.AreEqual(4, furniture.Count);
            var lamp = furniture[3];
            Assert.AreEqual("table_lamp", lamp["id"].AsString());
            Assert.AreEqual("dining_table", furniture[lamp["stackedOn"].AsInt()]["id"].AsString());
            Assert.AreEqual(1, furniture[2]["facing"].AsInt(), "SouthEast is facing 1 (+X)");
            Assert.IsTrue(doc["wallpaperLeft"].IsNull, "no theme default given: bare walls");
            Assert.AreEqual("wall_sky", ServerLayoutCodec.Encode(Room(), Catalog(), Ids, new InstanceIdBook(), Uuids(), null, "classic")["wallpaperRight"].AsString(),
                "unpapered walls are the theme default wallpaper");
            Assert.AreEqual(1, doc["walls"][0]["side"].AsInt());
            Assert.AreEqual(2, doc["walls"][0]["index"].AsInt());
        }

        [Test]
        public void Encode_LeavesOutPiecesTheServerDoesNotSell_AndDefaultsUnknownPaint()
        {
            var layout = Room();
            layout.TryPlace(Proto, new GridPos(5, 4), Facing.SouthWest, out _);
            layout.SetFloor(new GridPos(0, 0), "stone");
            layout.SetFloor(new GridPos(1, 0), "floor_oak");
            var leftOut = new List<string>();
            var doc = ServerLayoutCodec.Encode(layout, Catalog(), Ids, new InstanceIdBook(), Uuids(), leftOut);
            CollectionAssert.AreEqual(new[] { "ref_secret" }, leftOut);
            Assert.AreEqual(4, doc["furniture"].Count);
            Assert.AreEqual(1, doc["floorOverrides"].Count, "the unknown 'stone' becomes the default floor");
            Assert.AreEqual("floor_oak", doc["floorOverrides"][0]["id"].AsString());
        }

        [Test]
        public void InstanceIds_FollowAPieceWhenItMoves()
        {
            var layout = Room();
            var book = new InstanceIdBook();
            var ids = Uuids();
            var first = ServerLayoutCodec.Encode(layout, Catalog(), Ids, book, ids);
            string stoveId = first["furniture"][0]["instanceId"].AsString();
            layout.TryMove(layout.FindAt(new GridPos(1, 1)), new GridPos(5, 0), Facing.SouthWest);
            var second = ServerLayoutCodec.Encode(layout, Catalog(), Ids, book, ids);
            Assert.AreEqual(stoveId, second["furniture"][0]["instanceId"].AsString());

            // After a restart (new piece objects) the id follows the item: same cell first, then any copy.
            var restored = new InstanceIdBook();
            restored.Restore(book.ToList());
            var reloaded = Room();
            reloaded.TryMove(reloaded.FindAt(new GridPos(1, 1)), new GridPos(5, 0), Facing.SouthWest);
            var third = ServerLayoutCodec.Encode(reloaded, Catalog(), Ids, restored, ids);
            Assert.AreEqual(stoveId, third["furniture"][0]["instanceId"].AsString());
        }

        [Test]
        public void SavePayload_BuysOnlyCopiesAndPaintBeyondServerOwnership()
        {
            var layout = Room();
            layout.SetFloor(new GridPos(1, 0), "floor_oak");
            var doc = ServerLayoutCodec.Encode(layout, Catalog(), Ids, new InstanceIdBook(), Uuids());
            var owned = new Dictionary<string, int> { ["gas_stove"] = 1, ["dining_table"] = 2, ["wooden_chair"] = 0, ["window_basic"] = 1 };
            var payload = ServerLayoutCodec.SavePayload(doc, "bd2cd2fe-187f-4ad3-b85b-39767ab556f4", owned, Catalog());
            Assert.AreEqual("bd2cd2fe-187f-4ad3-b85b-39767ab556f4", payload["restaurantId"].AsString());
            Assert.AreEqual("{\"floor_oak\":1,\"table_lamp\":1,\"wooden_chair\":1}", payload["buy"].ToString());
            Assert.AreEqual("{\"dining_table\":1}", payload["storage"].ToString());
            Assert.IsFalse(payload.Has("revision"), "a ledger layout applies on top of the server's current one");
        }

        [Test]
        public void Decode_RestoresAPlayableSnapshot_AndFingerprintsMatch()
        {
            var layout = Room();
            var doc = ServerLayoutCodec.Encode(layout, Catalog(), Ids, new InstanceIdBook(), Uuids());
            var snapshot = ServerLayoutCodec.Decode(doc, Ids, id => id == "wood", id => true);
            var target = new RestaurantLayout(4, 4, new GridPos(0, 1));
            var pack = new ContentPack("t", TestContent.Dishes, new[] { Table, Chair, Stove, Lamp, Window }, new CustomerTypeDef[0],
                new StaffTypeDef[0], TestContent.AllIngredients, TestContent.Rules());
            Assert.IsTrue(snapshot.TryRestore(target, pack));
            Assert.AreEqual(4, target.Furniture.Count);
            Assert.IsNotNull(new List<PlacedFurniture>(target.Furniture).Find(f => f.Def == Lamp)?.StackedOn, "the lamp is back on its table");
            Assert.AreEqual("wood", target.FloorAt(new GridPos(0, 0)));
            var again = ServerLayoutCodec.Encode(target, Catalog(), Ids, new InstanceIdBook(), Uuids());
            Assert.AreEqual(ServerLayoutCodec.Fingerprint(doc), ServerLayoutCodec.Fingerprint(again));
        }

        [Test]
        public void Difference_NamesTheRoomAndThePiecesThatDisagree()
        {
            string server = ServerLayoutCodec.Fingerprint(ServerLayoutCodec.Encode(Room(), Catalog(), Ids, new InstanceIdBook(), Uuids()));
            var grown = new RestaurantLayout(7, 5, new GridPos(0, 3)); // e.g. a room grown by a test-mode level-up
            grown.TryPlace(Stove, new GridPos(1, 1), Facing.SouthWest, out _);
            grown.TryPlace(Table, new GridPos(3, 2), Facing.SouthWest, out _);
            grown.TryPlace(Chair, new GridPos(2, 2), Facing.SouthEast, out _);
            grown.TryPlace(Chair, new GridPos(6, 4), Facing.SouthWest, out _);
            grown.TryHang(Window, WallSide.Right, 2, out _);
            string local = ServerLayoutCodec.Fingerprint(ServerLayoutCodec.Encode(grown, Catalog(), Ids, new InstanceIdBook(), Uuids()));
            Assert.AreEqual("room 7x5 → 6x5, +f:wooden_chair@6,4/0, -f:table_lamp@3,2/0s", ServerLayoutCodec.Difference(local, server));
            Assert.AreEqual("", ServerLayoutCodec.Difference(server, server));
        }
    }

    public class OnlineSessionSyncTests
    {
        private static readonly FurnitureDef Chair = new FurnitureDef("wooden_chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 20);
        private static readonly FurnitureDef Stove = new FurnitureDef("gas_stove", "s", FurnitureKind.Stove, 1, 1, 1f, 0f, 120);
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef", "staff.chef", StaffRole.Chef, 4f);
        private static readonly StaffTypeDef Waiter = new StaffTypeDef("waiter", "staff.waiter", StaffRole.Waiter, 4f);

        private static RestaurantSession Session(long coins = 1000)
        {
            var pack = new ContentPack("test", TestContent.Dishes, new[] { Chair, Stove }, new CustomerTypeDef[0],
                new[] { Chef, Waiter }, TestContent.AllIngredients, TestContent.Rules()).WithEconomy();
            var layout = new RestaurantLayout(8, 8, new GridPos(0, 4));
            layout.TryPlace(Stove, new GridPos(2, 0), Facing.SouthWest, out _);
            layout.TryPlace(Chair, new GridPos(4, 4), Facing.SouthWest, out _);
            return new RestaurantSession(pack, layout, seed: 3, startingCoins: coins);
        }

        private const string Account = "7c0f6a55-3e1b-4a8e-9a43-2a1f0b6c9d10";

        [Test]
        public void AdministrativeWalletAndLevelChangesReconcileAlongsidePendingActions()
        {
            var session = Session();
            session.ServerLog.Record("sale", JsonValue.Object(), coins: 12, xp: 8);
            session.ApplyServerState(State(500, level: 3, xp: 10));
            Assert.AreEqual(512, session.Wallet.ConfirmedCoins, "retain only the queued prediction above the server wallet");
            Assert.AreEqual(1, session.Progress.Level, "do not overwrite pending XP until its action resolves");
            var key = session.ServerLog.Items[0].key;
            var sync = new OnlineSync(session);
            sync.ApplyResponse(JsonValue.Parse("{\"state\":" + State(512, level: 3, xp: 18) +
                ",\"results\":[{\"key\":\"" + key + "\",\"outcome\":\"accepted\"}]}"));
            Assert.AreEqual(512, session.Wallet.ConfirmedCoins);
            Assert.AreEqual(3, session.Progress.Level); Assert.AreEqual(18, session.Progress.Xp);
        }

        private static JsonValue State(long coins, int level = 1, int xp = 0, string staff = "[]", string name = null) => JsonValue.Parse(
            "{" + (name == null ? "" : "\"profile\":{\"id\":\"" + Account + "\",\"displayName\":\"" + name + "\"},") +
            "\"restaurantId\":\"bd2cd2fe-187f-4ad3-b85b-39767ab556f4\",\"coins\":" + coins + ",\"level\":" + level +
            ",\"levelXp\":" + xp + ",\"popularity\":{\"demand\":150},\"recipes\":{\"teh_tarik\":2},\"menu\":{\"drink\":[],\"main\":[],\"starter\":[],\"dessert\":[]}," +
            "\"inventory\":{\"ingredients\":{\"rice\":4},\"items\":{},\"furniture\":{}},\"staff\":" + staff + "}");

        [Test]
        public void Hire_RecordsAKeyedActionWithTheStaffMembersOwnId()
        {
            var s = Session();
            int cost = s.NextHireCost;
            Assert.AreEqual(HireResult.Hired, s.Hire(Chef, new GridPos(3, 3), out var chef));
            var hire = s.ServerLog.Items[s.ServerLog.Count - 1];
            Assert.AreEqual("hire", hire.type);
            var payload = JsonValue.Parse(hire.payload);
            Assert.AreEqual("chef", payload["role"].AsString());
            Assert.AreEqual(chef.ServerId, payload["staffId"].AsString());
            Assert.AreEqual(-cost, hire.coins);
        }

        [Test]
        public void ServerState_IsTheTruth_PlusActionsItHasNotSeen()
        {
            var s = Session();
            s.ServerLog.Record("sale", JsonValue.Object(), coins: 25, xp: 5, demand: 1);
            s.ApplyServerState(State(400));
            Assert.AreEqual(425, s.Wallet.DisplayCoins, "server coins plus the unacknowledged sale");
            Assert.AreEqual(151, s.Demand.Value, 1e-6);
            Assert.AreEqual(1, s.Progress.Level, "XP waits while a sale is unacknowledged");
            Assert.AreEqual(2, s.Recipes.LevelOf(TestContent.TehTarik));
            Assert.AreEqual(4, s.Inventory.Count(TestContent.Rice));
            Assert.AreEqual("bd2cd2fe-187f-4ad3-b85b-39767ab556f4", s.ServerRestaurantId);
        }

        [Test]
        public void Sync_AppliesResultsAndAsksToAdoptWhenStaffDiffer()
        {
            var s = Session();
            var sync = new OnlineSync(s);
            s.Hire(Chef, new GridPos(3, 3), out var chef);
            var request = sync.NextRequest(out var batch);
            Assert.AreEqual(1, batch.Count);
            string key = request["entries"][0]["key"].AsString();
            // The hire was rejected (slots full elsewhere): the server team has no chef.
            var round = sync.ApplyResponse(JsonValue.Parse("{\"state\":" + State(1000) + ",\"results\":[{\"key\":\"" + key +
                "\",\"outcome\":\"rejected\",\"reason\":\"STAFF_SLOT_FULL\"}]}"));
            Assert.AreEqual("STAFF_SLOT_FULL", round.Outcome.Rejected[0].Value);
            Assert.AreEqual("staff", round.AdoptReason);
            var save = s.SaveFromServer(sync.LastState);
            Assert.AreEqual(0, save.staff.Count);
            Assert.AreEqual(1000, save.coins);
        }

        [Test]
        public void Sync_KeepsPlayingWhenTheServerAgrees()
        {
            var s = Session();
            var sync = new OnlineSync(s);
            s.Hire(Chef, new GridPos(3, 3), out var chef);
            var request = sync.NextRequest(out _);
            string key = request["entries"][0]["key"].AsString();
            string staff = "[{\"id\":\"" + chef.ServerId + "\",\"role\":\"chef\",\"owner\":false,\"energy\":90,\"onDuty\":true}]";
            var round = sync.ApplyResponse(JsonValue.Parse("{\"state\":" + State(800, staff: staff) + ",\"results\":[{\"key\":\"" + key +
                "\",\"outcome\":\"accepted\",\"result\":{}}]}"));
            Assert.IsFalse(round.NeedsAdoption);
            Assert.AreEqual(0, s.ServerLog.Count);
            Assert.AreEqual(800, s.Wallet.DisplayCoins);
            Assert.AreEqual(90f, chef.Energy, 1e-3);
        }

        [Test]
        public void ServerProfile_GivesTheAccountsIdAndName_SignOutReturnsToTheGuestId()
        {
            var s = Session();
            string guest = s.Profile.PlayerId;
            s.ApplyServerState(State(1000, name: "Mei Ling"));
            Assert.AreEqual(Account, s.Profile.PlayerId, "the same id on every device");
            Assert.AreEqual("Mei Ling", s.Profile.DisplayName);
            Assert.AreEqual(guest, s.Profile.GuestId);

            var loaded = Session();
            loaded.ApplySave(s.CaptureSave());
            Assert.AreEqual(Account, loaded.Profile.PlayerId, "kept across restarts");
            Assert.AreEqual(guest, loaded.Profile.GuestId);

            loaded.Profile.SetAccount(null);
            Assert.AreEqual(guest, loaded.Profile.PlayerId);
            Assert.AreEqual("Mei Ling", loaded.Profile.DisplayName, "signing out keeps the name");
        }

        [Test]
        public void ServerProfile_KeepsARenameThatIsNotSentYet()
        {
            var s = Session();
            Assert.AreEqual(NameCheck.Ok, s.Rename("Kedai Ali"));
            s.ApplyServerState(State(1000, name: "Chef"));
            Assert.AreEqual("Kedai Ali", s.Profile.DisplayName);
            Assert.AreEqual(Account, s.Profile.PlayerId);
            s.ServerLog.Apply(JsonValue.Parse("[{\"key\":\"" + s.ServerLog.Items[0].key + "\",\"outcome\":\"accepted\",\"result\":{}}]"));
            s.ApplyServerState(State(1000, name: "Kedai Ali"));
            Assert.AreEqual("Kedai Ali", s.Profile.DisplayName);
        }

        [Test]
        public void SaveFromServer_TakesTheAccountsIdAndName()
        {
            var s = Session();
            var save = s.SaveFromServer(State(1000, name: "Mei Ling"));
            Assert.AreEqual(Account, save.accountId);
            Assert.AreEqual("Mei Ling", save.displayName);
            Assert.AreEqual(s.Profile.GuestId, save.playerId);
        }

        [TestCase("a", "a", 500, 3, true, true, AccountChange.SameAccount)]
        [TestCase(null, "a", 0, 1, true, false, AccountChange.AskKeepDevice)]
        [TestCase(null, "a", 0, 1, true, true, AccountChange.AskKeepDevice)]
        [TestCase(null, "a", 0, 1, false, false, AccountChange.LoadAccount)]
        [TestCase(null, "a", 500, 1, true, false, AccountChange.LoadAccount)]
        [TestCase(null, "a", 0, 4, true, false, AccountChange.LoadAccount)]
        [TestCase(null, "a", 500, 3, true, true, AccountChange.AskKeepDeviceLayout)]
        [TestCase("b", "a", 0, 1, true, false, AccountChange.LoadAccount)]
        [TestCase("b", "a", 500, 3, true, true, AccountChange.LoadAccount)]
        public void SignIn_AnAccountWithProgressKeepsItsOwn_UnlessThePlayerKeepsAGuestRenovation(string deviceOwner, string user,
            int xp, int layoutRevision, bool deviceHasPlay, bool deviceHasRenovation, AccountChange expected)
        {
            var state = JsonValue.Parse("{\"xp\":" + xp + ",\"layoutRevision\":" + layoutRevision + "}");
            Assert.AreEqual(expected, OnlineSync.OnSignIn(deviceOwner, user, state, deviceHasPlay, deviceHasRenovation));
        }

        [Test]
        public void KeepDeviceLayout_SendsOnlyTheLayout_AndBuysOnlyWhatTheAccountLacks()
        {
            var s = Session();
            var sync = new OnlineSync(s, newId: Uuids()) { Catalog = Catalog() };
            s.Hire(Chef, new GridPos(3, 3), out _);
            s.RecordLayoutSaved(120);
            Assert.IsTrue(sync.DeviceHasRenovation);
            // The account owns the stove already, and not the chair.
            var account = JsonValue.Parse("{\"restaurantId\":\"bd2cd2fe-187f-4ad3-b85b-39767ab556f4\",\"inventory\":{\"furniture\":{\"gas_stove\":1}}}");
            sync.KeepDeviceLayout(account);
            s.ServerRestaurantId = account["restaurantId"].AsString();
            CollectionAssert.AreEqual(new[] { "layout" }, new List<ServerAction>(s.ServerLog.Items).ConvertAll(a => a.type),
                "the guest hire is dropped");
            var payload = sync.NextRequest(out _)["entries"][0]["payload"];
            Assert.AreEqual("{\"wooden_chair\":1}", payload["buy"].ToString());
        }

        private static ServerCatalog Catalog() => ServerCatalog.FromConfig(JsonValue.Parse(
            "{\"rulesetId\":\"malaysia\",\"version\":2,\"body\":{\"catalog\":{\"furniture\":[" +
            "{\"id\":\"gas_stove\",\"placement\":\"floor\",\"price\":120},{\"id\":\"wooden_chair\",\"placement\":\"floor\",\"price\":20}," +
            "{\"id\":\"floor_wood\",\"placement\":\"floorTile\",\"price\":0}]," +
            "\"exterior\":[],\"items\":[]},\"rules\":{\"street\":{\"cells\":[]}}}}"));

        private static Func<string> Uuids()
        {
            int n = 0;
            return () => "22222222-2222-4222-8222-" + (++n).ToString("000000000000");
        }

        [Test]
        public void ForgetDevice_DropsQueuedPlay_KeepDevice_QueuesTheRestaurant()
        {
            var s = Session();
            var sync = new OnlineSync(s);
            s.ServerRestaurantId = "bd2cd2fe-187f-4ad3-b85b-39767ab556f4";
            s.Hire(Chef, new GridPos(3, 3), out _);
            Assert.IsTrue(sync.DeviceHasPlay);
            sync.ForgetDevice();
            Assert.AreEqual(0, s.ServerLog.Count);
            Assert.IsNull(s.ServerRestaurantId);
            sync.KeepDevice();
            CollectionAssert.AreEqual(new[] { "layout", "menu" }, new List<ServerAction>(s.ServerLog.Items).ConvertAll(a => a.type));
        }

        [Test]
        public void PlacingTheOwnerOnANewGame_IsNotSentAsAnAction()
        {
            var owner = new StaffTypeDef("owner_waiter", "staff.owner", StaffRole.Waiter, 4f, isOwner: true);
            var ownerChef = new StaffTypeDef("owner_chef", "staff.owner", StaffRole.Chef, 4f, isOwner: true);
            var pack = new ContentPack("test", TestContent.Dishes, new[] { Chair, Stove }, new CustomerTypeDef[0],
                new[] { Chef, Waiter, owner, ownerChef }, TestContent.AllIngredients, TestContent.Rules()).WithEconomy();
            var layout = new RestaurantLayout(8, 8, new GridPos(0, 4));
            layout.TryPlace(Stove, new GridPos(2, 0), Facing.SouthWest, out _);
            var s = new RestaurantSession(pack, layout, seed: 3);
            Assert.IsTrue(s.AssignOwner(StaffRole.Waiter, new GridPos(4, 4)));
            Assert.AreEqual(0, s.ServerLog.Count, "the server creates its own owner");
            Assert.IsTrue(s.AssignOwner(StaffRole.Chef, new GridPos(4, 5)));
            Assert.AreEqual("owner_role", s.ServerLog.Items[0].type, "a job change is an action");
        }

        [Test]
        public void SyncSave_RestoresTheLogAndRestaurant()
        {
            var s = Session();
            var sync = new OnlineSync(s);
            s.ServerRestaurantId = "bd2cd2fe-187f-4ad3-b85b-39767ab556f4";
            s.RecordMenuChanged();
            var save = sync.Capture("player");
            var other = Session();
            var restored = new OnlineSync(other);
            restored.Restore(save);
            Assert.AreEqual(s.ServerRestaurantId, other.ServerRestaurantId);
            Assert.AreEqual("menu", other.ServerLog.Items[0].type);
            var body = restored.NextRequest(out _)["entries"][0]["payload"];
            Assert.AreEqual(4, body.Count, "starter, main, dessert and drink lists, no revision");
        }
    }
}
