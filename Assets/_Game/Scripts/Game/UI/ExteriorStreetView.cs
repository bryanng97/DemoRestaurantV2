using System;
using System.Collections.Generic;
using DemoRestaurant.Game.Controls;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Save;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>Side-on street and facade editor. PointerEventData supports mouse and Android/iOS touch alike.</summary>
    public sealed class ExteriorStreetView : MonoBehaviour
    {
        private GameUi _ui;
        private RectTransform _canvas, _root, _stage, _building, _shop;
        private RectTransform _movingRect;
        private Image _movingImage;
        private Text _balance, _hint, _saveLabel;
        private Button _saveButton;
        private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private readonly List<(Text label,string key)> _localized = new List<(Text,string)>();
        private readonly Stack<ExteriorSave> _undo = new Stack<ExteriorSave>();
        private ExteriorSave _draft;
        private ExteriorPiece _moving;
        private ExteriorKind _category = ExteriorKind.Window;
        private int _selected, _nextInstance;
        private int? _activePointer;
        private Vector2 _grabOffset;
        private bool _placingNew, _cameraWasEnabled;
        private CameraController _camera;
        private GameObject _browseButtons, _editButtons;
        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public bool IsEditing { get; private set; }
        public Func<SaveData, bool> Persist { get; set; }
        public event Action Saved;
        private ExteriorCatalog Catalog => _ui.Session.ExteriorCatalog;

        public void Bind(GameUi ui)
        {
            _ui = ui;
            if (_levelSession != null) _levelSession.LevelUp -= OnRestaurantLevelUp;
            _levelSession = ui.Session;
            _levelSession.LevelUp += OnRestaurantLevelUp;
            _canvas = UiKit.CreateCanvas(transform, "ExteriorCanvas", 50);
            var background = UiKit.NewRect("StreetBackground", _canvas);
            UiKit.Stretch(background);
            background.gameObject.AddComponent<Image>().color = new Color32(64, 164, 209, 255);
            _root = _canvas;
            var safe = SafeArea.Create(_canvas);
            // Code-drawn street scenery is independent of the replaceable building sprites.
            for (int i = 0; i < 20; i++)
            {
                var tower = UiKit.NewRect("DistantBuilding", background);
                tower.anchorMin = tower.anchorMax = new Vector2(.5f, 0);
                tower.pivot = new Vector2(.5f, 0);
                tower.anchoredPosition = new Vector2((i - 9.5f) * 130, 350);
                tower.sizeDelta = new Vector2(125, 200 + (i * 71 % 190));
                tower.gameObject.AddComponent<Image>().color = new Color32(52, 136, 173, 255);
            }
            var road = UiKit.NewRect("Road", background);
            road.anchorMin = Vector2.zero; road.anchorMax = new Vector2(1, 0); road.pivot = Vector2.zero;
            road.sizeDelta = new Vector2(0, 345);
            road.gameObject.AddComponent<Image>().color = new Color32(101, 108, 109, 255);
            var pavement = UiKit.NewRect("Pavement", background);
            pavement.anchorMin = new Vector2(0, 0); pavement.anchorMax = new Vector2(1, 0);
            pavement.pivot = Vector2.zero; pavement.anchoredPosition = new Vector2(0, 305);
            pavement.sizeDelta = new Vector2(0, 80);
            pavement.gameObject.AddComponent<Image>().color = new Color32(217, 211, 188, 255);
            for (int i = -12; i < 13; i++)
                Solid(pavement, "PavingSeam", (i + 12) * 130, 0, 2, 80, new Color32(177, 172, 157, 255));

            var title = Label(safe, "Title", 32, Color.white, TextAnchor.MiddleCenter);
            title.text = Loc.Get("exterior.title");
            _localized.Add((title,"exterior.title"));
            title.rectTransform.anchorMin = new Vector2(.25f, 1); title.rectTransform.anchorMax = new Vector2(.75f, 1);
            title.rectTransform.pivot = new Vector2(.5f, 1); title.rectTransform.sizeDelta = new Vector2(0, 64);
            _balance = Label(safe, "ExteriorBalance", 27, Color.white, TextAnchor.MiddleLeft);
            UiKit.Place(_balance.rectTransform, 35, 22, 380, 60);
            var back = Button(safe, "Enter", "exterior.enter", 235, Close);
            back.anchorMin = back.anchorMax = back.pivot = new Vector2(1, 1);
            back.anchoredPosition = new Vector2(-30, -20);

            _stage = UiKit.NewRect("StreetStage", safe);
            _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(.5f, .5f);
            _stage.anchoredPosition = new Vector2(0, 65);
            _stage.sizeDelta = new Vector2(1000, 680);
            var receiver = _stage.gameObject.AddComponent<Image>();
            receiver.color = new Color(1, 1, 1, 0); receiver.raycastTarget = true;
            _stage.gameObject.AddComponent<ExteriorPointer>().Owner = this;
            // These are explicitly labelled street examples, not online friends or simulated player accounts.
            Neighbor(_stage, -690, false);
            Neighbor(_stage, 690, true);
            _building = UiKit.NewRect("YourFacade", _stage); UiKit.Stretch(_building);

            _browseButtons = UiKit.NewRect("StreetActions", safe).gameObject;
            var browse = (RectTransform)_browseButtons.transform;
            browse.anchorMin = browse.anchorMax = new Vector2(.5f, 0); browse.sizeDelta = new Vector2(700, 100);
            browse.anchoredPosition = new Vector2(0, 135);
            UiKit.Place(Button(browse, "EditExterior", "exterior.edit", 320, BeginEdit), 190, 0, 320, 72);

            _editButtons = UiKit.NewRect("EditorControls", safe).gameObject;
            var edit = (RectTransform)_editButtons.transform;
            UiKit.Stretch(edit);
            var toolbar = UiKit.NewRect("EditToolbar", edit);
            toolbar.anchorMin = toolbar.anchorMax = toolbar.pivot = new Vector2(.5f, 1);
            toolbar.anchoredPosition = new Vector2(0, -90); toolbar.sizeDelta = new Vector2(1080, 60);
            UiKit.Place(Button(toolbar, "Cancel", "exterior.cancel", 170, CancelEdit), 0, 0, 170, 58);
            UiKit.Place(Button(toolbar, "Undo", "exterior.undo", 150, Undo), 182, 0, 150, 58);
            UiKit.Place(Button(toolbar, "Flip", "exterior.flip", 150, Flip), 344, 0, 150, 58);
            UiKit.Place(Button(toolbar, "Store", "exterior.remove", 230, StoreSelected), 506, 0, 230, 58);
            _saveButton = UiKit.TextButton(toolbar, "SaveExterior", UiKit.Green, 23, Save, out _saveLabel);
            UiKit.Place((RectTransform)_saveButton.transform, 750, 0, 330, 58);
            _shop = UiKit.Box(edit, "ExteriorShop", UiKit.Wood, UiKit.Cream, 4);
            _shop.anchorMin = new Vector2(0, 0); _shop.anchorMax = new Vector2(1, 0);
            _shop.pivot = new Vector2(.5f, 0); _shop.sizeDelta = new Vector2(-30, 240);
            _shop.anchoredPosition = new Vector2(0, 8);
            _hint = Label(safe, "StreetHint", 22, UiKit.Cream, TextAnchor.MiddleCenter);
            _hint.rectTransform.anchorMin = new Vector2(.05f, 0); _hint.rectTransform.anchorMax = new Vector2(.95f, 0);
            _hint.rectTransform.pivot = new Vector2(.5f, 0); _hint.rectTransform.sizeDelta = new Vector2(0, 55);
            _hint.rectTransform.anchoredPosition = new Vector2(0, 250);
            _root.gameObject.SetActive(false);
        }

        public void Show()
        {
            if (Catalog == null) return;
            _draft = _ui.Session.CaptureExterior();
            _undo.Clear(); _moving = null; _selected = 0; _activePointer = null;
            foreach (var entry in _localized) entry.label.text = Loc.Get(entry.key);
            IsEditing = false;
            _camera = Camera.main != null ? Camera.main.GetComponent<CameraController>() : null;
            _cameraWasEnabled = _camera != null && _camera.enabled;
            if (_camera != null) _camera.enabled = false;
            _ui.SetGameplayVisible(false);
            _root.gameObject.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            // Leaving an open editor is an explicit cancel; only Save commits the draft.
            IsEditing = false; _moving = null; _activePointer = null;
            _root.gameObject.SetActive(false);
            _ui.SetGameplayVisible(true);
            if (_camera != null) _camera.enabled = _cameraWasEnabled;
        }

        private void BeginEdit()
        {
            IsEditing = true;
            _activePointer = null;
            _draft = _ui.Session.CaptureExterior();
            _nextInstance = 1;
            foreach (var p in _draft.pieces) _nextInstance = Math.Max(_nextInstance, p.instance + 1);
            Refresh();
        }
        private void CancelEdit() { IsEditing = false; _moving = null; _selected = 0; _activePointer = null; _draft = _ui.Session.CaptureExterior(); Refresh(); }
        private void Undo()
        {
            _moving = null; _selected = 0;
            if (_undo.Count > 0) _draft = _undo.Pop();
            Refresh();
        }
        private void Checkpoint() => _undo.Push(_draft.Copy());
        private ExteriorPiece Selected() => _draft.pieces.Find(p => p.instance == _selected);
        private void StoreSelected()
        {
            _moving = null;
            var p = Selected(); if (p == null) return;
            var kind = Catalog.Get(p.itemId).kind;
            if (ExteriorCatalog.Fixed(kind) || kind == ExteriorKind.Door) { _hint.text = Loc.Get("exterior.required"); return; }
            Checkpoint(); _draft.pieces.Remove(p); _selected = 0; Refresh();
        }
        private void Flip()
        {
            var p = _moving ?? Selected(); if (p == null) return;
            if (_moving == null) Checkpoint();
            p.flipped = !p.flipped; DrawBuilding();
        }
        private void Save()
        {
            // Save finishes a valid preview as well; deselection is never required.
            if (_moving != null && !PlaceMoving()) { _hint.text = Loc.Get("exterior.invalid"); return; }
            var result = _ui.Session.CommitExterior(_draft, Persist);
            if (result != LandPurchaseResult.Success)
            {
                _hint.text = Loc.Get(result == LandPurchaseResult.InsufficientCoins ? "exterior.no_coins" : "exterior.save_failed");
                return;
            }
            _draft = _ui.Session.CaptureExterior(); IsEditing = false; _selected = 0; _activePointer = null; _undo.Clear();
            Saved?.Invoke(); Refresh(); _hint.text = Loc.Get("exterior.saved");
        }

        private void Choose(ExteriorItemDef d)
        {
            if (d.unlockLevel > _ui.Session.Progress.Level) return;
            _moving = null;
            if (ExteriorCatalog.Single(d.kind))
            {
                var existing = _draft.pieces.Find(p => Catalog.Get(p.itemId).kind == d.kind);
                var replacement = new ExteriorPiece { instance = existing?.instance ?? _nextInstance++, itemId = d.id,
                    x = existing?.x ?? 500, y = existing?.y ?? (d.kind == ExteriorKind.Sign ? _box.Top + d.height + 15 : 620) };
                if (d.kind == ExteriorKind.Roof) replacement.y = 272;
                if (Catalog.CanPlace(_draft.pieces, replacement, Level))
                {
                    Checkpoint(); if (existing != null) _draft.pieces.Remove(existing);
                    _draft.pieces.Add(replacement); _selected = replacement.instance;
                }
            }
            else
            {
                _selected = _nextInstance++;
                _moving = new ExteriorPiece { instance = _selected, itemId = d.id, x = 500,
                    y = d.kind == ExteriorKind.Ground ? 650 : 540 };
                _placingNew = true;
                _grabOffset = Vector2.zero;
            }
            DrawBuilding(); RefreshCost();
        }

        public void PointerDown(PointerEventData e, int instance)
        {
            if (e.button != PointerEventData.InputButton.Left || _activePointer.HasValue) return;
            _activePointer = e.pointerId;
            if (!IsEditing)
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_building, e.position, e.pressEventCamera, out var tap))
                {
                    var door = _draft.pieces.Find(p => Catalog.Get(p.itemId).kind == ExteriorKind.Door);
                    var def = Catalog.Get(door.itemId);
                    if (Mathf.Abs(tap.x + 500 - door.x) <= def.width / 2 && 340 - tap.y >= door.y - def.height &&
                        340 - tap.y <= door.y) Close();
                }
                return;
            }
            if (_moving != null && _placingNew) { Move(e); return; }
            if (instance == 0 && RectTransformUtility.ScreenPointToLocalPointInRectangle(_building, e.position, e.pressEventCamera, out var hit))
            {
                float x = hit.x + 500, y = 340 - hit.y;
                for (int i = _draft.pieces.Count - 1; i >= 0; i--)
                {
                    var piece = _draft.pieces[i]; var def = Catalog.Get(piece.itemId);
                    if (!ExteriorCatalog.Fixed(def.kind) && x >= piece.x - def.width / 2 && x <= piece.x + def.width / 2 &&
                        y >= piece.y - def.height && y <= piece.y) { instance = piece.instance; break; }
                }
            }
            var p = _draft.pieces.Find(piece => piece.instance == instance);
            if (p == null || ExteriorCatalog.Fixed(Catalog.Get(p.itemId).kind)) return;
            _selected = instance; _moving = p.Copy(); _placingNew = false;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_building, e.position, e.pressEventCamera, out var picked))
                _grabOffset = new Vector2(p.x - (picked.x + 500), p.y - (340 - picked.y));
            DrawBuilding();
        }
        /// <summary>A card dragged up out of the shop bar: the piece follows the finger (single pieces swap at once).</summary>
        public void BeginCardDrag(ExteriorItemDef d, PointerEventData e)
        {
            if (!IsEditing || _activePointer.HasValue) return;
            Choose(d);
            if (_moving == null) return;
            _activePointer = e.pointerId;
            Move(e);
        }

        public void EndCardDrag(PointerEventData e)
        {
            if (_activePointer != e.pointerId) return;
            PointerUp(e);
        }

        private bool OverShop(PointerEventData e) => RectTransformUtility.RectangleContainsScreenPoint(_shop, e.position, e.pressEventCamera);

        public void PointerDrag(PointerEventData e)
        { if (IsEditing && _moving != null && _activePointer == e.pointerId) Move(e); }
        public void PointerUp(PointerEventData e)
        {
            if (_activePointer != e.pointerId) return;
            _activePointer = null;
            if (!IsEditing || _moving == null) return;
            if (OverShop(e))
            {
                // Dropped on the shop bar: a new piece goes back, a placed one goes to storage.
                var kind = Catalog.Get(_moving.itemId).kind;
                if (!_placingNew && (ExteriorCatalog.Fixed(kind) || kind == ExteriorKind.Door)) _hint.text = Loc.Get("exterior.required");
                else if (!_placingNew) { Checkpoint(); _draft.pieces.RemoveAll(p => p.instance == _moving.instance); }
                _moving = null; _placingNew = false; _selected = 0;
                DrawBuilding(); RefreshCost();
                return;
            }
            Move(e);
            if (!PlaceMoving()) _hint.text = Loc.Get("exterior.invalid");
            DrawBuilding(); RefreshCost();
        }
        private void Move(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_building, e.position, e.pressEventCamera, out var local)) return;
            var def = Catalog.Get(_moving.itemId);
            _moving.x = Mathf.Round((local.x + 500 + _grabOffset.x) / 5) * 5;
            _moving.y = def.kind == ExteriorKind.Ground ? 650 : def.kind == ExteriorKind.Door ? 620 : Mathf.Round((340 - local.y + _grabOffset.y) / 5) * 5;
            if (_movingRect != null)
            {
                Pose(_movingRect, _moving, def, _box);
                PlaceMarker(_moving, def);
                _movingImage.color = Catalog.CanPlace(_draft.pieces, _moving, Level) ? new Color(.65f, 1, .65f, .8f) : new Color(1, .45f, .45f, .8f);
            }
            RefreshCost();
        }
        private bool PlaceMoving()
        {
            if (_moving == null) return true;
            if (!Catalog.CanPlace(_draft.pieces, _moving, Level)) return false;
            Checkpoint(); _draft.pieces.RemoveAll(p => p.instance == _moving.instance);
            _draft.pieces.Add(_moving.Copy()); _moving = null; _placingNew = false;
            return true;
        }

        private void Update()
        {
            if (!IsOpen) return;
            _stage.localScale = Vector3.one * Mathf.Min(1, _root.rect.width / 1450f);
            _balance.text = Loc.Format("exterior.cost", _ui.Session.Wallet.DisplayCoins);
            if (_markerArrow != null) _markerArrow.anchoredPosition = new Vector2(0f, Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f)) * 12f);
            // Mouse previews follow the pointer immediately. Touch places on the next tap/drag.
            if (IsEditing && _moving != null && _placingNew && UnityEngine.InputSystem.Mouse.current != null &&
                UnityEngine.InputSystem.Mouse.current.delta.ReadValue().sqrMagnitude > 0)
            {
                var mouse = new PointerEventData(EventSystem.current) { position = UnityEngine.InputSystem.Mouse.current.position.ReadValue() };
                if (RectTransformUtility.RectangleContainsScreenPoint(_stage, mouse.position) &&
                    !RectTransformUtility.RectangleContainsScreenPoint(_shop, mouse.position)) Move(mouse);
            }
        }

        private void Refresh()
        {
            _editButtons.SetActive(IsEditing); _browseButtons.SetActive(!IsEditing);
            _hint.text = Loc.Get(IsEditing ? "exterior.hint" : "exterior.street_hint");
            if (IsEditing) DrawShop();
            DrawBuilding(); RefreshCost();
        }
        private void RefreshCost()
        {
            if (!IsEditing) return;
            var proposed = _draft;
            if (_moving != null && Catalog.CanPlace(_draft.pieces, _moving, Level))
            {
                proposed = _draft.Copy(); proposed.pieces.RemoveAll(p => p.instance == _moving.instance);
                proposed.pieces.Add(_moving.Copy());
            }
            bool valid = Catalog.TryQuote(_ui.Session.CaptureExterior(), proposed.pieces, _ui.Session.Progress.Level, out long cost, out _);
            _saveLabel.text = Loc.Format("exterior.save", cost);
            _saveButton.interactable = valid;
        }

        private void DrawShop()
        {
            Clear(_shop);
            int categoryIndex = 0;
            foreach (ExteriorKind kind in Enum.GetValues(typeof(ExteriorKind)))
            {
                var k = kind;
                var button = UiKit.TextButton(_shop, kind.ToString(), k == _category ? UiKit.Green : UiKit.WoodLight,
                    19, () => { _category = k; DrawShop(); }, out var label);
                label.text = Loc.Get("exterior." + k);
                UiKit.Place((RectTransform)button.transform, 14 + categoryIndex++ * 185, 10, 177, 43);
            }
            var viewport = UiKit.NewRect("ShopViewport", _shop);
            UiKit.Stretch(viewport, 12, 62, 12, 8);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = true; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var content = UiKit.NewRect("CatalogItems", viewport);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0, 1);
            scroll.viewport = viewport; scroll.content = content;
            int i = 0;
            var owned = _ui.Session.CaptureExterior().owned;
            foreach (var d in Catalog.Items)
            {
                if (d.kind != _category) continue;
                var item = d;
                var card = UiKit.Box(content, d.id, UiKit.CardRim, UiKit.CardFill, 2);
                UiKit.Place(card, i++ * 220, 0, 208, 163);
                UiKit.AddButton(card, () => Choose(item));
                // Drag a card up out of the bar to carry the piece straight to the building; sideways still scrolls.
                var pull = card.gameObject.AddComponent<ExteriorCardDrag>();
                pull.Owner = this; pull.Item = item; pull.Scroll = scroll;
                var icon = UiKit.NewRect("Art", card); UiKit.Place(icon, 44, 4, 120, 82);
                UiKit.AddImage(icon, Sprite(d));
                var name = Label(card, "ItemName", 18, UiKit.DarkText, TextAnchor.MiddleCenter);
                UiKit.Place(name.rectTransform, 3, 85, 202, 30); name.text = Loc.Get(d.nameKey);
                var price = Label(card, "Price", 17, UiKit.SoftText, TextAnchor.MiddleCenter);
                UiKit.Place(price.rectTransform, 3, 115, 202, 42);
                int count = owned.Find(o => o.itemId == d.id)?.count ?? 0;
                price.text = Loc.Format("exterior.cost", d.price) + "\n" + Loc.Format("exterior.owned", count);
            }
            content.sizeDelta = new Vector2(i * 220, 163);
        }

        private void DrawBuilding()
        {
            if (_building == null || _draft == null) return;
            _movingRect = null; _movingImage = null;
            Clear(_building);
            _box = Catalog.BoxFor(Level);
            // Keep the street scale fixed through the playable levels as well as roof changes.
            // Otherwise zooming out on each upgrade cancels the facade's visible growth.
            var frameBox = Catalog.BoxFor(_ui.Session.Progress.MaxLevel);
            float roofHeight = 0f, frameWidth = frameBox.Width;
            foreach (var item in Catalog.Items)
            {
                if (item.kind != ExteriorKind.Roof) continue;
                var bounds = RoofBounds(item,frameBox);
                roofHeight = Mathf.Max(roofHeight,frameBox.Top+RoofOverlap-bounds.y);
                frameWidth = Mathf.Max(frameWidth,2f*Mathf.Max(Mathf.Abs(bounds.x-500f),Mathf.Abs(bounds.xMax-500f)));
            }
            float s = Mathf.Min(1f, 960f / frameWidth, 530f / (frameBox.Height + roofHeight));
            _building.localScale = new Vector3(s, s, 1f);
            _building.anchoredPosition = new Vector2(0f, -280f * (1f - s)); // keep the pavement line in place
            DrawFacade(_building, _draft, true, _box);
            DrawHandTools();
        }

        private void OnRestaurantLevelUp(int level,int coins)
        {
            if (IsOpen) Refresh(); // Keep the draft and placed decorations; the new wall bounds only expand.
        }

        private DemoRestaurant.Logic.Session.RestaurantSession _levelSession;

        private void OnDestroy()
        {
            if (_levelSession != null) _levelSession.LevelUp -= OnRestaurantLevelUp;
        }

        // What is held or selected: a bouncing marker above it, and (while it sits still) flip / store buttons beside it,
        // big enough for a finger, so nobody has to reach for the toolbar.
        private RectTransform _marker, _markerArrow;
        private static Sprite _markerSprite;

        private void DrawHandTools()
        {
            _marker = _markerArrow = null;
            if (!IsEditing) return;
            var p = _moving ?? Selected();
            if (p == null) return;
            var d = Catalog.Get(p.itemId);
            if (ExteriorCatalog.Fixed(d.kind)) return;
            _marker = UiKit.NewRect("HeldMarker", _building);
            _markerArrow = UiKit.NewRect("Arrow", _marker);
            UiKit.Stretch(_markerArrow);
            var arrow = _markerArrow.gameObject.AddComponent<Image>();
            arrow.sprite = MarkerSprite(); arrow.raycastTarget = false;
            PlaceMarker(p, d);
            if (_moving != null) return;
            float top = d.kind == ExteriorKind.Roof ? RoofBounds(d,_box).y : p.y - d.height;
            var flip = UiKit.TextButton(_building, "HandFlip", UiKit.WoodLight, 22, Flip, out var flipLabel);
            flipLabel.text = Loc.Get("exterior.flip");
            UiKit.Place((RectTransform)flip.transform, p.x - 140, top - 116, 130, 58);
            if (d.kind == ExteriorKind.Door) return; // the door can't be stored
            var store = UiKit.TextButton(_building, "HandStore", UiKit.Red, 22, StoreSelected, out var storeLabel);
            storeLabel.text = Loc.Get("exterior.store_short");
            UiKit.Place((RectTransform)store.transform, p.x + 10, top - 116, 130, 58);
        }

        private void PlaceMarker(ExteriorPiece p, ExteriorItemDef d)
        {
            if (_marker == null) return;
            float top = d.kind == ExteriorKind.Roof ? RoofBounds(d,_box).y : p.y - d.height;
            UiKit.Place(_marker, p.x - 22, top - 50, 44, 40);
            _markerArrow.GetComponent<Image>().color = _moving != null && !Catalog.CanPlace(_draft.pieces, _moving, Level)
                ? new Color(1f, .35f, .3f) : new Color(1f, .82f, .2f);
        }

        private static Sprite MarkerSprite()
        {
            if (_markerSprite != null) return _markerSprite;
            const int w = 48, h = 44;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = y / (float)(h - 1) * (w / 2f) - Mathf.Abs(x + .5f - w / 2f);
                px[y * w + x] = d < 0f ? Color.clear : d < 3f || y > h - 4 ? new Color(.25f, .15f, .05f) : Color.white;
            }
            tex.SetPixels(px); tex.Apply();
            return _markerSprite = UnityEngine.Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
        }

        private void DrawFacade(RectTransform parent, ExteriorSave state, bool interactive, ExteriorBox box)
        {
            var wall = state.pieces.Find(p => Catalog.Get(p.itemId).kind == ExteriorKind.Wall);
            var wallDef = Catalog.Get(wall.itemId);
            var sprite = Sprite(wallDef);
            _wallSprite = sprite;
            // A tile keeps its size relative to the building (prototype tiles); the base art's wall images are 100 x 90 tiles.
            _wallTile = wallDef.width < 400f ? new Vector2(wallDef.width, wallDef.height) : new Vector2(100f, 90f);
            // Repeat the wall tile over the front (clipped to its size), as the reference game fills its building base.
            var front = UiKit.NewRect("Front", parent);
            UiKit.Place(front, box.Left, box.Top, box.Width, box.Height);
            front.gameObject.AddComponent<RectMask2D>();
            for (float y = 0; y < box.Height; y += _wallTile.y) for (float x = 0; x < box.Width; x += _wallTile.x)
            {
                var tile = UiKit.NewRect("WallTile", front);
                UiKit.Place(tile, x, y, _wallTile.x, _wallTile.y);
                UiKit.AddImage(tile, sprite, false).raycastTarget = false;
            }
            foreach (var p in state.pieces)
            {
                var kind = Catalog.Get(p.itemId).kind;
                if (kind == ExteriorKind.Wall || (interactive && _moving != null && p.instance == _moving.instance)) continue;
                DrawPiece(parent, p, interactive, false);
            }
            if (interactive && _moving != null) DrawPiece(parent, _moving, true, true);
        }
        private void DrawPiece(RectTransform parent, ExteriorPiece p, bool interactive, bool preview)
        {
            var d = Catalog.Get(p.itemId);
            var rect = UiKit.NewRect(d.id + "#" + p.instance, parent);
            var box = parent == _building ? _box : Catalog.BoxFor(1);
            Pose(rect, p, d, box);
            var image = d.kind == ExteriorKind.Roof ? UiKit.AddImage(Stretched(rect, "RoofArt"), Sprite(d), false) : UiKit.AddImage(rect, Sprite(d), false);
            if (preview)
            {
                _movingRect = rect; _movingImage = image;
                image.color = Catalog.CanPlace(_draft.pieces, p, Level) ? new Color(.65f, 1, .65f, .8f) : new Color(1, .45f, .45f, .8f);
            }
            if (interactive)
            {
                // All gestures belong to the persistent stage, so a visual refresh cannot cancel a touch drag.
                image.raycastTarget = false;
                if (_selected == p.instance)
                {
                    var outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = UiKit.Gold;
                    outline.effectDistance = new Vector2(3, 3);
                }
            }
            if (d.kind == ExteriorKind.Sign)
            {
                // Native export omits the Flash text field; Unity draws the live restaurant name.
                var plaque = UiKit.Place(UiKit.NewRect("NamePlaque",rect),33,22,d.width-66,d.height-37);
                var name = Label(plaque, "RestaurantName", 25, UiKit.DarkText, TextAnchor.MiddleCenter);
                UiKit.Stretch(name.rectTransform); name.text = interactive ? _ui.Session.Profile.DisplayName : Loc.Get("exterior.preview");
                if (p.flipped) name.rectTransform.localScale = new Vector3(-1, 1, 1);
            }
        }
        private void Neighbor(RectTransform stage, float x, bool right)
        {
            var rect = UiKit.NewRect("StreetExample", stage);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(1000, 680); rect.localScale = Vector3.one * .52f;
            rect.anchoredPosition = new Vector2(x, -150);
            var state = Catalog.Starter();
            state.pieces.Find(p => Catalog.Get(p.itemId).kind == ExteriorKind.Roof).itemId = right ? "roof_gables" : "roof_red";
            state.pieces.Find(p => Catalog.Get(p.itemId).kind == ExteriorKind.Wall).itemId = right ? "wall_red" : "wall_stone";
            DrawFacade(rect, state, false, Catalog.BoxFor(1));
            var label = Label(rect, "PreviewLabel", 28, UiKit.Cream, TextAnchor.MiddleCenter);
            UiKit.Place(label.rectTransform, 180, 650, 640, 60); label.text = Loc.Get("exterior.preview");
            _localized.Add((label,"exterior.preview"));
        }
        private Sprite Sprite(ExteriorItemDef d)
        {
            if (!_sprites.TryGetValue(d.sprite, out var s)) _sprites[d.sprite] = s = Resources.Load<Sprite>(d.sprite);
            return s;
        }
        /// <summary>How far a roof reaches down over the top of the wall.</summary>
        private const float RoofOverlap = 2f;
        private Sprite _wallSprite;

        private static RectTransform Stretched(RectTransform parent, string name)
        {
            var r = UiKit.NewRect(name, parent); UiKit.Stretch(r); return r;
        }

        private Vector2 _wallTile = new Vector2(100f, 90f);

        private int Level => _ui.Session.Progress.Level;
        /// <summary>The shop front at the current level (it grows like the reference game's building bases).</summary>
        private ExteriorBox _box = new ExteriorBox(100f, 900f, 260f, 620f);

        private static void Pose(RectTransform rect, ExteriorPiece p, ExteriorItemDef d, ExteriorBox box)
        {
            if (d.kind == ExteriorKind.Roof)
            {
                var bounds = RoofBounds(d,box,p.flipped);
                UiKit.Place(rect,bounds.x,bounds.y,bounds.width,bounds.height);
                rect.localScale = p.flipped ? new Vector3(-1, 1, 1) : Vector3.one;
                if (p.flipped) { rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition += new Vector2(bounds.width / 2, 0); }
                return;
            }
            UiKit.Place(rect, p.x - d.width / 2, p.y - d.height, d.width, d.height);
            if (p.flipped)
            {
                rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition += new Vector2(d.width / 2, 0);
                rect.localScale = new Vector3(-1, 1, 1);
            }
            else rect.localScale = Vector3.one;
        }

        private static Rect RoofBounds(ExteriorItemDef roof,ExteriorBox box,bool flipped=false)
        {
            float k = box.Width / ExteriorCatalog.BaseWidth;
            float w = roof.width*k, h = roof.height*k;
            float anchorX = flipped ? 1f-roof.roofAnchorX : roof.roofAnchorX;
            return new Rect(500f-w*anchorX,box.Top+RoofOverlap-h*roof.roofAnchorY,w,h);
        }
        private static RectTransform Solid(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var rect = UiKit.NewRect(name, parent); UiKit.Place(rect, x, y, w, h);
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
            return rect;
        }
        private static Text Label(Transform parent, string name, int size, Color color, TextAnchor anchor) =>
            UiKit.AddText(UiKit.NewRect(name, parent), size, anchor, color);
        private RectTransform Button(Transform parent, string name, string key, float width, Action clicked)
        {
            var b = UiKit.TextButton(parent, name, UiKit.WoodLight, 24, clicked, out var label);
            label.text = Loc.Get(key); _localized.Add((label,key));
            var rect = (RectTransform)b.transform; rect.sizeDelta = new Vector2(width, 64); return rect;
        }
        private static void Clear(Transform root)
        {
            foreach (Transform child in root) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        }
    }

    public sealed class ExteriorPointer : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public ExteriorStreetView Owner;
        public int Instance;
        public void OnPointerDown(PointerEventData e) => Owner.PointerDown(e, Instance);
        public void OnDrag(PointerEventData e) => Owner.PointerDrag(e);
        public void OnPointerUp(PointerEventData e) => Owner.PointerUp(e);
    }

    /// <summary>
    /// On a shop card: an upward drag carries the piece out of the bar (ExteriorStreetView.BeginCardDrag); any other
    /// drag is handed to the bar's scroll view, so the cards still scroll sideways.
    /// </summary>
    public sealed class ExteriorCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public ExteriorStreetView Owner;
        public ExteriorItemDef Item;
        public ScrollRect Scroll;
        private bool _pulling;

        public void OnBeginDrag(PointerEventData e)
        {
            _pulling = e.delta.y > Mathf.Abs(e.delta.x);
            if (_pulling) Owner.BeginCardDrag(Item, e);
            else if (Scroll != null) Scroll.OnBeginDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (_pulling) Owner.PointerDrag(e);
            else if (Scroll != null) Scroll.OnDrag(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_pulling) Owner.EndCardDrag(e);
            else if (Scroll != null) Scroll.OnEndDrag(e);
            _pulling = false;
        }
    }
}
