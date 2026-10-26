using System;
using System.Collections;
using System.Linq;
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
using UnityEngine.TestTools;

namespace DemoRestaurant.Render.Tests
{
    public sealed class KitchenStatePresentationTests
    {
        private static object Field(object target,string key)=>target.GetType().GetField(key,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static void Refresh(RestaurantView view)=>typeof(RestaurantView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);

        [UnityTest]
        public IEnumerator NativePotsFollowCookingReadyAndPickup_InAllFourDirections()
        {
            yield return new EnterPlayMode();
            var content=ContentLoader.Load("malaysia");
            if(ArtLibrary.For(content.Theme).HintsFor("stove01_cooking_loop")==null)Assert.Ignore("Native kitchen states are not installed.");
            foreach(Facing facing in Enum.GetValues(typeof(Facing)))
            {
                var root=new GameObject("KitchenStateTest");
                try
                {
                    var room=new RestaurantLayout(10,10,new GridPos(0,8));
                    room.TryPlace(content.Pack.GetFurniture("gas_stove"),new GridPos(3,2),facing,out var stove);
                    room.TryPlace(content.Pack.GetFurniture("dining_table"),new GridPos(6,6),Facing.SouthWest,out _);
                    room.TryPlace(content.Pack.GetFurniture("wooden_chair"),new GridPos(6,7),Facing.NorthEast,out _);
                    var meal=content.Pack.Dishes.First(d=>d.Category!=DishCategory.Drink);
                    var sim=new RestaurantSim(room,new RestaurantMenu(new[]{meal},1),3);
                    var view=root.AddComponent<RestaurantView>();view.Bind(sim,content,Array.Empty<int>());
                    sim.Hire(content.Pack.StaffTypes.First(t=>t.Role==StaffRole.Chef),new GridPos(0,0));
                    var renderer=(SpriteRenderer)((IDictionary)Field(view,"_furniture"))[stove];
                    Refresh(view);Assert.IsFalse(renderer.sprite.name.Contains("_cooking_"));
                    sim.SpawnCustomer(new CustomerTypeDef("office_worker","g",4,9999,1,foodPatienceSeconds:9999));
                    void Until(Func<bool> done){for(int i=0;i<20000 && !done();i++)sim.Tick(.02f);Assert.IsTrue(done());Refresh(view);}
                    Until(()=>sim.Tickets.Any(t=>t.State==TicketState.Cooking && t.Chef?.State==StaffState.Cooking));
                    StringAssert.Contains("_cooking_loop_",renderer.sprite.name,facing.ToString());
                    var position=stove.Origin;var scale=renderer.transform.localScale;
                    if(facing==Facing.SouthWest)FurnitureAnimationTests.CaptureActivity(root,new Vector2(.5f,-.7f),"kitchen-native-cooking.png");
                    Until(()=>sim.Tickets.Any(t=>t.IsOnCounter));
                    StringAssert.Contains("_done_loop_",renderer.sprite.name,facing.ToString());
                    Assert.AreEqual(0,((IDictionary)Field(view,"_plates")).Count,"Native ready cookware replaces the fallback meal drawn over a stove.");
                    Assert.AreEqual(position,stove.Origin);Assert.AreEqual(scale,renderer.transform.localScale);
                    if(facing==Facing.SouthWest)FurnitureAnimationTests.CaptureActivity(root,new Vector2(.5f,-.7f),"kitchen-native-ready.png");
                    var waiter=sim.Hire(content.Pack.StaffTypes.First(t=>t.Role==StaffRole.Waiter),new GridPos(4,4));
                    Until(()=>waiter.IsCarryingPlate);
                    Assert.IsFalse(renderer.sprite.name.Contains("_cooking_"));Assert.IsFalse(renderer.sprite.name.Contains("_done_"));
                    Assert.IsFalse(room.Grid.IsWalkable(stove.Origin));
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            yield return new ExitPlayMode();
        }
    }
}
