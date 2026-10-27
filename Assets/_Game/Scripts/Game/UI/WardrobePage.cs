using System;
using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Profile;
using DemoRestaurant.Logic.Session;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace DemoRestaurant.Game.UI
{
    /// <summary>Dedicated avatar editor: native model preview, rotation, category chooser and owned/shop items.</summary>
    public sealed class WardrobePage : IDisposable
    {
        private sealed class Option
        {
            public AvatarPartDef Part;
            public Image Rim;
            public Button Button;
            public Text Info;
        }
        private readonly GameUi _ui;
        private readonly RestaurantSession _session;
        private readonly AvatarCatalog _catalog;
        private readonly AvatarPainter _painter;
        private readonly RectTransform _root, _items, _categoryRow;
        private readonly Image _preview;
        private readonly Text _balance, _selectedName, _note, _buyLabel, _sellLabel, _filterLabel;
        private readonly Button _save, _buy, _sell, _filter, _random;
        private readonly List<(Text label, string key)> _labels = new List<(Text, string)>();
        private readonly Dictionary<AvatarSlot, Button> _categories = new Dictionary<AvatarSlot, Button>();
        private readonly List<Option> _options = new List<Option>();
        private readonly System.Random _rng = new System.Random();
        private AvatarLook _draft;
        private AvatarPartDef _selectedPart;
        private AvatarSlot _category = AvatarSlot.Eyes;
        private string _previewKey;
        private string _status;
        private bool _ownedOnly;
        private float _yaw;
        private readonly Button _rotateLeft, _rotateRight;
        public bool IsShowing => _root.gameObject.activeSelf;

        public WardrobePage(GameUi ui, AvatarPainter painter)
        {
            _ui = ui; _session = ui.Session; _catalog = ui.Content.Pack.Avatar; _painter = painter;
            _root = UiKit.CreateCanvas(ui.transform, "WardrobeCanvas", 60);
            _root.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280, 720);
            var background = _root.gameObject.AddComponent<Image>(); background.color = UiKit.Cream;
            var safe = SafeArea.Create(_root);
            var exit = Button(safe, "ui.wardrobe.back", UiKit.WoodLight, Close);
            Top((RectTransform)exit.transform, false, 16, 12, 168, 80);
            var title = UiKit.Label(safe, "Title", 0, 0, 0, 0, 34, TextAnchor.MiddleCenter, UiKit.DarkText);
            title.rectTransform.anchorMin = new Vector2(.3f, 1); title.rectTransform.anchorMax = new Vector2(.7f, 1);
            title.rectTransform.offsetMin = new Vector2(0, -84); title.rectTransform.offsetMax = new Vector2(0, -12);
            _labels.Add((title, "ui.wardrobe.title"));
            _save = Button(safe, "ui.profile.save", UiKit.Green, SaveLook);
            Top((RectTransform)_save.transform, true, 16, 12, 168, 80);
            _balance = UiKit.Label(safe, "Balance", 0, 0, 0, 0, 26, TextAnchor.MiddleRight, UiKit.DarkText);
            Top(_balance.rectTransform, true, 16, 100, 260, 42);

            var stage = UiKit.Box(safe, "AvatarStage", UiKit.CardRimSoft, UiKit.GoldFill, 3);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(.5f, 1);
            stage.anchoredPosition = new Vector2(0, -100); stage.sizeDelta = new Vector2(330, 326);
            var figure = UiKit.NewRect("Avatar", stage); UiKit.Stretch(figure, 24, 12, 24, 12);
            _preview = UiKit.AddImage(figure, null);
            var left = Button(safe, "ui.wardrobe.rotate_left", UiKit.WoodLight, () => Rotate(-1));
            var right = Button(safe, "ui.wardrobe.rotate_right", UiKit.WoodLight, () => Rotate(1));
            _rotateLeft=left; _rotateRight=right;
            foreach (var pair in new[] { (left, -1f), (right, 1f) })
            {
                var rect=(RectTransform)pair.Item1.transform;
                rect.SetParent(stage,false);
                rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(pair.Item2<0?0:1,.5f);
                rect.anchoredPosition=new Vector2(pair.Item2*12,0); rect.sizeDelta=new Vector2(80,96);
            }
            var turntable=stage.gameObject.AddComponent<WardrobeDragRotation>();
            turntable.Owner=this;
            _filter = Button(safe, "ui.wardrobe.shop", UiKit.WoodLight, () => { _ownedOnly = !_ownedOnly; BuildCatalog(); Refresh(); });
            _filterLabel = _filter.GetComponentInChildren<Text>();
            Top((RectTransform)_filter.transform, false, 16, 120, 240, 80);
            _random = Button(safe, "ui.profile.randomize", UiKit.Gold, Randomize);
            Top((RectTransform)_random.transform, false, 16, 216, 240, 80);
            var revert = Button(safe, "ui.wardrobe.reset", UiKit.WoodLight, Revert);
            Top((RectTransform)revert.transform, false, 16, 312, 240, 80);
            _selectedName = UiKit.Label(safe, "SelectedItem", 0, 0, 0, 0, 28, TextAnchor.MiddleRight, UiKit.DarkText, true);
            Top(_selectedName.rectTransform, true, 16, 150, 240, 68);
            _buy = Button(safe, "ui.wardrobe.buy", UiKit.Gold, Buy); _buyLabel = _buy.GetComponentInChildren<Text>();
            Top((RectTransform)_buy.transform, true, 16, 230, 240, 80);
            _sell = Button(safe, "ui.wardrobe.sell", UiKit.Red, Sell); _sellLabel = _sell.GetComponentInChildren<Text>();
            Top((RectTransform)_sell.transform, true, 16, 230, 240, 80);
            _note = UiKit.Label(safe, "WardrobeNote", 0, 0, 0, 0, 23, TextAnchor.UpperRight, UiKit.SoftText, true);
            Top(_note.rectTransform, true, 16, 320, 240, 100);

            var chooser = UiKit.Box(safe, "ItemChooser", UiKit.CardRimSoft, UiKit.CardCream, 3);
            chooser.anchorMin = Vector2.zero; chooser.anchorMax = new Vector2(1, 0);
            chooser.pivot = Vector2.zero; chooser.offsetMin = new Vector2(8, 8); chooser.offsetMax = new Vector2(-8, 272);
            var categoryViewport = Viewport(chooser, "CategoryViewport", 8, 8, 8, 184);
            // The category strip is a separate horizontal scroll from the items below it.
            categoryViewport.anchorMin = new Vector2(0, 1); categoryViewport.anchorMax = Vector2.one;
            categoryViewport.offsetMin = new Vector2(8, -80); categoryViewport.offsetMax = new Vector2(-8, -8);
            _categoryRow = UiKit.NewRect("Categories", categoryViewport);
            _categoryRow.anchorMin = _categoryRow.anchorMax = _categoryRow.pivot = new Vector2(0, 1);
            int x = 0;
            foreach (var slot in new[] { AvatarSlot.Eyes, AvatarSlot.Eyebrows, AvatarSlot.Gender, AvatarSlot.HairStyle,
                AvatarSlot.Hat, AvatarSlot.Shirt, AvatarSlot.Pants, AvatarSlot.SkinTone, AvatarSlot.HairColor,
                AvatarSlot.ShirtColor, AvatarSlot.PantsColor })
            {
                if (_catalog.PartsFor(slot).Count == 0) continue;
                var category = slot;
                var button = Button(_categoryRow, CategoryKey(slot), UiKit.WoodLight, () => SelectCategory(category));
                UiKit.Place((RectTransform)button.transform, x, 0, 158, 72); x += 170;
                _categories[slot] = button;
            }
            _categoryRow.sizeDelta = new Vector2(x, 72);
            Scroll(categoryViewport, _categoryRow);
            var viewport = Viewport(chooser, "ItemsViewport", 8, 88, 8, 8);
            _items = UiKit.NewRect("Items", viewport);
            _items.anchorMin = new Vector2(0, 0); _items.anchorMax = new Vector2(0, 1); _items.pivot = new Vector2(0, .5f);
            _items.sizeDelta = Vector2.zero;
            var row = _items.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12; row.childControlWidth = false; row.childControlHeight = true;
            row.childForceExpandWidth = false; row.childForceExpandHeight = true;
            row.padding = new RectOffset(4, 4, 4, 4);
            _items.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            Scroll(viewport, _items);
            _root.gameObject.SetActive(false);
            _session.Profile.Changed += OnSessionChanged; _session.Wallet.Changed += OnSessionChanged;
            Loc.LanguageChanged += OnLanguageChanged;
        }

        private static RectTransform Viewport(Transform parent, string name, float left, float top, float right, float bottom)
        {
            var rect = UiKit.NewRect(name, parent); UiKit.Stretch(rect, left, top, right, bottom);
            rect.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, .01f);
            rect.gameObject.AddComponent<RectMask2D>(); return rect;
        }
        private static void Scroll(RectTransform viewport, RectTransform content)
        {
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
        }
        private Button Button(Transform parent, string key, Color color, Action action)
        {
            var button = UiKit.TextButton(parent, key, color, 28, action, out var label);
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 20; label.resizeTextMaxSize = 28;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; _labels.Add((label, key)); return button;
        }
        private static void Top(RectTransform rect, bool right, float inset, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(right ? 1 : 0, 1);
            rect.anchoredPosition = new Vector2(right ? -inset : inset, -top); rect.sizeDelta = new Vector2(width, height);
        }
        private string CategoryKey(AvatarSlot slot) => _painter.NativeAvailable && slot == AvatarSlot.Gender
            ? "ui.avatar.slot.mouth" : "ui.avatar.slot." + slot.ToString().ToLowerInvariant();
        private string PartName(AvatarPartDef part) => Loc.Get(_painter.NativeAvailable && string.IsNullOrEmpty(part.Color)
            ? "avatar.native." + part.Slot.ToString().ToLowerInvariant() + "." + part.Id : part.NameKey);

        public void Show()
        {
            _draft = _session.Profile.Avatar.Clone(); _selectedPart = null; _yaw = 0; _status = null;
            _ui.SetGameplayVisible(false); _root.gameObject.SetActive(true);
            foreach (var pair in _labels) pair.label.text = Loc.Get(pair.key);
            SelectCategory(_category);
        }
        public void Close()
        {
            _root.gameObject.SetActive(false); ReleaseThumbnails();
            _painter.Release(_previewKey); _previewKey = null;
            _ui.SetGameplayVisible(true);
        }
        private void Rotate(int delta) { DragRotate(delta*45f); }
        public void DragRotate(float degrees)
        {
            if(!IsShowing)return;
            int before=Mathf.RoundToInt(_yaw/15f);
            _yaw=Mathf.Repeat(_yaw+degrees,360f);
            if(before!=Mathf.RoundToInt(_yaw/15f))RefreshPreview();
        }
        private void RefreshPreview()
        {
            _preview.sprite=_ui.CanCustomizeAvatar?_painter.TurntablePreview(_draft,_yaw):_ui.BakedOwnerFigure();
            _preview.enabled=_preview.sprite!=null;
        }
        private void SelectCategory(AvatarSlot slot) { _category = slot; _selectedPart = null; _status = null; BuildCatalog(); Refresh(); }
        private void Pick(AvatarPartDef part)
        {
            if (!_ui.CanCustomizeAvatar) { _ui.Toast(Loc.Get("ui.wardrobe.native_pending")); return; }
            if (!_draft.TrySet(_catalog, part.Slot, part.Id, _session.Progress.Level)) return;
            _selectedPart = part; _status = null; Refresh();
        }
        private void Buy()
        {
            var result = _session.BuyAvatarPart(_selectedPart);
            _status = Loc.Get("ui.wardrobe.result." + result.ToString().ToLowerInvariant()); BuildCatalog(); Refresh();
        }
        private void Sell()
        {
            var result = _session.SellAvatarPart(_selectedPart);
            if (result == WardrobeResult.Success)
                _draft.TrySet(_catalog, _selectedPart.Slot, _session.Profile.Avatar[_selectedPart.Slot], int.MaxValue);
            _status = Loc.Get("ui.wardrobe.result." + result.ToString().ToLowerInvariant()); BuildCatalog(); Refresh();
        }
        private void Randomize() { _draft = _session.RandomOwnedAvatar(_rng); _selectedPart = null; _status = null; BuildCatalog(); Refresh(); }
        private void Revert() { _draft = _session.Profile.Avatar.Clone(); _selectedPart = null; _status = null; BuildCatalog(); Refresh(); }
        private bool HasUnownedDraft()
        {
            foreach (var pair in _draft.Parts) if (!_session.OwnsAvatarPart(_catalog.Find(pair.Key, pair.Value))) return true;
            return false;
        }
        private void SaveLook()
        {
            if (!_ui.CanCustomizeAvatar) return;
            if (HasUnownedDraft()) { _ui.Toast(Loc.Get("ui.wardrobe.buy_first")); return; }
            _session.SaveAvatar(_draft); _draft = _session.Profile.Avatar.Clone();
            _ui.Toast(Loc.Get("ui.profile.saved")); Refresh(); Close();
        }
        private void BuildCatalog()
        {
            _options.Clear();
            foreach (Transform child in _items) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            ReleaseThumbnails();
            _items.anchoredPosition = Vector2.zero;
            foreach (var part in _catalog.PartsFor(_category))
            {
                if (_ownedOnly && !_session.OwnsAvatarPart(part)) continue;
                var card = UiKit.Box(_items, part.Id, UiKit.CardRimSoft, UiKit.CardCream, 3, out var rim, out _);
                card.sizeDelta = new Vector2(180, 0);
                var option = new Option { Part = part, Rim = rim };
                option.Button = UiKit.AddButton(card, () => Pick(part));
                var art = UiKit.NewRect("ItemPreview", card);
                art.anchorMin = new Vector2(0, .42f); art.anchorMax = Vector2.one;
                art.offsetMin = new Vector2(20, 0); art.offsetMax = new Vector2(-20, -8);
                if (ColorUtility.TryParseHtmlString(part.Color, out var color))
                {
                    var swatch = art.gameObject.AddComponent<Image>(); swatch.sprite = UiArt.Circle(); swatch.color = color;
                    swatch.preserveAspect = true; swatch.raycastTarget = false;
                }
                else
                {
                    var icon=_painter.ItemIcon(part,_draft);
                    UiKit.AddImage(art,icon);
                    if(icon==null)
                    {
                        var empty=UiKit.AddText(UiKit.NewRect("EmptyPart",art),36,TextAnchor.MiddleCenter,UiKit.SoftText);
                        UiKit.Stretch(empty.rectTransform);empty.text="—";
                    }
                }
                var name = UiKit.AddText(UiKit.NewRect("Name", card), 23, TextAnchor.MiddleCenter, UiKit.DarkText);
                name.rectTransform.anchorMin = new Vector2(0, .16f); name.rectTransform.anchorMax = new Vector2(1, .42f);
                name.rectTransform.offsetMin = new Vector2(6, 0); name.rectTransform.offsetMax = new Vector2(-6, 0);
                name.resizeTextForBestFit = true; name.resizeTextMinSize = 18; name.resizeTextMaxSize = 23;
                name.horizontalOverflow = HorizontalWrapMode.Wrap; name.text = PartName(part);
                option.Info = UiKit.AddText(UiKit.NewRect("Info", card), 21, TextAnchor.MiddleCenter, UiKit.SoftText);
                option.Info.rectTransform.anchorMin = Vector2.zero; option.Info.rectTransform.anchorMax = new Vector2(1, .17f);
                option.Info.rectTransform.offsetMin = new Vector2(4, 2); option.Info.rectTransform.offsetMax = new Vector2(-4, 0);
                _options.Add(option);
            }
        }
        private static void Destroy(UnityEngine.Object obj)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj); }
        private void ReleaseThumbnails() { _painter.ClearItemIcons(); }
        private void OnSessionChanged() { if (IsShowing) Refresh(); }
        private void OnLanguageChanged()
        {
            foreach (var pair in _labels) pair.label.text = Loc.Get(pair.key);
            if (IsShowing) { BuildCatalog(); Refresh(); }
        }
        private void Refresh()
        {
            if (_draft == null) return;
            if (_previewKey != _draft.Key)
            { _painter.Release(_previewKey); _previewKey = _painter.Retain(_draft); }
            _rotateLeft.gameObject.SetActive(!Application.isMobilePlatform);
            _rotateRight.gameObject.SetActive(!Application.isMobilePlatform);
            RefreshPreview();
            _balance.text = Loc.Format("exterior.cost", _session.Wallet.DisplayCoins);
            _selectedName.text = _selectedPart == null ? Loc.Get(CategoryKey(_category)) : PartName(_selectedPart);
            _save.interactable = _ui.CanCustomizeAvatar && _draft.Key != _session.Profile.Avatar.Key && !HasUnownedDraft();
            _random.interactable = _ui.CanCustomizeAvatar;
            _filterLabel.text = Loc.Get(_ownedOnly ? "ui.wardrobe.owned" : "ui.wardrobe.shop");
            bool paid = _selectedPart != null && _selectedPart.Price > 0;
            bool owned = _session.OwnsAvatarPart(_selectedPart);
            _buy.gameObject.SetActive(paid && !owned); _sell.gameObject.SetActive(paid && owned);
            _buy.interactable = paid && _session.WardrobeTradingOpen && _selectedPart.Price <= _session.Wallet.DisplayCoins;
            _sell.interactable = paid && _session.WardrobeTradingOpen && _session.Profile.Avatar[_selectedPart.Slot] != _selectedPart.Id;
            _buyLabel.text = Loc.Format("ui.wardrobe.buy", _selectedPart?.Price ?? 0);
            _sellLabel.text = Loc.Format("ui.wardrobe.sell", (_selectedPart?.Price ?? 0) / 2);
            _note.text = _status ?? (!_ui.CanCustomizeAvatar ? Loc.Get("ui.wardrobe.native_pending") : !_session.WardrobeTradingOpen
                ? Loc.Get("ui.wardrobe.online_note") : HasUnownedDraft() ? Loc.Get("ui.wardrobe.buy_first") : "");
            foreach (var pair in _categories) UiKit.SetButtonColor(pair.Value, pair.Key == _category ? UiKit.Gold : UiKit.WoodLight);
            foreach (var option in _options)
            {
                bool locked = option.Part.UnlockLevel > _session.Progress.Level;
                option.Button.interactable = _ui.CanCustomizeAvatar && !locked;
                option.Rim.color = _draft[option.Part.Slot] == option.Part.Id ? UiKit.Gold : UiKit.CardRimSoft;
                option.Info.text = locked ? Loc.Format("ui.profile.lv", option.Part.UnlockLevel) : _session.OwnsAvatarPart(option.Part)
                    ? Loc.Get("ui.wardrobe.item_owned") : Loc.Format("ui.wardrobe.price", option.Part.Price);
            }
        }
        public void Dispose()
        {
            _session.Profile.Changed -= OnSessionChanged; _session.Wallet.Changed -= OnSessionChanged;
            Loc.LanguageChanged -= OnLanguageChanged; ReleaseThumbnails(); _painter.Release(_previewKey); _previewKey = null;
        }
    }
    /// <summary>Owns drags beginning on the avatar stage; catalog scrolling has a separate viewport.</summary>
    public sealed class WardrobeDragRotation : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public WardrobePage Owner;
        private int? _pointer;
        private Vector2 _previous;
        public void OnBeginDrag(PointerEventData e)
        {
            if(_pointer.HasValue)return;
            _pointer=e.pointerId; _previous=e.pressPosition;
        }
        public void OnDrag(PointerEventData e)
        {
            if(_pointer!=e.pointerId || Owner==null || !Owner.IsShowing)return;
            var rect=(RectTransform)transform;
            if(RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,_previous,e.pressEventCamera,out var from) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,e.position,e.pressEventCamera,out var to))
                Owner.DragRotate((to.x-from.x)/Mathf.Max(1,rect.rect.width)*180f);
            _previous=e.position;
        }
        public void OnEndDrag(PointerEventData e) { if(_pointer==e.pointerId)_pointer=null; }
        private void OnDisable() { _pointer=null; }
    }

}
