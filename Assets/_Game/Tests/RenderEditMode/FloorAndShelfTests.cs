using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Game.StressTest;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Session;
using NUnit.Framework;
using UnityEngine;

namespace DemoRestaurant.Render.Tests
{
    public sealed class FloorAndShelfTests
    {
        [TestCase("prism")]
        [TestCase("pixel")]
        [TestCase("shadow")]
        [TestCase("body")]
        [TestCase("head")]
        [TestCase("bubble")]
        public void Placeholder_RecreatesDestroyedCachedSprite(string kind)
        {
            System.Func<Sprite> create = () => kind switch
            {
                "prism" => PlaceholderArt.Prism(1, 1, 0.6f, Color.magenta),
                "pixel" => PlaceholderArt.Pixel(),
                "shadow" => PlaceholderArt.Shadow(),
                "body" => PlaceholderArt.CharacterBody(),
                "head" => PlaceholderArt.CharacterHead(false, Color.white, Color.black),
                _ => PlaceholderArt.Bubble(),
            };
            var original = create();
            var texture = original.texture;
            Object.DestroyImmediate(original);
            if (texture != Texture2D.whiteTexture) Object.DestroyImmediate(texture);
            Sprite replacement = null;
            try
            {
                replacement = create();
                Assert.That(replacement != null, Is.True, "Destroyed cached sprites must be regenerated.");
                Assert.That(replacement.bounds.size.x, Is.GreaterThan(0f));
                Assert.That(create(), Is.SameAs(replacement), "Live sprites should still be cached.");
            }
            finally
            {
                if (replacement != null)
                {
                    var replacementTexture = replacement.texture;
                    Object.DestroyImmediate(replacement);
                    if (replacementTexture != Texture2D.whiteTexture) Object.DestroyImmediate(replacementTexture);
                }
            }
        }

        private static IEnumerable<TestCaseData> GroundedFurnitureCases()
        {
            foreach (string id in new[] { "jar_cabinet", "gas_stove", "sink", "bread_shelf", "dining_table" })
                foreach (Facing facing in System.Enum.GetValues(typeof(Facing))) yield return new TestCaseData(id, facing);
        }

