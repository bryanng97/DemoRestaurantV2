using System.Collections;
using System.IO;
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
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DemoRestaurant.Render.Tests
{
    public sealed class ExteriorStreetTests
    {
        [Test] public void NativeArtAndAllLocalizedNamesResolve()
        {
            var content = ContentLoader.Load("malaysia");
            Assert.IsNotNull(content.Exterior);
            // The starter catalog may be extended by an installed private art pack.
            Assert.GreaterOrEqual(content.Exterior.Items.Count,15);
            Assert.AreEqual(content.Exterior.Items.Count,content.Exterior.Items.Select(d=>d.id).Distinct().Count());
            foreach (var language in Loc.SupportedLanguages)
            {
                Loc.SetLanguage(language);
                foreach (var d in content.Exterior.Items)
                {
                    Assert.IsNotNull(Resources.Load<Sprite>(d.sprite),d.sprite);
                    Assert.AreNotEqual(d.nameKey,Loc.Get(d.nameKey));
                }
            }
            Loc.SetLanguage("en");
        }

        [UnityTest] public IEnumerator TouchDragBuySaveAndCancelKeepIndoorLayoutIntact()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("ExteriorTouchTest");
            var oldMain = Camera.main;
            if (oldMain != null) oldMain.tag = "Untagged";
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            var oldMode = InputSystem.settings.updateMode;
            var oldFocus = InputSystem.settings.backgroundBehavior;
            var oldEditorFocus = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.EnableDevice(touchscreen);
            try
            {
                Loc.SetLanguage("en");
                var content = ContentLoader.Load("malaysia");
                var room = new RestaurantLayout(15,12,new GridPos(0,10));
                var session = new RestaurantSession(content.Pack,room,7,1000);
                session.ConfigureExterior(content.Exterior);
                var cameraGo = new GameObject("ExteriorTestCamera"); cameraGo.transform.SetParent(root.transform);
                cameraGo.tag = "MainCamera";
                var camera = cameraGo.AddComponent<Camera>(); camera.orthographic = true;
                camera.transform.position = new Vector3(0,0,-10);
                var controller = cameraGo.AddComponent<CameraController>();
                var uiGo = new GameObject("Ui"); uiGo.transform.SetParent(root.transform);
                var ui = uiGo.AddComponent<GameUi>();
                ui.Bind(session,content,new StaffController(session,new GridPos[0],new GridPos[0]),new AvatarPainter(content.Pack.Avatar),60);
                var streetGo = new GameObject("Street"); streetGo.transform.SetParent(root.transform);
                var street = streetGo.AddComponent<ExteriorStreetView>(); street.Bind(ui); ui.Exterior = street;
                int saves = 0; street.Persist = save => { saves++; return true; };
                street.Show(); yield return null;
                Assert.IsFalse(controller.enabled);
                Button Find(string name) => street.GetComponentsInChildren<Button>(true).First(b => b.name == name);
                Find("EditExterior").onClick.Invoke(); yield return null;
                Assert.IsTrue(street.IsEditing);
                Canvas.ForceUpdateCanvases();
                var stage = (RectTransform)street.GetComponentsInChildren<ExteriorPointer>().Single().transform;
                var building=(RectTransform)typeof(ExteriorStreetView).GetField("_building",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(street);
                var window=session.CaptureExterior().pieces.Single(p=>p.instance==4);
                float windowCentreY=window.y-content.Exterior.Get(window.itemId).height*.5f;
                Vector2 Point(float x,float y) => RectTransformUtility.WorldToScreenPoint(null,building.TransformPoint(new Vector3(x-500,340-y)));
                var hits = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Point(280,windowCentreY) },hits);
                Assert.IsTrue(hits.Count > 0 && hits[0].gameObject == stage.gameObject,
                    "Touch target must be the persistent stage: " + string.Join(",",hits.Select(h => h.gameObject.name)));
                void Touch(int id,UnityEngine.InputSystem.TouchPhase phase,Vector2 at)
                {
                    InputSystem.QueueStateEvent(touchscreen,new TouchState { touchId = id,phase = phase,position = at });
                    InputSystem.Update();
                    if(phase==UnityEngine.InputSystem.TouchPhase.Began)
                        Assert.IsTrue(touchscreen.primaryTouch.press.isPressed,"Touch event must reach the test device before UI processing: enabled="+touchscreen.enabled+", phase="+touchscreen.primaryTouch.phase.ReadValue()+", update="+InputSystem.settings.updateMode+", focus="+Application.isFocused);
                    EventSystem.current.currentInputModule.Process();
                }

                Touch(1,UnityEngine.InputSystem.TouchPhase.Began,Point(280,windowCentreY));
                Assert.AreEqual(4,typeof(ExteriorStreetView).GetField("_selected",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(street),
                    "Immediate touch selection: UI point="+((UnityEngine.InputSystem.UI.InputSystemUIInputModule)EventSystem.current.currentInputModule).point.action.ReadValue<Vector2>()+
                    ", click="+((UnityEngine.InputSystem.UI.InputSystemUIInputModule)EventSystem.current.currentInputModule).leftClick.action.ReadValue<float>()+
                    ", active="+typeof(ExteriorStreetView).GetField("_activePointer",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(street));
                yield return null; yield return null;
                Assert.AreEqual(4,typeof(ExteriorStreetView).GetField("_selected",System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(street),
                    "Touch down must select the window. Device="+touchscreen.primaryTouch.press.isPressed+", module="+EventSystem.current.currentInputModule+
                    ", point enabled="+((UnityEngine.InputSystem.UI.InputSystemUIInputModule)EventSystem.current.currentInputModule).point.action.enabled+
                    ", click enabled="+((UnityEngine.InputSystem.UI.InputSystemUIInputModule)EventSystem.current.currentInputModule).leftClick.action.enabled+
                    ", action pos="+((UnityEngine.InputSystem.UI.InputSystemUIInputModule)EventSystem.current.currentInputModule).point.action.ReadValue<Vector2>()+
                    ", click="+((UnityEngine.InputSystem.UI.InputSystemUIInputModule)EventSystem.current.currentInputModule).leftClick.action.ReadValue<float>()+
                    ", expected pos="+Point(280,480));
                Touch(1,UnityEngine.InputSystem.TouchPhase.Moved,Point(360,windowCentreY)); yield return null; yield return null;
                Touch(1,UnityEngine.InputSystem.TouchPhase.Ended,Point(360,windowCentreY)); yield return null; yield return null;
                // Live state stays untouched until Save; the draft really moved through the EventSystem touch path.
                Assert.AreEqual(280,session.CaptureExterior().pieces.Single(p => p.instance == 4).x);
                Find("SaveExterior").onClick.Invoke(); yield return null;
                Assert.IsFalse(street.IsEditing);
                Assert.AreEqual(360,session.CaptureExterior().pieces.Single(p => p.instance == 4).x);
                Assert.AreEqual(1,saves);

                Find("EditExterior").onClick.Invoke(); yield return null;
                Find("Ground").onClick.Invoke(); yield return null;
                Find("topiary").onClick.Invoke(); yield return null;
                Touch(2,UnityEngine.InputSystem.TouchPhase.Began,Point(40,600)); yield return null; yield return null;
                Touch(2,UnityEngine.InputSystem.TouchPhase.Ended,Point(40,600)); yield return null; yield return null;
                Find("SaveExterior").onClick.Invoke(); yield return null;
                Assert.AreEqual(650,session.Wallet.ConfirmedCoins);
                Assert.IsTrue(session.CaptureExterior().pieces.Any(p => p.itemId == "topiary"));
                Assert.AreEqual(0,room.Furniture.Count,"Exterior edits must not change the indoor floor plan.");
                Find("EditExterior").onClick.Invoke(); yield return null;
                Find("Wall").onClick.Invoke(); yield return null;
                Find("wall_red").onClick.Invoke(); yield return null;
                Find("Cancel").onClick.Invoke(); yield return null;
                Assert.AreEqual("wall_cream",session.CaptureExterior().pieces.Single(p => p.instance == 1).itemId);
                Assert.AreEqual(650,session.Wallet.ConfirmedCoins);
                Capture(street,camera,"rc-outdoor-street.png");
                Find("EditExterior").onClick.Invoke(); yield return null;
                Capture(street,camera,"rc-outdoor-editor.png");
                street.Close(); Assert.IsTrue(controller.enabled);
                Loc.SetLanguage("zh"); street.Show(); yield return null;
                Assert.AreEqual("进入餐厅",Find("Enter").GetComponentInChildren<Text>().text);
                Find("EditExterior").onClick.Invoke(); yield return null;
                Capture(street,camera,"rc-outdoor-editor-zh.png");
                Find("Cancel").onClick.Invoke(); yield return null;
                Touch(3,UnityEngine.InputSystem.TouchPhase.Began,Point(500,530)); yield return null;
                Assert.IsFalse(street.IsOpen,"Tapping the door must return indoors.");
                street.Show(); yield return null;
                Find("EditExterior").onClick.Invoke(); yield return null;
                Touch(3,UnityEngine.InputSystem.TouchPhase.Ended,Point(500,530)); yield return null;
                Touch(4,UnityEngine.InputSystem.TouchPhase.Began,Point(360,windowCentreY)); yield return null;
                Assert.AreEqual(4,typeof(ExteriorStreetView).GetField("_selected",System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(street),
                    "Reopening after entering through the door must not retain a stale pointer lock.");
                street.Close(); Loc.SetLanguage("en");
            }
            finally
            {
                InputSystem.settings.updateMode = oldMode;
                InputSystem.settings.backgroundBehavior = oldFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorFocus;
                InputSystem.RemoveDevice(touchscreen);
                Object.DestroyImmediate(root);
                if (oldMain != null) oldMain.tag = "MainCamera";
            }
            yield return new ExitPlayMode();
        }

        private static void Capture(ExteriorStreetView street,Camera camera,string filename)
        {
            // Optional visual evidence only in the isolated validation clone; never write into a user's save folder.
            var project = Directory.GetParent(Application.dataPath).FullName;
            if (Path.GetFileName(project) != "ValidationProject") return;
            string logs = Path.Combine(Directory.GetParent(Directory.GetParent(project).FullName).FullName,"Logs");
            Directory.CreateDirectory(logs);
            var canvas = street.GetComponentInChildren<Canvas>();
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
            var stage = street.GetComponentInChildren<ExteriorPointer>().transform;
            var oldStageScale = stage.localScale;
            var scaler = canvas.GetComponent<CanvasScaler>();
            bool oldScalerEnabled = scaler.enabled;
            float oldScale = canvas.scaleFactor;
            scaler.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var rt = new RenderTexture(1920,1080,24); var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            var texture = new Texture2D(1920,1080,TextureFormat.RGBA32,false);
            try
            {
                camera.targetTexture = rt;
                canvas.scaleFactor = 1; // Render at the canvas's native 1920 x 1080 reference resolution.
                Canvas.ForceUpdateCanvases();
                stage.localScale = Vector3.one; // Native reference viewport, independent of the headless GameView's size.
                camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0,0,1920,1080),0,0); texture.Apply();
                File.WriteAllBytes(Path.Combine(logs,filename),texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = oldActive; camera.targetTexture = oldTarget;
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera;
                canvas.scaleFactor = oldScale; scaler.enabled = oldScalerEnabled;
                stage.localScale = oldStageScale;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(texture);
            }
        }
    }
}
