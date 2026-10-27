using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Game.UI;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Profile;
using DemoRestaurant.Logic.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DemoRestaurant.Render.Tests
{
    public sealed class WardrobeAndTrashPresentationTests
    {
        private static object Field(object value,string name) => value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);
        private static void Call(object value,string name,params object[] args) => value.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(value,args);

        [UnityTest]
        public IEnumerator ProceduralSkinSupportsOutfitPreviewPurchaseAndSavedOwner()
        {
            yield return new EnterPlayMode();
            var root=new GameObject("WardrobeProof");
            try
            {
                var content=ContentLoader.Load("malaysia");
                content.Theme.skins=Array.Empty<string>();
                var session=new RestaurantSession(content.Pack,new RestaurantLayout(12,11,new GridPos(0,10)),3,startingCoins:1000);
                session.ArrivalsEnabled=false;
                var painter=new AvatarPainter(content.Pack.Avatar,native:false);
                var view=root.AddComponent<RestaurantView>();view.Avatars=painter;
                view.OwnerLook=()=>session.Profile.Avatar;
                view.Bind(session.Sim,content,Array.Empty<int>());
                var ownerType=content.Pack.OwnerType(StaffRole.Chef);
                Assert.IsNotNull(ownerType);
                var owner=session.Sim.Hire(ownerType,new GridPos(3,3));
                session.Profile.Changed+=view.RefreshOwner;
                var staff=new DemoRestaurant.Game.Controls.StaffController(session,new[]{new GridPos(3,3)},new[]{new GridPos(4,3)});
                var ui=root.AddComponent<GameUi>();ui.Bind(session,content,staff,painter,60);
                ui.OpenProfile();Assert.IsTrue(((ProfileDialog)Field(ui,"_profile")).IsShowing);
                root.GetComponentsInChildren<Button>(true).Single(b=>b.name=="OpenWardrobe").onClick.Invoke();
                var dialog=(WardrobePage)Field(ui,"_wardrobe");
                Assert.IsFalse(((ProfileDialog)Field(ui,"_profile")).IsShowing);
                Call(dialog,"SelectCategory",AvatarSlot.Hat);
                var cap=content.Pack.Avatar.Find(AvatarSlot.Hat,"cap");Call(dialog,"Pick",cap);
                var draft=(AvatarLook)Field(dialog,"_draft");
                Assert.AreSame(painter.TurntablePreview(draft,0),((Image)Field(dialog,"_preview")).sprite,"Try-on must use the draft, not the fixed baked owner.");
                Assert.IsTrue(((Button)Field(dialog,"_buy")).gameObject.activeSelf);
                Assert.IsFalse(((Button)Field(dialog,"_save")).interactable);
                Call(dialog,"Buy");Assert.AreEqual(1000-cap.Price,session.Wallet.DisplayCoins);
                Assert.IsTrue(((Button)Field(dialog,"_save")).interactable);
                Call(dialog,"SaveLook");
                Assert.IsTrue(session.Profile.HasCustomAvatar);Assert.AreEqual("cap",session.Profile.Avatar[AvatarSlot.Hat]);
                Assert.IsFalse((bool)Field(view,"_ownerBaked"));
                Assert.AreEqual(session.Profile.Avatar.Key,Field(view,"_ownerLookKey"));
                Assert.AreSame(painter.Head(session.Profile.Avatar),ui.PortraitFor(owner));
                Assert.IsNull(ui.BakedOwnerFigure());
                // A retained preview must survive browsing more looks than the painter cache holds.
                for(int i=0;i<24;i++)painter.Body(AvatarLook.Random(content.Pack.Avatar,new System.Random(i),99));
                Assert.IsTrue(((Image)Field(dialog,"_preview")).sprite!=null);
                ((CanvasGroup)Field(ui,"_toastGroup")).alpha=0;
                ui.OpenWardrobe();
                if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null) CaptureUi(root,"wardrobe-procedural-preview.png");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator NativeSkinWardrobeClicksPreviewAndSaveOriginalModelWithWorkingAnimations()
        {
            yield return new EnterPlayMode();
            var root=new GameObject("NativeWardrobeProof");
            try
            {
                var content=ContentLoader.Load("malaysia");
                var session=new RestaurantSession(content.Pack,new RestaurantLayout(12,11,new GridPos(0,10)),3,startingCoins:1000);
                var cap=content.Pack.Avatar.Find(AvatarSlot.Hat,"cap");session.BuyAvatarPart(cap);
                var look=session.Profile.Avatar.Clone();look.TrySet(content.Pack.Avatar,AvatarSlot.Hat,"cap",99);session.SaveAvatar(look);
                var save=session.CaptureSave();session.ApplySave(save);
                Assert.IsTrue(session.Profile.HasCustomAvatar,"Reproduce the prior saved custom outfit.");
                var painter=new AvatarPainter(content.Pack.Avatar);
                var view=root.AddComponent<RestaurantView>();view.Avatars=painter;view.OwnerLook=()=>session.Profile.Avatar;
                view.OwnerCustomized=()=>session.Profile.HasCustomAvatar;
                view.Bind(session.Sim,content,Array.Empty<int>());
                var owner=session.Sim.Hire(content.Pack.OwnerType(StaffRole.Chef),new GridPos(3,3));
                session.Profile.Changed+=view.RefreshOwner;
                var staff=new DemoRestaurant.Game.Controls.StaffController(session,new[]{new GridPos(3,3)},new[]{new GridPos(4,3)});
                var ui=root.AddComponent<GameUi>();ui.Bind(session,content,staff,painter,60);
                Assert.IsTrue(painter.NativeAvailable,"The original model, clip and texture manifest must be installed.");
                Assert.IsTrue(ui.CanCustomizeAvatar);
                var art=ArtLibrary.For(content.Theme);var native=art.GetOptional("char_owner_idle_f_00");
                Assert.IsNotNull(native);Assert.IsNull(ui.BakedOwnerFigure());
                Assert.AreSame(painter.Head(session.Profile.Avatar),ui.PortraitFor(owner));
                ui.OpenWardrobe();var dialog=(WardrobePage)Field(ui,"_wardrobe");
                foreach(var category in new[]{AvatarSlot.Eyes,AvatarSlot.HairStyle,AvatarSlot.Hat})
                {
                    Call(dialog,"SelectCategory",category);
                    Assert.AreSame(painter.TurntablePreview((AvatarLook)Field(dialog,"_draft"),0),((Image)Field(dialog,"_preview")).sprite);
                }
                var before=((Image)Field(dialog,"_preview")).sprite;
                var option=((IEnumerable)Field(dialog,"_options")).Cast<object>().First(o=>
                {
                    var part=(AvatarPartDef)o.GetType().GetField("Part").GetValue(o);
                    return part.Slot==AvatarSlot.Hat && part.Id=="none";
                });
                var noneButton=((Image)option.GetType().GetField("Rim").GetValue(option)).GetComponent<Button>();
                Assert.IsTrue(noneButton.interactable);noneButton.onClick.Invoke();
                var draft=(AvatarLook)Field(dialog,"_draft");
                Assert.AreEqual("none",draft[AvatarSlot.Hat]);
                Assert.AreNotSame(before,((Image)Field(dialog,"_preview")).sprite);
                Assert.IsTrue(((Button)Field(dialog,"_save")).interactable);
                ((Button)Field(dialog,"_save")).onClick.Invoke();
                Assert.AreEqual("none",session.Profile.Avatar[AvatarSlot.Hat]);
                Assert.IsFalse(((Button)Field(dialog,"_save")).interactable);
                Assert.IsTrue(((Button)Field(dialog,"_random")).interactable);
                Assert.IsFalse((bool)Field(view,"_ownerBaked"));
                Assert.AreEqual(session.Profile.Avatar.Key,Field(view,"_ownerLookKey"));
                var frames=painter.NativeFrames(session.Profile.Avatar);
                foreach(var anim in new[]{CharacterFrames.Walk,CharacterFrames.Carry,CharacterFrames.Cook,CharacterFrames.Clean,CharacterFrames.Fix})
                {
                    Assert.Greater(frames.Get(anim,false).Length,1);
                    StringAssert.StartsWith("native_avatar_",frames.Get(anim,true)[0].name);
                }
                Assert.IsTrue(frames.TryHand(CharacterFrames.Carry,false,out var hand));
                Assert.That(hand.x,Is.InRange(0f,1f));Assert.That(hand.y,Is.InRange(0f,1f));
                Assert.IsTrue(session.OwnsAvatarPart(cap));
                ((CanvasGroup)Field(ui,"_toastGroup")).alpha=0;
                Assert.IsFalse(dialog.IsShowing,"Saving exits the dedicated wardrobe.");
                ui.OpenWardrobe();
                if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null) CaptureUi(root,"wardrobe-native-working.png");
                Call(dialog,"SelectCategory",AvatarSlot.Eyes);
                var eyeOption=((IEnumerable)Field(dialog,"_options")).Cast<object>().First();
                var eyeImage=((Image)eyeOption.GetType().GetField("Rim").GetValue(eyeOption)).transform.Find("ItemPreview").GetComponent<Image>();
                Assert.AreSame(Resources.Load<Texture2D>("Art/RefProto/AvatarRuntime/Layers/Eyes5"),eyeImage.sprite.texture,
                    "Eye cards use the original eye layer without a head, hair or hat.");
                CaptureUi(root,"wardrobe-eyes-items.png");
                Call(dialog,"SelectCategory",AvatarSlot.Eyebrows);
                CaptureUi(root,"wardrobe-eyebrows-items.png");
                Call(dialog,"SelectCategory",AvatarSlot.Hat);
                var drag=root.GetComponentInChildren<WardrobeDragRotation>();
                var stage=(RectTransform)drag.transform;
                Vector2 start=RectTransformUtility.WorldToScreenPoint(null,stage.TransformPoint(stage.rect.center));
                var pointer=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){pointerId=17,pressPosition=start,position=start+Vector2.right*80};
                drag.OnBeginDrag(pointer);drag.OnDrag(pointer);drag.OnEndDrag(pointer);
                Assert.AreNotEqual(0f,(float)Field(dialog,"_yaw"));
                var previewBefore=((Image)Field(dialog,"_preview")).sprite;
                root.GetComponentsInChildren<Button>(true).Single(b=>b.name=="ui.wardrobe.rotate_right").onClick.Invoke();
                Assert.AreNotSame(previewBefore,((Image)Field(dialog,"_preview")).sprite);
                Call(dialog,"Pick",cap);
                Assert.AreEqual("none",session.Profile.Avatar[AvatarSlot.Hat],"Trying on must not save before confirmation.");
                root.GetComponentsInChildren<Button>(true).Single(b=>b.name=="ui.wardrobe.back").onClick.Invoke();
                Assert.IsFalse(dialog.IsShowing);
                Assert.AreEqual("none",session.Profile.Avatar[AvatarSlot.Hat],"Back discards the unsaved look.");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator NativeAvatarPartsChangeActualPixelsAndKeepHeadAboveClothes()
        {
            yield return new EnterPlayMode();
            var content=ContentLoader.Load("malaysia");
            var painter=new AvatarPainter(content.Pack.Avatar);
            try
            {
                Assert.IsTrue(painter.NativeAvailable);
                var look=AvatarLook.Default(content.Pack.Avatar);
                look.TrySet(content.Pack.Avatar,AvatarSlot.HairStyle,"short",99);
                var original=Pixels(painter.Body(look).texture);
                uint baseline=Hash(original);
                var choices=new[]{(AvatarSlot.Gender,"male"),(AvatarSlot.SkinTone,"brown"),(AvatarSlot.Eyes,"happy"),
                    (AvatarSlot.Eyebrows,"bold"),(AvatarSlot.HairStyle,"spiky"),(AvatarSlot.HairColor,"black"),
                    (AvatarSlot.Hat,"chef"),(AvatarSlot.Shirt,"tee"),(AvatarSlot.ShirtColor,"red"),
                    (AvatarSlot.Pants,"trousers"),(AvatarSlot.PantsColor,"khaki")};
                foreach(var choice in choices)
                {
                    var draft=look.Clone();Assert.IsTrue(draft.TrySet(content.Pack.Avatar,choice.Item1,choice.Item2,99),choice.Item1+":"+choice.Item2);
                    Assert.AreNotEqual(baseline,Hash(Pixels(painter.Body(draft).texture)),choice.Item1+" must visibly change the original model.");
                    yield return null;
                }
                // Catch an upside-down command-buffer render: brown hair belongs above the blue outfit.
                double hairY=0,blueY=0;int hair=0,blue=0;
                for(int i=0;i<original.Length;i++)
                {
                    var c=original[i];if(c.a<128)continue;
                    if(c.r>c.g*1.35f && c.r>c.b*1.6f){hairY+=i/256;hair++;}
                    if(c.b>c.r*1.3f && c.b>c.g*1.15f){blueY+=i/256;blue++;}
                }
                Assert.Greater(hair,20);Assert.Greater(blue,20);Assert.Greater(hairY/hair,blueY/blue);
                var front=painter.Preview(look,0);var rotated=painter.Preview(look,1);
                Assert.AreSame(front,painter.Preview(look,8));
                Assert.AreSame(rotated,painter.Preview(look,1),"Reuse cached native viewing angles.");
                Assert.AreNotEqual(Hash(Pixels(front.texture)),Hash(Pixels(rotated.texture)),"Rotate the model rather than stretch or mirror the preview.");
                Assert.AreSame(painter.Back(look),painter.Preview(look,2));
                Assert.AreSame(painter.Preview(look,7),painter.Preview(look,-1));
                var eyePart=content.Pack.Avatar.Find(AvatarSlot.Eyes,"round");
                var eyeIcon=painter.ItemIcon(eyePart,look);
                Assert.AreSame(Resources.Load<Texture2D>("Art/RefProto/AvatarRuntime/Layers/Eyes5"),eyeIcon.texture);
                var alternate=look.Clone();alternate.TrySet(content.Pack.Avatar,AvatarSlot.Hat,"headband",99);
                Assert.AreSame(eyeIcon,painter.ItemIcon(eyePart,alternate),"Other worn items cannot appear in a category icon.");
                foreach(var slot in new[]{AvatarSlot.HairStyle,AvatarSlot.Hat,AvatarSlot.Shirt,AvatarSlot.Pants})
                {
                    var part=content.Pack.Avatar.Find(slot,slot==AvatarSlot.Hat?"cap":look[slot]);
                    var icon=painter.ItemIcon(part,look);
                    Assert.IsNotNull(icon,"Render only the selected native meshes for "+slot);
                    Assert.AreNotEqual(Hash(Pixels(painter.Body(look).texture)),Hash(Pixels(icon.texture)));
                }
                var frames=painter.NativeFrames(look);var walk=frames.Get(CharacterFrames.Walk,false);
                Assert.AreNotEqual(Hash(Pixels(walk[0].texture)),Hash(Pixels(walk[1].texture)),"Sample the original animation instead of freezing the actor.");
                Assert.AreNotEqual(Hash(Pixels(walk[0].texture)),Hash(Pixels(frames.Get(CharacterFrames.Walk,true)[0].texture)),"Front and back views must differ.");
            }
            finally{painter.Dispose();}
            yield return new ExitPlayMode();
        }

        private static uint Hash(Color32[] pixels)
        {
            uint value=2166136261;
            unchecked{foreach(var p in pixels){value=(value^p.r)*16777619;value=(value^p.g)*16777619;value=(value^p.b)*16777619;value=(value^p.a)*16777619;}}
            return value;
        }
        private static Color32[] Pixels(Texture texture)
        {
            var target=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);
            var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);var old=RenderTexture.active;
            try
            {
                Graphics.Blit(texture,target);RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();return image.GetPixels32();
            }
            finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);}
        }

        [UnityTest]
        public IEnumerator TrashViewRoutesTapOnceAndIgnoresDecorationMode()
        {
            yield return new EnterPlayMode();
            var root=new GameObject("TrashProof");
            try
            {
                var content=ContentLoader.Load("malaysia");
                var session=new RestaurantSession(content.Pack,new RestaurantLayout(12,11,new GridPos(0,10)),4);
                var room=root.AddComponent<RestaurantView>();room.Bind(session.Sim,content,Array.Empty<int>());
                session.Sim.RestoreTrash(new[]{new DemoRestaurant.Logic.Simulation.TrashPiece{x=4,y=4,variant=0},
                    new DemoRestaurant.Logic.Simulation.TrashPiece{x=5,y=4,variant=2}},0);
                bool decorating=false;
                var trash=new GameObject("Trash").AddComponent<TrashView>();trash.transform.SetParent(root.transform);
                trash.Bind(session,ArtLibrary.For(content.Theme),()=>decorating);Call(trash,"LateUpdate");
                var art=ArtLibrary.For(content.Theme);
                foreach(var key in new[]{"trash_banana_peel","trash_pizza_slice","trash_soda_can","trash_chicken_bone","trash_apple_core"})
                    Assert.IsNotNull(art.GetOptional(key),"Use the original RC asset for "+key);
                Assert.AreEqual(0,((List<Sprite>)Field(trash,"_generated")).Count,"Installed native trash must never use the procedural fallback.");
                long before=session.Wallet.DisplayCoins;var point=IsoProjection.GridToWorld(4.5f,4.5f);
                decorating=true;Assert.IsFalse(trash.TryTap(point));Assert.AreEqual(2,session.Sim.TrashCount);
                decorating=false;Assert.IsTrue(trash.TryTap(point));Assert.IsFalse(trash.TryTap(point));
                Assert.AreEqual(1,session.Sim.TrashCount);Assert.AreEqual(before+session.Settings.trashCoins,session.Wallet.DisplayCoins);
                Call(trash,"LateUpdate");
                Assert.AreEqual(1,((Dictionary<int,SpriteRenderer>)Field(trash,"_renderers")).Count);
                if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
                    FurnitureAnimationTests.CaptureActivity(root,IsoProjection.GridToWorld(5.5f,4.5f),"trash-current-scene.png");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            yield return new ExitPlayMode();
        }

        private static void CaptureUi(GameObject root,string filename)
        {
            var canvas=root.GetComponentsInChildren<Canvas>(true).First(c=>c.gameObject.activeInHierarchy && c.renderMode==RenderMode.ScreenSpaceOverlay);
            var go=new GameObject("WardrobeCamera");var camera=go.AddComponent<Camera>();
            var target=new RenderTexture(1920,1080,24);var pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.targetTexture=target;
                camera.backgroundColor=new Color(.57f,.73f,.28f);camera.clearFlags=CameraClearFlags.SolidColor;
                canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine("Logs",filename),pixels.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;
                RenderTexture.active=previous;camera.targetTexture=null;
                UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