        [TestCaseSource(nameof(GroundedFurnitureCases))]
        public void IllustratedFurnitureBase_FitsItsBlockedCellsAndPreviewMatchesPlacement(string id, Facing facing)
        {
            var content = ContentLoader.Load("malaysia");
            // This fixture checks the original illustration's base, independently of installed replacement skins.
            content.Theme.skins = System.Array.Empty<string>();
            var room = new RestaurantLayout(8, 8, new GridPos(0, 7));
            room.TryPlace(content.Pack.GetFurniture(id), new GridPos(2, 3), facing, out var item);
            var root = new GameObject("GroundingTest");
            var preview = new GameObject("Preview").AddComponent<SpriteRenderer>();
            try
            {
                var sim = new Logic.Simulation.RestaurantSim(room, new Logic.Menu.RestaurantMenu(content.Pack.Dishes, 3), 7);
                var view = root.AddComponent<RestaurantView>(); view.Bind(sim, content, new int[0]);
                var placed = root.transform.Find(id + "#" + item.InstanceId).GetComponent<SpriteRenderer>();
                view.DrawFurniturePreview(preview, item.Def, item.Origin, facing);
                Assert.That(preview.transform.position, Is.EqualTo(placed.transform.position));
                Assert.That(preview.transform.localScale, Is.EqualTo(placed.transform.localScale));
                Assert.That(preview.flipX, Is.EqualTo(placed.flipX));
                foreach (var corner in FurnitureGrounding.BaseCorners(placed.sprite, content.ArtForFurniture(id), placed.flipX))
                {
                    var ground = IsoProjection.WorldToGrid(placed.transform.TransformPoint(corner));
                    Assert.That(ground.X, Is.InRange(item.Origin.X + .039f, item.Origin.X + item.SizeX - .039f));
                    Assert.That(ground.Y, Is.InRange(item.Origin.Y + .039f, item.Origin.Y + item.SizeY - .039f));
                }
                // Check the PNG's opaque bottom silhouette within the base span, excluding unsupported countertop overhangs.
                var art = content.ArtForFurniture(id);
                var pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    pixels.LoadImage(System.IO.File.ReadAllBytes(Application.dataPath + "/_Game/Resources/" + content.Theme.artFolder + "/" + art.Sprite + ".png"));
                    int examined = 0;
                    float baseTop = Mathf.Max(art.GroundCorners[0].y, art.GroundCorners[2].y) + .02f;
                    for (int x = 0; x < pixels.width; x++)
                    {
                        if ((x + .5f) / pixels.width < art.GroundCorners[0].x ||
                            (x + .5f) / pixels.width > art.GroundCorners[2].x) continue;
                        int y = 0;
                        while (y < pixels.height && pixels.GetPixel(x, y).a < .8f) y++;
                        if (y >= pixels.height || (y + .5f) / pixels.height > baseTop) continue;
                        var local = new Vector2((x + .5f) / pixels.width * placed.sprite.rect.width - placed.sprite.pivot.x,
                            (y + .5f) / pixels.height * placed.sprite.rect.height - placed.sprite.pivot.y) / placed.sprite.pixelsPerUnit;
                        if (placed.flipX) local.x = -local.x;
                        var ground = IsoProjection.WorldToGrid(placed.transform.TransformPoint(local));
                        Assert.That(ground.X, Is.InRange(item.Origin.X, item.Origin.X + item.SizeX), "Base pixel extends into a neighboring tile.");
                        Assert.That(ground.Y, Is.InRange(item.Origin.Y, item.Origin.Y + item.SizeY), "Base pixel extends into a neighboring tile.");
                        examined++;
                    }
                    Assert.That(examined, Is.GreaterThan(pixels.width / 2));
                }
                finally { Object.DestroyImmediate(pixels); }
            }
            finally { Object.DestroyImmediate(preview.gameObject); Object.DestroyImmediate(root); }
        }

        [Test]
        public void CabinetBesideChair_ServesCustomersWithoutEnteringAnyOtherFurniture()
        {
            var content = ContentLoader.Load("malaysia");
            var room = Starter(content);
            var shelf = room.FindAt(new GridPos(0, 3)); room.Remove(shelf);
            var cabinet = room.FindAt(new GridPos(11, 0));
            Assert.That(room.TryMove(cabinet, new GridPos(2, 4), Facing.SouthEast), Is.True);
            Assert.That(new LayoutEditor(room, content.Pack).ValidateForService(), Is.EqualTo(ServiceLayoutIssue.None));
            var session = new RestaurantSession(content.Pack, room, 12345);
            session.Sim.Hire(content.Pack.GetStaffType("chef"), new GridPos(6, 1));
            session.Sim.Hire(content.Pack.GetStaffType("waiter"), new GridPos(9, 3));
            var seats = new Dictionary<Logic.Simulation.Customer, PlacedFurniture>();
            for (int i = 0; i < 10800; i++)
            {
                foreach (var c in session.Sim.Customers) if (c.Seat != null) seats[c] = c.Seat;
                session.Tick(1f / 60);
                foreach (var c in session.Sim.Customers) { seats.TryGetValue(c, out var seat); Check(c.Position, seat); }
                foreach (var s in session.Sim.Staff) Check(s.Position, null);
            }
            Assert.That(session.TotalCustomersServed, Is.GreaterThan(0));
            void Check(Vec2 point, PlacedFurniture ownSeat)
            {
                foreach (var furniture in room.Furniture)
                {
                    if (furniture == ownSeat) continue;
                    float dx = point.X - Mathf.Clamp(point.X, furniture.Origin.X, furniture.Origin.X + furniture.SizeX);
                    float dy = point.Y - Mathf.Clamp(point.Y, furniture.Origin.Y, furniture.Origin.Y + furniture.SizeY);
                    Assert.That(dx * dx + dy * dy, Is.GreaterThanOrEqualTo(.04f - .00001f),
                        $"Actor at {point} intersects {furniture.Def.Id} at {furniture.Origin}.");
                }
            }
        }

