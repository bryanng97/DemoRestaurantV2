using System;
using System.Collections;
using System.IO;
using System.Linq;
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
using UnityEngine.TestTools;

namespace DemoRestaurant.Render.Tests
{
    public sealed class ExteriorRoofFramingTests
    {
        private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Call(ExteriorStreetView street,string method,params object[] args) => typeof(ExteriorStreetView).GetMethod(method,Private).Invoke(street,args);
        private static RectTransform Building(ExteriorStreetView street) => (RectTransform)typeof(ExteriorStreetView).GetField("_building",Private).GetValue(street);
        private static RectTransform Part(RectTransform parent,string name) => parent.Cast<Transform>().OfType<RectTransform>().Single(r=>r.gameObject.activeSelf && (r.name==name || r.name.StartsWith(name+"#")));
        private static Rect WorldRect(RectTransform rect)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners.Min(v=>v.x),corners.Min(v=>v.y),corners.Max(v=>v.x),corners.Max(v=>v.y));
        }
        private static void Same(Rect expected,Rect actual,string part)
        {
            Assert.AreEqual(expected.x,actual.x,.001f,part+" moved horizontally when changing the roof.");
            Assert.AreEqual(expected.y,actual.y,.001f,part+" moved vertically when changing the roof.");
            Assert.AreEqual(expected.width,actual.width,.001f,part+" changed width when changing the roof.");
            Assert.AreEqual(expected.height,actual.height,.001f,part+" changed height when changing the roof.");
        }

        [UnityTest] public IEnumerator SwappingFlippingUndoingAndSavingRoofsKeepTheFacadeAndFixturesFixed()
        {
            yield return new EnterPlayMode();
            var root=new GameObject("ExteriorRoofFramingTest"); var oldMain=Camera.main;
            if(oldMain!=null) oldMain.tag="Untagged";
            string oldLanguage=Loc.Language;
            try
            {
                Loc.SetLanguage("zh");
                var content=ContentLoader.Load("malaysia");
                int playableCap=content.Pack.Economy.settings.phase1MaxLevel;
                content.Pack.Economy.settings.phase1MaxLevel=50; // Fixture only: cover future facade sizes without changing shipped values.
                var session=new RestaurantSession(content.Pack,new RestaurantLayout(15,12,new GridPos(0,10)),7,1000000);
                session.ConfigureExterior(content.Exterior);
                var cameraGo=new GameObject("RoofCamera");cameraGo.transform.SetParent(root.transform);cameraGo.tag="MainCamera";
                var camera=cameraGo.AddComponent<Camera>();camera.orthographic=true;camera.transform.position=new Vector3(0,0,-10);
                cameraGo.AddComponent<CameraController>();
                var uiGo=new GameObject("UI");uiGo.transform.SetParent(root.transform);
                var ui=uiGo.AddComponent<GameUi>();ui.Bind(session,content,new StaffController(session,Array.Empty<GridPos>(),Array.Empty<GridPos>()),new AvatarPainter(content.Pack.Avatar),60);
                var streetGo=new GameObject("Street");streetGo.transform.SetParent(root.transform);
                var street=streetGo.AddComponent<ExteriorStreetView>();street.Bind(ui);ui.Exterior=street;street.Persist=_=>true;
                foreach(int level in new[]{1,40})
                {
                    session.Progress.Restore(level,0);street.Show();Call(street,"BeginEdit");yield return null;
                    var fixtures=session.CaptureExterior().pieces.Where(p=>content.Exterior.Get(p.itemId).kind==ExteriorKind.Door || content.Exterior.Get(p.itemId).kind==ExteriorKind.Window).ToArray();
                    var building=Building(street);
                    var initialWall=WorldRect(Part(building,"Front"));
                    var initialFixtures=fixtures.ToDictionary(p=>p.instance,p=>WorldRect(Part(building,p.itemId+"#"+p.instance)));
                    void Check()
                    {
                        Canvas.ForceUpdateCanvases();building=Building(street);
                        Same(initialWall,WorldRect(Part(building,"Front")),"Wall");
                        foreach(var piece in fixtures) Same(initialFixtures[piece.instance],WorldRect(building.Cast<Transform>().OfType<RectTransform>().Single(r=>r.gameObject.activeSelf && r.name==piece.itemId+"#"+piece.instance)),piece.itemId);
                    }
                    foreach(var roof in content.Exterior.Items.Where(d=>d.kind==ExteriorKind.Roof && d.unlockLevel<=level))
                    {
                        Call(street,"Choose",roof);yield return null;Check();CheckAttachment(building,roof);
                        if(level==1 && new[]{"ext_roof_04","ext_roof_06","ext_roof_29"}.Contains(roof.id)) Capture(street,camera,"roof-stable-"+roof.id+".png");
                        Call(street,"Flip");yield return null;Check();CheckAttachment(building,roof);
                    }
                    Call(street,"Undo");yield return null;Check();
                    Call(street,"Save");yield return null;Assert.IsFalse(street.IsEditing);
                    // Leaving edit mode intentionally restores the wider street view.
                    // Reopening the street must keep that browsing frame stable.
                    initialWall=WorldRect(Part(Building(street),"Front"));
                    initialFixtures=fixtures.ToDictionary(p=>p.instance,p=>WorldRect(Part(Building(street),p.itemId+"#"+p.instance)));
                    Check();
                    street.Close();street.Show();yield return null;Check();
                    street.Close();
                }
                // Real XP/level-up events must update an already-open street and make the wall
                // visibly larger, with no compensating camera zoom and no lost decorations.
                content.Pack.Economy.settings.phase1MaxLevel=playableCap;
                session.Progress.Restore(1,0);street.Show();Call(street,"BeginEdit");
                var nest=content.Exterior.Get("ext_roof_19");
                if(nest!=null) Call(street,"Choose",nest);
                Call(street,"Save");yield return null;
                var saved=session.CaptureExterior();
                var fixedFixtures=saved.pieces.Where(p=>content.Exterior.Get(p.itemId).kind==ExteriorKind.Door || content.Exterior.Get(p.itemId).kind==ExteriorKind.Window).ToArray();
                var first=WorldRect(Part(Building(street),"Front"));
                var fixtureRects=fixedFixtures.ToDictionary(p=>p.instance,p=>WorldRect(Part(Building(street),p.itemId+"#"+p.instance)));
                var scale=Building(street).localScale;
                Capture(street,camera,"exterior-growth-level1.png");
                float lastWidth=first.width;
                foreach(int target in new[]{5,10}.Where(l=>l<=playableCap))
                {
                    while(session.Progress.Level<target) session.Progress.Add(session.Progress.XpToNext);
                    yield return null;
                    var wall=WorldRect(Part(Building(street),"Front"));
                    Assert.IsTrue(street.IsOpen);
                    Assert.Greater(wall.width,lastWidth,"Level-up must visibly enlarge the wall without reopening the street.");
                    Assert.AreEqual(scale,Building(street).localScale,"Zoom must not cancel the upgrade.");
                    Assert.AreEqual(first.width*content.Exterior.BoxFor(target).Width/ExteriorCatalog.BaseWidth,wall.width,.01f);
                    foreach(var p in fixedFixtures) Same(fixtureRects[p.instance],WorldRect(Part(Building(street),p.itemId+"#"+p.instance)),p.itemId);
                    var after=session.CaptureSave();Assert.AreEqual(target,after.level);
                    Assert.AreEqual(saved.pieces.Count,after.exterior.pieces.Count);
                    foreach(var p in saved.pieces)
                    {
                        var same=after.exterior.pieces.Single(x=>x.instance==p.instance);
                        Assert.AreEqual(p.itemId,same.itemId);Assert.AreEqual(p.x,same.x);Assert.AreEqual(p.y,same.y);
                    }
                    Capture(street,camera,"exterior-growth-level"+target+".png");
                    lastWidth=wall.width;
                }
                street.Close();
                content.Pack.Economy.settings.phase1MaxLevel=50; // Exercise later visual tiers; the shipped cap stays unchanged.
                street.Show();yield return null;
                var previous=WorldRect(Part(Building(street),"Front"));var futureScale=Building(street).localScale;
                foreach(int target in new[]{15,20,30,40})
                {
                    while(session.Progress.Level<target) session.Progress.Add(session.Progress.XpToNext);
                    yield return null;
                    var grown=WorldRect(Part(Building(street),"Front"));
                    Assert.Greater(grown.height,previous.height,"Later configured tiers must visibly increase the facade height.");
                    Assert.GreaterOrEqual(grown.width,previous.width-.01f);
                    Assert.AreEqual(futureScale,Building(street).localScale);
                    Assert.IsTrue(content.Exterior.Validate(session.CaptureExterior().pieces,target));
                    previous=grown;
                }
                street.Close();
            }
            finally { UnityEngine.Object.DestroyImmediate(root);if(oldMain!=null) oldMain.tag="MainCamera";Loc.SetLanguage(oldLanguage); }
            yield return new ExitPlayMode();
        }

        [Test] public void EditZoomKeepsStarterFacadeLargeWithoutFittingUnrelatedRoofArtwork()
        {
            var zoom=typeof(ExteriorStreetView).GetMethod("EditZoom",BindingFlags.NonPublic|BindingFlags.Static);
            var starter=new ExteriorBox(100,900,302,620);
            float scale=(float)zoom.Invoke(null,new object[]{starter,1920f,464f});
            Assert.That(scale,Is.GreaterThan(1f),"The starter wall should fill the edit work area, not become a street thumbnail.");
            Assert.That(scale,Is.LessThanOrEqualTo(1.2f));
            Assert.That(starter.Width*scale,Is.LessThanOrEqualTo(1760f));
            float spacious=(float)zoom.Invoke(null,new object[]{starter,1920f,800f});
            Assert.That(spacious,Is.EqualTo(1.2f).Within(.001f),"Use RC's zoom when the work area permits it.");
        }

        [Test] public void ExitGuardDetectsPendingAndPlacedChangesWithoutTreatingPickupAsAnEdit()
        {
            var method=typeof(ExteriorStreetView).GetMethod("HasExteriorChanges",BindingFlags.NonPublic|BindingFlags.Static);
            var original=new DemoRestaurant.Logic.Layout.ExteriorSave();
            original.pieces.Add(new DemoRestaurant.Logic.Layout.ExteriorPiece { instance=1,itemId="window",x=100,y=400 });
            var draft=original.Copy();
            bool Changed(DemoRestaurant.Logic.Layout.ExteriorPiece moving=null) =>
                (bool)method.Invoke(null,new object[]{original,draft,moving});
            Assert.IsFalse(Changed());
            var held=draft.pieces[0].Copy();
            Assert.IsFalse(Changed(held),"Picking up an unchanged piece does not lose any changes on exit.");
            held.x+=5; Assert.IsTrue(Changed(held));
            Assert.IsTrue(Changed(new DemoRestaurant.Logic.Layout.ExteriorPiece { instance=2,itemId="window",x=200,y=400 }));
            draft.pieces[0].flipped=true; Assert.IsTrue(Changed());
            draft=original.Copy(); draft.pieces.Clear(); Assert.IsTrue(Changed());
        }

        [Test] public void NativeNestUsesItsInternalRegistrationPoint()
        {
            var nest=ContentLoader.Load("malaysia").Exterior.Get("ext_roof_19");
            if(nest==null) Assert.Ignore("Private native roof catalog is not installed.");
            Assert.That(nest.roofAnchorY,Is.InRange(.82f,.84f),"Native twigs extend below the actual wall attachment.");
        }

        private static void CheckAttachment(RectTransform building,ExteriorItemDef def)
        {
            var roof=Part(building,def.id);var wall=WorldRect(Part(building,"Front"));
            var origin=roof.TransformPoint(new Vector3(roof.rect.xMin+roof.rect.width*def.roofAnchorX,roof.rect.yMax-roof.rect.height*def.roofAnchorY));
            Assert.AreEqual(wall.center.x,origin.x,.01f,"Roof registration must stay centred, including when flipped.");
            Assert.Less(Mathf.Abs(wall.yMax-origin.y),3*building.lossyScale.y,"Roof registration must meet the wall top.");
        }

        private static void Capture(ExteriorStreetView street,Camera camera,string name)
        {
            if(Path.GetFileName(Directory.GetParent(Application.dataPath).FullName)!="ValidationProject") return;
            typeof(ExteriorStreetTests).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{street,camera,name});
        }
    }
}
