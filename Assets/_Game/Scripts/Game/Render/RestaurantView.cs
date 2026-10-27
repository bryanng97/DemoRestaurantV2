using System.Collections.Generic;
using System.Diagnostics;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Iso;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace DemoRestaurant.Game.Render
{
    /// <summary>
    /// Draws a <see cref="RestaurantSim"/>: flat floor, back walls with windows and doors, one sprite per
    /// furniture piece, and characters. Owns no game state; everything it shows is read from the sim each frame.
    ///
    /// Sprites come from the pack's art folder (bottom-centre pivot) and are fitted to their footprint at
    /// runtime, so artists don't need to hit an exact pixel size. Missing sprites fall back to PlaceholderArt.
    /// </summary>
    public sealed class RestaurantView : MonoBehaviour
    {
        // Sorting bands on the Default layer. Dynamic objects get 0..N from the depth sorter.
        private const int FloorOrder = -30000;
        private const int WaitingAreaOrder = -29900;
        /// <summary>Street paving and the farm: above the floor, below the walls.</summary>
        public const int OutsideGroundOrder = -29880;
        /// <summary>Things outside the walls (street decoration, guests on the street) sort from here, behind the walls.</summary>
        private const int OutsideOrder = -29800;
        private const int WallsOrder = -29500;
        private const int WallDecorOrder = -29400;
        private const int ShadowOrder = -29000;

        /// <summary>Half-size of a character's footprint in cells, for depth sorting.</summary>
        private const float CharacterRadius = 0.2f;
        /// <summary>Trim padding the slicer adds around every sprite, in world units at 100 PPU.</summary>
        private const float SpritePad = 0.02f;

        private readonly Dictionary<Agent, CharacterView> _characters = new Dictionary<Agent, CharacterView>();
        private readonly Dictionary<OrderTicket, PlateView> _plates = new Dictionary<OrderTicket, PlateView>();
        private readonly Stack<SpriteRenderer> _freePlates = new Stack<SpriteRenderer>();
        private readonly List<OrderTicket> _platesToRemove = new List<OrderTicket>();
        private readonly Dictionary<DishDef, Sprite> _dishIcons = new Dictionary<DishDef, Sprite>();
        private readonly Dictionary<(DishDef Dish,int Level), Sprite[]> _servingFrames = new Dictionary<(DishDef,int), Sprite[]>();
        private readonly Dictionary<Customer, PlateView> _tableMeals = new Dictionary<Customer, PlateView>();
        private readonly List<Customer> _mealsToRemove = new List<Customer>();
        private Sprite _workMat;
        private readonly Dictionary<PlacedFurniture, SpriteRenderer> _furniture = new Dictionary<PlacedFurniture, SpriteRenderer>();
        private readonly Dictionary<string, Stack<CharacterView>> _pools = new Dictionary<string, Stack<CharacterView>>();

        private readonly List<DepthItem> _depthItems = new List<DepthItem>();
        private readonly List<Renderer> _depthTargets = new List<Renderer>();
        private readonly List<SortingGroup> _depthGroups = new List<SortingGroup>();
        /// <summary>Per depth item: outside the walls (street), so it sorts behind them.</summary>
        private readonly List<bool> _depthOutside = new List<bool>();
        private readonly List<int> _order = new List<int>();
        private readonly IsoDepthSorter _sorter = new IsoDepthSorter();
        private readonly Stopwatch _sortTimer = new Stopwatch();

        private RestaurantSim _sim;
        private readonly List<Sprite> _doorFrames = new List<Sprite>();
        private SpriteRenderer _doorRenderer;
        private float _doorFrame;
        private LoadedContentPack _content;
        private ArtLibrary _art;
        private sealed class FurnitureLoopPlayer
        {
            public FurnitureLoopFrames Frames;
            public Vector3 Position;
            public string Key, State = "idle";
            public float StateStarted;
            public PlacedFurniture Furniture;
            public Sprite IdleSprite;
        }
        private readonly Dictionary<string, FurnitureLoopFrames> _furnitureFrames = new Dictionary<string, FurnitureLoopFrames>();
        private readonly Dictionary<SpriteRenderer, FurnitureLoopPlayer> _furnitureLoops = new Dictionary<SpriteRenderer, FurnitureLoopPlayer>();
        private readonly List<SpriteRenderer> _deadFurnitureLoops = new List<SpriteRenderer>();
        private RestaurantLayout _activeLayout;
        private FlatFloorMesh _floor;
        private bool _editing;
        private IReadOnlyList<int> _windowSegments;
        private Sprite _waitingRug;
        private Transform _wallRoot, _waitingRoot, _outdoorRoot;
        private readonly List<Transform> _streetSegments = new List<Transform>();
        private Sprite _streetTile;
        private float _streetStep, _streetScale;
        private Vector2 _streetAnchorOffset;
        private int _streetFirst = int.MinValue, _streetCount;
        private readonly Dictionary<string, Sprite> _sidewalkSprites = new Dictionary<string, Sprite>();

        /// <summary>Milliseconds spent in the depth sort last frame (shown in the perf HUD).</summary>
        public double LastSortMs { get; private set; }
        public int CharacterCount => _characters.Count;

        public void Bind(RestaurantSim sim, LoadedContentPack content, IReadOnlyList<int> windowSegments)
        {
            _sim = sim;
            var cleaningFeedback = gameObject.AddComponent<TableCleaningFeedback>();
            cleaningFeedback.Bind(sim, this);
            _content = content;
            _art = ArtLibrary.For(content.Theme);
            _activeLayout = sim.Layout;
            _windowSegments = windowSegments;

            var grid = sim.Layout.Grid;
            BuildFloor(grid.Width, grid.Height);
            BuildWalls(sim.Layout.RoomWidth, sim.Layout.RoomHeight, windowSegments);
            BuildWallItems();
            BuildWaitingArea(sim.Layout.WaitingSpots);
            BuildStreet();

            foreach (var f in sim.Layout.Furniture)
                AddFurniture(f);
            sim.Layout.FurniturePlaced += AddFurniture;
            sim.Layout.FurnitureRemoved += RemoveFurniture;
            sim.Layout.FloorChanged += RefreshFloorCell;
            sim.Layout.BoundsChanged += RebuildRoom;
            sim.Layout.EntranceChanged += RebuildRoom;
            sim.Layout.WallItemsChanged += BuildWallItems;
            sim.Layout.WallpaperChanged += RebuildWalls;

            foreach (var c in sim.Customers)
                AddCustomer(c);
            sim.CustomerSpawned += AddCustomer;
            sim.CustomerLeft += RemoveCustomer;

            foreach (var s in sim.Staff)
                AddStaff(s);
            sim.StaffHired += AddStaff;
            sim.StaffDismissed += RemoveStaff;
        }

        private void OnDestroy()
        {
            if (_sim == null)
                return;
            _activeLayout.FurniturePlaced -= AddFurniture;
            _activeLayout.FurnitureRemoved -= RemoveFurniture;
            _activeLayout.FloorChanged -= RefreshFloorCell;
            _activeLayout.BoundsChanged -= RebuildRoom;
            _activeLayout.EntranceChanged -= RebuildRoom;
            _activeLayout.WallItemsChanged -= BuildWallItems;
            _activeLayout.WallpaperChanged -= RebuildWalls;
            if (_waitingRug != null) { Destroy(_waitingRug.texture); Destroy(_waitingRug); }
            if (_workMat != null) { Destroy(_workMat.texture); Destroy(_workMat); }
            foreach (var frames in _furnitureFrames.Values) frames?.Dispose();
            _sim.CustomerSpawned -= AddCustomer;
            _sim.CustomerLeft -= RemoveCustomer;
            _sim.StaffHired -= AddStaff;
            _sim.StaffDismissed -= RemoveStaff;
            Avatars?.Release(_ownerLookKey);
        }

        private void LateUpdate()
        {
            if (_sim == null)
                return;
            RefreshStreetCoverage();
            if (_floorDirty && _floor != null) { _floorDirty = false; _floor.Refresh(); }
            if (_wallsDirty) { _wallsDirty = false; RebuildWallsNow(); }

            float t = Time.time;
            AnimateFurniture(t);
            if (_editing) { SortDepth(); return; }
            foreach (var pair in _characters)
                pair.Value.Sync(pair.Key, PoseFor(pair.Key), t);

            SyncPlates();
            SyncTableMeals();
            SyncDirtyDishes();
            AnimateDoor(Time.deltaTime);
            TintBrokenToilets();
            SortDepth();
        }

        // ---------------------------------------------------------------- depth

        private void RegisterFurnitureLoop(SpriteRenderer renderer, string key, PlacedFurniture furniture = null)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!_furnitureFrames.TryGetValue(key, out var frames))
                _furnitureFrames[key] = frames = FurnitureLoopFrames.Load(_art, key);
            if (frames != null || furniture?.Def.Kind == FurnitureKind.Stove || furniture?.Def.Kind == FurnitureKind.Entertainment)
                _furnitureLoops[renderer] = new FurnitureLoopPlayer { Frames = frames, Position = renderer.transform.localPosition,
                    Key = key, Furniture = furniture, IdleSprite = renderer.sprite };
        }

        /// <summary>The moving preview shares the native loop, after the editor has applied its floating offset.</summary>
        public void AnimateFurniturePreview(SpriteRenderer renderer, FurnitureDef def, Facing facing, float time)
        {
            if (def == null || !renderer.enabled) return;
            var art = _content.ArtForFurniture(def.Id);
            string key = def.Kind != FurnitureKind.WallDecor && FacingUtil.IsBackView(facing) &&
                !string.IsNullOrEmpty(art.SpriteBack) && _art.GetOptional(art.SpriteBack) != null ? art.SpriteBack : art.Sprite;
            if (string.IsNullOrEmpty(key)) return;
            if (!_furnitureFrames.TryGetValue(key, out var frames)) _furnitureFrames[key] = frames = FurnitureLoopFrames.Load(_art, key);
            if (frames == null) return;
            renderer.sprite = frames.At(time);
            var offset = frames.AnchorOffset; var scale = renderer.transform.localScale;
            renderer.transform.localPosition -= new Vector3(offset.x*scale.x*(renderer.flipX ? -1 : 1), offset.y*scale.y, 0);
        }

        private void AnimateFurniture(float time)
        {
            _deadFurnitureLoops.Clear();
            foreach (var pair in _furnitureLoops)
            {
                var renderer = pair.Key;
                if (renderer == null) { _deadFurnitureLoops.Add(renderer); continue; }
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var player = pair.Value;
                string state = "idle";
                if (!_editing && player.Furniture?.Def.Kind == FurnitureKind.Stove)
                    foreach (var ticket in _sim.Tickets)
                        if (ticket.Stove == player.Furniture || ticket.Chef?.Stove == player.Furniture)
                        {
                            if (ticket.State == TicketState.Cooking && ticket.Chef?.State == StaffState.Cooking) { state = "cooking"; break; }
                            if (ticket.IsOnCounter) { state = "done"; break; }
                        }
                if (!_editing && player.Furniture?.Def.Kind == FurnitureKind.Entertainment)
                    state = _sim.IsBroken(player.Furniture) ? "broken" : _sim.IsEntertainmentPlaying(player.Furniture) ? "on" : "off";
                if (state != player.State) { player.State = state; player.StateStarted = time; }
                string animationKey = player.Key + (state == "idle" ? "" : "_" + state);
                if (!_furnitureFrames.TryGetValue(animationKey, out var frames))
                    _furnitureFrames[animationKey] = frames = FurnitureLoopFrames.Load(_art, player.Key, animationKey);
                frames = frames ?? player.Frames;
                renderer.sprite = frames != null ? frames.At(state == "idle" ? time : time - player.StateStarted) : player.IdleSprite;
                var offset = frames != null ? frames.AnchorOffset : Vector2.zero;
                var scale = renderer.transform.localScale;
                renderer.transform.localPosition = pair.Value.Position - new Vector3(offset.x * scale.x * (renderer.flipX ? -1 : 1), offset.y * scale.y, 0);
            }
            foreach (var renderer in _deadFurnitureLoops) _furnitureLoops.Remove(renderer);
        }

        private void SortDepth()
        {
            _sortTimer.Restart();
            _depthItems.Clear();
            _depthTargets.Clear();
            _depthGroups.Clear();
            _depthOutside.Clear();

            // A swinging door is at floor level: a divider behind it must not draw over its leaf.
            // Keep the walls in their static band, but sort the door with the room's furniture.
            if (_doorRenderer != null && _doorRenderer.enabled)
            {
                var door = _activeLayout.Entrance;
                var item = door.X == 0
                    ? DepthItem.FromFootprint(0, door.Y, .01f, door.Y + 1)
                    : DepthItem.FromFootprint(door.X, 0, door.X + 1, .01f);
                SetScreenBounds(ref item, _doorRenderer.bounds);
                _depthItems.Add(item); _depthTargets.Add(_doorRenderer);
                _depthGroups.Add(null); _depthOutside.Add(false);
            }

            foreach (var pair in _furniture)
            {
                var f = pair.Key;
                // A piece standing on a table sorts at the table's top, so it draws above it.
                var item = DepthItem.FromFootprint(f.Origin.X, f.Origin.Y, f.Origin.X + f.SizeX, f.Origin.Y + f.SizeY,
                    f.StackedOn != null ? f.StackedOn.Def.Height : 0f);
                SetScreenBounds(ref item, pair.Value.bounds);
                _depthItems.Add(item);
                _depthTargets.Add(pair.Value);
                _depthGroups.Add(null);
                _depthOutside.Add(_activeLayout.IsStreetCell(f.Origin)); // the garden is in front of the room: sorts normally
            }

            foreach (var pair in _characters)
            {
                if (_editing) break;
                Vec2 p = pair.Key.Position;
                // A chair facing away has its backrest in front of the sitter. Keep the real seat height
                // for positioning, but sort that sitter behind the chair's foreground frame.
                bool behindChair = pair.Key is Customer seated && seated.IsSeated && FacingUtil.IsBackView(seated.Facing);
                var item = DepthItem.FromFootprint(p.X - CharacterRadius, p.Y - CharacterRadius,
                    p.X + CharacterRadius, p.Y + CharacterRadius, behindChair ? -0.01f : pair.Key.Z);
                SetScreenBounds(ref item, pair.Value.Bounds);
                _depthItems.Add(item);
                _depthTargets.Add(null);
                _depthGroups.Add(pair.Value.Group);
                _depthOutside.Add(p.X < 0f || p.Y < 0f);
            }

            foreach (var pair in _plates)
            {
                if (_editing) break;
                // Same cell as its counter but higher, so it always draws on top of it.
                var spot = pair.Value.Spot;
                var item = DepthItem.FromFootprint(spot.X + 0.1f, spot.Y + 0.1f, spot.X + 0.9f, spot.Y + 0.9f, pair.Value.Z);
                SetScreenBounds(ref item, pair.Value.Renderer.bounds);
                _depthItems.Add(item);
                _depthTargets.Add(pair.Value.Renderer);
                _depthGroups.Add(null);
                _depthOutside.Add(false);
            }

            foreach (var pair in _dirtyDishes)
            {
                if (_editing) break;
                // Same cell as the table, but on top of it.
                var spot = pair.Value.Spot;
                var item = DepthItem.FromFootprint(spot.X + 0.1f, spot.Y + 0.1f, spot.X + 0.9f, spot.Y + 0.9f, pair.Value.Z);
                SetScreenBounds(ref item, pair.Value.Renderer.bounds);
                _depthItems.Add(item);
                _depthTargets.Add(pair.Value.Renderer);
                _depthGroups.Add(null);
                _depthOutside.Add(false);
            }

            foreach (var plate in _tableMeals.Values)
            {
                if (_editing) break;
                var spot = plate.Spot;
                var item = DepthItem.FromFootprint(spot.X+.1f,spot.Y+.1f,spot.X+.9f,spot.Y+.9f,plate.Z);
                SetScreenBounds(ref item,plate.Renderer.bounds);
                _depthItems.Add(item); _depthTargets.Add(plate.Renderer); _depthGroups.Add(null); _depthOutside.Add(false);
            }

            _sorter.Sort(_depthItems, _order);
            int outside = 0;
            for (int i = 0; i < _order.Count; i++)
            {
                int idx = _order[i];
                // Inside: 0, 1, 2... above the walls. Outside the walls: below them, still in depth order.
                int order = _depthOutside[idx] ? OutsideOrder + Mathf.Min(outside++, 250) : i;
                if (_depthGroups[idx] != null)
                    _depthGroups[idx].sortingOrder = order;
                else
                    _depthTargets[idx].sortingOrder = order;
            }
            // Furniture uses Renderer orders while characters use SortingGroups. Flush the group
            // cache after changing both so the next render cannot combine different frames' orders.
            SortingGroup.UpdateAllSortingGroups();
            _sortTimer.Stop();
            LastSortMs = _sortTimer.Elapsed.TotalMilliseconds;
        }

        private static void SetScreenBounds(ref DepthItem item, Bounds b)
        {
            item.ScreenMinX = b.min.x;
            item.ScreenMinY = b.min.y;
            item.ScreenMaxX = b.max.x;
            item.ScreenMaxY = b.max.y;
        }

        // ---------------------------------------------------------------- room

        private void BuildFloor(int width, int height)
        {
            var gridGo = new GameObject("FloorGrid");
            gridGo.transform.SetParent(transform, false);
            var floorGo = new GameObject("Floor");
            floorGo.transform.SetParent(gridGo.transform, false);
            _floor = floorGo.AddComponent<FlatFloorMesh>();
            _floor.Bind(_activeLayout, _content, _art, FloorOrder);
        }

        // Many cells can change in one frame (a paving drag, its live preview): redraw once, in LateUpdate.
        private bool _floorDirty, _wallsDirty;
        private void RefreshFloorCell(GridPos cell) => _floorDirty = true;

        /// <summary>Preview a draft while the simulation keeps its original layout and pauses.</summary>
        public void ShowLayout(RestaurantLayout layout, bool editing)
        {
            _activeLayout.FurniturePlaced -= AddFurniture;
            _activeLayout.FurnitureRemoved -= RemoveFurniture;
            _activeLayout.FloorChanged -= RefreshFloorCell;
            _activeLayout.BoundsChanged -= RebuildRoom;
            _activeLayout.EntranceChanged -= RebuildRoom;
            _activeLayout.WallItemsChanged -= BuildWallItems;
            _activeLayout.WallpaperChanged -= RebuildWalls;
            foreach (var sr in _furniture.Values) { sr.gameObject.SetActive(false); Destroy(sr.gameObject); }
            _furniture.Clear();
            _activeLayout = layout;
            _editing = editing;
            foreach (var f in layout.Furniture) AddFurniture(f);
            layout.FurniturePlaced += AddFurniture;
            layout.FurnitureRemoved += RemoveFurniture;
            layout.FloorChanged += RefreshFloorCell;
            layout.BoundsChanged += RebuildRoom;
            layout.EntranceChanged += RebuildRoom;
            layout.WallItemsChanged += BuildWallItems;
            layout.WallpaperChanged += RebuildWalls;
            RebuildRoom();
            foreach (var c in _characters.Values) c.SetActive(!editing);
            foreach (var p in _plates.Values) p.Renderer.enabled = !editing;
            foreach (var p in _tableMeals.Values) p.Renderer.enabled = !editing;
            foreach (var p in _dirtyDishes.Values) p.Renderer.enabled = !editing;
        }

        private void RebuildWalls() => _wallsDirty = true;

        private void RebuildWallsNow()
        {
            if (_wallRoot != null) { _wallRoot.gameObject.SetActive(false); Destroy(_wallRoot.gameObject); }
            _wallRoot = null;
            BuildWalls(_activeLayout.RoomWidth, _activeLayout.RoomHeight, _windowSegments);
        }

        private readonly Dictionary<string, string> _wallpaperSprites = new Dictionary<string, string>();

        /// <summary>The wall art for one wall cell: its wallpaper, or the theme's wall.</summary>
        private Sprite WallSpriteAt(Sprite fallback, WallSide side, int cell)
        {
            var id = _activeLayout.WallpaperAt(side, cell);
            if (id == null) return fallback;
            if (_wallpaperSprites.Count == 0)
                foreach (var w in _content.Theme.wallpapers) _wallpaperSprites[w.id] = w.sprite;
            return _wallpaperSprites.TryGetValue(id, out var key) ? _art.GetOptional(key) ?? fallback : fallback;
        }

        private void RebuildRoom()
        {
            foreach (var old in new[] { _floor != null ? _floor.transform.parent : null, _wallRoot, _waitingRoot, _outdoorRoot })
            {
                if (old != null) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
            }
            _wallRoot = _waitingRoot = _outdoorRoot = null;
            var grid = _activeLayout.Grid;
            BuildFloor(grid.Width, grid.Height);
            BuildWalls(_activeLayout.RoomWidth, _activeLayout.RoomHeight, _windowSegments);
            BuildWallItems();
            BuildWaitingArea(_activeLayout.WaitingSpots);
            BuildStreet();
        }

        /// <summary>Debug overlay: how a character is drawn right now, or "no view".</summary>
        public string DescribeCharacter(Agent agent) => _characters.TryGetValue(agent, out var view) ? view.Describe() : "no view";

        public PlacedFurniture PickFurniture(Vector2 world)
        {
            PlacedFurniture picked = null;
            int highest = int.MinValue;
            foreach (var pair in _furniture)
                if (pair.Value.enabled && pair.Value.bounds.Contains(new Vector3(world.x, world.y, pair.Value.transform.position.z)) && pair.Value.sortingOrder > highest)
                { picked = pair.Key; highest = pair.Value.sortingOrder; }
            return picked;
        }

        public void SetFurnitureVisible(PlacedFurniture furniture, bool visible)
        {
            if (furniture != null && _furniture.TryGetValue(furniture, out var renderer)) renderer.enabled = visible;
        }

        /// <summary>
        /// How far a piece standing on <paramref name="surface"/> is drawn above the floor (0 on the floor). With native
        /// skin art the top is measured from the picture: its height minus the top face's diamond is the side's height.
        /// Otherwise the surface's height from furniture.json is used.
        /// </summary>
        public float StackLift(PlacedFurniture surface)
        {
            if (surface == null) return 0f;
            var art = _art.Resolve(_content.ArtForFurniture(surface.Def.Id));
            var sprite = FurnitureSprite(art, surface.Facing);
            if (sprite != null && art.TilePixelWidth > 0f)
            {
                // A baked floor mat extends the front sprite's base, not the stove's worktop height.
                int frontCells = sprite == _art.Get(art.Sprite) ? art.FrontCells : 0;
                float diamond = (surface.SizeX + surface.SizeY + frontCells) * art.TilePixelWidth / 4f;
                float side = Mathf.Max(0f, sprite.rect.height - diamond);
                return side / art.TilePixelWidth * 2f * IsoProjection.HalfTileWidth;
            }
            return surface.Def.Height * IsoProjection.HeightScale;
        }

        public void DrawFurniturePreview(SpriteRenderer sr, FurnitureDef def, GridPos origin, Facing facing)
        {
            var art = _art.Resolve(_content.ArtForFurniture(def.Id));
            bool rotated = facing == Facing.SouthEast || facing == Facing.NorthWest;
            int sx = rotated ? def.SizeY : def.SizeX, sy = rotated ? def.SizeX : def.SizeY;
            var sprite = FurnitureSprite(art, facing);
            sr.sprite = sprite ?? PlaceholderArt.Prism(sx, sy, def.Height, art.PlaceholderColor);
            var oldMat = sr.transform.Find("WorkMat");
            if (oldMat != null) oldMat.gameObject.SetActive(false);
            sr.flipX = FurnitureMirrored(facing) ^ art.MirrorSprite;
            if (sprite == null)
            {
                sr.flipX = false; // The procedural prism is already drawn for the rotated footprint.
                sr.transform.localPosition = IsoProjection.GridToWorld(origin);
                sr.transform.localScale = Vector3.one;
                return;
            }
            Vector2 centre = IsoProjection.GridToWorld(origin.X + sx * 0.5f, origin.Y + sy * 0.5f);
            if (FurnitureGrounding.Apply(sr, art, centre, sx, sy)) { DrawBackWorkMat(sr,def,art,origin,facing); return; }
            // The extra cells (a stove mat) are only drawn on the front view.
            if (art.FrontCells > 0 && sprite == _art.Get(art.Sprite))
            {
                // The art also covers cells in front of the footprint (e.g. a stove's mat): fit to that wider area.
                float ox = origin.X, oy = origin.Y;
                int k = art.FrontCells;
                switch (facing)
                {
                    case Facing.SouthWest: sy += k; break;
                    case Facing.SouthEast: sx += k; break;
                    case Facing.NorthEast: oy -= k; sy += k; break;
                    default: ox -= k; sx += k; break;
                }
                centre = IsoProjection.GridToWorld(ox + sx * 0.5f, oy + sy * 0.5f);
            }
            float width = (sx + sy) * IsoProjection.HalfTileWidth * art.Scale;
            float depth = (sx + sy) * 0.5f * IsoProjection.HalfTileHeight * art.Scale;
            // Native-size skins: one cell is TilePixelWidth pixels; otherwise stretch to the footprint width.
            float scale = art.TilePixelWidth > 0
                ? 2f * IsoProjection.HalfTileWidth / art.TilePixelWidth * sprite.pixelsPerUnit * art.Scale
                : width / sprite.bounds.size.x;
            if (art.TilePixelWidth > 0)
            {
                // Native art narrower than its footprint (a chair, a plant) stands in the middle of it: its base is taken
                // as a diamond as wide as the art, centred on the footprint.
                float footprintWidth = (sx + sy) * IsoProjection.HalfTileWidth;
                depth = (sx + sy) * 0.5f * IsoProjection.HalfTileHeight * Mathf.Min(1f, sprite.bounds.size.x * scale / footprintWidth);
            }
            sr.transform.localPosition = new Vector3(centre.x, centre.y - depth - SpritePad, 0);
            sr.transform.localScale = new Vector3(scale, scale, 1);
            DrawBackWorkMat(sr,def,art,origin,facing);
        }

        private void DrawBackWorkMat(SpriteRenderer body, FurnitureDef def, FurnitureArt art, GridPos origin, Facing facing)
        {
            if (art.FrontCells <= 0 || !FacingUtil.IsBackView(facing) ||
                FurnitureSprite(art,facing) == _art.Get(art.Sprite)) return;
            var child = body.transform.Find("WorkMat");
            if (child == null)
            {
                child = new GameObject("WorkMat").transform; child.SetParent(body.transform,false);
                child.gameObject.AddComponent<SpriteRenderer>();
                child.gameObject.AddComponent<FurnitureWorkMat>().Body = body;
            }
            var renderer = child.GetComponent<SpriteRenderer>();
            if (_workMat == null) _workMat = PlaceholderArt.Floor(1,1,new Color32(55,59,53,255),new Color32(55,59,53,255));
            renderer.sprite = _workMat; renderer.sortingOrder = FloorOrder + 1;
            var front = FurnitureWorkArea.FrontTile(def, origin, facing);
            var point = body.transform.parent.TransformPoint(IsoProjection.GridToWorld(front));
            child.localPosition = body.transform.InverseTransformPoint(point);
            child.localScale = new Vector3(1f/body.transform.localScale.x,1f/body.transform.localScale.y,1);
            child.gameObject.SetActive(true); child.GetComponent<FurnitureWorkMat>().Refresh();
        }

        /// <summary>A rug-coloured marking on each waiting-area cell so players can see where the queue forms.</summary>
        private void BuildWaitingArea(IReadOnlyList<GridPos> spots)
        {
            if (spots.Count == 0)
                return;
            if (_waitingRug == null) _waitingRug = PlaceholderArt.Floor(1, 1,
                new Color(0.78f, 0.36f, 0.3f, 0.45f), new Color(0.78f, 0.36f, 0.3f, 0.45f));
            var root = new GameObject("WaitingArea").transform;
            _waitingRoot = root;
            root.SetParent(transform, false);
            foreach (var spot in spots)
            {
                var go = new GameObject($"Spot{spot}");
                go.transform.SetParent(root, false);
                go.transform.localPosition = IsoProjection.GridToWorld(spot);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _waitingRug;
                sr.sortingOrder = WaitingAreaOrder;
            }
        }

        private void BuildWalls(int width, int height, IReadOnlyList<int> windowSegments)
        {
            var wall = _art.Get(_content.Theme.wall);
            if (wall == null)
            {
                var windows = new List<float>();
                foreach (int s in windowSegments) windows.Add(s * 3 + 0.8f);
                _wallRoot = CreateStatic("Walls", PlaceholderArt.BackWalls(width, height, 3f, new Color(0.96f, 0.87f, 0.70f),
                    new Color(0.55f, 0.36f, 0.24f), windows, new float[0]), Vector2.zero, WallsOrder);
                return;
            }

            var window = _art.Get(_content.Theme.window);
            // A skin can animate the door: frames <door>_open_00, _01, ... (closed to open).
            _doorFrames.Clear();
            for (int i = 0; ; i++)
            {
                var frame = _art.GetOptional($"{_content.Theme.door}_open_{i:D2}");
                if (frame == null) break;
                _doorFrames.Add(frame);
            }
            string doorKey = _doorFrames.Count > 0 ? _content.Theme.door + "_open_00" : _content.Theme.door;
            var door = _art.Get(doorKey);
            _doorRenderer = null; _doorFrame = 0f;
            // n = cells per wall segment (gameplay: the door takes one segment); m = cells one wall sprite covers.
            int n = Mathf.Max(1, _content.Theme.wallCellsPerSegment);
            var wallSkin = _art.SkinOf(_content.Theme.wall);
            int m = wallSkin != null && wallSkin.wallCellsPerSegment > 0 ? wallSkin.wallCellsPerSegment : n;
            float segWidth = n * IsoProjection.HalfTileWidth;
            var root = new GameObject("Walls").transform;
            _wallRoot = root;
            root.SetParent(transform, false);
            // The door is wherever the layout's entrance is (it can be moved in the build editor).
            var (doorWall, doorSeg) = DoorPlanner.SegmentOf(_activeLayout.Entrance, n);

            // Back-right wall runs along grid Y = 0 (sprites are drawn for this wall); the back-left wall along
            // X = 0 uses the same art, mirrored.
            // Single-cell wall art leaves a doorway at the entrance cell (the door fills it; open, you see outside).
            var entrance = _activeLayout.Entrance;
            int doorCell = m == 1 && door != null ? (doorWall == DoorWall.Right ? entrance.X : entrance.Y) : -1;
            for (int i = 0, x = 0; x < width; x += m, i++)
                if (!(doorWall == DoorWall.Right && x == doorCell))
                    Place(root, $"WallR{i}", WallSpriteAt(wall, WallSide.Right, x), WallBottom(x, Mathf.Min(x + m, width), rightWall: true), m * IsoProjection.HalfTileWidth, false, WallsOrder + i);
            for (int i = 0, y = 0; y < height; y += m, i++)
                if (!(doorWall == DoorWall.Left && y == doorCell))
                    Place(root, $"WallL{i}", WallSpriteAt(wall, WallSide.Left, y), WallBottom(y, Mathf.Min(y + m, height), rightWall: false), m * IsoProjection.HalfTileWidth, true, WallsOrder + i);
            if (doorCell >= 0)
            {
                bool rightDoor = doorWall == DoorWall.Right;
                Vector2 a = rightDoor ? IsoProjection.GridToWorld(doorCell, 0) : IsoProjection.GridToWorld(0, doorCell);
                Vector2 b = rightDoor ? IsoProjection.GridToWorld(doorCell + 1, 0) : IsoProjection.GridToWorld(0, doorCell + 1);
                _doorRenderer = PlaceDoor(root, door, (a.x + b.x) * 0.5f, rightDoor, doorKey, segWidth);
            }

            for (int rightPass = 0; rightPass < 2; rightPass++)
            {
                bool right = rightPass == 1;
                int length = right ? width : height;
                for (int seg = 0, c = 0; c < length; c += n, seg++)
                {
                    Vector2 a = right ? IsoProjection.GridToWorld(c, 0) : IsoProjection.GridToWorld(0, c);
                    Vector2 b = right ? IsoProjection.GridToWorld(Mathf.Min(c + n, length), 0) : IsoProjection.GridToWorld(0, Mathf.Min(c + n, length));
                    float cx = (a.x + b.x) * 0.5f;
                    string side = right ? "R" : "L";
                    if (door != null && doorWall == (right ? DoorWall.Right : DoorWall.Left) && seg == doorSeg && doorCell < 0)
                        _doorRenderer = PlaceOnWall(root, $"Door{side}{seg}", door, cx, WallDecorWidth(doorKey, door, segWidth * 0.55f), rightWall: right, lift: 0f);
                    else if (window != null && Contains(windowSegments, seg) && !(doorCell >= 0 && doorWall == (right ? DoorWall.Right : DoorWall.Left) && seg == doorSeg))
                        PlaceOnWall(root, $"Window{side}{seg}", window, cx, WallDecorWidth(_content.Theme.window, window, segWidth * 0.5f), rightWall: right, lift: 0.35f);
                }
            }
        }

        /// <summary>Bottom-centre of a wall sprite covering cells [from, to) of one back wall.</summary>
        private static Vector2 WallBottom(int from, int to, bool rightWall)
        {
            Vector2 a = rightWall ? IsoProjection.GridToWorld(from, 0) : IsoProjection.GridToWorld(0, from);
            Vector2 b = rightWall ? IsoProjection.GridToWorld(to, 0) : IsoProjection.GridToWorld(0, to);
            return new Vector2((a.x + b.x) * 0.5f, b.y - SpritePad);
        }

        /// <summary>Native width for skins drawn at native size, otherwise the given share of a segment.</summary>
        private float WallDecorWidth(string key, Sprite sprite, float fitted)
        {
            var skin = _art.SkinOf(key);
            if (skin == null || skin.tilePixelWidth <= 0) return fitted;
            var hints = _art.HintsFor(key);
            return sprite.rect.width / skin.tilePixelWidth * 2f * IsoProjection.HalfTileWidth * (hints != null && hints.scale > 0 ? hints.scale : 1f);
        }

        private static readonly Color BrokenTint = new Color(1f, 0.55f, 0.5f);

        /// <summary>Broken toilets show red until fixed (tap one to fix it, or a cleaner does).</summary>
        private void TintBrokenToilets()
        {
            foreach (var pair in _furniture)
                if (pair.Key.Def.Kind == FurnitureKind.Toilet || pair.Key.Def.Kind == FurnitureKind.Entertainment)
                    pair.Value.color = _sim.IsBroken(pair.Key) ? BrokenTint : Color.white;
        }

        /// <summary>Door frames per second while it swings.</summary>
        private const float DoorFps = 24f;

        /// <summary>Opens the door while a guest is at it (within 1.5 cells of the entrance), closes it otherwise.</summary>
        private void AnimateDoor(float dt)
        {
            if (_doorRenderer == null || _doorFrames.Count < 2) return;
            var entrance = _activeLayout.Entrance;
            bool someoneAtDoor = false;
            foreach (var c in _sim.Customers)
            {
                float dx = c.Position.X - entrance.X, dy = c.Position.Y - entrance.Y;
                if (dx * dx + dy * dy < 2.25f) { someoneAtDoor = true; break; }
            }
            float target = someoneAtDoor ? _doorFrames.Count - 1 : 0f;
            _doorFrame = Mathf.MoveTowards(_doorFrame, target, dt * DoorFps);
            _doorRenderer.sprite = _doorFrames[Mathf.Clamp(Mathf.RoundToInt(_doorFrame), 0, _doorFrames.Count - 1)];
        }

        /// <summary>
        /// The door in its doorway. Animated skin doors share one canvas for every frame; the closed (first) frame is
        /// lined up with the doorway using the skin's anchor, so the swing plays around the hinge.
        /// </summary>
        private SpriteRenderer PlaceDoor(Transform root, Sprite door, float cx, bool rightWall, string doorKey, float segWidth)
        {
            var go = new GameObject("Door");
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = WallDecorOrder;
            PositionDoor(sr, door, cx, rightWall, doorKey, segWidth);
            return sr;
        }

        private void PositionDoor(SpriteRenderer sr, Sprite door, float cx, bool rightWall, string doorKey, float segWidth)
        {
            var anim = _doorFrames.Count > 0 ? _art.HintsFor(_content.Theme.door + "_open") : null;
            var skin = _art.ManifestWithHints(_content.Theme.door + "_open");
            if (anim == null || anim.firstFrameWidth <= 0f || skin == null || skin.tilePixelWidth <= 0f)
            {
                PositionOnWall(sr, door, cx, WallDecorWidth(doorKey, door, segWidth * 0.55f), rightWall, 0f);
                return;
            }
            float worldPerPixel = 2f * IsoProjection.HalfTileWidth / skin.tilePixelWidth;
            float canvasWidth = door.rect.width * worldPerPixel, canvasHeight = door.rect.height * worldPerPixel;
            PositionOnWall(sr, door, cx, canvasWidth, rightWall, 0f);
            // Where the closed door's bottom-centre must go: as if it were the whole sprite.
            float closedWidth = anim.firstFrameWidth * worldPerPixel;
            float lowX = rightWall ? cx + closedWidth * 0.5f : cx - closedWidth * 0.5f;
            float lineY = rightWall ? -lowX * 0.5f : lowX * 0.5f;
            // The canvas is mirrored on the back-left wall, so its anchor offset flips too.
            float dx = (anim.anchor.x - 0.5f) * canvasWidth * (rightWall ? 1f : -1f);
            sr.transform.localPosition = new Vector3(cx - dx, lineY - SpritePad - anim.anchor.y * canvasHeight, 0f);
        }

        public void SetDoorVisible(bool visible)
        {
            if (_doorRenderer != null) _doorRenderer.enabled = visible;
        }

        /// <summary>The closed door drawn in the back-left wall cell <paramref name="index"/> (the editor's door preview).</summary>
        public void DrawDoorPreview(SpriteRenderer sr, int index)
        {
            string doorKey = _doorFrames.Count > 0 ? _content.Theme.door + "_open_00" : _content.Theme.door;
            var door = _doorFrames.Count > 0 ? _doorFrames[0] : _art.Get(_content.Theme.door);
            if (door == null) { sr.sprite = PlaceholderArt.Prism(1, 1, 1.4f, new Color(0.9f, 0.85f, 0.7f)); return; }
            Vector2 a = IsoProjection.GridToWorld(0, index), b = IsoProjection.GridToWorld(0, index + 1);
            float segWidth = Mathf.Max(1, _content.Theme.wallCellsPerSegment) * IsoProjection.HalfTileWidth;
            PositionDoor(sr, door, (a.x + b.x) * 0.5f, rightWall: false, doorKey, segWidth);
        }

        // ---------------------------------------------------------------- pieces hung on the back walls

        private Transform _wallItemsRoot;
        private readonly Dictionary<PlacedWallItem, SpriteRenderer> _wallItems = new Dictionary<PlacedWallItem, SpriteRenderer>();

        private void BuildWallItems()
        {
            if (_wallItemsRoot != null) Destroy(_wallItemsRoot.gameObject);
            _wallItems.Clear();
            _wallItemsRoot = new GameObject("WallItems").transform;
            _wallItemsRoot.SetParent(transform, false);
            foreach (var w in _activeLayout.WallItems)
            {
                var go = new GameObject($"{w.Def.Id}#{w.InstanceId}");
                go.transform.SetParent(_wallItemsRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = WallDecorOrder;
                DrawWallItemPreview(sr, w.Def, w.Side, w.Index);
                RegisterFurnitureLoop(sr, _content.ArtForFurniture(w.Def.Id).Sprite);
                _wallItems[w] = sr;
            }
        }

        public void SetWallItemVisible(PlacedWallItem item, bool visible)
        {
            if (item != null && _wallItems.TryGetValue(item, out var sr)) sr.enabled = visible;
        }

        /// <summary>
        /// Draws a wall piece on the wall plane over cells [index, index + width). The art is drawn for the back-right
        /// wall and mirrored on the back-left one; the skin's <c>wallLift</c> sets how high it hangs.
        /// </summary>
        public void DrawWallItemPreview(SpriteRenderer sr, FurnitureDef def, WallSide side, int index)
        {
            bool right = side == WallSide.Right;
            int width = System.Math.Max(1, def.SizeX);
            Vector2 a = right ? IsoProjection.GridToWorld(index, 0) : IsoProjection.GridToWorld(0, index);
            Vector2 b = right ? IsoProjection.GridToWorld(index + width, 0) : IsoProjection.GridToWorld(0, index + width);
            float cx = (a.x + b.x) * 0.5f;
            var art = _content.ArtForFurniture(def.Id);
            var sprite = _art.Get(art.Sprite);
            var hints = _art.HintsFor(art.Sprite);
            float lift = hints != null && hints.wallLift > 0f ? hints.wallLift : 0.35f;
            if (sprite == null)
            {
                sprite = PlaceholderArt.Prism(1, 1, 0.6f, art.PlaceholderColor);
                lift = 0.35f;
            }
            float fitted = width * IsoProjection.HalfTileWidth * 0.6f;
            PositionOnWall(sr, sprite, cx, WallDecorWidth(art.Sprite, sprite, fitted), right, lift);
        }

        /// <summary>
        /// The wall piece under a world point (a point on the wall plane, not the floor), or the wall cell it is over.
        /// </summary>
        public static bool WallCellAt(Vector2 local, RestaurantLayout layout, out WallSide side, out int index)
        {
            // On the back-left wall (grid X = 0) world x = -y / 2; on the back-right (Y = 0) world x = x / 2.
            // Height above the wall's floor line tells the wall from the floor in front of it and the street behind it.
            const float wallHeight = 1.7f;
            side = local.x < 0f ? WallSide.Left : WallSide.Right;
            float h = side == WallSide.Left ? local.y - local.x * 0.5f : local.y + local.x * 0.5f;
            index = Mathf.FloorToInt(Mathf.Abs(local.x) / IsoProjection.HalfTileWidth);
            return h > 0f && h < wallHeight && index >= 0 && index < layout.WallLength(side);
        }

        /// <summary>Places a wall-mounted sprite so its lowest corner sits `lift` above the wall's floor line.</summary>
        /// <summary>Like <see cref="PlaceOnWall"/>, for an existing renderer (wall pieces and their editor preview).</summary>
        private static void PositionOnWall(SpriteRenderer sr, Sprite sprite, float cx, float width, bool rightWall, float lift)
        {
            float lowX = rightWall ? cx + width * 0.5f : cx - width * 0.5f;
            float lineY = rightWall ? -lowX * 0.5f : lowX * 0.5f;
            sr.sprite = sprite;
            sr.flipX = !rightWall;
            sr.transform.localPosition = new Vector3(cx, lineY + lift - SpritePad, 0f);
            float s = width / sprite.bounds.size.x;
            sr.transform.localScale = new Vector3(s, s, 1f);
        }

        private static SpriteRenderer PlaceOnWall(Transform parent, string name, Sprite sprite, float cx, float width, bool rightWall, float lift)
        {
            // Floor line of the back-right wall is y = -x/2, of the back-left wall y = x/2.
            // The sprite's lowest point is its far-end corner (right end on the right wall, left end on the left).
            float lowX = rightWall ? cx + width * 0.5f : cx - width * 0.5f;
            float lineY = rightWall ? -lowX * 0.5f : lowX * 0.5f;
            return Place(parent, name, sprite, new Vector2(cx, lineY + lift - SpritePad), width, !rightWall, WallDecorOrder);
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }

        /// <summary>Creates a sprite whose bottom-centre pivot lands on `bottomCentre`, scaled to `width` world units.</summary>
        private static SpriteRenderer Place(Transform parent, string name, Sprite sprite, Vector2 bottomCentre, float width, bool flipX, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = bottomCentre;
            float s = width / sprite.bounds.size.x;
            go.transform.localScale = new Vector3(s, s, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.flipX = flipX;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>
        /// Builds reusable street segments behind the walls. Their coverage follows the camera so the road and
        /// pavements continue beyond both visible ends independently of the playable guest route.
        /// </summary>
        private void BuildStreet()
        {
            var layout = _activeLayout;
            _outdoorRoot = new GameObject("Outside").transform;
            _outdoorRoot.SetParent(transform, false);
            _streetSegments.Clear(); _streetFirst = int.MinValue; _streetCount = 0;
            _streetTile = _art.GetOptional("street_tile");
            var skin = _art.SkinOf("street_tile");
            if (_streetTile != null && skin != null && skin.tilePixelWidth > 0)
            {
                var anchor = _art.HintsFor("street_tile")?.anchor ?? Vector2.zero;
                _streetScale = 2f * IsoProjection.HalfTileWidth / skin.tilePixelWidth * _streetTile.pixelsPerUnit;
                float width = _streetTile.bounds.size.x * _streetScale;
                _streetAnchorOffset = new Vector2((anchor.x - .5f) * width, anchor.y * _streetTile.bounds.size.y * _streetScale);
                _streetStep = width / IsoProjection.HalfTileWidth;
            }
            else
            {
                _streetTile = null; _streetStep = 16; _streetScale = 1;
                if (RestaurantLayout.StreetGap > 0)
                    AddGround("DoorPath", -RestaurantLayout.StreetGap, layout.Entrance.Y, RestaurantLayout.StreetGap, 1,
                        new Color(.84f, .80f, .72f), new Color(.80f, .76f, .68f));
            }
            RefreshStreetCoverage();
        }

        private void RefreshStreetCoverage()
        {
            if (_outdoorRoot == null || _streetStep <= 0) return;
            var camera = Camera.main;
            if (camera == null) return;
            float halfHeight = camera.orthographicSize, halfWidth = halfHeight * camera.aspect;
            var position = camera.transform.position;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            // Inverse-project every visible corner; diagonal roads need coverage beyond both X and Y edges.
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    var point = transform.InverseTransformPoint(new Vector3(position.x + x * halfWidth, position.y + y * halfHeight, 0));
                    float gridY = IsoProjection.WorldToGrid(point).Y;
                    min = Mathf.Min(min, gridY); max = Mathf.Max(max, gridY);
                }
            StreetSegmentRange(min, max, _streetStep, out int first, out int count);
            if (first == _streetFirst && count == _streetCount) return;
            _streetFirst = first; _streetCount = count;
            while (_streetSegments.Count < count) _streetSegments.Add(CreateStreetSegment(_streetSegments.Count));
            for (int i = 0; i < _streetSegments.Count; i++)
            {
                var segment = _streetSegments[i];
                segment.gameObject.SetActive(i < count);
                if (i >= count) continue;
                float y = (first + i) * _streetStep;
                segment.localPosition = _streetTile != null
                    ? IsoProjection.GridToWorld(-RestaurantLayout.StreetGap, y) - _streetAnchorOffset
                    : IsoProjection.GridToWorld(0, y);
            }
        }

        private static void StreetSegmentRange(float min, float max, float step, out int first, out int count)
        {
            // Two spare repeats cover the tile's own diagonal footprint and movement between camera updates.
            first = Mathf.FloorToInt(min / step) - 2;
            count = Mathf.CeilToInt(max / step) + 2 - first + 1;
        }

        private Transform CreateStreetSegment(int index)
        {
            var segment = new GameObject("Street" + index).transform;
            segment.SetParent(_outdoorRoot, false);
            if (_streetTile != null)
            {
                var renderer = segment.gameObject.AddComponent<SpriteRenderer>();
                renderer.sprite = _streetTile; renderer.sortingOrder = OutsideGroundOrder;
                segment.localScale = Vector3.one * _streetScale;
            }
            else
            {
                var sidewalk = _art.SceneColor(m => m.sidewalkColor, new Color(.93f, .71f, .71f));
                var asphalt = _art.SceneColor(m => m.roadColor, new Color(.45f, .46f, .48f));
                const int road = 6;
                AddStreetStrip(segment, "Sidewalk", RestaurantLayout.StreetOuterX, RestaurantLayout.StreetWidth, sidewalk);
                AddStreetStrip(segment, "Road", RestaurantLayout.StreetOuterX - road, road, asphalt);
                AddStreetStrip(segment, "FarSidewalk", RestaurantLayout.StreetOuterX - road - RestaurantLayout.StreetWidth,
                    RestaurantLayout.StreetWidth, sidewalk);
            }
            return segment;
        }

        private void AddStreetStrip(Transform parent, string name, int x, int width, Color color)
        {
            var strip = new GameObject(name).transform;
            strip.SetParent(parent, false); strip.localPosition = IsoProjection.GridToWorld(x, 0);
            var renderer = strip.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = SidewalkSprite(width, (int)_streetStep, color, Shade(color, .98f));
            renderer.sortingOrder = OutsideGroundOrder;
        }

        private static Color Shade(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);

        private void AddGround(string name, int x, int y, int w, int h, Color a, Color b, int lift = 0)
        {
            CreateStatic(name, SidewalkSprite(w, h, a, b), IsoProjection.GridToWorld(x, y), OutsideGroundOrder + lift)
                .SetParent(_outdoorRoot, true);
        }

        private Sprite SidewalkSprite(int w, int h, Color a, Color b)
        {
            string key = w + "x" + h + ":" + ColorUtility.ToHtmlStringRGB(a) + ColorUtility.ToHtmlStringRGB(b);
            if (!_sidewalkSprites.TryGetValue(key, out var sprite))
                _sidewalkSprites[key] = sprite = PlaceholderArt.Floor(w, h, a, b);
            return sprite;
        }

        private Transform CreateStatic(string name, Sprite sprite, Vector2 position, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return go.transform;
        }

        // ---------------------------------------------------------------- furniture

        private void AddFurniture(PlacedFurniture f)
        {
            var art = _content.ArtForFurniture(f.Def.Id);
            var sprite = FurnitureSprite(art, f.Facing);
            string name = $"{f.Def.Id}#{f.InstanceId}";
            SpriteRenderer sr;
            if (sprite != null)
            {
                // Sprite spans the footprint's width (left corner to right corner) times its art scale,
                // centred on the footprint, with its lowest pixel on the footprint's front corner.
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                sr = go.AddComponent<SpriteRenderer>();
                DrawFurniturePreview(sr, f.Def, f.Origin, f.Facing);
                sr.transform.localPosition += new Vector3(0f, StackLift(f.StackedOn), 0f);
            }
            else
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = IsoProjection.GridToWorld(f.Origin);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PlaceholderArt.Prism(f.SizeX, f.SizeY, f.Def.Height, art.PlaceholderColor);
            }
            _furniture[f] = sr;
            RegisterFurnitureLoop(sr, FacingUtil.IsBackView(f.Facing) && !string.IsNullOrEmpty(art.SpriteBack) && _art.GetOptional(art.SpriteBack) != null ? art.SpriteBack : art.Sprite, f);
        }

        /// <summary>The back view when facing away and the art has one (any skin), otherwise the front view.</summary>
        private Sprite FurnitureSprite(FurnitureArt art, Facing facing) =>
            (FacingUtil.IsBackView(facing) && !string.IsNullOrEmpty(art.SpriteBack) ? _art.GetOptional(art.SpriteBack) : null) ?? _art.Get(art.Sprite);

        private static bool FurnitureMirrored(Facing facing) => facing == Facing.SouthEast || facing == Facing.NorthWest;

        private void RemoveFurniture(PlacedFurniture f)
        {
            if (_furniture.TryGetValue(f, out var sr))
            {
                sr.gameObject.SetActive(false);
                Destroy(sr.gameObject);
                _furniture.Remove(f);
                _furnitureLoops.Remove(sr);
            }
        }

        // ---------------------------------------------------------------- characters

        private void AddCustomer(Customer c) => AddCharacter(c, "customer:" + c.Type.Id, _content.ArtForCustomer(c.Type.Id));

        /// <summary>Draws the player's avatar and avatar-styled staff (set before <see cref="Bind"/>).</summary>
        public AvatarPainter Avatars { get; set; }
        /// <summary>Recipe grade selects the native plate/coaster style; standalone views default to grade one.</summary>
        public System.Func<DishDef,int> RecipeLevel { get; set; }
        /// <summary>The player's current look, for their avatar working in the restaurant.</summary>
        public System.Func<Logic.Profile.AvatarLook> OwnerLook { get; set; }
        public System.Func<bool> OwnerCustomized { get; set; }

        private void AddStaff(Staff s)
        {
            if(s.IsOwner && (OwnerCustomized?.Invoke()??false) && Avatars!=null && Avatars.NativeAvailable && OwnerLook?.Invoke() is Logic.Profile.AvatarLook customized)
            {
                _ownerLookKey=Avatars.Retain(customized);
                var view=CharacterView.Create(transform,"owner",Avatars.NativeFrames(customized),_content.Theme.characterHeight);
                view.Bind(s);_characters[s]=view;_ownerBaked=false;
                return;
            }
            var art = _content.ArtForStaff(s.Type.Id);
            if (FramesFor(art) != null)
            {
                // Baked animation frames in the skin win over the avatar painter (the owner too, for now).
                _ownerBaked |= s.IsOwner;
                AddCharacter(s, "staff:" + s.Type.Id, art);
                return;
            }
            var look = s.IsOwner ? OwnerLook?.Invoke() : art.Avatar;
            if (look == null || Avatars == null)
                AddCharacter(s, "staff:" + s.Type.Id, art);
            else if (s.IsOwner)
            {
                // Only one of you: never pooled, rebuilt when the look changes.
                _ownerLookKey = Avatars.Retain(look);
                _characters[s] = CreateAvatarView(s, look, "owner");
            }
            else
            {
                if (!_pools.TryGetValue("staff:" + s.Type.Id, out var pool))
                    _pools["staff:" + s.Type.Id] = pool = new Stack<CharacterView>();
                if (pool.Count > 0)
                {
                    var view = pool.Pop();
                    view.Bind(s);
                    _characters[s] = view;
                }
                else
                {
                    Avatars.Retain(look); // a staff type's look stays for the whole session
                    _characters[s] = CreateAvatarView(s, look, "staff:" + s.Type.Id);
                }
            }
        }

        private string _ownerLookKey;
        private bool _ownerBaked;
        private readonly Dictionary<string, CharacterFrames> _frameSets = new Dictionary<string, CharacterFrames>();

        /// <summary>The skin's baked frames for this character, or null when it has none.</summary>
        private CharacterFrames FramesFor(CharacterArt art)
        {
            if (string.IsNullOrEmpty(art?.AnimKey)) return null;
            if (!_frameSets.TryGetValue(art.AnimKey, out var set))
                _frameSets[art.AnimKey] = set = CharacterFrames.Load(_art, art.AnimKey);
            return set;
        }

        /// <summary>The player changed their look: redraw their avatar in the restaurant.</summary>
        public void RefreshOwner()
        {
            if (_sim == null || Avatars == null || OwnerLook == null) return;
            if (_ownerBaked && (!(OwnerCustomized?.Invoke()??false)||!Avatars.NativeAvailable)) return;
            var look = OwnerLook();
            if (look == null || look.Key == _ownerLookKey) return;
            foreach (var s in _sim.Staff)
            {
                if (!s.IsOwner || !_characters.TryGetValue(s, out var old)) continue;
                old.Destroy();
                _characters.Remove(s);
                Avatars.Release(_ownerLookKey);
                _ownerLookKey = null;
                _ownerBaked = false;
                AddStaff(s);
            }
        }

        private CharacterView CreateAvatarView(Agent agent, Logic.Profile.AvatarLook look, string poolKey)
        {
            // Chibi proportions: a bit taller than the concept sprites so the face reads at the same size.
            var view = CharacterView.Create(transform, poolKey, Avatars.Body(look), Avatars.Back(look), null, null, 1f, 0.5f, 0f,
                Color.white, _content.Theme.characterHeight * 1.18f, AvatarPainter.LegFraction);
            view.Bind(agent);
            return view;
        }

        private void AddCharacter(Agent agent, string poolKey, CharacterArt art)
        {
            if (!_pools.TryGetValue(poolKey, out var pool))
                _pools[poolKey] = pool = new Stack<CharacterView>();

            var frames = FramesFor(art);
            var view = pool.Count > 0 ? pool.Pop() : frames != null
                ? CharacterView.Create(transform, poolKey, frames, _content.Theme.characterHeight)
                : CharacterView.Create(transform, poolKey,
                _art.Get(art.Front), _art.Get(art.Back) ?? _art.Get(art.Front),
                _art.Get(art.SeatedFront), _art.Get(art.SeatedBack), art.SeatedHeight, art.SeatedHipX, art.SeatedHipY,
                art.PlaceholderColor, _content.Theme.characterHeight, art.LegFraction);
            view.Bind(agent);
            _characters[agent] = view;
        }

        private void RemoveCustomer(Customer c) => RemoveCharacter(c);
        private void RemoveStaff(Staff s)
        {
            RemoveCharacter(s);
            if (s.IsOwner) { Avatars?.Release(_ownerLookKey); _ownerLookKey = null; _ownerBaked = false; }
        }

        private void RemoveCharacter(Agent agent)
        {
            if (_characters.TryGetValue(agent, out var view))
            {
                if (_pools.TryGetValue(view.PoolKey, out var pool))
                {
                    view.SetActive(false);
                    pool.Push(view);
                }
                else
                    view.Destroy(); // the owner's view isn't pooled
                _characters.Remove(agent);
            }
        }

        private Sprite DishIcon(DishDef dish)
        {
            if (dish == null)
                return null;
            if (!_dishIcons.TryGetValue(dish, out var icon))
                _dishIcons[dish] = icon = _art.Get(_content.IconForDish(dish.Id));
            return icon;
        }

        private Sprite[] ServingFrames(DishDef dish)
        {
            if (dish == null) return System.Array.Empty<Sprite>();
            var cacheKey = (dish,ServingLevel(dish));
            if (_servingFrames.TryGetValue(cacheKey,out var frames)) return frames;
            string key = ServingKey(dish);
            var found = new List<Sprite>();
            if (!string.IsNullOrEmpty(key))
                for (int i = 0; i < 64; i++)
                {
                    var frame = _art.GetOptional(key+"_"+i.ToString("D2"));
                    if (frame == null) break;
                    found.Add(frame);
                }
            if (found.Count == 0)
            {
                var sprite = _art.GetOptional(key) ?? DishIcon(dish);
                if (sprite != null) found.Add(sprite);
            }
            return _servingFrames[cacheKey] = found.ToArray();
        }

        private int ServingLevel(DishDef dish) => dish == null ? 1 : Mathf.Clamp(RecipeLevel?.Invoke(dish) ?? 1,1,10);
        private string ServingKey(DishDef dish)
        {
            string key = _content.ServingForDish(dish?.Id);
            if (string.IsNullOrEmpty(key)) return key;
            string variant = key+"_level"+ServingLevel(dish).ToString("D2");
            return _art.GetOptional(variant+"_00") != null ? variant : key;
        }

        private Sprite ServingSprite(DishDef dish) => ServingFrame(dish,0);
        private Sprite ServingFrame(DishDef dish,float progress)
        {
            var frames = ServingFrames(dish);
            return frames.Length == 0 ? null : frames[Mathf.FloorToInt(Mathf.Clamp01(progress)*(frames.Length-1))];
        }

        private float ServingWidth(DishDef dish)
        {
            string key = ServingKey(dish);
            var skin = string.IsNullOrEmpty(key) ? null : _art.SkinOf(key+"_00") ?? _art.SkinOf(key);
            return DishPresentation.WidthFor(dish,ServingSprite(dish),skin?.tilePixelWidth ?? 0);
        }

        private float ServingAnchor(DishDef dish)
        {
            var hints = _art.HintsFor(ServingKey(dish));
            return hints != null && hints.anchor != Vector2.zero ? hints.anchor.y : -1f;
        }

        /// <summary>The skin's seat height for a chair's art, or -1 when the skin doesn't set one.</summary>
        private float SkinSeatHeight(PlacedFurniture seat)
        {
            var key = _content.ArtForFurniture(seat.Def.Id).Sprite;
            var hints = _art.HintsFor(key);
            return hints != null && hints.seatHeight > 0f ? hints.seatHeight : -1f;
        }

        /// <summary>What a character is doing this frame, as far as drawing is concerned.</summary>
        private CharacterPose PoseFor(Agent agent)
        {
            if (agent is Customer c)
            {
                bool waiting = c.State == CustomerState.Ordering || c.State == CustomerState.WaitingForFood;
                // Patience bar while queuing for a seat or waiting for food: green when calm, red when about to leave.
                bool impatient = c.State == CustomerState.WaitingForFood || c.State == CustomerState.WaitingInQueue;
                float patience = c.PatienceLeft;
                bool bathroom = c.State == CustomerState.OnToilet || c.State == CustomerState.UsingSink;
                bool playing = c.State == CustomerState.PlayingEntertainment;
                var fixture = playing ? c.Entertainment : c.BathroomFixture;
                return new CharacterPose
                {
                    Moving = c.IsMoving,
                    Seated = c.IsSeated,
                    SeatZ = c.IsSeated ? SkinSeatHeight(c.Seat) : -1f,
                    Eating = c.State == CustomerState.Eating,
                    // The reference uses its cooking hand motion while the customer faces the sink.
                    Cooking = c.State == CustomerState.UsingSink || playing,
                    ShowBubble = waiting || bathroom || playing,
                    BubbleIcon = waiting ? ServingSprite(c.Order) : (bathroom || playing) && fixture != null ? _art.GetOptional(_content.ArtForFurniture(fixture.Def.Id).Sprite) : null,
                    Progress = playing ? _sim.EntertainmentProgress(c) : bathroom ? _sim.BathroomProgress(c) : impatient ? patience : -1f,
                    ActivityKey = playing ? "ui.activity.playing" : bathroom ? (c.State == CustomerState.OnToilet ? "ui.activity.toilet" : "ui.activity.washing") : null,
                    ProgressColor = (bathroom || playing) ? new Color(.25f,.75f,1f) : Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.45f, 0.85f, 0.35f), patience),
                    Tint = c.IsAngry ? new Color(1f, 0.6f, 0.55f) : Color.white,
                };
            }

            var s = (Staff)agent;
            bool cooking = s.State == StaffState.Cooking || s.State == StaffState.Cleaning || s.State == StaffState.Pouring
                || s.State == StaffState.Fixing || s.State == StaffState.PickingTrash;
            // Bar over the head: cooking progress while cooking; otherwise energy once it starts to matter
            // (below full speed) or while resting (blue, filling up).
            float energy = s.Energy / StaffEnergyRules.Max;
            bool showEnergy = !cooking && (s.IsResting || s.SpeedMultiplier < 1f);
            return new CharacterPose
            {
                Moving = s.IsWalking,
                ShowBubble = s.State == StaffState.Cooking,
                Cooking = s.State == StaffState.Cooking || s.State == StaffState.Pouring,
                Cleaning = s.State == StaffState.Cleaning || s.State == StaffState.PickingTrash,
                Fixing = s.State == StaffState.Fixing,
                BubbleIcon = s.State == StaffState.Cooking ? ServingSprite(s.Ticket?.Dish) : null,
                Progress = cooking ? s.CookProgress : showEnergy ? energy : -1f,
                ProgressColor = cooking ? new Color(0.45f, 0.85f, 0.35f)
                    : s.IsResting ? new Color(0.43f, 0.63f, 0.9f)
                    : Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.95f, 0.8f, 0.25f), energy),
                Tint = s.IsResting ? new Color(0.82f, 0.88f, 1f) : Color.white,
                CarryIcon = s.IsCarryingPlate ? ServingSprite(s.Ticket?.Dish) : null,
                CarryWidth = ServingWidth(s.Ticket?.Dish),
                CarryOffsetY = DishPresentation.PivotOffset(ServingSprite(s.Ticket?.Dish),ServingWidth(s.Ticket?.Dish),ServingAnchor(s.Ticket?.Dish)),
            };
        }

        // ---------------------------------------------------------------- cooked plates waiting on stoves

        private bool NativeReadyPot(PlacedFurniture stove)
        {
            if (stove == null) return false;
            var art = _content.ArtForFurniture(stove.Def.Id);
            string key = FacingUtil.IsBackView(stove.Facing) && !string.IsNullOrEmpty(art.SpriteBack) && _art.GetOptional(art.SpriteBack) != null ? art.SpriteBack : art.Sprite;
            return _art.SkinOf(key)?.ByKey.ContainsKey(key + "_done_loop") == true;
        }

        private void SyncPlates()
        {
            foreach (var t in _sim.Tickets)
            {
                if (!t.IsOnCounter || NativeReadyPot(t.Stove))
                    continue;
                var sprite = ServingSprite(t.Dish);
                if (sprite == null)
                    continue;

                var plate = _plates.TryGetValue(t,out var existing) ? existing.Renderer :
                    _freePlates.Count > 0 ? _freePlates.Pop() : CreatePlateRenderer();
                var spot = t.CounterSpot.Value;
                var counter = _sim.Layout.FindAt(spot);
                float z = counter != null ? counter.Def.Height : 0.8f;
                float width = ServingWidth(t.Dish);
                DishPresentation.Fit(plate,sprite,width);
                float lift = counter != null ? StackLift(counter) : z * IsoProjection.HeightScale;
                plate.transform.localPosition = IsoProjection.GridToWorld(spot.X + 0.5f, spot.Y + 0.5f) + new Vector2(0,lift+DishPresentation.PivotOffset(sprite,width,ServingAnchor(t.Dish)));
                z = lift / IsoProjection.HeightScale;
                plate.gameObject.SetActive(true);
                plate.enabled = true;
                _plates[t] = new PlateView { Renderer = plate, Spot = spot, Z = z };
            }

            _platesToRemove.Clear();
            foreach (var pair in _plates)
            {
                if (!pair.Key.IsOnCounter || NativeReadyPot(pair.Key.Stove))
                    _platesToRemove.Add(pair.Key);
            }
            foreach (var t in _platesToRemove)
            {
                var plate = _plates[t].Renderer;
                plate.gameObject.SetActive(false);
                _freePlates.Push(plate);
                _plates.Remove(t);
            }
        }

        // ---------------------------------------------------------------- dirty dishes left on tables

        private readonly Dictionary<PlacedFurniture, PlateView> _dirtyDishes = new Dictionary<PlacedFurniture, PlateView>();
        private readonly List<PlacedFurniture> _dishesToRemove = new List<PlacedFurniture>();
        private Sprite _dirtyPlate;

        private void SyncTableMeals()
        {
            foreach (var customer in _sim.Customers)
            {
                if (customer.State != CustomerState.Eating || customer.Table == null) continue;
                float progress = customer.Order.EatSeconds > 0 ? 1f-customer.TimeRemaining/customer.Order.EatSeconds : 1f;
                var sprite = ServingFrame(customer.Order,progress);
                if (sprite == null) continue;
                var plate = _tableMeals.TryGetValue(customer,out var existing) ? existing.Renderer :
                    _freePlates.Count > 0 ? _freePlates.Pop() : CreatePlateRenderer();
                PlaceOnDiningTable(plate,sprite,customer.Table,customer.Seat,ServingWidth(customer.Order),ServingAnchor(customer.Order),out var view);
                _tableMeals[customer] = view;
            }
            _mealsToRemove.Clear();
            foreach (var pair in _tableMeals) if (pair.Key.State != CustomerState.Eating || pair.Key.Table == null) _mealsToRemove.Add(pair.Key);
            foreach (var customer in _mealsToRemove)
            {
                var plate = _tableMeals[customer].Renderer;
                plate.gameObject.SetActive(false); _freePlates.Push(plate); _tableMeals.Remove(customer);
            }
        }

        private void PlaceOnDiningTable(SpriteRenderer plate,Sprite sprite,PlacedFurniture table,PlacedFurniture seat,float width,float anchorY,out PlateView view)
        {
            var piece = table ?? seat;
            float lift = table != null ? StackLift(table) : seat.Def.SeatHeight * IsoProjection.HeightScale;
            DishPresentation.Fit(plate,sprite,width);
            plate.transform.localPosition = IsoProjection.GridToWorld(piece.Origin.X+piece.SizeX*.5f,piece.Origin.Y+piece.SizeY*.5f)
                + new Vector2(0,lift+DishPresentation.PivotOffset(sprite,width,anchorY));
            plate.enabled = true; plate.gameObject.SetActive(true);
            view = new PlateView { Renderer = plate,Spot = piece.Origin,Z = lift/IsoProjection.HeightScale };
        }

        /// <summary>Empty plates on the table in front of every seat that still needs clearing.</summary>
        private void SyncDirtyDishes()
        {
            foreach (var seat in _sim.DirtySeats)
            {
                if (_dirtyDishes.ContainsKey(seat))
                    continue;
                // The shared table's centre; on the seat if this pack permits lone chairs.
                var table = SeatFacing.TableFor(_sim.Layout,seat);
                var plate = _freePlates.Count > 0 ? _freePlates.Pop() : CreatePlateRenderer();
                var dish = _sim.DirtyDishFor(seat);
                bool hasAnimation = ServingFrames(dish).Length > 1;
                if (_dirtyPlate == null) _dirtyPlate = _art.GetOptional("plate_empty") ?? DirtyPlateSprite();
                var sprite = hasAnimation ? ServingFrame(dish,1) : _dirtyPlate;
                float width = hasAnimation ? ServingWidth(dish) :
                    DishPresentation.WidthFor(null,sprite,_art.SkinOf("plate_empty")?.tilePixelWidth ?? 0);
                PlaceOnDiningTable(plate,sprite,table,seat,width,hasAnimation ? ServingAnchor(dish) : .5f,out var view);
                _dirtyDishes[seat] = view;
            }

            _dishesToRemove.Clear();
            foreach (var pair in _dirtyDishes)
                if (!_sim.IsSeatDirty(pair.Key))
                    _dishesToRemove.Add(pair.Key);
            foreach (var seat in _dishesToRemove)
            {
                var plate = _dirtyDishes[seat].Renderer;
                plate.gameObject.SetActive(false);
                _freePlates.Push(plate);
                _dirtyDishes.Remove(seat);
            }
        }

        public SpriteRenderer DirtyPlateRenderer(PlacedFurniture seat) =>
            seat != null && _dirtyDishes.TryGetValue(seat, out var plate) ? plate.Renderer : null;

        /// <summary>Tap the empty plate or its table. The larger plate hit area stays usable on a phone.</summary>
        public bool TryTapDirtyTable(Vector2 world)
        {
            if (_editing) return false;
            PlacedFurniture best = null;
            float distance = float.PositiveInfinity;
            float radius = Camera.main != null && Camera.main.orthographic
                ? Camera.main.orthographicSize * 44f / Mathf.Max(1, Screen.height) : .1f;
            var picked = PickFurniture(world);
            foreach (var pair in _dirtyDishes)
            {
                if (!_sim.IsSeatDirty(pair.Key)) continue;
                var bounds = pair.Value.Renderer.bounds;
                bounds.Expand(new Vector3(radius * 2, radius * 2, 0));
                var table = SeatFacing.TableFor(_sim.Layout, pair.Key);
                if (!bounds.Contains(new Vector3(world.x, world.y, bounds.center.z)) && picked != table && picked != pair.Key) continue;
                float d = ((Vector2)pair.Value.Renderer.bounds.center - world).sqrMagnitude;
                if (d < distance) { best = pair.Key; distance = d; }
            }
            if (best == null) return false;
            _sim.TogglePlayerCleaning(best);
            return true; // An active task still owns the tap; do not open something behind the table.
        }

        /// <summary>An empty plate with leftovers and a spoon, drawn once (stand-in until there is art).</summary>
        private static Sprite DirtyPlateSprite()
        {
            const int w = 96, h = 32;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float nx = (x + 0.5f - w * 0.5f) / (w * 0.5f - 1f), ny = (y + 0.5f - h * 0.5f) / (h * 0.5f - 1f);
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                Color c = new Color(0, 0, 0, 0);
                if (r < 1f) c = r > 0.86f ? new Color(0.72f, 0.7f, 0.66f) : r > 0.62f ? new Color(0.97f, 0.96f, 0.93f) : new Color(0.92f, 0.9f, 0.86f);
                // Leftover smudges and grains of rice.
                float s1 = (nx + 0.2f) * (nx + 0.2f) * 4f + (ny - 0.1f) * (ny - 0.1f) * 9f;
                float s2 = (nx - 0.3f) * (nx - 0.3f) * 9f + (ny + 0.25f) * (ny + 0.25f) * 16f;
                if (r < 0.6f && (s1 < 0.35f || s2 < 0.25f)) c = new Color(0.62f, 0.38f, 0.2f);
                if (r < 0.6f && ((x * 7 + y * 13) % 23 == 0)) c = Color.white;
                // Spoon handle across the rim.
                if (Mathf.Abs(ny * 0.5f - (nx - 0.55f) * 0.9f) < 0.06f && nx > 0.35f && nx < 1.05f) c = new Color(0.78f, 0.8f, 0.84f);
                px[y * w + x] = c;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "dirty_plate", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.3f), 100f);
        }

        private SpriteRenderer CreatePlateRenderer()
        {
            var go = new GameObject("Plate");
            go.transform.SetParent(transform, false);
            return go.AddComponent<SpriteRenderer>();
        }

        private struct PlateView
        {
            public SpriteRenderer Renderer;
            public GridPos Spot;
            public float Z;
        }

        private struct CharacterPose
        {
            public bool Moving;
            public bool Seated;
            /// <summary>Seated: the skin's seat height for this chair, or -1 to use the simulation's.</summary>
            public float SeatZ;
            public bool Eating;
            /// <summary>A chef cooking: stirring motion at the stove.</summary>
            public bool Cooking;
            public bool Cleaning;
            public bool Fixing;
            public bool ShowBubble;
            public string ActivityKey;
            public Sprite BubbleIcon;
            /// <summary>0..1 for a progress bar under the bubble, negative for none.</summary>
            public float Progress;
            public Color ProgressColor;
            /// <summary>Body tint (red-ish for angry customers).</summary>
            public Color Tint;
            public Sprite CarryIcon;
            public float CarryWidth;
            public float CarryOffsetY;
        }

        /// <summary>
        /// A standing sprite cut into upper body and two legs, so one still image can walk.
        /// Cut once per sprite and shared by every character using it.
        /// </summary>
        private sealed class WalkRig
        {
            private static readonly Dictionary<Sprite, WalkRig> Cache = new Dictionary<Sprite, WalkRig>();
            private const int OverlapPixels = 3;

            public Sprite Upper, LegLeft, LegRight;
            /// <summary>Where the upper body and each leg sit, in the sprite's own units (relative to the sprite's centre).</summary>
            public float UpperY, LegLeftX, LegRightX, Height;

            /// <summary>Null when the sprite can't be cut (e.g. tightly packed in an atlas): use the whole sprite.</summary>
            public static WalkRig For(Sprite sprite, float legFraction)
            {
                if (sprite == null)
                    return null;
                if (Cache.TryGetValue(sprite, out var rig))
                    return rig;
                try
                {
                    var tex = sprite.texture;
                    var r = sprite.textureRect; // throws for tight-packed atlas sprites
                    float ppu = sprite.pixelsPerUnit;
                    float legH = Mathf.Round(r.height * Mathf.Clamp(legFraction, 0.05f, 0.5f));
                    float halfW = Mathf.Floor(r.width * 0.5f);
                    // Each leg reaches a few pixels past the middle, so no gap opens when they swing apart.
                    float leftW = halfW + OverlapPixels, rightX = halfW - OverlapPixels, rightW = r.width - rightX;
                    rig = new WalkRig
                    {
                        // Upper body overlaps the legs by a few pixels so no seam shows when it bobs.
                        Upper = Sprite.Create(tex, new Rect(r.x, r.y + legH - OverlapPixels, r.width, r.height - legH + OverlapPixels),
                            new Vector2(0.5f, 0f), ppu, 0, SpriteMeshType.FullRect),
                        LegLeft = Sprite.Create(tex, new Rect(r.x, r.y, leftW, legH), new Vector2(0.5f, 0f), ppu, 0, SpriteMeshType.FullRect),
                        LegRight = Sprite.Create(tex, new Rect(r.x + rightX, r.y, rightW, legH), new Vector2(0.5f, 0f), ppu, 0, SpriteMeshType.FullRect),
                        UpperY = (legH - OverlapPixels) / ppu,
                        LegLeftX = (leftW * 0.5f - r.width * 0.5f) / ppu,
                        LegRightX = (rightX + rightW * 0.5f - r.width * 0.5f) / ppu,
                        Height = r.height / ppu,
                    };
                }
                catch (System.Exception)
                {
                    rig = null;
                }
                Cache[sprite] = rig;
                return rig;
            }
        }

        /// <summary>
        /// One character: floor shadow, body sprite (front/back, mirrored for the other two facings),
        /// a bubble (order or dish being cooked) with optional progress bar, and a carried plate.
        /// Standing and walking use a <see cref="WalkRig"/> (legs step, body bobs and sways); sitting uses the whole sprite.
        /// </summary>
        private sealed class CharacterView
        {
            // Walk feel, in world units / degrees. One full cycle = a left step and a right step.
            private const float CycleCells = 1.1f;
            private const float LegLift = 0.028f; // small: the concept sprites have a floor shadow drawn under the feet
            private const float LegSwing = 0.022f;
            private const float BodyBob = 0.035f;
            private const float BodySwayDegrees = 3.5f;
            private const float FootfallSquash = 0.035f;

            private Transform _root, _visual, _shadowT, _progressFill;
            private SpriteRenderer _shadowSr;
            private RectTransform _activityCanvas;
            private UnityEngine.UI.Text _activityText;
            private SpriteRenderer _body, _head, _bubble, _icon, _carry, _progressBack, _progressBar;
            private Transform _rigRoot, _upperT, _legLT, _legRT;
            private SpriteRenderer _upper, _legL, _legR;
            private Sprite _front, _back;
            private Sprite _seatedFront, _seatedBack;
            private CharacterFrames _frames;
            private Rect _seatedFrontBounds, _seatedBackBounds;
            private float _height, _seatedHeight, _seatedHipX, _seatedHipY, _legFraction;
            private float _phase, _walkPhase, _stride, _shadowBase = 0.8f;
            private Vec2 _lastPosition;
            private bool _rigActive;

            public string PoolKey { get; private set; }
            public SortingGroup Group { get; private set; }
            public Bounds Bounds
            {
                get
                {
                    if (_rigActive)
                    {
                        var r = _upper.bounds;
                        r.Encapsulate(_legL.bounds);
                        r.Encapsulate(_legR.bounds);
                        return r;
                    }
                    var b = _body.bounds;
                    if (_head != null) b.Encapsulate(_head.bounds);
                    return b;
                }
            }

            public static CharacterView Create(Transform parent, string poolKey, Sprite front, Sprite back,
                Sprite seatedFront, Sprite seatedBack, float seatedHeight, float seatedHipX, float seatedHipY, Color tint, float height,
                float legFraction = 0.22f)
            {
                var v = new CharacterView { PoolKey = poolKey, _front = front, _back = back,
                    _seatedFront = seatedFront, _seatedBack = seatedBack, _height = height,
                    _seatedHeight = seatedHeight, _seatedHipX = seatedHipX, _seatedHipY = seatedHipY, _legFraction = legFraction };
                v._seatedFrontBounds = InkBounds(seatedFront);
                v._seatedBackBounds = InkBounds(seatedBack ?? seatedFront);
                var root = new GameObject(poolKey);
                root.transform.SetParent(parent, false);
                v._root = root.transform;
                v.Group = root.AddComponent<SortingGroup>();

                // The shadow lives outside the sorting group so it stays under all furniture.
                var shadowGo = new GameObject("Shadow");
                shadowGo.transform.SetParent(parent, false);
                v._shadowT = shadowGo.transform;
                var shadowSr = shadowGo.AddComponent<SpriteRenderer>();
                shadowSr.sprite = PlaceholderArt.Shadow();
                shadowSr.sortingOrder = ShadowOrder;
                v._shadowSr = shadowSr;
                shadowGo.transform.localScale = new Vector3(0.8f, 0.8f, 1f);

                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                v._visual = visual.transform;

                float bubbleY;
                if (front != null)
                {
                    v._body = AddLayer(visual.transform, "Body", front, 0, Vector3.zero);
                    float s = height / front.bounds.size.y;
                    v._body.transform.localScale = new Vector3(s, s, 1f);
                    bubbleY = height + 0.02f;
                }
                else
                {
                    v._body = AddLayer(visual.transform, "Body", PlaceholderArt.CharacterBody(), 0, Vector3.zero);
                    v._body.color = tint;
                    v._head = AddLayer(visual.transform, "Head",
                        PlaceholderArt.CharacterHead(false, new Color(0.96f, 0.80f, 0.66f), new Color(0.18f, 0.13f, 0.11f)),
                        1, new Vector3(0, 0.48f, 0));
                    bubbleY = 0.95f;
                }

                // Walk rig: legs under the upper body; scaled and mirrored as one piece.
                var rigGo = new GameObject("WalkRig");
                rigGo.transform.SetParent(visual.transform, false);
                v._rigRoot = rigGo.transform;
                v._legL = AddLayer(v._rigRoot, "LegLeft", null, 0, Vector3.zero);
                v._legR = AddLayer(v._rigRoot, "LegRight", null, 0, Vector3.zero);
                v._upper = AddLayer(v._rigRoot, "Upper", null, 1, Vector3.zero);
                v._legLT = v._legL.transform;
                v._legRT = v._legR.transform;
                v._upperT = v._upper.transform;
                rigGo.SetActive(false);

                // Carried plate: in front of the body, at hand height.
                v._carry = AddLayer(visual.transform, "Carry", null, 5, new Vector3(0.12f, bubbleY * 0.38f, 0));

                v._bubble = AddLayer(visual.transform, "Bubble", PlaceholderArt.Bubble(), 2, new Vector3(0.18f, bubbleY, 0));
                v._bubble.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
                v._icon = AddLayer(v._bubble.transform, "Icon", null, 3, new Vector3(0.0625f, 0.1f, 0));

                // Cooking progress bar under the bubble (bubble-local units).
                // Progress / patience bar just above the head, independent of the bubble.
                v._progressBack = AddLayer(visual.transform, "ProgressBack", PlaceholderArt.Pixel(), 3, new Vector3(-0.2f, bubbleY - 0.03f, 0));
                v._progressBack.color = new Color(0.25f, 0.16f, 0.1f, 0.85f);
                v._progressBack.transform.localScale = new Vector3(0.4f * 100f, 0.05f * 100f, 1f);
                v._progressBar = AddLayer(visual.transform, "ProgressFill", PlaceholderArt.Pixel(), 4, new Vector3(-0.19f, bubbleY - 0.03f, 0));
                v._progressBar.color = new Color(0.45f, 0.85f, 0.35f);
                v._progressFill = v._progressBar.transform;
                return v;
            }

            /// <summary>A character drawn from the skin's baked animation frames.</summary>
            public static CharacterView Create(Transform parent, string poolKey, CharacterFrames frames, float height)
            {
                var idle = frames.Get(CharacterFrames.Idle, false)[0];
                var v = Create(parent, poolKey, idle, idle, null, null, 1f, 0.5f, 0f, Color.white, height);
                v._frames = frames;
                return v;
            }

            private static Rect InkBounds(Sprite sprite)
            {
                if (sprite == null) return default;
                var vertices = sprite.vertices; // Tight sprite mesh excludes transparent padding; no texture readback.
                if (vertices.Length == 0) return new Rect(sprite.bounds.min, sprite.bounds.size);
                Vector2 min = vertices[0], max = min;
                foreach (var vertex in vertices) { min = Vector2.Min(min, vertex); max = Vector2.Max(max, vertex); }
                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }

            private static SpriteRenderer AddLayer(Transform parent, string name, Sprite sprite, int order, Vector3 offset)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = offset;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = order;
                return sr;
            }

            public void Bind(Agent a)
            {
                _phase = (a.Id * 0.618f) % 1f * Mathf.PI * 2f;
                _walkPhase = 0f;
                _stride = 0f;
                _lastPosition = a.Position;
                _icon.sprite = null;
                _carry.sprite = null;
                SetActive(true);
            }

            public void SetActive(bool active)
            {
                _root.gameObject.SetActive(active);
                _shadowT.gameObject.SetActive(active);
            }

            /// <summary>Debug overlay: shown or hidden, and where it is drawn.</summary>
            public string Describe() => (_root.gameObject.activeInHierarchy ? "shown" : "HIDDEN") + " @" + (Vector2)_root.position;

            public void Destroy()
            {
                Object.Destroy(_root.gameObject);
                Object.Destroy(_shadowT.gameObject);
            }

            public void Sync(Agent a, in CharacterPose pose, float time)
            {
                SyncActivity(pose);
                _root.localPosition = IsoProjection.GridToWorld(a.Position, pose.Seated && pose.SeatZ >= 0f ? pose.SeatZ : a.Z);
                _shadowT.localPosition = IsoProjection.GridToWorld(a.Position);
                // Outside the walls (on the street) the shadow goes behind the walls with the character.
                _shadowSr.sortingOrder = a.Position.X < 0f || a.Position.Y < 0f ? OutsideGroundOrder + 1 : ShadowOrder;

                bool mirrored = FacingUtil.IsMirrored(a.Facing);
                bool backView = FacingUtil.IsBackView(a.Facing);
                bool seatedArt = pose.Seated && _seatedFront != null;

                // Stride follows the distance actually walked, so feet don't slide (tired staff step slower too).
                float moved = (a.Position - _lastPosition).Length;
                _lastPosition = a.Position;
                if (moved > 0.75f) moved = 0f; // teleported (respawn, renovation): no giant step
                bool walking = pose.Moving && moved > 1e-4f;
                _stride = Mathf.MoveTowards(_stride, walking ? 1f : 0f, Time.deltaTime * 6f);
                if (walking)
                    _walkPhase += moved / CycleCells * Mathf.PI * 2f;

                if (_frames != null)
                {
                    SyncFrames(a, pose, time, mirrored, backView);
                    return;
                }

                var rig = _front != null && !seatedArt ? WalkRig.For(backView ? _back : _front, _legFraction) : null;
                _rigActive = rig != null;
                _rigRoot.gameObject.SetActive(_rigActive);
                _body.enabled = !_rigActive;

                if (_front != null)
                {
                    _body.sprite = seatedArt ? (backView ? _seatedBack ?? _seatedFront : _seatedFront) : backView ? _back : _front;
                    float height = seatedArt ? _seatedHeight : _height;
                    var ink = backView ? _seatedBackBounds : _seatedFrontBounds;
                    float scale = height / (seatedArt ? ink.height : _body.sprite.bounds.size.y);
                    _body.transform.localScale = new Vector3(scale, scale, 1f);
                    // A sitting sprite is anchored at its hips, rather than standing on the cushion.
                    float hipX = seatedArt ? (ink.xMin + ink.width * _seatedHipX) * scale * (mirrored ? -1f : 1f) : 0;
                    float hipY = seatedArt ? (ink.yMin + ink.height * _seatedHipY) * scale : 0;
                    _body.transform.localPosition = new Vector3(-hipX, -hipY, 0);
                    float headY = seatedArt ? height * (1f - _seatedHipY) + 0.02f : height + 0.02f;
                    _bubble.transform.localPosition = new Vector3(0.18f, headY, 0);
                    _progressBack.transform.localPosition = _progressFill.localPosition = new Vector3(-0.2f, headY - 0.03f, 0);
                }
                _shadowT.gameObject.SetActive(!pose.Seated);
                _body.flipX = mirrored;
                if (_head != null)
                    _head.flipX = mirrored;

                if (_rigActive)
                    AnimateRig(rig, pose, mirrored, time);
                else
                {
                    // Whole-sprite fallback (sitting, placeholder art, or a sprite that couldn't be cut).
                    float bob = 0f, squash = 1f;
                    if (pose.Moving)
                    {
                        float s = Mathf.Sin(time * 14f + _phase);
                        bob = Mathf.Abs(s) * 0.05f;
                        squash = 1f - Mathf.Abs(s) * 0.03f;
                    }
                    else if (pose.Eating || pose.Progress >= 0f)
                    {
                        bob = Mathf.Max(0f, Mathf.Sin(time * 8f + _phase)) * (pose.Seated ? 0.008f : 0.03f);
                    }
                    _visual.localPosition = new Vector3(0, bob, 0);
                    _visual.localScale = new Vector3(2f - squash, squash, 1f);
                    _shadowT.localScale = new Vector3(_shadowBase, _shadowBase, 1f);
                }

                _bubble.enabled = pose.ShowBubble;
                _icon.enabled = pose.ShowBubble && pose.BubbleIcon != null;
                if (_icon.enabled && _icon.sprite != pose.BubbleIcon)
                    SetSprite(_icon, pose.BubbleIcon, 0.2f);

                bool showProgress = pose.Progress >= 0f;
                _progressBack.enabled = showProgress;
                _progressBar.enabled = showProgress;
                if (showProgress)
                {
                    _progressFill.localScale = new Vector3(Mathf.Clamp01(pose.Progress) * 0.38f * 100f, 0.03f * 100f, 1f);
                    _progressBar.color = pose.ProgressColor;
                }
                if (_front != null)
                    _body.color = pose.Tint;

                _carry.enabled = pose.CarryIcon != null;
                if (_carry.enabled)
                {
                    DishPresentation.Fit(_carry,pose.CarryIcon,pose.CarryWidth);
                    _carry.transform.localPosition = new Vector3(mirrored ? -.30f : .30f,
                        _height*.25f+pose.CarryOffsetY,0);
                }
                // Plate goes behind the body when walking away from the camera.
                _carry.sortingOrder = backView ? -1 : 5;
            }

            private void SyncActivity(in CharacterPose pose)
            {
                if (string.IsNullOrEmpty(pose.ActivityKey))
                {
                    if (_activityCanvas != null) _activityCanvas.gameObject.SetActive(false);
                    return;
                }
                if (_activityCanvas == null)
                {
                    var go = new GameObject("ActivityStatus", typeof(RectTransform), typeof(Canvas));
                    go.transform.SetParent(_root, false);
                    var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 29000;
                    _activityCanvas = (RectTransform)go.transform; _activityCanvas.sizeDelta = new Vector2(128, 26);
                    var back = UI.UiKit.Box(_activityCanvas, "Back", new Color(.09f,.25f,.34f,.95f), new Color(.12f,.37f,.48f,.95f), 2);
                    UI.UiKit.Stretch(back);
                    _activityText = UI.UiKit.AddText(UI.UiKit.NewRect("Label",back),16,TextAnchor.MiddleCenter,Color.white);
                    _activityText.resizeTextForBestFit = true; _activityText.resizeTextMinSize = 11; _activityText.resizeTextMaxSize = 16;
                    _activityText.horizontalOverflow = HorizontalWrapMode.Wrap; _activityText.verticalOverflow = VerticalWrapMode.Truncate;
                    UI.UiKit.Stretch(_activityText.rectTransform,4,0,4,0);
                    foreach (var graphic in go.GetComponentsInChildren<UnityEngine.UI.Graphic>()) graphic.raycastTarget = false;
                }
                _activityCanvas.gameObject.SetActive(true);
                _activityCanvas.localPosition = new Vector3(0,_height+.31f,0);
                float pixel = Camera.main != null && Camera.main.orthographic ? 2f*Camera.main.orthographicSize/Mathf.Max(1,Screen.height) : .006f;
                _activityCanvas.localScale = Vector3.one * Mathf.Clamp(pixel,.003f,.012f);
                _activityText.text = Loc.Get(pose.ActivityKey) + " " + Mathf.RoundToInt(Mathf.Clamp01(pose.Progress)*100) + "%";
                _activityCanvas.sizeDelta = new Vector2(Mathf.Clamp(_activityText.preferredWidth+20,104,220),26);
            }

            /// <summary>
            /// Baked frames: picks the animation for what the character is doing, steps walk frames by distance walked
            /// (so feet don't slide) and everything else by time, and keeps the feet on the floor point of the canvas.
            /// </summary>
            private void SyncFrames(Agent a, in CharacterPose pose, float time, bool mirrored, bool backView)
            {
                // The baked sitting pose includes the chair height, so it stands on the chair's floor point.
                if (pose.Seated) _root.localPosition = IsoProjection.GridToWorld(a.Position, 0f);
                bool carrying = pose.CarryIcon != null;
                string anim = pose.Seated ? (pose.Eating ? CharacterFrames.Eat : CharacterFrames.Sit)
                    : pose.Moving && _stride > 0.05f ? (carrying ? CharacterFrames.Carry : CharacterFrames.Walk)
                    : pose.Cleaning ? CharacterFrames.Clean
                    : pose.Fixing ? CharacterFrames.Fix
                    : pose.Cooking ? CharacterFrames.Cook
                    : carrying ? CharacterFrames.Hold
                    : CharacterFrames.Idle;
                if (!_frames.Has(anim))
                    anim = anim == CharacterFrames.Carry ? CharacterFrames.Walk
                        : pose.Seated && _frames.Has(CharacterFrames.Sit) ? CharacterFrames.Sit : CharacterFrames.Idle;

                var list = _frames.Get(anim, backView);
                int n = list.Length, index;
                if (anim == CharacterFrames.Walk || anim == CharacterFrames.Carry)
                    index = (int)Mathf.Repeat(Mathf.Floor(_walkPhase / (Mathf.PI * 2f) * n), n);
                else
                    index = (int)Mathf.Repeat(Mathf.Floor((time + _phase) * (anim == CharacterFrames.Idle ? 5f : 10f)), n);
                var sprite = list[index];

                float scale = _height / _frames.StandingHeight;
                var ground = CharacterFrames.FromPivot(sprite, _frames.Anchor);
                _rigActive = false;
                _rigRoot.gameObject.SetActive(false);
                _body.enabled = true;
                _body.sprite = sprite;
                _body.flipX = mirrored;
                _body.color = pose.Tint;
                _body.transform.localScale = new Vector3(scale, scale, 1f);
                _body.transform.localPosition = new Vector3((mirrored ? ground.x : -ground.x) * scale, -ground.y * scale, 0f);
                _visual.localPosition = Vector3.zero;
                _visual.localScale = Vector3.one;
                _shadowT.gameObject.SetActive(!pose.Seated);
                _shadowT.localScale = new Vector3(_shadowBase, _shadowBase, 1f);

                float headY = (pose.Seated ? _height * 0.95f : _height) + 0.02f;
                _bubble.transform.localPosition = new Vector3(0.18f, headY, 0);
                _progressBack.transform.localPosition = _progressFill.localPosition = new Vector3(-0.2f, headY - 0.03f, 0);
                _bubble.enabled = pose.ShowBubble;
                _icon.enabled = pose.ShowBubble && pose.BubbleIcon != null;
                if (_icon.enabled && _icon.sprite != pose.BubbleIcon)
                    SetSprite(_icon, pose.BubbleIcon, 0.2f);
                bool showProgress = pose.Progress >= 0f;
                _progressBack.enabled = showProgress;
                _progressBar.enabled = showProgress;
                if (showProgress)
                {
                    _progressFill.localScale = new Vector3(Mathf.Clamp01(pose.Progress) * 0.38f * 100f, 0.03f * 100f, 1f);
                    _progressBar.color = pose.ProgressColor;
                }

                // The plate rests on the raised hand of the carry pose.
                _carry.enabled = carrying;
                if (carrying)
                {
                    DishPresentation.Fit(_carry, pose.CarryIcon, pose.CarryWidth);
                    var hand = _frames.TryHand(anim, backView, out var h)
                        ? (CharacterFrames.FromPivot(sprite, h) - ground) * scale
                        : new Vector2(0.30f, _height * 0.25f);
                    _carry.transform.localPosition = new Vector3(mirrored ? -hand.x : hand.x, hand.y + pose.CarryOffsetY, 0f);
                }
                _carry.sortingOrder = backView ? -1 : 5;
            }

            /// <summary>
            /// Walk cycle from one still image: legs take turns lifting and swinging, the upper body bobs on each step,
            /// sways side to side and squashes a little on footfall. Standing still settles the legs and breathes;
            /// a busy chef bobs while cooking. Offsets are authored in world units and converted to sprite units.
            /// </summary>
            private void AnimateRig(WalkRig rig, in CharacterPose pose, bool mirrored, float time)
            {
                float scale = _height / rig.Height;
                float inv = 1f / scale;
                _rigRoot.localScale = new Vector3(mirrored ? -scale : scale, scale, 1f);
                _upper.sprite = rig.Upper;
                _legL.sprite = rig.LegLeft;
                _legR.sprite = rig.LegRight;
                _upper.color = _legL.color = _legR.color = pose.Tint;

                float s = Mathf.Sin(_walkPhase), c = Mathf.Cos(_walkPhase);
                float liftL = Mathf.Max(0f, s) * LegLift * _stride;
                float liftR = Mathf.Max(0f, -s) * LegLift * _stride;
                float swing = c * LegSwing * _stride;
                _legLT.localPosition = new Vector3(rig.LegLeftX + swing * inv, liftL * inv, 0f);
                _legRT.localPosition = new Vector3(rig.LegRightX - swing * inv, liftR * inv, 0f);

                float bodyBob = Mathf.Abs(s) * BodyBob * _stride;
                float stir = 0f;
                if (!pose.Moving && pose.Cooking)
                {
                    // Stirring at the stove: a quick bounce and a side-to-side sway of the upper body.
                    bodyBob += Mathf.Abs(Mathf.Sin(time * 11f + _phase)) * 0.04f;
                    stir = Mathf.Sin(time * 5.5f + _phase) * 7f;
                }
                else if (!pose.Moving && pose.Progress >= 0f)
                    bodyBob += Mathf.Max(0f, Mathf.Sin(time * 8f + _phase)) * 0.02f; // busy (clearing a table)
                float breathe = 1f + Mathf.Sin(time * 2.2f + _phase) * 0.012f * (1f - _stride);
                float squashY = (1f - (1f - Mathf.Abs(s)) * FootfallSquash * _stride) * breathe;
                _upperT.localPosition = new Vector3(0f, rig.UpperY + bodyBob * inv, 0f);
                _upperT.localScale = new Vector3(2f - squashY, squashY, 1f);
                _upperT.localRotation = Quaternion.Euler(0f, 0f, s * BodySwayDegrees * _stride + stir);

                _visual.localPosition = Vector3.zero;
                _visual.localScale = Vector3.one;
                // Shadow tightens a little as the body lifts, so steps read as contact with the floor.
                float shadow = _shadowBase * (1f - bodyBob * 2.5f);
                _shadowT.localScale = new Vector3(shadow, shadow, 1f);
            }

            private static void SetSprite(SpriteRenderer sr, Sprite sprite, float width)
            {
                sr.sprite = sprite;
                if (sprite != null)
                {
                    float s = width / sprite.bounds.size.x;
                    sr.transform.localScale = new Vector3(s, s, 1f);
                }
            }
        }
    }
}
