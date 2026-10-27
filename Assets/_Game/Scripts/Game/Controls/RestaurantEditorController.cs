using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.DebugTools;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Game.UI;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Session;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace DemoRestaurant.Game.Controls
{
    /// <summary>Shop sections, as in the reference game's shop.</summary>
    public enum BuildCategory { Floor, Kitchen, Table, Chair, Decoration, Functional, Outdoor, Wall, Land, Wallpaper }

    /// <summary>
    /// Decorating as in the reference game: tap (or click) a piece to pick it up, it follows the finger (or mouse), and a
    /// tap drops it. A piece from the shop bar is picked up the same way. Dropping a held piece back on the shop bar puts
    /// it in storage. Wall pieces and the door slide along the back walls. Nothing is saved until Save ("open").
    /// Touch: a finger down on a piece picks it up and drags it; lifting the finger drops it there. A tap without a drag
    /// keeps it in hand (rotate / store buttons appear beside it) until the next tap.
    /// </summary>
    public sealed class RestaurantEditorController : MonoBehaviour
    {
        private RestaurantSession _session;
        private LoadedContentPack _content;
        private RestaurantView _view;
        private GameUi _ui;
        private CameraController _camera;
        private PerfHud _debugHud;
        private bool _debugWasEnabled;
        private RestaurantEditorPanel _panel;
        private SpriteRenderer _ghost, _footprint, _workFootprint, _problem, _marker;
        private Vector3 _ghostBase;
        private GameObject _guides;
        private Sprite _guideSprite;
        private readonly System.Collections.Generic.Dictionary<string, Sprite> _footprints = new System.Collections.Generic.Dictionary<string, Sprite>();

        // In hand: a piece from the shop (_placing only), a placed floor piece (_moving), a wall piece (_movingWall) or the door.
        private FurnitureDef _placing;
        private PlacedFurniture _moving;
        private PlacedWallItem _movingWall;
        private bool _movingDoor;
        private FloorArt _floor;
        private FloorArt _wallpaper;
        private WallSide _paintSide;
        private GridPos _origin;
        private GridPos _pointerOffset;
        private Facing _facing;
        private WallSide _wallSide;
        private int _wallIndex;
        private bool _hasTarget, _valid;

        // Touch gesture state.
        private int _touchId = -1;
        private bool _touchOnUi, _touchMulti, _touchMoved, _pickedThisTouch;
        private Vector2 _touchStart;
        /// <summary>A touch that began on a shop card: dragging it up out of the bar picks that piece up.</summary>
        private System.Action _pendingCard;
        // Mouse gesture state.
        private Vector2 _mouseDown, _rightDown;
        private bool _mouseDragPick;
        private System.Action _pendingCardMouse;
        private bool _cardMouseHorizontal, _cardTouchHorizontal;
        // Floor painting: a drag covers the rectangle between where it started and where the pointer is.
        private bool _painting;
        private GridPos _paintFrom, _paintTo;
        private System.Func<LayoutSnapshot, long, bool> _persistLayout;

        public bool IsEditing { get; private set; }
        public event System.Action Saved;
        public LayoutEditor Editor { get; private set; }
        public BuildCategory Category { get; private set; }
        /// <summary>Something is in hand.</summary>
        public bool HasSelection => _placing != null || _movingDoor;
        public bool CanConfirm => HasSelection && _hasTarget && _valid;
        /// <summary>A placed piece is in hand (it can go to storage).</summary>
        public bool CanRemove => _placing != null && !_movingDoor;
        public bool CanRotate => _placing != null && _placing.Kind != FurnitureKind.WallDecor;
        /// <summary>A floor or a wallpaper is chosen: drags pave (or paper) instead of moving pieces.</summary>
        public bool HasFloor => _floor != null || _wallpaper != null;
        public bool HasWallpaper => _wallpaper != null;
        /// <summary>The floor or wallpaper being laid, for the tool indicator; null when neither.</summary>
        public FloorArt PaintTool => _floor ?? _wallpaper;
        public string SelectedId => _placing?.Id ?? _floor?.id ?? _wallpaper?.id;
        public long Balance => _session.Wallet.ConfirmedCoins;
        public long ExpansionCost
        {
            get
            {
                if (Editor == null || _content.Pack.Rules.Expansion == null) return 0;
                _content.Pack.Rules.Expansion.TryQuote(_session.Layout.Grid.Width, _session.Layout.Grid.Height,
                    Editor.Draft.Grid.Width, Editor.Draft.Grid.Height, out long price);
                return price;
            }
        }

        private ILandPurchaseServer Server => _session.Economy as ILandPurchaseServer;
        private bool HoldingWallThing => _movingDoor || (_placing != null && _placing.Kind == FurnitureKind.WallDecor);

        /// <summary>Furniture the player owns (placed in the saved room or in storage).</summary>
        public System.Collections.Generic.IReadOnlyDictionary<string, int> Owned => Server?.OwnedFurniture;

        /// <summary>Copies of each piece placed in the draft (floor and walls).</summary>
        public System.Collections.Generic.Dictionary<string, int> PlacedInDraft() =>
            Editor == null ? new System.Collections.Generic.Dictionary<string, int>() : FurnitureOwnership.CountPlaced(Editor.Draft);

        /// <summary>Coins for pieces placed beyond what is owned (bought on Save).</summary>
        public long FurnitureCost => Editor == null ? 0 : FurnitureOwnership.PurchaseCost(_content.Pack, Owned, PlacedInDraft());

        public long TotalCost => ExpansionCost + FurnitureCost;

        public int PlayerLevel => _session.Progress.Level;

        public bool ExpansionQuote(bool alongX, out long cost)
        {
            cost = 0;
            var rules = _content.Pack.Rules.Expansion;
            if (Editor == null || rules == null) return false;
            var grid = Editor.Draft.Grid;
            return rules.TryQuote(grid.Width, grid.Height, grid.Width + (alongX ? rules.step : 0),
                grid.Height + (alongX ? 0 : rules.step), out cost);
        }

        public void Expand(bool alongX)
        {
            ClearSelection();
            if (!Editor.Expand(alongX)) _panel.ShowMessage("ui.build.land_max");
            else _panel.RefreshCatalog();
        }

        public void Bind(RestaurantSession session, LoadedContentPack content, RestaurantView view, GameUi ui)
        {
            _session = session; _content = content; _view = view; _ui = ui;
            _persistLayout = (snapshot, balance) => LayoutSaveStore.TrySave(snapshot, _content, balance);
            _camera = Camera.main.GetComponent<CameraController>();
            _camera.CanPanInEditor = () => !HasSelection && !HasFloor;
            _panel = new GameObject("RestaurantEditorPanel").AddComponent<RestaurantEditorPanel>();
            _panel.transform.SetParent(transform, false);
            _panel.Build(this, content);
            _session.Wallet.Changed += _panel.RefreshState;
            _ghost = CreateRenderer("FurniturePreview", 30000);
            _footprint = CreateRenderer("PlacementFootprint", 29999);
            _workFootprint = CreateRenderer("WorkAreaFootprint", 29999);
            _problem = CreateRenderer("BlockedHighlight", 29998);
            _marker = CreateRenderer("HeldMarker", 30001);
            _marker.sprite = MarkerSprite();
            _ghost.enabled = _footprint.enabled = _workFootprint.enabled = _problem.enabled = _marker.enabled = false;
        }

        private SpriteRenderer CreateRenderer(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_view.transform, false);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sortingOrder = order;
            return sr;
        }

        private void OnEnable() => EnhancedTouchSupport.Enable();
        private void OnDisable() => EnhancedTouchSupport.Disable();

        public void Begin(BuildCategory category)
        {
            if (!IsEditing)
            {
                Editor = new LayoutEditor(_session.Layout, _content.Pack);
                IsEditing = true;
                _touchId = -1; _touchMulti = false;
                _view.ShowLayout(Editor.Draft, true);
                _ui.SetGameplayVisible(false);
                _camera.Editing = true;
                _debugHud = GetComponent<PerfHud>();
                if (_debugHud != null) { _debugWasEnabled = _debugHud.enabled; _debugHud.enabled = false; }
                BuildGuides();
                Editor.Draft.BoundsChanged += DraftBoundsChanged;
            }
            SetCategory(category);
            _panel.SetVisible(true);
        }

        public void SetCategory(BuildCategory category)
        { ClearSelection(); Category = category; _panel.RefreshCatalog(); }

        /// <summary>Picks a piece up from the shop bar (or storage): it follows the pointer until dropped.</summary>
        public void SelectFurniture(FurnitureDef def)
        {
            if (def.UnlockLevel > PlayerLevel)
            {
                _panel.ShowMessage(Loc.Format("ui.build.locked", def.UnlockLevel), raw: true);
                return;
            }
            ClearSelection(); _placing = def; _facing = Facing.SouthWest;
            if (!Application.isMobilePlatform && Touch.activeTouches.Count == 0 && Mouse.current != null) FollowPointer(Mouse.current.position.ReadValue());
            _panel.RefreshState();
        }

        public void SelectWallpaper(FloorArt wallpaper)
        { ClearSelection(); _wallpaper = wallpaper; _panel.RefreshState(); }

        public void SelectFloor(FloorArt floor)
        { ClearSelection(); _floor = floor; _panel.RefreshState(); }

        /// <summary>Empties the hand: a picked-up piece goes back where it was.</summary>
        public void ClearSelection()
        {
            ClearPaintPreview();
            _view.SetFurnitureVisible(_moving, true);
            _view.SetWallItemVisible(_movingWall, true);
            if (_movingDoor) _view.SetDoorVisible(true);
            _moving = null; _movingWall = null; _movingDoor = false; _placing = null; _floor = null; _wallpaper = null; _hasTarget = false;
            _painting = false;
            if (_problem != null) _problem.enabled = false;
            _pointerOffset = new GridPos(0, 0);
            if (_ghost != null) _ghost.enabled = _footprint.enabled = _workFootprint.enabled = _marker.enabled = false;
            _panel?.SetHandButtons(false, Vector2.zero);
        }

        /// <summary>Why the layout can't open: names the unreachable piece and outlines it in red.</summary>
        private void ShowProblem(ServiceLayoutIssue issue, PlacedFurniture blocked)
        {
            if (issue != ServiceLayoutIssue.BlockedAccess || blocked == null)
            {
                _panel.ShowMessage("ui.build.issue." + issue);
                return;
            }
            _panel.ShowMessage(Loc.Format("ui.build.issue.blocked_item", Loc.Get(blocked.Def.NameKey)), raw: true);
            string key = "problem:" + blocked.SizeX + ":" + blocked.SizeY;
            if (!_footprints.TryGetValue(key, out var sprite))
            {
                var red = new Color(1f, 0.1f, 0.05f, 0.6f);
                sprite = PlaceholderArt.Floor(blocked.SizeX, blocked.SizeY, red, red);
                _footprints[key] = sprite;
            }
            _problem.sprite = sprite;
            _problem.transform.localPosition = IsoProjection.GridToWorld(blocked.Origin);
            _problem.enabled = true;
        }

        public void Rotate()
        {
            if (!CanRotate) return;
            _facing = (Facing)(((int)_facing + 1) % 4);
            RefreshPreview();
        }

        /// <summary>Drops what is in hand at the current target. The hand is empty afterwards.</summary>
        public void Confirm()
        {
            // Recheck the current draft, including any changes made since the pointer last moved.
            RefreshPreview();
            if (!CanConfirm) return;
            bool changed;
            if (_movingDoor)
            {
                var move = Editor.MoveEntrance(new GridPos(0, _wallIndex), 1, _content.Pack.Rules.WaitingSpots);
                changed = move == LayoutEditor.DoorMove.Moved;
                if (move == LayoutEditor.DoorMove.Blocked) { _panel.ShowMessage("ui.build.door_blocked"); return; }
            }
            else if (_movingWall != null) changed = Editor.MoveOnWall(_movingWall, _wallSide, _wallIndex) ||
                (_movingWall.Side == _wallSide && _movingWall.Index == _wallIndex);
            else if (_placing.Kind == FurnitureKind.WallDecor) changed = Editor.Hang(_placing, _wallSide, _wallIndex);
            else if (_moving != null) changed = Editor.Move(_moving, _origin, _facing) || (_moving.Origin == _origin && _moving.Facing == _facing);
            else changed = Editor.Place(_placing, _origin, _facing);
            if (!changed) return;
            ClearSelection();
            _panel.RefreshState();
        }

        /// <summary>The piece in hand goes to storage (the door can't be stored).</summary>
        public void Remove()
        {
            if (_moving != null) Editor.Remove(_moving);
            else if (_movingWall != null) Editor.TakeDown(_movingWall);
            else if (_movingDoor) { _panel.ShowMessage("ui.build.door_cant_store"); return; }
            else { ClearSelection(); _panel.RefreshState(); return; } // a shop piece just goes back on the shelf
            _moving = null; _movingWall = null;
            ClearSelection();
            _panel.ShowMessage("ui.build.stored");
            _panel.RefreshState();
        }

        /// <summary>Everything placed goes to storage (the reference game's "store all").</summary>
        public void StoreAll()
        {
            ClearSelection();
            if (Editor.StoreAll()) _panel.ShowMessage("ui.build.stored_all");
            _panel.RefreshState();
        }

        public void Undo() { ClearSelection(); Editor.Undo(); _panel.RefreshState(); }
        public void Redo() { ClearSelection(); Editor.Redo(); _panel.RefreshState(); }
        public void FillFloor()
        {
            ClearPaintPreview();
            if (_floor != null) Editor.FillFloor(_floor.id);
            else if (_wallpaper != null) Editor.FillWalls(_wallpaper.id);
            _panel.RefreshState();
        }

        public void Save()
        {
            if (!IsEditing) return;
            if (HasSelection && _hasTarget)
            {
                RefreshPreview();
                if (!CanConfirm) { _panel.ShowMessage("ui.build.invalid_placement"); return; }
                Confirm();
            }
            ClearSelection();
            // A room needs a chair and a stove. Pieces nobody can reach don't stop the save (as in the reference
            // game): the player is warned, and guests simply don't sit where they can't be served.
            var issue = Editor.ValidateForService(out var blocked);
            if (issue != ServiceLayoutIssue.None && issue != ServiceLayoutIssue.BlockedAccess)
            {
                ShowProblem(issue, blocked);
                return;
            }
            bool unreachable = issue == ServiceLayoutIssue.BlockedAccess;
            var snapshot = LayoutSnapshot.Capture(Editor.Draft);
            var server = Server;
            var placed = PlacedInDraft();
            long balanceBefore = server?.Balance ?? 0;
            // Land and any furniture beyond what is owned are paid together; the room and ownership are saved with it.
            var result = server == null ? LandPurchaseResult.Unavailable : server.CommitLayout(snapshot.width, snapshot.height, placed,
                balance =>
                {
                    snapshot.SetOwned(FurnitureOwnership.AfterPurchase(server.OwnedFurniture, placed));
                    return _persistLayout(snapshot, balance);
                });
            if (result != LandPurchaseResult.Success)
            {
                _panel.ShowMessage(result == LandPurchaseResult.InsufficientCoins ? "ui.build.land_insufficient" :
                    result == LandPurchaseResult.Unavailable ? "ui.build.land_unavailable" : "ui.build.save_failed");
                return;
            }
            _session.Sim.ApplyLayout(snapshot, _content.Pack);
            _session.Wallet.SetConfirmed(server.Balance);
            _session.RecordLayoutSaved(balanceBefore - server.Balance);
            End();
            Saved?.Invoke();
            _ui.Toast(Loc.Get(unreachable ? "ui.build.saved_unreachable" : "ui.build.saved"));
        }

        public void Cancel()
        {
            End();
            _ui.Toast(Loc.Get("ui.build.cancelled"));
        }

        private void End()
        {
            ClearSelection();
            Editor.Draft.BoundsChanged -= DraftBoundsChanged;
            IsEditing = false; _camera.Editing = false;
            if (_debugHud != null) _debugHud.enabled = _debugWasEnabled;
            _view.ShowLayout(_session.Layout, false);
            _camera.SetRoomBounds(_session.Layout);
            _ui.SetGameplayVisible(true); _panel.SetVisible(false);
            ClearGuides();
            Editor = null;
        }

        private void Update()
        {
            if (!IsEditing) return;
            var mouse = Mouse.current;
            bool touching = Touch.activeTouches.Count > 0;
            if (!Application.isMobilePlatform && !touching && mouse != null && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed)
                FollowPointer(mouse.position.ReadValue());
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) { ClearSelection(); _panel.RefreshState(); }
                if (keyboard.rKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame || keyboard.qKey.wasPressedThisFrame) Rotate();
                if (keyboard.enterKey.wasPressedThisFrame) Confirm();
                if (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) Remove();
                bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
                if (ctrl && keyboard.zKey.wasPressedThisFrame) Undo();
                if (ctrl && keyboard.yKey.wasPressedThisFrame) Redo();
            }
            EdgePan();
            UpdateHandButtons();
            bool touched = HandleTouches();
            AnimateHeld();
            if (touched) return;
            if (!Application.isMobilePlatform && mouse != null) HandleMouse(mouse);
        }

        /// <summary>
        /// Mouse: click a piece to pick it up and click again to put it down, or press, drag and release. A shop card
        /// dragged right out of the drawer comes along the same way. A right click (without scrolling the view) turns the
        /// piece in hand; clicking or releasing over the shop bar stores it.
        /// </summary>
        private void HandleMouse(Mouse mouse)
        {
            var point = mouse.position.ReadValue();
            if (HasFloor && !_painting && !mouse.leftButton.isPressed) ShowPaintHover(point);
            if (mouse.rightButton.wasPressedThisFrame) _rightDown = point;
            if (mouse.rightButton.wasReleasedThisFrame && HasSelection && (point - _rightDown).sqrMagnitude < 8f * 8f) Rotate();

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _mouseDown = point;
                _mouseDragPick = false;
                _pendingCardMouse = null; _cardMouseHorizontal = false;
                if (UiInput.IsPointerOverUi(point))
                {
                    // Clicking the shop bar while holding a placed piece puts it in storage.
                    if (CanRemove && _panel.IsOverShopBar(point)) Remove();
                    else if (!HasSelection) _pendingCardMouse = _panel.CardAt(point);
                    return;
                }
                if (HasFloor) { BeginPaint(point); return; }
                if (HasSelection) Confirm();
                else _mouseDragPick = PickUp(point);
                return;
            }
            if (_painting && mouse.leftButton.isPressed) UpdatePaint(point);
            if (mouse.leftButton.isPressed && _pendingCardMouse != null)
                ResolveCatalogGesture(point - _mouseDown, ref _pendingCardMouse, ref _cardMouseHorizontal);
            if (mouse.leftButton.isPressed && _pendingCardMouse != null && _cardMouseHorizontal && _panel.IsRightOfCatalog(point))
            {
                var select = _pendingCardMouse;
                _pendingCardMouse = null;
                select();
                if (_placing != null && _moving == null)
                {
                    _mouseDragPick = true;
                    _mouseDown = new Vector2(-10000f, -10000f); // dragged out of the shop: releasing always drops it
                    FollowPointer(point);
                }
            }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                _pendingCardMouse = null;
                if (_painting) EndPaint();
                if (_mouseDragPick && HasSelection && (point - _mouseDown).sqrMagnitude > 12f * 12f)
                {
                    if (_panel.IsOverShopBar(point)) Remove();
                    else if (!UiInput.IsPointerOverUi(point)) Confirm();
                }
                _mouseDragPick = false;
            }
        }

        /// <summary>Rotate / store buttons beside a piece held still on a touch screen (mouse users have the toolbar).</summary>
        private void UpdateHandButtons()
        {
            // A UI-owned touch must not hide its own button before Button.onClick runs on release.
            bool show = HasSelection && _hasTarget && (_touchId < 0 || _touchOnUi) &&
                !_touchMulti && (Application.isMobilePlatform || Touchscreen.current != null) && _ghost.enabled;
            Vector2 screen = show ? (Vector2)Camera.main.WorldToScreenPoint(_ghost.bounds.center + new Vector3(0f, _ghost.bounds.extents.y, 0f)) : Vector2.zero;
            _panel.SetHandButtons(show, screen, _touchId >= 0 && _touchOnUi);
        }

        /// <summary>
        /// Finger down on a piece picks it up and drags it; lifting the finger drops it. A tap without a drag keeps it in
        /// hand, and the next tap drops it. Lifting over the shop bar puts it in storage. Floors paint while held.
        /// Pinches and touches that start on the UI never place anything.
        /// </summary>
        private bool HandleTouches()
        {
            var touches = Touch.activeTouches;
            if (touches.Count == 0)
            {
                _touchId = -1; _touchMulti = false;
                return false;
            }
            if (touches.Count > 1) { _touchMulti = true; return true; }
            var touch = touches[0];
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                _touchId = touch.touchId;
                _touchStart = touch.screenPosition; _cardTouchHorizontal = false;
                _touchMoved = false; _pickedThisTouch = false;
                _touchOnUi = UiInput.IsPointerOverUi(touch.screenPosition);
                _pendingCard = _touchOnUi && !_touchMulti ? _panel.CardAt(touch.screenPosition) : null;
                if (!_touchOnUi && !_touchMulti)
                {
                    if (HasFloor) BeginPaint(touch.screenPosition);
                    else if (!HasSelection) _pickedThisTouch = PickUp(touch.screenPosition);
                    else FollowPointer(TouchTarget(touch.screenPosition));
                }
            }
            // A vertical gesture stays owned by the list even when the finger leaves its bounds.
            if (touch.touchId == _touchId && _touchOnUi && _pendingCard != null && !_touchMulti &&
                touch.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                ResolveCatalogGesture(touch.screenPosition - _touchStart, ref _pendingCard, ref _cardTouchHorizontal);
            // A rightward gesture transfers to placement only after leaving the drawer.
            if (touch.touchId == _touchId && _touchOnUi && _pendingCard != null && !_touchMulti &&
                touch.phase == UnityEngine.InputSystem.TouchPhase.Moved && _cardTouchHorizontal && _panel.IsRightOfCatalog(touch.screenPosition))
            {
                var select = _pendingCard;
                _pendingCard = null;
                select();
                if (_placing != null && _moving == null)
                {
                    _touchOnUi = false; _touchMoved = true; _pickedThisTouch = true;
                    FollowPointer(TouchTarget(touch.screenPosition));
                }
            }
            if (touch.touchId != _touchId || _touchOnUi || _touchMulti) return true;
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                if ((touch.screenPosition - _touchStart).sqrMagnitude > 12f * 12f) _touchMoved = true;
                if (_painting) UpdatePaint(touch.screenPosition);
                else if (HasSelection) FollowPointer(TouchTarget(touch.screenPosition));
            }
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended)
            {
                var end = touch.screenPosition;
                if (_painting) { UpdatePaint(end); EndPaint(); }
                if (HasSelection)
                {
                    if (_panel.IsOverShopBar(end)) Remove();
                    else if (!UiInput.IsPointerOverUi(end))
                    {
                        FollowPointer(TouchTarget(end));
                        // The tap that picked a piece up keeps it in hand; a drag, or any later tap, drops it.
                        if (_touchMoved || !_pickedThisTouch) Confirm();
                    }
                }
                _touchId = -1; _pendingCard = null;
            }
            else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled) { _touchId = -1; _pendingCard = null; CancelPaint(); }
            return true;
        }

        /// <summary>Lock to the first clear gesture direction; scrolling can never become a furniture drag.</summary>
        private static void ResolveCatalogGesture(Vector2 delta, ref System.Action pending, ref bool horizontal)
        {
            if (horizontal) return;
            float threshold = UnityEngine.EventSystems.EventSystem.current != null
                ? Mathf.Max(12f, UnityEngine.EventSystems.EventSystem.current.pixelDragThreshold) : 12f;
            if (Mathf.Abs(delta.y) > threshold && Mathf.Abs(delta.y) >= Mathf.Abs(delta.x)) pending = null;
            else if (delta.x > threshold && delta.x > Mathf.Abs(delta.y)) horizontal = true;
            else if (delta.x < -threshold) pending = null;
        }

        /// <summary>Keep the last destination over UI so reaching the toolbar doesn't move the piece.</summary>
        private void FollowPointer(Vector2 screen)
        {
            if (!HasSelection || UiInput.IsPointerOverUi(screen)) return;
            if (HoldingWallThing)
            {
                var local = LocalPoint(screen);
                var side = local.x < 0f ? WallSide.Left : WallSide.Right;
                if (_movingDoor) side = WallSide.Left; // the door stays on the street side
                int length = Editor.Draft.WallLength(side);
                int index = Mathf.Clamp(Mathf.FloorToInt(Mathf.Abs(local.x) / IsoProjection.HalfTileWidth), 0, length - 1);
                if (_hasTarget && side == _wallSide && index == _wallIndex) return;
                _wallSide = side; _wallIndex = index; _hasTarget = true;
                RefreshPreview();
                return;
            }
            var cell = CellAtScreen(screen) + _pointerOffset;
            if (_hasTarget && cell == _origin) return;
            _origin = cell;
            _hasTarget = true;
            // A chair dropped beside a table turns to face it (Rotate still works afterwards).
            if (_placing.Kind == FurnitureKind.Chair)
                _facing = SeatFacing.TowardTable(Editor.Draft, _origin, _facing);
            RefreshPreview();
        }

        private Vector2 LocalPoint(Vector2 screen) => _view.transform.InverseTransformPoint(Camera.main.ScreenToWorldPoint(screen));

        private GridPos CellAtScreen(Vector2 screen)
        {
            var grid = IsoProjection.WorldToGrid(LocalPoint(screen));
            return new GridPos(Mathf.FloorToInt(grid.X), Mathf.FloorToInt(grid.Y));
        }

        /// <summary>
        /// Starts a floor drag (a tap paves one tile, a drag the whole rectangle it covers) or a wallpaper drag along
        /// one back wall (wall cells are kept as the floor cells at the wall's foot, so the same outline shows them).
        /// </summary>
        private void BeginPaint(Vector2 screen)
        {
            GridPos cell;
            if (_wallpaper != null)
            {
                if (!RestaurantView.WallCellAt(LocalPoint(screen), Editor.Draft, out var side, out int index))
                { _panel.ShowMessage("ui.build.wallpaper_tap_wall"); return; }
                _paintSide = side;
                cell = WallFoot(side, index);
            }
            else
            {
                cell = CellAtScreen(screen);
                if (!Editor.Draft.IsFloorCell(cell)) { _panel.ShowMessage("ui.build.outside"); return; }
            }
            _painting = true;
            _paintFrom = _paintTo = cell;
            ShowPaintArea();
        }

        private static GridPos WallFoot(WallSide side, int index) => side == WallSide.Right ? new GridPos(index, 0) : new GridPos(0, index);

        /// <summary>The wall cell on one wall nearest the pointer (dragging past the corner stays on that wall).</summary>
        private int WallIndexAt(Vector2 screen, WallSide side)
        {
            float along = Mathf.Abs(LocalPoint(screen).x) / IsoProjection.HalfTileWidth;
            return Mathf.Clamp(Mathf.FloorToInt(along), 0, Editor.Draft.WallLength(side) - 1);
        }

        private void UpdatePaint(Vector2 screen)
        {
            if (!_painting) return;
            GridPos cell;
            if (_wallpaper != null) cell = WallFoot(_paintSide, WallIndexAt(screen, _paintSide));
            else
            {
                cell = CellAtScreen(screen);
                var grid = Editor.Draft.Grid;
                cell = new GridPos(Mathf.Clamp(cell.X, 0, grid.Width - 1), Mathf.Clamp(cell.Y, 0, grid.Height - 1));
            }
            if (cell == _paintTo) return;
            _paintTo = cell;
            ShowPaintArea();
        }

        private void EndPaint()
        {
            if (!_painting) return;
            _painting = false;
            _footprint.enabled = false;
            ClearPaintPreview();
            if (_wallpaper != null)
            {
                bool right = _paintSide == WallSide.Right;
                Editor.PaintWall(_paintSide, right ? _paintFrom.X : _paintFrom.Y, right ? _paintTo.X : _paintTo.Y, _wallpaper.id);
            }
            else Editor.PaintArea(_paintFrom, _paintTo, _floor.id);
            _panel.RefreshState();
        }

        /// <summary>Mouse hover with a floor or wallpaper chosen: outlines the tile (or wall cell) a click would cover.</summary>
        private void ShowPaintHover(Vector2 screen)
        {
            if (UiInput.IsPointerOverUi(screen)) { _footprint.enabled = false; ClearPaintPreview(); return; }
            if (_wallpaper != null)
            {
                if (!RestaurantView.WallCellAt(LocalPoint(screen), Editor.Draft, out var side, out int index)) { _footprint.enabled = false; ClearPaintPreview(); return; }
                _paintSide = side;
                _paintFrom = _paintTo = WallFoot(side, index);
            }
            else
            {
                var cell = CellAtScreen(screen);
                if (!Editor.Draft.IsFloorCell(cell)) { _footprint.enabled = false; ClearPaintPreview(); return; }
                _paintFrom = _paintTo = cell;
            }
            ShowPaintArea();
        }

        // Live preview: the chosen floor / wallpaper is put on the draft where it would go, remembering what was there,
        // and taken off again before the real (undoable) paint or when the pointer moves on.
        private readonly System.Collections.Generic.List<(GridPos cell, string was)> _previewFloor =
            new System.Collections.Generic.List<(GridPos, string)>();
        private readonly System.Collections.Generic.List<(WallSide side, int index, string was)> _previewWall =
            new System.Collections.Generic.List<(WallSide, int, string)>();

        private string _previewKey;

        private void ClearPaintPreview()
        {
            _previewKey = null;
            if (Editor == null) { _previewFloor.Clear(); _previewWall.Clear(); return; }
            for (int i = _previewFloor.Count - 1; i >= 0; i--) Editor.Draft.SetFloor(_previewFloor[i].cell, _previewFloor[i].was);
            for (int i = _previewWall.Count - 1; i >= 0; i--) Editor.Draft.SetWallpaper(_previewWall[i].side, _previewWall[i].index, _previewWall[i].was);
            _previewFloor.Clear();
            _previewWall.Clear();
        }

        private void ApplyPaintPreview()
        {
            // Hovering re-asks every frame: only redo the preview when the area or the choice changed.
            string key = (_wallpaper != null ? "w" + _paintSide + _wallpaper.id : "f" + _floor?.id) + _paintFrom + _paintTo;
            if (key == _previewKey) return;
            ClearPaintPreview();
            _previewKey = key;
            var draft = Editor.Draft;
            if (_wallpaper != null)
            {
                bool right = _paintSide == WallSide.Right;
                int a = right ? _paintFrom.X : _paintFrom.Y, b = right ? _paintTo.X : _paintTo.Y;
                for (int i = Mathf.Min(a, b); i <= Mathf.Max(a, b); i++)
                {
                    string was = draft.WallpaperAt(_paintSide, i);
                    if (draft.SetWallpaper(_paintSide, i, _wallpaper.id)) _previewWall.Add((_paintSide, i, was));
                }
            }
            else if (_floor != null)
            {
                for (int y = Mathf.Min(_paintFrom.Y, _paintTo.Y); y <= Mathf.Max(_paintFrom.Y, _paintTo.Y); y++)
                for (int x = Mathf.Min(_paintFrom.X, _paintTo.X); x <= Mathf.Max(_paintFrom.X, _paintTo.X); x++)
                {
                    var cell = new GridPos(x, y);
                    if (!draft.IsFloorCell(cell)) continue;
                    string was = draft.FloorAt(cell);
                    if (draft.SetFloor(cell, _floor.id)) _previewFloor.Add((cell, was));
                }
            }
        }

        private void CancelPaint() { _painting = false; ClearPaintPreview(); if (_footprint != null) _footprint.enabled = false; }

        /// <summary>Outlines the rectangle a floor drag will paint.</summary>
        private void ShowPaintArea()
        {
            int x0 = Mathf.Min(_paintFrom.X, _paintTo.X), y0 = Mathf.Min(_paintFrom.Y, _paintTo.Y);
            int w = Mathf.Abs(_paintFrom.X - _paintTo.X) + 1, h = Mathf.Abs(_paintFrom.Y - _paintTo.Y) + 1;
            string key = "paint:" + w + ":" + h;
            if (!_footprints.TryGetValue(key, out var sprite))
            {
                var tint = new Color(1f, 0.82f, 0.2f, 0.22f); // light: the real floor shows through as a preview
                sprite = PlaceholderArt.Floor(w, h, tint, tint);
                _footprints[key] = sprite;
            }
            _footprint.sprite = sprite;
            _footprint.color = Color.white;
            _footprint.transform.localPosition = IsoProjection.GridToWorld(new GridPos(x0, y0));
            _footprint.enabled = true;
            ApplyPaintPreview();
        }

        /// <summary>Picks up the piece under the pointer: floor furniture first, then a wall piece or the door.</summary>
        private bool PickUp(Vector2 screen)
        {
            if (HasFloor) { BeginPaint(screen); return false; }
            var cell = CellAtScreen(screen);
            var item = _view.PickFurniture(Camera.main.ScreenToWorldPoint(screen)) ?? Editor.Draft.FindAt(cell);
            if (item != null)
            {
                _moving = item; _placing = item.Def; _origin = item.Origin; _facing = item.Facing; _hasTarget = true;
                _pointerOffset = new GridPos(item.Origin.X - cell.X, item.Origin.Y - cell.Y);
                _view.SetFurnitureVisible(item, false);
                RefreshPreview();
                return true;
            }
            if (!RestaurantView.WallCellAt(LocalPoint(screen), Editor.Draft, out var side, out int index)) return false;
            if (index == Editor.Draft.DoorIndexOn(side) && side == WallSide.Left)
            {
                _movingDoor = true; _wallSide = side; _wallIndex = index; _hasTarget = true;
                _view.SetDoorVisible(false);
                RefreshPreview();
                return true;
            }
            var wallItem = Editor.Draft.WallItemAt(side, index);
            if (wallItem == null) return false;
            _movingWall = wallItem; _placing = wallItem.Def; _wallSide = wallItem.Side; _wallIndex = wallItem.Index; _hasTarget = true;
            _view.SetWallItemVisible(wallItem, false);
            RefreshPreview();
            return true;
        }

        private void RefreshPreview()
        {
            _ghost.enabled = HasSelection && _hasTarget;
            _footprint.enabled = false;
            _workFootprint.enabled = false;
            if (_ghost.enabled && HoldingWallThing)
            {
                if (_movingDoor)
                {
                    var door = new GridPos(0, _wallIndex);
                    _valid = _wallSide == WallSide.Left && (door == Editor.Draft.Entrance || Editor.Draft.CanMoveEntrance(door, new GridPos[0]));
                    _view.DrawDoorPreview(_ghost, _wallIndex);
                }
                else
                {
                    _valid = Editor.Draft.CanHang(_placing, _wallSide, _wallIndex, _movingWall);
                    _view.DrawWallItemPreview(_ghost, _placing, _wallSide, _wallIndex);
                }
                _ghost.color = _valid ? new Color(0.65f, 1, 0.65f, 0.85f) : new Color(1, 0.4f, 0.35f, 0.75f);
            }
            else if (_ghost.enabled)
            {
                _footprint.enabled = true;
                // A stackable piece goes on the floor when it can, otherwise onto the table or counter there.
                bool onFloor = _moving != null ? Editor.Draft.CanMove(_moving, _origin, _facing) && Editor.Draft.SurfaceFor(_placing, _origin, _facing, _moving) == null
                    : Editor.Draft.CanPlace(_placing, _origin, _facing);
                var surface = onFloor ? null : Editor.Draft.SurfaceFor(_placing, _origin, _facing, _moving);
                _valid = (onFloor || surface != null) && FurnitureWorkArea.CanEdit(Editor.Draft, _placing, _origin, _facing, _moving);
                _view.DrawFurniturePreview(_ghost, _placing, _origin, _facing);
                _ghost.transform.localPosition += new Vector3(0f, _view.StackLift(surface), 0f);
                _ghost.color = _valid ? new Color(0.65f, 1, 0.65f, 0.75f) : new Color(1, 0.4f, 0.35f, 0.7f);
                bool rotated = _facing == Facing.SouthEast || _facing == Facing.NorthWest;
                int sx = rotated ? _placing.SizeY : _placing.SizeX, sy = rotated ? _placing.SizeX : _placing.SizeY;
                // On an occupied tile, keep only the red footprint. Drawing another full sprite over
                // the existing decoration makes an invalid destination look like a second placed item.
                for (int y = _origin.Y; y < _origin.Y + sy && _ghost.enabled; y++)
                for (int x = _origin.X; x < _origin.X + sx; x++)
                {
                    var occupant = Editor.Draft.FindAt(new GridPos(x, y));
                    if (occupant != null && occupant != _moving && occupant != surface) { _ghost.enabled = false; break; }
                }
                Color tint = _valid ? new Color(0.2f, 0.9f, 0.3f, 0.4f) : new Color(1, 0.15f, 0.1f, 0.5f);
                string key = sx + ":" + sy + ":" + _valid;
                if (!_footprints.TryGetValue(key, out var footprint))
                { footprint = PlaceholderArt.Floor(sx, sy, tint, tint); _footprints[key] = footprint; }
                _footprint.sprite = footprint;
                _footprint.transform.localPosition = IsoProjection.GridToWorld(_origin);
                if (_placing.Kind == FurnitureKind.Stove)
                {
                    string workKey = "work:" + _valid;
                    if (!_footprints.TryGetValue(workKey, out var workSprite))
                    {
                        Color workTint = _valid ? new Color(.2f, .75f, 1f, .55f) : tint;
                        _footprints[workKey] = workSprite = PlaceholderArt.Floor(1, 1, workTint, workTint);
                    }
                    _workFootprint.sprite = workSprite;
                    _workFootprint.transform.localPosition = IsoProjection.GridToWorld(FurnitureWorkArea.FrontTile(_placing, _origin, _facing));
                    _workFootprint.enabled = true;
                }
            }
            _ghostBase = _ghost.transform.localPosition;
            _panel.RefreshState();
        }

        /// <summary>
        /// Where a finger aims a new piece: a little above the fingertip, so the piece being placed isn't hidden under
        /// the finger. A placed piece keeps its grab offset instead.
        /// </summary>
        private Vector2 TouchTarget(Vector2 screen)
        {
            if (_moving != null || _movingWall != null || _movingDoor || _placing == null) return screen;
            float lift = (Screen.dpi > 0f ? Screen.dpi : 160f) * 0.35f; // about 9 mm
            return screen + new Vector2(0f, lift);
        }

        /// <summary>
        /// Dragging a held piece to the edge of the view (or past it) scrolls the room that way, faster the further out,
        /// and the piece keeps following the pointer. The shop bar is not an edge: dropping there stores the piece.
        /// </summary>
        private void EdgePan()
        {
            if (!(HasSelection || _painting) || !Application.isFocused) return;
            Vector2 point;
            bool touch = false;
            if (_touchId >= 0)
            {
                if (_touchOnUi || _touchMulti) return;
                point = default;
                foreach (var t in Touch.activeTouches) if (t.touchId == _touchId) { point = t.screenPosition; touch = true; }
                if (!touch) return;
            }
            else if (!Application.isMobilePlatform && Touch.activeTouches.Count == 0 && Mouse.current != null)
                point = Mouse.current.position.ReadValue();
            else return;
            if (_panel.IsOverShopBar(point)) return;

            float margin = Mathf.Min(Screen.width, Screen.height) * 0.1f;
            float bottom = _panel.ShopBarTop();
            Vector2 push = Vector2.zero;
            if (point.x < margin) push.x = -(margin - point.x) / margin;
            else if (point.x > Screen.width - margin) push.x = (point.x - (Screen.width - margin)) / margin;
            if (point.y > Screen.height - margin) push.y = (point.y - (Screen.height - margin)) / margin;
            else if (point.y < bottom + margin && point.y > bottom) push.y = -(bottom + margin - point.y) / margin;
            push = Vector2.ClampMagnitude(push, 1f);
            if (push == Vector2.zero) return;
            _camera.PanWorld(push * Camera.main.orthographicSize * 1.4f * Time.deltaTime);
            if (_painting) UpdatePaint(point);
            else FollowPointer(touch ? TouchTarget(point) : point);
        }

        /// <summary>
        /// Makes what is in hand obvious: the piece floats and bobs a little above its spot, the footprint pulses,
        /// and a marker bounces over it (gold where it fits, red where it doesn't).
        /// </summary>
        private void AnimateHeld()
        {
            bool held = HasSelection && _hasTarget;
            bool paving = HasFloor && _footprint.enabled;
            float t = Time.unscaledTime;
            if (held && _ghost.enabled)
            {
                _ghost.transform.localPosition = _ghostBase + new Vector3(0f, 0.12f + Mathf.Sin(t * 6f) * 0.04f, 0f);
                _view.AnimateFurniturePreview(_ghost, _placing, _facing, t);
            }
            if ((held || paving) && _footprint.enabled)
            {
                var c = _footprint.color;
                c.a = 0.75f + Mathf.Sin(t * 6f) * 0.25f;
                _footprint.color = c;
            }
            _marker.enabled = held && (_ghost.enabled || _footprint.enabled) || paving;
            if (!_marker.enabled) return;
            var top = _ghost.enabled ? _ghost.bounds.max.y : _footprint.bounds.max.y;
            var x = _ghost.enabled ? _ghost.bounds.center.x : _footprint.bounds.center.x;
            var local = _view.transform.InverseTransformPoint(new Vector3(x, top, 0f));
            _marker.transform.localPosition = new Vector3(local.x, local.y + 0.18f + Mathf.Abs(Mathf.Sin(t * 5f)) * 0.12f, 0f);
            _marker.color = paving || _valid ? new Color(1f, 0.82f, 0.2f) : new Color(1f, 0.3f, 0.25f);
        }

        /// <summary>A small downward arrow with a dark rim, drawn once.</summary>
        private static Sprite MarkerSprite()
        {
            const int w = 48, h = 44;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Triangle pointing down: full width at the top, a point at the bottom.
                float half = (y / (float)(h - 1)) * (w / 2f);
                float d = half - Mathf.Abs(x + 0.5f - w / 2f);
                px[y * w + x] = d < 0f ? Color.clear : d < 3f || y > h - 4 ? new Color(0.25f, 0.15f, 0.05f, 1f) : Color.white;
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 128f);
        }

        private void BuildGuides()
        {
            ClearGuides();
            _guides = new GameObject("BuildGrid"); _guides.transform.SetParent(_view.transform, false);
            var grid = Editor.Draft.Grid;
            _guideSprite = PlaceholderArt.Floor(grid.Width, grid.Height,
                new Color(1, 1, 1, 0.12f), new Color(0.3f, 0.18f, 0.08f, 0.15f));
            var sr = _guides.AddComponent<SpriteRenderer>(); sr.sortingOrder = -29950; sr.sprite = _guideSprite;
            _camera.SetRoomBounds(Editor.Draft);
        }

        private void DraftBoundsChanged()
        { BuildGuides(); if (Category == BuildCategory.Land) _panel.RefreshCatalog(); else _panel.RefreshState(); }

        private void ClearGuides()
        {
            if (_guides != null) { _guides.SetActive(false); Destroy(_guides); _guides = null; }
            if (_guideSprite != null) { Destroy(_guideSprite.texture); Destroy(_guideSprite); _guideSprite = null; }
        }

        private void OnDestroy()
        {
            if (_session != null && _panel != null) _session.Wallet.Changed -= _panel.RefreshState;
            if (_camera != null) _camera.CanPanInEditor = null;
            foreach (var sprite in _footprints.Values) { Destroy(sprite.texture); Destroy(sprite); }
            if (_guideSprite != null) { Destroy(_guideSprite.texture); Destroy(_guideSprite); }
        }
    }
}
