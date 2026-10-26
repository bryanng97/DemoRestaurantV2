using System.Collections;
using System.Linq;
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
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;

namespace DemoRestaurant.Tests
{
    public sealed class HudDrawerTouchTests
    {
        [UnityTest]
        public IEnumerator MainHud_TouchOpensClosesAndActivatesLargeSafeAreaButtons()
        {
            yield return new EnterPlayMode();
            var previousMode = InputSystem.settings.updateMode;
            var previousFocus = InputSystem.settings.backgroundBehavior;
            var previousEditorFocus = InputSystem.settings.editorInputBehaviorInPlayMode;
            var oldEventSystem = EventSystem.current;
            string language = Loc.Language;
            var root = new GameObject("HudTouchTest");
            var touch = InputSystem.AddDevice<Touchscreen>(); InputSystem.EnableDevice(touch);
            try
            {
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                var content = ContentLoader.Load("malaysia");
                var session = new RestaurantSession(content.Pack, new RestaurantLayout(12, 11, new GridPos(0, 10)), 3);
                var ui = root.AddComponent<GameUi>();
                ui.Bind(session, content, new StaffController(session, new GridPos[0], new GridPos[0]), new AvatarPainter(content.Pack.Avatar), 60);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var drawers = root.GetComponentsInChildren<HudDrawer>(true);
                var actions = drawers.Single(d => d.name == "ActionsDrawer");
                var places = drawers.Single(d => d.name == "PlacesDrawer");
                Assert.IsFalse(actions.Expanded); Assert.IsFalse(places.Expanded);
                Capture(root, "hud-collapsed");
                var module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
                int id = 0;
                void Tap(RectTransform rect)
                {
                    var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                    InputSystem.QueueStateEvent(touch, new TouchState { touchId = ++id, phase = UnityEngine.InputSystem.TouchPhase.Began, position = point });
                    InputSystem.Update(); module.Process();
                    InputSystem.QueueStateEvent(touch, new TouchState { touchId = id, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = point });
                    InputSystem.Update(); module.Process();
                }
                Tap((RectTransform)actions.transform.Find("Toggle"));
                Assert.IsTrue(actions.Expanded);
                foreach (var lang in new[] { "en", "ms", "zh" })
                {
                    Loc.SetLanguage(lang); Canvas.ForceUpdateCanvases();
                    foreach (var drawer in drawers)
                    {
                        drawer.SetExpanded(true);
                        foreach (RectTransform button in drawer.Content)
                        {
                            Assert.GreaterOrEqual(button.rect.width, 150); Assert.GreaterOrEqual(button.rect.height, 140);
                            var corners = new Vector3[4]; button.GetWorldCorners(corners);
                            foreach (var corner in corners)
                            {
                                var p = RectTransformUtility.WorldToScreenPoint(null, corner);
                                Assert.That(p.x, Is.InRange(Screen.safeArea.xMin - 1, Screen.safeArea.xMax + 1));
                                Assert.That(p.y, Is.InRange(Screen.safeArea.yMin - 1, Screen.safeArea.yMax + 1));
                            }
                        }
                    }
                }
                Tap((RectTransform)actions.transform.Find("Toggle"));
                Assert.IsTrue(actions.Expanded); Assert.IsFalse(places.Expanded);
                yield return null; Canvas.ForceUpdateCanvases();
                Capture(root, "hud-actions");
                Tap((RectTransform)actions.Content.GetChild(1)); // Menu action still receives a real touch.
                Assert.IsFalse(actions.Expanded); Assert.IsTrue(ui.MenuPanel.IsOpen);
                ui.MenuPanel.Toggle();
                Tap((RectTransform)places.transform.Find("Toggle"));
                Assert.IsTrue(places.Expanded);
                yield return null; Canvas.ForceUpdateCanvases();
                Capture(root, "hud-explore");
                Tap((RectTransform)places.Content.GetChild(1));
                Assert.IsFalse(places.Expanded); Assert.IsTrue(ui.GardenPanel.IsOpen);
            }
            finally
            {
                Loc.SetLanguage(language);
                Object.DestroyImmediate(root);
                InputSystem.RemoveDevice(touch);
                InputSystem.settings.updateMode = previousMode;
                InputSystem.settings.backgroundBehavior = previousFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorFocus;
                if (oldEventSystem == null && EventSystem.current != null) Object.DestroyImmediate(EventSystem.current.gameObject);
            }
            yield return new ExitPlayMode();
        }

        private static void Capture(GameObject root, string name)
        {
            var canvas = root.GetComponentInChildren<Canvas>();
            var mode = canvas.renderMode; var oldCamera = canvas.worldCamera;
            var cameraObject = new GameObject("HudPreviewCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.55f,.76f,.62f);
            camera.cullingMask = 1 << 31; camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10);
            var objects = root.GetComponentsInChildren<Transform>(true);
            var layers = objects.Select(t => t.gameObject.layer).ToArray();
            for (int i = 0; i < objects.Length; i++) objects[i].gameObject.layer = 31;
            var target = new RenderTexture(1920, 1080, 24);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(1920,1080,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,1920,1080),0,0); pixels.Apply();
                var directory = System.IO.Path.Combine(Application.dataPath,"../Logs");
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory,name+".png"),pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                canvas.renderMode = mode; canvas.worldCamera = oldCamera;
                for (int i = 0; i < objects.Length; i++) objects[i].gameObject.layer = layers[i];
                Object.DestroyImmediate(pixels); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject);
                Canvas.ForceUpdateCanvases();
            }
        }
    }
}
