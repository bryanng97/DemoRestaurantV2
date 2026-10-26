using System.Collections;
using System.Reflection;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Controls;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Game.UI;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace DemoRestaurant.Render.Tests
{
    public sealed class DecorationTouchTests
    {
        private static void Call(object target, string method) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        [UnityTest]
        public IEnumerator Touchscreen_PaintsDragsPlacesAndRejectsOccupiedUiAndPinchGestures()
        {
            yield return new EnterPlayMode();
            var oldUpdateMode = InputSystem.settings.updateMode;
            var oldFocus = InputSystem.settings.backgroundBehavior;
            var oldEditorFocus = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var oldEventSystem = EventSystem.current;
            var oldMain = Camera.main;
            if (oldMain != null) oldMain.tag = "Untagged";
            var root = new GameObject("TouchEditorTest");
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            InputSystem.EnableDevice(touchscreen);
            EnhancedTouchSupport.Enable();
            try
            {
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                var content = ContentLoader.Load("malaysia");
                var room = new RestaurantLayout(15, 12, new GridPos(0, 10));
                room.TryPlace(content.Pack.GetFurniture("gas_stove"), new GridPos(3, 0), Facing.SouthWest, out _);
                room.TryPlace(content.Pack.GetFurniture("wooden_chair"), new GridPos(4, 6), Facing.SouthWest, out _);
                var session = new RestaurantSession(content.Pack, room, 7, startingCoins: 10000);
                var cameraRoot = new GameObject("TouchCamera"); cameraRoot.transform.SetParent(root.transform);
                cameraRoot.tag = "MainCamera";
                var camera = cameraRoot.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 5;
                camera.transform.position = new Vector3(.75f, -3.375f, -10);
                var cameraController = cameraRoot.AddComponent<CameraController>(); cameraController.enabled = false;
                var viewRoot = new GameObject("View"); viewRoot.transform.SetParent(root.transform);
                var view = viewRoot.AddComponent<RestaurantView>(); view.Bind(session.Sim, content, new int[0]);
                var uiRoot = new GameObject("UI"); uiRoot.transform.SetParent(root.transform);
                var ui = uiRoot.AddComponent<GameUi>();
                ui.Bind(session, content, new StaffController(session, new GridPos[0], new GridPos[0]), new AvatarPainter(content.Pack.Avatar), 60);
                var controller = root.AddComponent<RestaurantEditorController>();
                controller.Bind(session, content, view, ui); ui.Builder = controller; controller.enabled = false;
                controller.Begin(BuildCategory.Decoration);
                yield return null;
                Canvas.ForceUpdateCanvases(); camera.Render();
                Vector2 Point(int x, int y) => camera.WorldToScreenPoint(IsoProjection.GridToWorld(x + .5f, y + .5f));
                // New purchases are deliberately aimed 9 mm above the finger; existing pieces keep their grab offset.
                Vector2 PurchasePoint(int x, int y) => Point(x,y) - Vector2.up * (Screen.dpi > 0 ? Screen.dpi : 160f) * .35f;
                void Touch(int id, UnityEngine.InputSystem.TouchPhase phase, Vector2 point)
                {
                    InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, phase = phase, position = point });
                    InputSystem.Update(); Call(controller, "HandleTouches");
                }
                void Flush() { InputSystem.Update(); Call(controller, "HandleTouches"); }

                // Stove editing shows the solid body and the separate, walkable chef work tile in every facing.
                controller.SelectFurniture(content.Pack.GetFurniture("gas_stove"));
                typeof(RestaurantEditorController).GetField("_origin", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,new GridPos(8,6));
                typeof(RestaurantEditorController).GetField("_hasTarget", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,true);
                for(int rotation=0;rotation<4;rotation++)
                {
                    Call(controller,"RefreshPreview");
                    var work=(SpriteRenderer)typeof(RestaurantEditorController).GetField("_workFootprint",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
                    Assert.IsTrue(work.enabled);Assert.IsTrue(controller.CanConfirm);
                    var expected=FurnitureWorkArea.FrontTile(content.Pack.GetFurniture("gas_stove"),new GridPos(8,6),(Facing)rotation);
                    Assert.That(Vector3.Distance(work.transform.localPosition,IsoProjection.GridToWorld(expected)),Is.LessThan(.001f));
                    controller.Rotate();
                }
                controller.ClearSelection();

                // Catalog selection -> a world drag -> release places without a separate desktop click.
                controller.SelectFurniture(content.Pack.GetFurniture("potted_palm"));
                Assert.That(UiInput.IsPointerOverUi(Point(6, 5)), Is.False);
                Touch(1, UnityEngine.InputSystem.TouchPhase.Began, PurchasePoint(6, 5));
                Touch(1, UnityEngine.InputSystem.TouchPhase.Moved, PurchasePoint(6, 6));
                Touch(1, UnityEngine.InputSystem.TouchPhase.Ended, PurchasePoint(6, 6)); Flush();
                Assert.That(controller.Editor.Draft.FindAt(new GridPos(6, 6))?.Def.Id, Is.EqualTo("potted_palm"));
                Assert.That(controller.HasSelection, Is.False, "The hand is empty after a drop, as in the reference game.");
                Assert.That(controller.CanConfirm, Is.False, "A placed copy must not remain an active unconfirmed target.");

                // Flooring works on finger-down, and continues when a real finger moves more than 20 pixels.
                controller.SelectFloor(content.Theme.floors[2]);
                Touch(2, UnityEngine.InputSystem.TouchPhase.Began, Point(5, 7));
                Touch(2, UnityEngine.InputSystem.TouchPhase.Moved, Point(5, 8));
                Touch(2, UnityEngine.InputSystem.TouchPhase.Ended, Point(5, 8)); Flush();
                Assert.That(controller.Editor.Draft.FloorAt(new GridPos(5, 7)), Is.EqualTo("pattern"));
                Assert.That(controller.Editor.Draft.FloorAt(new GridPos(5, 8)), Is.EqualTo("pattern"));

                // Existing furniture is picked up on press, so the first drag can move it.
                controller.ClearSelection();
                var palm = controller.Editor.Draft.FindAt(new GridPos(6, 6));
                Touch(3, UnityEngine.InputSystem.TouchPhase.Began, Point(6, 6));
                Assert.That(controller.HasSelection, Is.True);
                Touch(3, UnityEngine.InputSystem.TouchPhase.Moved, Point(7, 6));
                Touch(3, UnityEngine.InputSystem.TouchPhase.Ended, Point(7, 6)); Flush();
                Assert.That(palm.Origin, Is.EqualTo(new GridPos(7, 6)));

                // A tap picks a piece up and keeps it in hand, so Rotate / Store stay usable before the next tap drops it.
                controller.ClearSelection();
                Touch(9, UnityEngine.InputSystem.TouchPhase.Began, Point(7, 6));
                Touch(9, UnityEngine.InputSystem.TouchPhase.Ended, Point(7, 6)); Flush();
                Assert.That(controller.CanRemove, Is.True);
                controller.Rotate(); controller.Confirm();
                Assert.That(palm.Facing, Is.EqualTo(Facing.SouthEast));

                int count = controller.Editor.Draft.Furniture.Count;
                controller.SelectFurniture(content.Pack.GetFurniture("potted_palm"));
                Touch(4, UnityEngine.InputSystem.TouchPhase.Began, PurchasePoint(3, 0));
                Touch(4, UnityEngine.InputSystem.TouchPhase.Ended, PurchasePoint(3, 0)); Flush();
                Assert.That(controller.Editor.Draft.Furniture.Count, Is.EqualTo(count));

                Touch(5, UnityEngine.InputSystem.TouchPhase.Began, Point(9, 6));
                Touch(6, UnityEngine.InputSystem.TouchPhase.Began, Point(9, 7));
                Touch(5, UnityEngine.InputSystem.TouchPhase.Ended, Point(9, 6));
                Touch(6, UnityEngine.InputSystem.TouchPhase.Ended, Point(9, 7)); Flush();
                Assert.That(controller.Editor.Draft.Furniture.Count, Is.EqualTo(count), "Pinch must not place furniture.");
                Touch(7, UnityEngine.InputSystem.TouchPhase.Began, Point(9, 6));
                Touch(7, UnityEngine.InputSystem.TouchPhase.Canceled, Point(9, 6)); Flush();
                Assert.That(controller.Editor.Draft.Furniture.Count, Is.EqualTo(count));

                var shopBar = root.transform.Find("RestaurantEditorPanel/BuildCanvas/SafeArea/ShopBar") as RectTransform;
                var uiPoint = RectTransformUtility.WorldToScreenPoint(null, shopBar.TransformPoint(shopBar.rect.center));
                Assert.That(UiInput.IsPointerOverUi(uiPoint), Is.True);
                Assert.That(cameraController.CanPanInEditor(), Is.False, "Holding a piece: one finger moves it, not the camera.");
                Touch(8, UnityEngine.InputSystem.TouchPhase.Began, uiPoint);
                Touch(8, UnityEngine.InputSystem.TouchPhase.Ended, uiPoint); Flush();
                Assert.That(controller.Editor.Draft.Furniture.Count, Is.EqualTo(count));

                // Picking a placed piece up and lifting the finger over the shop bar puts it in storage.
                controller.ClearSelection();
                Touch(12, UnityEngine.InputSystem.TouchPhase.Began, Point(7, 6));
                Touch(12, UnityEngine.InputSystem.TouchPhase.Moved, uiPoint);
                Touch(12, UnityEngine.InputSystem.TouchPhase.Ended, uiPoint); Flush();
                Assert.That(controller.Editor.Draft.Furniture.Count, Is.EqualTo(count - 1));
                Assert.That(controller.HasSelection, Is.False);
                controller.Undo();
                Assert.That(cameraController.CanPanInEditor(), Is.True);

                int saved = 0;
                typeof(RestaurantEditorController).GetField("_persistLayout", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(controller, new System.Func<LayoutSnapshot, long, bool>((snapshot, balance) => { saved++; return true; }));
                controller.SelectFurniture(content.Pack.GetFurniture("potted_palm"));
                Touch(10, UnityEngine.InputSystem.TouchPhase.Began, PurchasePoint(3, 0));
                Touch(10, UnityEngine.InputSystem.TouchPhase.Ended, PurchasePoint(3, 0)); Flush();
                controller.Save();
                Assert.That(saved, Is.Zero, "An invalid preview must not be saved.");
                Assert.That(controller.IsEditing, Is.True);

                controller.SelectFurniture(content.Pack.GetFurniture("potted_palm"));
                Touch(11, UnityEngine.InputSystem.TouchPhase.Began, PurchasePoint(9, 6));
                Touch(11, UnityEngine.InputSystem.TouchPhase.Ended, PurchasePoint(9, 6)); Flush();
                controller.Save(); // No Deselect after release.
                Assert.That(saved, Is.EqualTo(1));
                Assert.That(controller.IsEditing, Is.False);
                Assert.That(session.Layout.FindAt(new GridPos(9, 6))?.Def.Id, Is.EqualTo("potted_palm"));

                controller.Begin(BuildCategory.Decoration);
                Touch(12, UnityEngine.InputSystem.TouchPhase.Began, Point(7, 6));
                Touch(12, UnityEngine.InputSystem.TouchPhase.Moved, Point(9, 7));
                controller.Save(); // Save also commits a valid moving preview directly.
                Assert.That(saved, Is.EqualTo(2));
                Assert.That(controller.IsEditing, Is.False);
                Assert.That(session.Layout.FindAt(new GridPos(7, 6)), Is.Null);
                Assert.That(session.Layout.FindAt(new GridPos(9, 7))?.Def.Id, Is.EqualTo("potted_palm"));
                Assert.That(session.Layout.Furniture.Count, Is.EqualTo(count + 1));
            }
            finally
            {
                InputSystem.settings.updateMode = oldUpdateMode;
                InputSystem.settings.backgroundBehavior = oldFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorFocus;
                InputSystem.RemoveDevice(touchscreen); EnhancedTouchSupport.Disable();
                if (oldEventSystem == null && EventSystem.current != null) Object.DestroyImmediate(EventSystem.current.gameObject);
                Object.DestroyImmediate(root);
                if (oldMain != null) oldMain.tag = "MainCamera";
            }
            yield return new ExitPlayMode();
        }
    }
}
