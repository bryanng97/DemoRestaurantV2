using System.Collections;
using System.Reflection;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace DemoRestaurant.Render.Tests
{
    public sealed class FurnitureOcclusionTests
    {
        private static object Read(object target, string field) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        [TestCase(Facing.SouthWest)]
        [TestCase(Facing.SouthEast)]
        public void MovingBehindTable_OccludesFeetInTheSameRender(Facing tableFacing)
        {
            var root = new GameObject("OcclusionTest");
            var cameraRoot = new GameObject("OcclusionCamera");
            RenderTexture target = null;
            Texture2D pixels = null;
            var oldTarget = RenderTexture.active;
            try
            {
                var content = ContentLoader.Load("malaysia");
                var room = new RestaurantLayout(8, 8, new GridPos(0, 7));
                Assert.That(room.TryPlace(content.Pack.GetFurniture("dining_table"), new GridPos(2, 3), tableFacing, out _), Is.True);
                // Additional objects keep the previous foreground group order strictly above the
                // table's new order, rather than tying it in a two-object scene.
                foreach (var spot in new[] { new GridPos(5, 5), new GridPos(5, 2), new GridPos(1, 0), new GridPos(6, 0) })
                    Assert.That(room.TryPlace(content.Pack.GetFurniture("jar_cabinet"), spot, Facing.SouthWest, out _), Is.True);
                var sim = new RestaurantSim(room, new RestaurantMenu(content.Pack.Dishes, 3), 7);
                var waiter = sim.Hire(new StaffTypeDef("waiter", "w", StaffRole.Waiter, 3), new GridPos(6, 6));
                var view = root.AddComponent<RestaurantView>();
                view.Bind(sim, content, new int[0]);
                // Isolate this fixture from any scene open in the editor.
                foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                var camera = cameraRoot.AddComponent<Camera>(); camera.enabled = false;
                camera.orthographic = true; camera.orthographicSize = 2.5f; camera.aspect = 1;
                camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                var centre = IsoProjection.GridToWorld(2.5f, 3.5f);
                camera.transform.position = new Vector3(centre.x, centre.y + .4f, -10);
                target = new RenderTexture(512, 512, 24); camera.targetTexture = target;
                pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
                var lateUpdate = typeof(RestaurantView).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                // Prime the native group cache with the actor in front of the table.
                lateUpdate.Invoke(view, null); SortingGroup.UpdateAllSortingGroups(); camera.Render();
                typeof(Agent).GetProperty("Position").SetValue(waiter, new Vec2(1.8f, 3.5f));
                lateUpdate.Invoke(view, null);
                var character = ((IDictionary)Read(view, "_characters"))[waiter];
                var characterRoot = (Transform)Read(character, "_root");
                Vector2 feet = IsoProjection.GridToWorld(waiter.Position) + Vector2.up * .1f;
                var point = camera.WorldToViewportPoint(feet);
                int x = Mathf.RoundToInt(point.x * 512), y = Mathf.RoundToInt(point.y * 512);
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(x - 1, y - 1, 3, 3), 0, 0); pixels.Apply();
                var withCharacter = pixels.GetPixels(0, 0, 3, 3);
                // The foreground table must fully cover these foot pixels in the immediate render,
                // without waiting for another frame to refresh Unity's SortingGroup cache.
                characterRoot.gameObject.SetActive(false); SortingGroup.UpdateAllSortingGroups();
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(x - 1, y - 1, 3, 3), 0, 0); pixels.Apply();
                var withoutCharacter = pixels.GetPixels(0, 0, 3, 3);
                for (int i = 0; i < withCharacter.Length; i++)
                {
                    var difference = withCharacter[i] - withoutCharacter[i];
                    Assert.That(Mathf.Abs(difference.r) + Mathf.Abs(difference.g) + Mathf.Abs(difference.b),
                        Is.LessThan(.02f), "Feet drawn over the tabletop after changing depth order.");
                }
            }
            finally
            {
                RenderTexture.active = oldTarget;
                Object.DestroyImmediate(cameraRoot); Object.DestroyImmediate(root);
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }
    }
}
