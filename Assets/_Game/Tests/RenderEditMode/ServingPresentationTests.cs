using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using DemoRestaurant.Game.Audio;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Iso;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DemoRestaurant.Render.Tests
{
    public sealed class ServingPresentationTests
    {
        private static object Field(object instance,string name) => instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(instance);
        private static void Refresh(RestaurantView view) => typeof(RestaurantView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
        private static SpriteRenderer Plate(object entry) => (SpriteRenderer)Field(entry,"Renderer");
        private static void Until(RestaurantSim sim,Func<bool> condition)
        {
            for(int i=0;i<50000 && !condition();i++) sim.Tick(.01f);
            Assert.IsTrue(condition(),"Simulation did not reach the expected serving stage.");
        }

        [UnityTest] public IEnumerator OrderedMealsKeepTheirSizeThroughKitchenHandsAndDiningTable()
        {
            yield return new EnterPlayMode();
            var content=ContentLoader.Load("malaysia");
            Assert.IsNotNull(content.ArtForStaff("waiter").Avatar,"Waiters must have empty hands in their base character art.");
            var art=ArtLibrary.For(content.Theme);
            int dishIndex=0;
            foreach(int recipeLevel in new[]{1,10})
            foreach(var dish in content.Pack.Dishes)
            {
                var root=new GameObject("ServingPresentationTest");
                try
                {
                    var layout=new RestaurantLayout(9,9,new GridPos(0,7));
                    var table=content.Pack.Furniture.First(f=>f.Id=="dining_table");
                    var chair=content.Pack.Furniture.First(f=>f.Id=="wooden_chair");
                    var stove=content.Pack.Furniture.First(f=>f.Id=="gas_stove");
                    layout.TryPlace(table,new GridPos(5,5),Facing.SouthWest,out var diningTable);
                    layout.TryPlace(chair,new GridPos(5,6),Facing.NorthEast,out _);
                    layout.TryPlace(stove,new GridPos(2,2),(Facing)(dishIndex++%4),out _);
                    layout.TryPlace(content.Pack.Furniture.First(f=>f.Id=="fridge"),new GridPos(6,1),Facing.SouthWest,out _);
                    var sim=new RestaurantSim(layout,new RestaurantMenu(new[]{dish},1),7,new SimSettings { CleanSeconds=4 });
                    sim.Hire(new StaffTypeDef("chef","c",StaffRole.Chef,10,100),new GridPos(2,1));
                    var view=root.AddComponent<RestaurantView>();
                    view.Avatars=new AvatarPainter(content.Pack.Avatar); view.RecipeLevel=_=>recipeLevel;
                    view.Bind(sim,content,Array.Empty<int>());
                    var guest=sim.SpawnCustomer(new CustomerTypeDef("office_worker","g",10,999,1,999));
                    string key=content.ServingForDish(dish.Id);
                    if(art.GetOptional(key+"_level"+recipeLevel.ToString("D2")+"_00")!=null) key+="_level"+recipeLevel.ToString("D2");
                    Sprite expected=art.GetOptional(key+"_00") ?? art.GetOptional(key) ?? art.Get(content.IconForDish(dish.Id));
                    Assert.IsNotNull(expected,dish.Id);
                    var skin=art.SkinOf(key+"_00") ?? art.SkinOf(key);
                    float width=skin!=null && skin.tilePixelWidth>0 ? expected.rect.width/skin.tilePixelWidth : DishPresentation.WidthFor(dish);
                    var nativeFrames=Enumerable.Range(0,64).Select(i=>art.GetOptional(key+"_"+i.ToString("D2"))).TakeWhile(s=>s!=null).ToArray();
                    if(nativeFrames.Length>0) Assert.Greater(nativeFrames.Length,1,"The full eating sequence must be imported.");
                    if(dish.Category!=DishCategory.Drink)
                    {
                        Until(sim,()=>sim.Tickets.Any(t=>t.IsOnCounter)); Refresh(view);
                        var plates=(IDictionary)Field(view,"_plates");
                        var stovePiece=layout.Furniture.Single(f=>f.Def.Kind==FurnitureKind.Stove);
                        var stoveRenderer=(SpriteRenderer)((IDictionary)Field(view,"_furniture"))[stovePiece];
                        if(stoveRenderer.sprite.name.Contains("_done_loop_"))
                            Assert.AreEqual(0,plates.Count,"The native ready pot must not also have a meal drawn over its lid.");
                        else
                        {
                            Assert.AreEqual(1,plates.Count);
                            var onStove=Plate(plates.Values.Cast<object>().Single());
                            Assert.AreSame(expected,onStove.sprite);
                            Assert.AreEqual(width,onStove.bounds.size.x,.001f);
                        }
                        if(dish.Id=="roti_canai") Capture(view,"serving-stove.png");
                    }
                    var waiter=sim.Hire(content.Pack.StaffTypes.First(s=>s.Id=="waiter"),new GridPos(3,3));
                    Refresh(view);
                    var character=((IDictionary)Field(view,"_characters"))[waiter];
                    var carry=(SpriteRenderer)Field(character,"_carry");
                    Assert.IsFalse(carry.enabled,"No permanent coffee or plate before pickup.");
                    Until(sim,()=>waiter.IsCarryingPlate); Refresh(view);
                    Assert.IsTrue(carry.enabled); Assert.AreSame(expected,carry.sprite,dish.Id);
                    var guestView=((IDictionary)Field(view,"_characters"))[guest];
                    Assert.AreSame(expected,((SpriteRenderer)Field(guestView,"_icon")).sprite,"Order bubble must match the meal delivered.");
                    Assert.AreEqual(width,carry.bounds.size.x,.001f,dish.Id);
                    Assert.AreEqual(0,((IDictionary)Field(view,"_plates")).Count,"Pickup removes the stove's plate.");
                    if(dish.Id=="nasi_lemak") Capture(view,"serving-carry.png");
                    Until(sim,()=>guest.State==CustomerState.Eating); Refresh(view);
                    Assert.IsFalse(carry.enabled,"Delivery empties the waiter's hands.");
                    var meals=(IDictionary)Field(view,"_tableMeals");
                    Assert.AreEqual(1,meals.Count);
                    var onTable=Plate(meals[guest]);
                    Assert.AreSame(expected,onTable.sprite); Assert.AreEqual(width,onTable.bounds.size.x,.001f);
                    var tableRenderer=((IDictionary)Field(view,"_furniture"))[diningTable] as SpriteRenderer;
                    Assert.Greater(onTable.sortingOrder,tableRenderer.sortingOrder,"Meal must draw above the table.");
                    if(dish.Id=="nasi_lemak") Capture(view,"serving-table.png");
                    Vector3 tablePosition=onTable.transform.localPosition;
                    sim.Tick(dish.EatSeconds*.55f); Refresh(view);
                    Assert.AreEqual(CustomerState.Eating,guest.State);
                    if(nativeFrames.Length>1)
                    {
                        int frame=(int)((1-guest.TimeRemaining/dish.EatSeconds)*(nativeFrames.Length-1));
                        Assert.AreSame(nativeFrames[frame],onTable.sprite,"Food must disappear using the original animation frames.");
                        Assert.AreNotSame(expected,onTable.sprite);
                        Assert.AreEqual(tablePosition,onTable.transform.localPosition,"The original plate must not jump between eating frames.");
                        Assert.AreEqual(width,onTable.bounds.size.x,.001f);
                        if(dish.Id=="nasi_lemak") Capture(view,"serving-eating.png");
                    }
                    Until(sim,()=>sim.DirtySeatCount>0); Refresh(view);
                    Assert.AreEqual(0,((IDictionary)Field(view,"_tableMeals")).Count);
                    var dirty=Plate(((IDictionary)Field(view,"_dirtyDishes")).Values.Cast<object>().Single());
                    if(nativeFrames.Length>1)
                    {
                        Assert.AreSame(nativeFrames.Last(),dirty.sprite,"Leave the original empty plate, bowl or cup, rather than a generated oval.");
                        Assert.AreEqual(width,dirty.bounds.size.x,.001f);
                        Assert.AreEqual(tablePosition,dirty.transform.localPosition);
                        if(dish.Id=="nasi_lemak") Capture(view,"serving-empty.png");
                        if(dish.Category==DishCategory.Drink) Capture(view,"serving-empty-cup.png");
                    }
                    Assert.Greater(dirty.sortingOrder,tableRenderer.sortingOrder);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator BackStoveMatsFollowFacingAndPreviewVisibilityAndMusicUsesTheFullSliderRange()
        {
            yield return new EnterPlayMode();
            var root=new GameObject("StoveAndMusicTest");
            float oldVolume=GameSettings.MusicVolume;
            bool hadVolume=PlayerPrefs.HasKey("settings.musicVolume");
            float savedVolume=PlayerPrefs.GetFloat("settings.musicVolume",1);
            try
            {
                var content=ContentLoader.Load("malaysia");
                var sim=new RestaurantSim(new RestaurantLayout(8,8,new GridPos(0,6)),new RestaurantMenu(content.Pack.Dishes,1),7);
                var view=root.AddComponent<RestaurantView>(); view.Bind(sim,content,Array.Empty<int>());
                var preview=new GameObject("Preview").AddComponent<SpriteRenderer>(); preview.transform.SetParent(root.transform,false);
                var art=ArtLibrary.For(content.Theme);
                foreach(var stove in content.Pack.Furniture.Where(f=>f.Kind==FurnitureKind.Stove))
                {
                    var hints=art.Resolve(content.ArtForFurniture(stove.Id));
                    if(hints.FrontCells<=0 || art.GetOptional(hints.SpriteBack)==null) continue;
                    foreach(Facing facing in Enum.GetValues(typeof(Facing)))
                    {
                        var cell=new GridPos(3,3); view.DrawFurniturePreview(preview,stove,cell,facing);
                        var mat=preview.GetComponentInChildren<FurnitureWorkMat>();
                        if(!FacingUtil.IsBackView(facing)) { Assert.IsNull(mat,"Front art already contains its mat."); continue; }
                        Assert.IsNotNull(mat,stove.Id);
                        Vector2 expected=IsoProjection.GridToWorld(cell+FacingUtil.ToDelta(facing));
                        Assert.Less(Vector2.Distance(expected,mat.transform.position),.001f,stove.Id+facing);
                        // The generated sprite has transparent antialias padding outside its one-cell diamond.
                        Assert.AreEqual(1f,mat.transform.lossyScale.x,.001f);
                        Assert.AreEqual(1f,mat.transform.lossyScale.y,.001f);
                        preview.enabled=false; mat.Refresh(); Assert.IsFalse(mat.GetComponent<SpriteRenderer>().enabled);
                        preview.enabled=true; mat.Refresh(); Assert.IsTrue(mat.GetComponent<SpriteRenderer>().enabled);
                        preview.color=new Color(1,0,0,.4f); mat.Refresh();
                        Assert.AreEqual(preview.color,mat.GetComponent<SpriteRenderer>().color);
                        preview.color=Color.white;
                    }
                }
                Assert.AreEqual(1f,content.Theme.musicVolume);
                var music=root.AddComponent<RestaurantMusic>(); GameSettings.SetMusicVolume(1); music.Bind(content.Theme);
                yield return new WaitForSecondsRealtime(1);
                var source=(AudioSource)Field(music,"_current");
                Assert.IsNotNull(source.clip); Assert.AreEqual(1f,source.volume,.001f);
                GameSettings.SetMusicVolume(.4f); Assert.AreEqual(.4f,source.volume,.001f);
                GameSettings.SetMusicVolume(0); Assert.AreEqual(0,source.volume);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); GameSettings.SetMusicVolume(oldVolume);
                if(hadVolume) PlayerPrefs.SetFloat("settings.musicVolume",savedVolume); else PlayerPrefs.DeleteKey("settings.musicVolume");
            }
            yield return new ExitPlayMode();
        }

        private static void Capture(RestaurantView view,string filename)
        {
            var cameraGo=new GameObject("ProofCamera"); var camera=cameraGo.AddComponent<Camera>();
            var texture=new RenderTexture(1400,1050,24); var image=new Texture2D(1400,1050,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                camera.orthographic=true; camera.orthographicSize=2.8f;
                camera.transform.position=new Vector3(0,-1.1f,-10); camera.backgroundColor=new Color(.57f,.73f,.28f);
                camera.clearFlags=CameraClearFlags.SolidColor; camera.targetTexture=texture; camera.Render();
                RenderTexture.active=texture; image.ReadPixels(new Rect(0,0,1400,1050),0,0); image.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes(Path.Combine("Logs",filename),image.EncodeToPNG());
            }
            finally { RenderTexture.active=previous; camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(cameraGo); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
