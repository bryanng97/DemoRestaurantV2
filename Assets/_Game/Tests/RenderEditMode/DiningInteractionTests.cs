using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Controls;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Game.UI;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DemoRestaurant.Render.Tests
{
    public sealed class DiningInteractionTests
    {
        private static object Call(object target, string method, params object[] args) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        private static object Field(object target, string name) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        [UnityTest]
        public IEnumerator WorldTouchAcceptsTap_RejectsDragPinchAndDecorationMode()
        {
            yield return new EnterPlayMode();
            var oldMain = Camera.main; if (oldMain != null) oldMain.tag = "Untagged";
            var oldMode = InputSystem.settings.updateMode;
            var oldFocus = InputSystem.settings.backgroundBehavior;
            var oldEditorFocus = InputSystem.settings.editorInputBehaviorInPlayMode;
            var root = new GameObject("WorldTapTest");
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            try
            {
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                root.tag = "MainCamera"; var camera = root.AddComponent<Camera>(); camera.orthographic = true;
                camera.transform.position = new Vector3(0, 0, -10);
                var handler = root.AddComponent<WallTapHandler>(); handler.enabled = false;
                EnhancedTouchSupport.Enable();
                bool decorating = false; int taps = 0;
                var layout = new RestaurantLayout(8, 8, new GridPos(0, 6));
                handler.Bind(() => layout, ContentLoader.Load("malaysia"), () => decorating, _ => { });
                handler.FloorTap = _ => { taps++; return true; };
                var point = new Vector2(Screen.width * .5f, Screen.height * .6f);
                Assert.IsFalse(UiInput.IsPointerOverUi(point));
                void Touch(int id, UnityEngine.InputSystem.TouchPhase phase, Vector2 position)
                {
                    InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, phase = phase, position = position });
                    InputSystem.Update(); Call(handler, "Update");
                }
                void Flush() { InputSystem.Update(); Call(handler, "Update"); }
                Touch(1, UnityEngine.InputSystem.TouchPhase.Began, point);
                Touch(1, UnityEngine.InputSystem.TouchPhase.Ended, point); Flush();
                Assert.AreEqual(1, taps);
                Touch(2, UnityEngine.InputSystem.TouchPhase.Began, point);
                Touch(2, UnityEngine.InputSystem.TouchPhase.Moved, point + Vector2.right * 60);
                Touch(2, UnityEngine.InputSystem.TouchPhase.Ended, point + Vector2.right * 60); Flush();
                Assert.AreEqual(1, taps, "Camera pans cannot clean or repair a table.");
                Touch(3, UnityEngine.InputSystem.TouchPhase.Began, point);
                Touch(4, UnityEngine.InputSystem.TouchPhase.Began, point + Vector2.right * 60);
                Touch(3, UnityEngine.InputSystem.TouchPhase.Ended, point);
                Touch(4, UnityEngine.InputSystem.TouchPhase.Ended, point + Vector2.right * 60); Flush();
                Assert.AreEqual(1, taps, "Pinch releases cannot trigger world actions.");
                decorating = true;
                Touch(5, UnityEngine.InputSystem.TouchPhase.Began, point);
                Touch(5, UnityEngine.InputSystem.TouchPhase.Ended, point); Flush();
                Assert.AreEqual(1, taps);
                decorating = false;
                Touch(6, UnityEngine.InputSystem.TouchPhase.Began, point);
                decorating = true;
                Touch(6, UnityEngine.InputSystem.TouchPhase.Ended, point); Flush();
                Assert.AreEqual(1, taps, "Opening the editor while pressed cancels the world action.");
            }
            finally
            {
                InputSystem.settings.updateMode = oldMode; InputSystem.settings.backgroundBehavior = oldFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorFocus;
                InputSystem.RemoveDevice(touchscreen); EnhancedTouchSupport.Disable(); UnityEngine.Object.DestroyImmediate(root);
                if (oldMain != null) oldMain.tag = "MainCamera";
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator FloatingButtonsReceiveTouchRelease_AndStoreBothNewAndPlacedPieces()
        {
            yield return new EnterPlayMode();
            var oldMain = Camera.main; if (oldMain != null) oldMain.tag = "Untagged";
            var oldEvents = EventSystem.current;
            var oldMode = InputSystem.settings.updateMode;
            var oldFocus = InputSystem.settings.backgroundBehavior;
            var oldEditorFocus = InputSystem.settings.editorInputBehaviorInPlayMode;
            var root = new GameObject("HandButtonTouchTest");
            var touchscreen = InputSystem.AddDevice<Touchscreen>(); EnhancedTouchSupport.Enable();
            try
            {
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                var content = ContentLoader.Load("malaysia");
                var layout = new RestaurantLayout(15, 12, new GridPos(0, 10));
                layout.TryPlace(content.Pack.GetFurniture("gas_stove"), new GridPos(3, 0), Facing.SouthWest, out _);
                layout.TryPlace(content.Pack.GetFurniture("wooden_chair"), new GridPos(4, 6), Facing.SouthWest, out _);
                var session = new RestaurantSession(content.Pack, layout, 7, startingCoins: 10000);
                var cameraRoot = new GameObject("Camera"); cameraRoot.transform.SetParent(root.transform); cameraRoot.tag = "MainCamera";
                var camera = cameraRoot.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 5;
                camera.transform.position = new Vector3(.75f, -3.375f, -10);
                cameraRoot.AddComponent<CameraController>().enabled = false;
                var view = new GameObject("View").AddComponent<RestaurantView>(); view.transform.SetParent(root.transform);
                view.Bind(session.Sim, content, Array.Empty<int>());
                var ui = new GameObject("UI").AddComponent<GameUi>(); ui.transform.SetParent(root.transform);
                ui.Bind(session, content, new StaffController(session, Array.Empty<GridPos>(), Array.Empty<GridPos>()), new AvatarPainter(content.Pack.Avatar), 60);
                var controller = root.AddComponent<RestaurantEditorController>(); controller.Bind(session, content, view, ui);
                ui.Builder = controller; controller.enabled = false; controller.Begin(BuildCategory.Table);
                yield return null;
                controller.SelectFurniture(content.Pack.GetFurniture("dining_table"));
                var world = camera.WorldToScreenPoint(IsoProjection.GridToWorld(7.5f, 5.5f));
                Call(controller, "FollowPointer", (Vector2)world); Call(controller, "UpdateHandButtons");
                yield return null;
                Canvas.ForceUpdateCanvases();
                var hand = root.transform.Find("RestaurantEditorPanel/BuildCanvas/SafeArea/HandButtons");
                Assert.IsTrue(hand.gameObject.activeInHierarchy);
                var buttons = hand.GetComponentsInChildren<Button>();
                Vector2 Centre(Button button) => RectTransformUtility.WorldToScreenPoint(null,
                    ((RectTransform)button.transform).TransformPoint(((RectTransform)button.transform).rect.center));
                void Touch(int id, UnityEngine.InputSystem.TouchPhase phase, Vector2 point)
                {
                    InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, phase = phase, position = point });
                    InputSystem.Update(); Call(controller, "HandleTouches");
                    Call(controller, "UpdateHandButtons"); EventSystem.current.currentInputModule.Process();
                }
                var before = Field(controller, "_facing");
                var point = Centre(buttons[0]);
                Assert.IsTrue(UiInput.IsPointerOverUi(point), "Rotate's screen centre must hit its UI raycaster. point=" + point + ", screen=" + Screen.width + "x" + Screen.height + ", hand=" + ((RectTransform)hand).anchoredPosition + ", parent=" + ((RectTransform)hand.parent).rect + ", canvas=" + hand.GetComponentInParent<Canvas>().isActiveAndEnabled + ", graphic=" + buttons[0].GetComponent<Image>().raycastTarget);
                Touch(1, UnityEngine.InputSystem.TouchPhase.Began, point);
                Assert.IsTrue((bool)Field(controller, "_touchOnUi"), "Button touch must be UI-owned. Touches=" + UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count + ", touchId=" + Field(controller, "_touchId") + ", device=" + touchscreen.enabled + ", selection=" + controller.HasSelection);
                yield return null; Call(controller, "UpdateHandButtons");
                Assert.IsTrue(hand.gameObject.activeInHierarchy, "Pressing Rotate must not deactivate the pressed UI.");
                Assert.AreEqual(point, Centre(buttons[0]), "The button must stay under the finger while pressed.");
                Touch(1, UnityEngine.InputSystem.TouchPhase.Ended, point);
                Assert.AreNotEqual(before, Field(controller, "_facing"));
                InputSystem.Update(); Call(controller, "HandleTouches"); Call(controller, "UpdateHandButtons");
                point = Centre(buttons[1]);
                Touch(2, UnityEngine.InputSystem.TouchPhase.Began, point); yield return null;
                Touch(2, UnityEngine.InputSystem.TouchPhase.Ended, point);
                Assert.IsFalse(controller.HasSelection, "Store cancels an unpurchased shop preview.");
                Assert.AreEqual(2, controller.Editor.Draft.Furniture.Count);
                InputSystem.Update(); Call(controller, "HandleTouches");
                controller.Editor.Place(content.Pack.GetFurniture("dining_table"), new GridPos(7, 5), Facing.SouthWest);
                Touch(3, UnityEngine.InputSystem.TouchPhase.Began, world);
                Touch(3, UnityEngine.InputSystem.TouchPhase.Ended, world);
                InputSystem.Update(); Call(controller, "HandleTouches"); Call(controller, "UpdateHandButtons");
                Assert.IsTrue(controller.HasSelection);
                point = Centre(buttons[1]);
                Touch(4, UnityEngine.InputSystem.TouchPhase.Began, point); yield return null;
                Touch(4, UnityEngine.InputSystem.TouchPhase.Ended, point);
                Assert.IsFalse(controller.HasSelection);
                Assert.IsNull(controller.Editor.Draft.FindAt(new GridPos(7, 5)), "Store removes a placed piece from the draft.");
            }
            finally
            {
                InputSystem.settings.updateMode = oldMode; InputSystem.settings.backgroundBehavior = oldFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorFocus;
                InputSystem.RemoveDevice(touchscreen); EnhancedTouchSupport.Disable(); UnityEngine.Object.DestroyImmediate(root);
                if (oldEvents == null && EventSystem.current != null) UnityEngine.Object.DestroyImmediate(EventSystem.current.gameObject);
                else EventSystem.current = oldEvents;
                if (oldMain != null) oldMain.tag = "MainCamera";
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator DoorSortsBetweenNeighbourDividers_AndDirtyTableTapStartsVisibleProgress()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("DiningFeedbackTest");
            try
            {
                var content = ContentLoader.Load("malaysia");
                var layout = new RestaurantLayout(10, 10, new GridPos(0, 6));
                var divider = content.Pack.Furniture.FirstOrDefault(f => f.Id == "ref_classic_white_divider") ?? content.Pack.GetFurniture("potted_palm");
                layout.TryPlace(divider, new GridPos(0, 5), Facing.SouthWest, out var behind);
                layout.TryPlace(divider, new GridPos(0, 7), Facing.SouthWest, out var front);
                layout.TryPlace(content.Pack.GetFurniture("gas_stove"), new GridPos(3, 0), Facing.SouthWest, out _);
                layout.TryPlace(content.Pack.GetFurniture("dining_table"), new GridPos(5, 5), Facing.SouthWest, out _);
                layout.TryPlace(content.Pack.GetFurniture("wooden_chair"), new GridPos(5, 6), Facing.NorthEast, out _);
                var sim = new RestaurantSim(layout, new RestaurantMenu(new[] { content.Pack.Dishes[0] }, 1), 9,
                    new SimSettings { CleanSeconds = 10000 });
                sim.Hire(content.Pack.StaffTypes.First(s => s.Role == StaffRole.Chef), new GridPos(3, 1));
                sim.Hire(content.Pack.StaffTypes.First(s => s.Role == StaffRole.Waiter), new GridPos(4, 2));
                var view = root.AddComponent<RestaurantView>(); view.Bind(sim, content, Array.Empty<int>());
                sim.SpawnCustomer(new DemoRestaurant.Logic.Content.CustomerTypeDef("test", "t", 4, 9999, 1, foodPatienceSeconds: 9999));
                for (int i = 0; i < 30000 && sim.DirtySeatCount == 0; i++) sim.Tick(.05f);
                Assert.AreEqual(1, sim.DirtySeatCount);
                Call(view, "LateUpdate");
                var furniture = (IDictionary)Field(view, "_furniture");
                var door = (SpriteRenderer)Field(view, "_doorRenderer");
                Assert.Greater(door.sortingOrder, ((SpriteRenderer)furniture[behind]).sortingOrder);
                Assert.Less(door.sortingOrder, ((SpriteRenderer)furniture[front]).sortingOrder);
                Capture(root, new Vector2(-3, -1.1f), "doorway-depth.png");
                var frames = (System.Collections.Generic.List<Sprite>)Field(view, "_doorFrames");
                if (frames.Count > 1)
                {
                    door.sprite = frames.Last(); Call(view, "SortDepth");
                    Assert.Greater(door.sortingOrder, ((SpriteRenderer)furniture[behind]).sortingOrder);
                    Assert.Less(door.sortingOrder, ((SpriteRenderer)furniture[front]).sortingOrder);
                    Capture(root, new Vector2(-3, -1.1f), "doorway-open-depth.png");
                }
                var seat = sim.DirtySeats.Single(); var plate = view.DirtyPlateRenderer(seat);
                Assert.IsTrue(view.TryTapDirtyTable(plate.bounds.center));
                sim.Tick(2.5f); yield return null;
                Assert.AreEqual(.5f, sim.PlayerCleaningProgress(seat), .001f);
                var track = root.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == "TaskProgress");
                Assert.IsTrue(track.enabled);
                Capture(root, new Vector2(-.1f, -2.2f), "table-cleaning-progress.png");
                sim.Tick(2.6f); yield return null;
                Assert.IsFalse(sim.IsSeatDirty(seat));
                Assert.IsTrue(root.GetComponentsInChildren<SpriteRenderer>().Any(r => r.name == "CleanSpark"));
                Assert.IsFalse(view.TryTapDirtyTable(plate.bounds.center));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            yield return new ExitPlayMode();
        }

        private static void Capture(GameObject root, Vector2 centre, string filename)
        {
            var cameraRoot = new GameObject("DiningProofCamera"); var camera = cameraRoot.AddComponent<Camera>();
            var target = new RenderTexture(1000, 750, 24);
            var image = new Texture2D(1000, 750, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var children = root.GetComponentsInChildren<Transform>(true);
            var layers = children.Select(t => t.gameObject.layer).ToArray();
            try
            {
                foreach (var child in children) child.gameObject.layer = 31;
                camera.cullingMask = 1 << 31; camera.orthographic = true; camera.orthographicSize = 1.7f;
                camera.transform.position = new Vector3(centre.x, centre.y, -10);
                camera.backgroundColor = new Color(.57f, .73f, .28f); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1000, 750), 0, 0); image.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes(Path.Combine("Logs", filename), image.EncodeToPNG());
            }
            finally
            {
                for (int i = 0; i < children.Length; i++) children[i].gameObject.layer = layers[i];
                RenderTexture.active = previous; camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraRoot); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
