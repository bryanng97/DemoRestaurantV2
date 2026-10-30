using System;
using System.Collections.Generic;
using DemoRestaurant.Game.Controls;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Online;
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
        private Button _saveButton, _undoButton, _flipButton, _storeButton;
        private RectTransform _selectionTools;
        private Text _costLabel;
        private GameObject _exitPrompt;
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
        private RectTransform _enterButton;
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
            _enterButton = back;
            back.anchorMin = back.anchorMax = back.pivot = new Vector2(1, 1);
            back.anchoredPosition = new Vector2(-30, -20); back.sizeDelta = new Vector2(300, 104);

            _stage = UiKit.NewRect("StreetStage", safe);
            _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(.5f, .5f);
            _stage.anchoredPosition = new Vector2(0, 65);
            _stage.sizeDelta = new Vector2(1000, 680);
            var receiver = _stage.gameObject.AddComponent<Image>();
            receiver.color = new Color(1, 1, 1, 0); receiver.raycastTarget = true;
            _stage.gameObject.AddComponent<ExteriorPointer>().Owner = this;
            // While browsing, the whole screen swipes the street (neighbours stand outside the stage's rect).
            _streetTouch = UiKit.NewRect("StreetTouch", safe); UiKit.Stretch(_streetTouch);
            _streetTouch.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0);
            _streetTouch.gameObject.AddComponent<ExteriorPointer>().Owner = this;
            _streetTouch.SetSiblingIndex(_stage.GetSiblingIndex());
            // The street pans as one: your front in the middle, the random street's restaurants either side.
            _pan = UiKit.NewRect("StreetPan", _stage); UiKit.Stretch(_pan);
            _neighbours = UiKit.NewRect("Neighbours", _pan); UiKit.Stretch(_neighbours);
            _building = UiKit.NewRect("YourFacade", _pan); UiKit.Stretch(_building);
            _streetStatus = Label(safe, "StreetStatus", 24, UiKit.Cream, TextAnchor.MiddleLeft);
            _streetStatus.rectTransform.anchorMin = _streetStatus.rectTransform.anchorMax = _streetStatus.rectTransform.pivot = new Vector2(0, 1);
            _streetStatus.rectTransform.anchoredPosition = new Vector2(35, -82); _streetStatus.rectTransform.sizeDelta = new Vector2(520, 44);
            FitLabel(_streetStatus, 18, 24);

            _browseButtons = UiKit.NewRect("StreetActions", safe).gameObject;
            var browse = (RectTransform)_browseButtons.transform;
            browse.anchorMin = browse.anchorMax = new Vector2(.5f, 0); browse.sizeDelta = new Vector2(700, 100);
            browse.anchoredPosition = new Vector2(0, 135);
            UiKit.Place(Button(browse, "EditExterior", "exterior.edit", 320, BeginEdit), 135, 0, 430, 128);
            _refreshButton = Button(safe, "NewStreet", "ui.street.refresh", 300, () => LoadStreet(true));
            AnchorTop(_refreshButton, true, 30, 136, 300, 90);

            _editButtons = UiKit.NewRect("EditorControls", safe).gameObject;
            var edit = (RectTransform)_editButtons.transform;
            UiKit.Stretch(edit);
            var toolbar = UiKit.NewRect("EditToolbar", edit);
            UiKit.Stretch(toolbar);
            var exit = Button(toolbar, "ExitEditor", "exterior.exit_editor", 230, RequestExitEdit);
            AnchorTop(exit, false, 30, 22, 230, 104);
            var undo = Button(toolbar, "Undo", "exterior.undo_icon", 104, Undo);
            _undoButton = undo.GetComponent<Button>();
            AnchorTop(undo, true, 266, 22, 104, 104);
            _saveButton = UiKit.TextButton(toolbar, "SaveExterior", UiKit.Green, 32, Save, out _saveLabel);
            _saveLabel.text = Loc.Get("exterior.done"); _localized.Add((_saveLabel, "exterior.done"));
            AnchorTop((RectTransform)_saveButton.transform, true, 30, 22, 220, 104);
            _costLabel = Label(toolbar, "RenovationCost", 26, UiKit.Cream, TextAnchor.MiddleRight);
            AnchorTop(_costLabel.rectTransform, true, 30, 132, 350, 44);
            FitLabel(_costLabel, 22, 26);

            _selectionTools = UiKit.Box(edit, "SelectionTools", UiKit.CardRim, UiKit.Cream, 3);
            _selectionTools.anchorMin = _selectionTools.anchorMax = _selectionTools.pivot = new Vector2(1, 0);
            _selectionTools.anchoredPosition = new Vector2(-30, 352); _selectionTools.sizeDelta = new Vector2(440, 112);
            var flip = Button(_selectionTools, "Flip", "exterior.flip", 190, Flip);
            UiKit.Place(flip, 8, 8, 190, 96); _flipButton = flip.GetComponent<Button>();
            var store = Button(_selectionTools, "Store", "exterior.store_short", 222, StoreSelected);
            UiKit.Place(store, 210, 8, 222, 96); _storeButton = store.GetComponent<Button>();
            _selectionTools.gameObject.SetActive(false);
            _shop = UiKit.Box(edit, "ExteriorShop", UiKit.Wood, UiKit.Cream, 4);
            _shop.anchorMin = new Vector2(0, 0); _shop.anchorMax = new Vector2(1, 0);
            _shop.pivot = new Vector2(.5f, 0); _shop.sizeDelta = new Vector2(-30, 336);
            _shop.anchoredPosition = new Vector2(0, 8);
            _hint = Label(safe, "StreetHint", 22, UiKit.Cream, TextAnchor.MiddleCenter);
            _hint.rectTransform.anchorMin = new Vector2(.05f, 0); _hint.rectTransform.anchorMax = new Vector2(.95f, 0);
            _hint.rectTransform.pivot = new Vector2(.5f, 0); _hint.rectTransform.sizeDelta = new Vector2(0, 55);
            _hint.rectTransform.anchoredPosition = new Vector2(0, 346);
            var promptCard = UiKit.Modal(_canvas, "UnsavedExterior", new Vector2(980, 450), out _exitPrompt);
            var promptTitle = UiKit.Label(promptCard, "Title", 40, 28, 900, 62, 36, TextAnchor.MiddleCenter, UiKit.DarkText);
            _localized.Add((promptTitle, "exterior.unsaved_title"));
            var promptBody = UiKit.Label(promptCard, "Body", 60, 110, 860, 120, 28, TextAnchor.MiddleCenter, UiKit.DarkText, true);
            _localized.Add((promptBody, "exterior.unsaved_body"));
            UiKit.Place(Button(promptCard, "ContinueEditing", "exterior.keep_editing", 280, HideExitPrompt), 40, 302, 280, 104);
            UiKit.Place(Button(promptCard, "Discard", "exterior.discard", 280, () => { HideExitPrompt(); CancelEdit(); }), 350, 302, 280, 104);
            var savePrompt = UiKit.TextButton(promptCard, "SaveChanges", UiKit.Green, 30,
                () => { HideExitPrompt(); Save(); }, out var promptSaveLabel);
            _localized.Add((promptSaveLabel, "exterior.save_changes"));
            FitLabel(promptSaveLabel, 24, 30); UiKit.Place((RectTransform)savePrompt.transform, 660, 302, 280, 104);
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
            if (_shops.Count == 0) LoadStreet(false);
        }

        public void Close()
        {
            // Leaving an open editor is an explicit cancel; only Save commits the draft.
            IsEditing = false; _moving = null; _activePointer = null;
            HideExitPrompt();
            _root.gameObject.SetActive(false);
            _ui.SetGameplayVisible(true);
            if (_camera != null) _camera.enabled = _cameraWasEnabled;
        }

        private void BeginEdit()
        {
            IsEditing = true; HideExitPrompt();
            _activePointer = null;
            _draft = _ui.Session.CaptureExterior();
            _undo.Clear();
            _nextInstance = 1;
            foreach (var p in _draft.pieces) _nextInstance = Math.Max(_nextInstance, p.instance + 1);
            Refresh();
        }
        private void HideExitPrompt() => _exitPrompt.SetActive(false);

        private void RequestExitEdit()
        {
            if (!HasExteriorChanges(_ui.Session.CaptureExterior(), _draft, _moving)) { CancelEdit(); return; }
            _exitPrompt.transform.SetAsLastSibling(); _exitPrompt.SetActive(true);
        }

        private static bool HasExteriorChanges(ExteriorSave original, ExteriorSave draft, ExteriorPiece moving)
        {
            int count = draft.pieces.Count;
            if (moving != null && !draft.pieces.Exists(p => p.instance == moving.instance)) count++;
            if (original.pieces.Count != count) return true;
            foreach (var saved in original.pieces)
            {
                var piece = moving != null && moving.instance == saved.instance
                    ? moving : draft.pieces.Find(p => p.instance == saved.instance);
                if (piece == null || piece.itemId != saved.itemId || piece.x != saved.x || piece.y != saved.y || piece.flipped != saved.flipped)
                    return true;
            }
            return false;
        }

        private void CancelEdit() { HideExitPrompt(); IsEditing = false; _moving = null; _selected = 0; _activePointer = null; _draft = _ui.Session.CaptureExterior(); Refresh(); }
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
            var p = _moving ?? Selected(); if (p == null) return;
            var kind = Catalog.Get(p.itemId).kind;
            if (ExteriorCatalog.Fixed(kind) || kind == ExteriorKind.Door) { _hint.text = Loc.Get("exterior.required"); return; }
            if (_draft.pieces.Exists(piece => piece.instance == p.instance))
            {
                Checkpoint(); _draft.pieces.RemoveAll(piece => piece.instance == p.instance);
            }
            _moving = null; _placingNew = false; _activePointer = null; _selected = 0; Refresh();
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
                // Browsing: a drag pans the street; a tap (decided on release) enters a door or a neighbour.
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_stage, e.position, e.pressEventCamera, out _pressLocal);
                _pressPan = _panX; _dragged = false;
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
        {
            if (_activePointer != e.pointerId) return;
            if (IsEditing) { if (_moving != null) Move(e); return; }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_stage, e.position, e.pressEventCamera, out var local)) return;
            float dx = local.x - _pressLocal.x;
            if (Mathf.Abs(dx) > TapSlop) _dragged = true;
            if (_dragged) _panX = Mathf.Clamp(_pressPan + dx, -_panRight, _panLeft);
        }
        public void PointerUp(PointerEventData e)
        {
            if (_activePointer != e.pointerId) return;
            _activePointer = null;
            if (!IsEditing) { if (!_dragged) BrowseTap(e); return; }
            if (_moving == null) return;
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
            ApplyStreetFraming();
            RefreshSelectionTools();
            _balance.text = Loc.Format("exterior.cost", _ui.Session.Wallet.DisplayCoins);
            if (_markerArrow != null) _markerArrow.anchoredPosition = new Vector2(0f, Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f)) * 12f);
            // Mouse previews follow the pointer immediately. Touch places on the next tap/drag.
            if (!_exitPrompt.activeSelf && IsEditing && _moving != null && _placingNew && UnityEngine.InputSystem.Mouse.current != null &&
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
            _enterButton.gameObject.SetActive(!IsEditing);
            _refreshButton.gameObject.SetActive(!IsEditing);
            _streetStatus.gameObject.SetActive(!IsEditing);
            UiKit.Place(_balance.rectTransform, 35, IsEditing ? 138 : 22, 300, 60);
            _hint.rectTransform.anchorMax = new Vector2(IsEditing ? .68f : .95f, 0);
            _hint.text = Loc.Get(IsEditing ? "exterior.hint" : "exterior.street_hint");
            if (IsEditing) DrawShop();
            DrawBuilding(); RefreshCost(); RefreshSelectionTools();
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
            _costLabel.text = Loc.Format("exterior.edit_cost", cost);
            _saveButton.interactable = valid;
        }

        private void DrawShop()
        {
            Clear(_shop);
            var categoryViewport = UiKit.NewRect("CategoryViewport", _shop);
            categoryViewport.anchorMin = new Vector2(0, 1); categoryViewport.anchorMax = Vector2.one;
            categoryViewport.offsetMin = new Vector2(12, -106); categoryViewport.offsetMax = new Vector2(-12, -10);
            categoryViewport.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, .01f);
            categoryViewport.gameObject.AddComponent<RectMask2D>();
            var categoryScroll = categoryViewport.gameObject.AddComponent<ScrollRect>();
            categoryScroll.horizontal = true; categoryScroll.vertical = false;
            categoryScroll.movementType = ScrollRect.MovementType.Clamped;
            var categoryRow = UiKit.NewRect("Categories", categoryViewport);
            categoryRow.anchorMin = categoryRow.anchorMax = categoryRow.pivot = new Vector2(0, 1);
            categoryScroll.viewport = categoryViewport; categoryScroll.content = categoryRow;
            int categoryIndex = 0;
            foreach (ExteriorKind kind in Enum.GetValues(typeof(ExteriorKind)))
            {
                var k = kind;
                var button = UiKit.TextButton(categoryRow, kind.ToString(), k == _category ? UiKit.Green : UiKit.WoodLight,
                    30, () => { _category = k; DrawShop(); }, out var label);
                label.text = Loc.Get("exterior." + k);
                FitLabel(label, 24, 30);
                UiKit.Place((RectTransform)button.transform, categoryIndex++ * 240, 0, 228, 96);
            }
            categoryRow.sizeDelta = new Vector2(categoryIndex * 240, 96);
            categoryScroll.horizontalNormalizedPosition = Mathf.Clamp01(((int)_category * 240f - 240f) /
                Mathf.Max(1, categoryIndex * 240 - _root.rect.width + 54));
            var viewport = UiKit.NewRect("ShopViewport", _shop);
            UiKit.Stretch(viewport, 12, 116, 12, 8);
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
                UiKit.Place(card, i++ * 252, 0, 240, 208);
                UiKit.AddButton(card, () => Choose(item));
                // Drag a card up out of the bar to carry the piece straight to the building; sideways still scrolls.
                var pull = card.gameObject.AddComponent<ExteriorCardDrag>();
                pull.Owner = this; pull.Item = item; pull.Scroll = scroll;
                var icon = UiKit.NewRect("Art", card); UiKit.Place(icon, 60, 6, 120, 92);
                UiKit.AddImage(icon, Sprite(d));
                var name = Label(card, "ItemName", 28, UiKit.DarkText, TextAnchor.MiddleCenter);
                UiKit.Place(name.rectTransform, 8, 100, 224, 54); name.text = Loc.Get(d.nameKey); FitLabel(name, 22, 28);
                var price = Label(card, "Price", 23, UiKit.SoftText, TextAnchor.MiddleCenter);
                UiKit.Place(price.rectTransform, 8, 156, 224, 50);
                int count = owned.Find(o => o.itemId == d.id)?.count ?? 0;
                price.text = Loc.Format("exterior.cost", d.price) + "\n" + Loc.Format("exterior.owned", count);
            }
            content.sizeDelta = new Vector2(i * 252, 208);
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
            ApplyStreetFraming();
            DrawFacade(_building, _draft, true, _box);
            DrawHandTools();
        }

        private void ApplyStreetFraming()
        {
            if (_stage == null || _draft == null) return;
            _neighbours.gameObject.SetActive(!IsEditing);
            _streetTouch.gameObject.SetActive(!IsEditing);
            _pan.anchoredPosition = new Vector2(IsEditing ? 0f : _panX, 0f);
            _stage.localScale = Vector3.one * (IsEditing ? 1f : Mathf.Min(1, _root.rect.width / 1450f));
            if (!IsEditing) return; // DrawBuilding restores the fixed street framing on leaving edit mode.
            var box = Catalog.BoxFor(Level);
            var safe = (RectTransform)_stage.parent;
            float baseline = _shop.anchoredPosition.y + _shop.sizeDelta.y + 60;
            float ceiling = safe.rect.height - 212;
            // RC WorldCustomiseBuilding uses a fixed 1.2 zoom. Fit the body and ordinary roof
            // headroom to the mobile work area, never the bounds of every roof in the catalog.
            float scale = EditZoom(box, safe.rect.width, Mathf.Max(120, ceiling - baseline));
            _building.localScale = Vector3.one * scale;
            _building.anchoredPosition = new Vector2(0,
                baseline - safe.rect.height * .5f - _stage.anchoredPosition.y + (box.Bottom - 340) * scale);
        }

        private static float EditZoom(ExteriorBox box, float width, float height)
        {
            const float referenceZoom = 1.2f;
            float roofHeadroom = 120f * box.Width / ExteriorCatalog.BaseWidth;
            return Mathf.Min(referenceZoom, Mathf.Max(120, width - 160) / box.Width,
                height / (box.Height + roofHeadroom));
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
            RefreshSelectionTools();
        }

        private void RefreshSelectionTools()
        {
            if (_selectionTools == null) return;
            var selected = _moving ?? Selected();
            bool show = IsEditing && selected != null;
            _selectionTools.gameObject.SetActive(show);
            _undoButton.interactable = _undo.Count > 0;
            if (!show) return;
            var kind = Catalog.Get(selected.itemId).kind;
            _flipButton.interactable = kind != ExteriorKind.Wall;
            _storeButton.interactable = !ExteriorCatalog.Fixed(kind) && kind != ExteriorKind.Door;
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
        // ---- Phase 5: the random street either side of your front (as the reference game's street)

        private const float NeighbourScale = .52f, FirstGap = 690f, NextGap = 560f, TapSlop = 14f;
        private RectTransform _pan, _neighbours, _streetTouch, _refreshButton;
        private Text _streetStatus;
        private readonly List<(RectTransform rect, StreetRestaurant shop)> _shops = new List<(RectTransform, StreetRestaurant)>();
        private float _panX, _panLeft, _panRight, _pressPan;
        private Vector2 _pressLocal;
        private bool _dragged, _loadingStreet;
        private int _streetGeneration;

        /// <summary>Slot i alternates right, left, right...; the first neighbour on each side stands clear of your front.</summary>
        private static float SlotX(int i)
        {
            int k = i / 2;
            return (i % 2 == 0 ? 1 : -1) * (FirstGap + k * NextGap);
        }

        private async void LoadStreet(bool fresh)
        {
            var online = _ui.Online;
            if (online == null || !online.Auth.IsSignedIn)
            {
                ShowExamples();
                _streetStatus.text = Loc.Get("ui.street.sign_in");
                return;
            }
            if (_loadingStreet) return;
            _loadingStreet = true;
            int generation = ++_streetGeneration;
            _streetStatus.text = Loc.Get("ui.street.loading");
            var result = await online.Api.RandomStreet();
            _loadingStreet = false;
            if (_neighbours == null || generation != _streetGeneration) return;
            if (!result.Ok) { _streetStatus.text = Loc.Get("ui.street.failed"); return; }
            var street = StreetRestaurant.ParseStreet(result.Data);
            ClearStreet();
            _streetStatus.text = street.Count == 0 ? Loc.Get("ui.street.empty") : Loc.Format("ui.street.count", street.Count);
            if (fresh) _panX = 0f;
            for (int i = 0; i < street.Count; i++)
            {
                var rect = DrawNeighbour(SlotX(i), Catalog.Starter(), street[i].Level, street[i].Name,
                    Loc.Format("ui.street.row_info", street[i].OwnerName, street[i].Level, street[i].Popularity.ToString("0.0")));
                _shops.Add((rect, street[i]));
            }
            // Each front loads on its own; until then the starter front stands in.
            for (int i = 0; i < street.Count; i++)
            {
                var shop = street[i];
                var front = await online.Api.GetRestaurantExterior(shop.RestaurantId);
                if (_neighbours == null || generation != _streetGeneration) return;
                var save = front.Ok ? StreetFacade.Parse(front.Data, Catalog, online.Sync.Ids.ToLocal) : null;
                if (save == null) continue;
                var old = _shops[i].rect;
                var rect = DrawNeighbour(SlotX(i), save, shop.Level, shop.Name,
                    Loc.Format("ui.street.row_info", shop.OwnerName, shop.Level, shop.Popularity.ToString("0.0")));
                rect.SetSiblingIndex(old.GetSiblingIndex());
                Destroy(old.gameObject);
                _shops[i] = (rect, shop);
            }
        }

        /// <summary>Offline or signed out: two labelled example fronts, not real players.</summary>
        private void ShowExamples()
        {
            ClearStreet();
            for (int i = 0; i < 2; i++)
            {
                var state = Catalog.Starter();
                var roof = state.pieces.Find(p => Catalog.Get(p.itemId).kind == ExteriorKind.Roof);
                var wall = state.pieces.Find(p => Catalog.Get(p.itemId).kind == ExteriorKind.Wall);
                if (Catalog.Get(i == 0 ? "roof_gables" : "roof_red") != null) roof.itemId = i == 0 ? "roof_gables" : "roof_red";
                if (Catalog.Get(i == 0 ? "wall_red" : "wall_stone") != null) wall.itemId = i == 0 ? "wall_red" : "wall_stone";
                DrawNeighbour(SlotX(i), state, 1, Loc.Get("exterior.preview"), null);
            }
        }

        private void ClearStreet()
        {
            _shops.Clear();
            Clear(_neighbours);
            _panLeft = _panRight = 0f;
        }

        private RectTransform DrawNeighbour(float x, ExteriorSave state, int level, string name, string info)
        {
            var rect = UiKit.NewRect("Neighbour", _neighbours);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(1000, 680); rect.localScale = Vector3.one * NeighbourScale;
            rect.anchoredPosition = new Vector2(x, -150);
            DrawFacade(rect, state, false, Catalog.BoxFor(level));
            var title = Label(rect, "ShopName", 40, Color.white, TextAnchor.MiddleCenter);
            UiKit.Place(title.rectTransform, 80, 640, 840, 64); title.text = name; FitLabel(title, 26, 40);
            title.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .55f);
            if (!string.IsNullOrEmpty(info))
            {
                var detail = Label(rect, "ShopInfo", 30, UiKit.Cream, TextAnchor.MiddleCenter);
                UiKit.Place(detail.rectTransform, 80, 704, 840, 50); detail.text = info; FitLabel(detail, 20, 30);
            }
            // How far the street can pan: the outermost neighbour reaches the middle of the screen.
            if (x > 0) _panRight = Mathf.Max(_panRight, x); else _panLeft = Mathf.Max(_panLeft, -x);
            return rect;
        }

        private void BrowseTap(PointerEventData e)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_building, e.position, e.pressEventCamera, out var tap))
            {
                var door = _draft.pieces.Find(p => Catalog.Get(p.itemId).kind == ExteriorKind.Door);
                var def = Catalog.Get(door.itemId);
                if (Mathf.Abs(tap.x + 500 - door.x) <= def.width / 2 && 340 - tap.y >= door.y - def.height &&
                    340 - tap.y <= door.y) { Close(); return; }
            }
            foreach (var (rect, shop) in _shops)
            {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out var local)) continue;
                // The front and its name plate, in the neighbour's 1000 x 680 design space.
                if (local.x < -420 || local.x > 420 || local.y < -420 || local.y > 300) continue;
                Close();
                _ui.Street?.VisitFromStreet(shop);
                return;
            }
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
            var b = UiKit.TextButton(parent, name, UiKit.WoodLight, 32, clicked, out var label);
            label.text = Loc.Get(key); FitLabel(label, 24, 32); _localized.Add((label,key));
            var rect = (RectTransform)b.transform; rect.sizeDelta = new Vector2(width, 64); return rect;
        }
        private static void AnchorTop(RectTransform rect, bool right, float inset, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(right ? 1 : 0, 1);
            rect.anchoredPosition = new Vector2(right ? -inset : inset, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void FitLabel(Text text, int min, int max)
        {
            text.resizeTextForBestFit = true; text.resizeTextMinSize = min; text.resizeTextMaxSize = max;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
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