        private static RestaurantLayout Starter(LoadedContentPack content)
        {
            var room = new RestaurantLayout(15, 12, new GridPos(content.Theme.entranceX, content.Theme.entranceY));
            typeof(StressTestBootstrap).GetMethod("FurnishShowroom", BindingFlags.Static | BindingFlags.NonPublic) // the full room, with shelves
                .Invoke(null, new object[] { room, content.Pack });
            var queue = (GridPos[])typeof(StressTestBootstrap).GetField("WaitingArea", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            room.SetWaitingArea(queue);
            return room;
        }

        [Test]
        public void StarterRoom_ServesMealsWithoutWalkingThroughShelves()
        {
            var content = ContentLoader.Load("malaysia");
            var room = Starter(content);
            Assert.That(new LayoutEditor(room, content.Pack).ValidateForService(), Is.EqualTo(ServiceLayoutIssue.None));
            var session = new RestaurantSession(content.Pack, room, 12345);
            session.Sim.Hire(content.Pack.GetStaffType("chef"), new GridPos(6, 1));
            session.Sim.Hire(content.Pack.GetStaffType("waiter"), new GridPos(9, 3));
            for (int i = 0; i < 10800; i++)
            {
                session.Tick(1f / 60);
                foreach (var customer in session.Sim.Customers) Check(customer.Position);
                foreach (var staff in session.Sim.Staff) Check(staff.Position);
            }
            Assert.That(session.TotalCustomersServed, Is.GreaterThan(0));
            void Check(Vec2 point)
            {
                foreach (var shelf in room.Furniture)
                {
                    if (shelf.Def.Id != "bread_shelf") continue;
                    const float r = .2f;
                    Assert.That(point.X + r > shelf.Origin.X && point.X - r < shelf.Origin.X + shelf.SizeX &&
                        point.Y + r > shelf.Origin.Y && point.Y - r < shelf.Origin.Y + shelf.SizeY, Is.False);
                }
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void LegacyRoom_MigratesShelfThatCutsOffTheDoorwayWithoutOverlappingFurniture(int version)
        {
            var content = ContentLoader.Load("malaysia");
            // Explicit legacy art fixture: its former 2x1 shelf expanded to 2x2. Today's native shelf stays 2x1.
            var shelf = new FurnitureDef("bread_shelf", "s", FurnitureKind.Decor, 2, 2, 2, 0, 90);
            var divider = new FurnitureDef("divider", "d", FurnitureKind.Decor, 4, 1, 1, 0, 1);
            var pack = new ContentPack("legacy", content.Pack.Dishes,
                content.Pack.Furniture.Where(f=>f.Id!="bread_shelf").Concat(new[]{shelf,divider}).ToArray(), content.Pack.CustomerTypes);
            var target = new RestaurantLayout(6,8,new GridPos(0,7));
            target.TryPlace(content.Pack.GetFurniture("gas_stove"),new GridPos(3,0),Facing.SouthWest,out _);
            target.TryPlace(content.Pack.GetFurniture("dining_table"),new GridPos(3,3),Facing.SouthWest,out _);
            target.TryPlace(content.Pack.GetFurniture("wooden_chair"),new GridPos(3,2),Facing.SouthWest,out _);
            target.TryPlace(divider,new GridPos(2,4),Facing.SouthWest,out _);
            target.TryPlace(shelf,new GridPos(0,3),Facing.SouthWest,out _);
            var old = LayoutSnapshot.Capture(target);
            old.version = version;
            old.hasBalance = true; old.confirmedCoins = 543;
            old.floors[0] = "stone";
            old.SetOwned(new Dictionary<string,int>{{"bread_shelf",3}});
            Assert.That(old.TryRestore(target, pack), Is.True);
            Assert.That(new LayoutEditor(target, pack).ValidateForService(), Is.EqualTo(ServiceLayoutIssue.BlockedAccess));
            Assert.That(LayoutMigration.TryUpdateShelfFootprints(old, target, pack, out var migrated), Is.True);
            Assert.That(migrated.TryRestore(target, pack), Is.True);
            Assert.That(new LayoutEditor(target, pack).ValidateForService(), Is.EqualTo(ServiceLayoutIssue.None));
            Assert.That(target.Furniture.Count, Is.EqualTo(old.furniture.Length));
            Assert.That(target.FindAt(new GridPos(3, 3)).Def.Id, Is.EqualTo("dining_table"));
            Assert.That(target.FloorAt(new GridPos(0, 0)), Is.EqualTo("stone"));
            Assert.That(migrated.confirmedCoins, Is.EqualTo(543));
            Assert.AreEqual(3,migrated.OwnedCounts()["bread_shelf"],"Copies in storage survive footprint migration.");
        }

        [Test]
        public void FlatFloor_UsesSharedCornersAndPaintsWithoutMovingItsSurface()
        {
            var root = new GameObject("FlatFloorTest");
            try
            {
                var content = ContentLoader.Load("malaysia");
                var room = new RestaurantLayout(3, 3, new GridPos(0, 0));
                var floor = root.AddComponent<FlatFloorMesh>();
                floor.Bind(room, content, new ArtLibrary(content.Theme.artFolder), -30000);
                var mesh = root.GetComponent<MeshFilter>().sharedMesh;
                var before = mesh.vertices;
                Assert.That(before.Length, Is.EqualTo(36));
                for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                {
                    int index = (y * 3 + x) * 4;
                    Assert.That((Vector2)before[index], Is.EqualTo(IsoProjection.GridToWorld(x, y)));
                    Assert.That((Vector2)before[index + 2], Is.EqualTo(IsoProjection.GridToWorld(x + 1, y + 1)));
                    Assert.That(before[index].z, Is.Zero);
                    if (x < 2) Assert.That(before[index + 1], Is.EqualTo(before[index + 4]));
                    if (y < 2) Assert.That(before[index + 3], Is.EqualTo(before[index + 12]));
                }
                var woodUv = mesh.uv;
                room.SetFloor(new GridPos(1, 1), "stone"); floor.Refresh();
                CollectionAssert.AreEqual(before, mesh.vertices);
                Assert.That(mesh.uv[16], Is.Not.EqualTo(woodUv[16]));
                room.SetFloor(new GridPos(1, 1), "pattern"); floor.Refresh();
                CollectionAssert.AreEqual(before, mesh.vertices);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(Facing.SouthWest)]
        [TestCase(Facing.SouthEast)]
        public void NativeShelf_BlocksItsDeclaredFootprintInBothRotations(Facing facing)
        {
            var content = ContentLoader.Load("malaysia");
            var shelf = content.Pack.GetFurniture("bread_shelf");
            Assert.That(shelf.SizeX, Is.EqualTo(2)); Assert.That(shelf.SizeY, Is.EqualTo(1));
            var room = new RestaurantLayout(8, 8, new GridPos(0, 7));
            Assert.That(room.TryPlace(shelf, new GridPos(0, 3), facing, out _), Is.True);
            var placed=room.FindAt(new GridPos(0,3));
            var path = new List<GridPos>();
            Assert.That(new Pathfinder(room.Grid).TryFindPath(new GridPos(0, 2), new GridPos(0, 3+placed.SizeY), path), Is.True);
            for(int y=3;y<3+placed.SizeY;y++) for(int x=0;x<placed.SizeX;x++) Assert.IsFalse(room.Grid.IsWalkable(new GridPos(x,y)));
            foreach (var cell in path) Assert.That(room.Grid.IsWalkable(cell), Is.True);
            Assert.That(path.Count, Is.GreaterThan(placed.SizeY+1), "Must detour around the blocked shelf, rather than taking the wall gap.");
        }
    }
}
