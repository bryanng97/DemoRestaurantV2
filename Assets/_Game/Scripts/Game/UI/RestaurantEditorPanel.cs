using System;
using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Controls;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Layout;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// The decorate screen in the reference game's layout: the shop bar docked along the bottom (Shop / Storage tabs,
    /// category filters and a sliding row of cards), Save and Cancel at its right end, a small toolbar above it, and
    /// rotate / store buttons that float beside a piece held on a touch screen. Everything sits in the safe area and
    /// stretches with the screen width, so it works on phones of any aspect.
    /// </summary>
    public sealed class RestaurantEditorPanel : MonoBehaviour
    {
        private const float BarHeight = 250f, ButtonColumn = 230f;

        private RestaurantEditorController _controller;
        private LoadedContentPack _content;
        private ArtLibrary _art;
        private RectTransform _root, _bar, _cards, _hand;
        private Text _status;
        private Button _rotate, _store, _undo, _redo, _fill, _storeAll, _handRotate, _handStore;
        private Text _fillLabel;
        // "Laying: <floor or wallpaper>" with its picture and a close button, so the chosen tool is always clear.
        private RectTransform _chip;
        private Image _chipIcon;
        private Text _chipText;
        private Button _shopTab, _storageTab;
        private bool _storage;
        private readonly List<(Text label, string key)> _labels = new List<(Text, string)>();
        private readonly List<(BuildCategory category, Button button)> _filters = new List<(BuildCategory, Button)>();
        private readonly List<(string id, Image rim)> _cardRims = new List<(string, Image)>();
        /// <summary>Every card's rect and what choosing it does, so a finger can drag a piece straight out of the bar.</summary>
        private readonly List<(RectTransform card, Action select)> _cardPicks = new List<(RectTransform, Action)>();
        /// <summary>Furniture cards: the line under the name shows lock level, copies in storage, or the price.</summary>
        private readonly List<(FurnitureDef def, Text info, Image icon)> _shopCards = new List<(FurnitureDef, Text, Image)>();

        public void Build(RestaurantEditorController controller, LoadedContentPack content)
        {
            _controller = controller; _content = content; _art = ArtLibrary.For(content.Theme);
            var canvas = UiKit.CreateCanvas(transform, "BuildCanvas", 30);
            _root = canvas;
            var safe = SafeArea.Create(canvas);

            // The shop bar along the bottom.
            _bar = UiKit.Box(safe, "ShopBar", UiKit.Wood, UiKit.Cream, 6f);
            _bar.anchorMin = new Vector2(0, 0); _bar.anchorMax = new Vector2(1, 0); _bar.pivot = new Vector2(0.5f, 0);
            _bar.offsetMin = new Vector2(12, 10); _bar.offsetMax = new Vector2(-12, 10 + BarHeight);

            // Tool chip, top centre: what floor or wallpaper is being laid, with an X to put it down.
            _chip = UiKit.Box(safe, "ToolChip", UiKit.Gold, UiKit.GoldFill, 4f);
            _chip.anchorMin = _chip.anchorMax = new Vector2(0.5f, 1f); _chip.pivot = new Vector2(0.5f, 1f);
            _chip.sizeDelta = new Vector2(560, 84); _chip.anchoredPosition = new Vector2(0, -96);
            var chipIcon = UiKit.NewRect("Icon", _chip);
            chipIcon.anchorMin = chipIcon.anchorMax = new Vector2(0, 0.5f); chipIcon.pivot = new Vector2(0, 0.5f);
            chipIcon.sizeDelta = new Vector2(68, 68); chipIcon.anchoredPosition = new Vector2(10, 0);
            _chipIcon = UiKit.AddImage(chipIcon, null);
            _chipText = UiKit.AddText(UiKit.NewRect("Text", _chip), 24, TextAnchor.MiddleLeft, UiKit.DarkText);
            _chipText.rectTransform.anchorMin = new Vector2(0, 0); _chipText.rectTransform.anchorMax = new Vector2(1, 1);
            _chipText.rectTransform.offsetMin = new Vector2(90, 6); _chipText.rectTransform.offsetMax = new Vector2(-84, -6);
            _chipText.resizeTextForBestFit = true; _chipText.resizeTextMinSize = 14; _chipText.resizeTextMaxSize = 24;
            var close = UiKit.TextButton(_chip, "Close", UiKit.Red, 28, () => { _controller.ClearSelection(); RefreshState(); }, out var closeLabel);
            closeLabel.text = "X";
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1, 0.5f); closeRect.pivot = new Vector2(1, 0.5f);
            closeRect.sizeDelta = new Vector2(64, 64); closeRect.anchoredPosition = new Vector2(-10, 0);
            _chip.gameObject.SetActive(false);

            _shopTab = TabButton(_bar, "ui.build.tab_shop", 14, () => ShowStorage(false));
            _storageTab = TabButton(_bar, "ui.build.tab_storage", 154, () => ShowStorage(true));
            var categories = new[]
            {
                (BuildCategory.Kitchen, "ui.build.kitchen"), (BuildCategory.Table, "ui.build.table"),
                (BuildCategory.Chair, "ui.build.chair"), (BuildCategory.Decoration, "ui.build.decoration"),
                (BuildCategory.Functional, "ui.build.functional"), (BuildCategory.Outdoor, "ui.build.outdoor"),
                (BuildCategory.Wall, "ui.build.wall"), (BuildCategory.Wallpaper, "ui.build.wallpaper"), (BuildCategory.Floor, "ui.build.floor"), (BuildCategory.Land, "ui.build.land"),
            };
            int x = 316;
            foreach (var (category, key) in categories)
            {
                // Land is only sold where the room doesn't simply grow with the level.
                if (category == BuildCategory.Land && content.Pack.Rules.Expansion == null) continue;
                _filters.Add((category, TabButton(_bar, key, x, () => { _storage = false; _controller.SetCategory(category); }, width: 104, small: true)));
                x += 110;
            }

            // The sliding row of cards.
            var viewport = UiKit.NewRect("CardsViewport", _bar);
            viewport.anchorMin = new Vector2(0, 0); viewport.anchorMax = new Vector2(1, 1);
            viewport.offsetMin = new Vector2(14, 12); viewport.offsetMax = new Vector2(-ButtonColumn - 14, -72);
            viewport.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _cards = UiKit.NewRect("Cards", viewport);
            _cards.anchorMin = new Vector2(0, 0); _cards.anchorMax = new Vector2(0, 1); _cards.pivot = new Vector2(0, 0.5f);
            _cards.sizeDelta = Vector2.zero;
            scroll.viewport = viewport; scroll.content = _cards; scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var row = _cards.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10; row.childForceExpandWidth = false; row.childForceExpandHeight = true;
            row.childControlWidth = false; row.childControlHeight = true; row.padding = new RectOffset(4, 4, 4, 4);
            _cards.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Save / Cancel at the right end of the bar.
            var save = AddButton(_bar, "ui.build.save", 0, 0, ButtonColumn - 24, 92, 28, UiKit.Green, _controller.Save);
            AnchorRight((RectTransform)save.transform, 12, 18, ButtonColumn - 24, 92);
            var cancel = AddButton(_bar, "ui.build.cancel", 0, 0, ButtonColumn - 24, 70, 24, UiKit.Wood, _controller.Cancel);
            AnchorRight((RectTransform)cancel.transform, 12, 120, ButtonColumn - 24, 70);

            // Toolbar above the bar, right side: hand actions (for mouse users), undo / redo, fill floor, store all.
            var tools = UiKit.NewRect("Toolbar", safe);
            tools.anchorMin = tools.anchorMax = tools.pivot = new Vector2(1, 0);
            tools.anchoredPosition = new Vector2(-12, BarHeight + 22); tools.sizeDelta = new Vector2(6 * 128, 64);
            _rotate = AddButton(tools, "ui.build.rotate", 0, 0, 120, 60, 20, UiKit.WoodLight, _controller.Rotate);
            _store = AddButton(tools, "ui.build.store", 128, 0, 120, 60, 20, UiKit.Red, _controller.Remove);
            _undo = AddButton(tools, "ui.build.undo", 256, 0, 120, 60, 20, UiKit.WoodLight, _controller.Undo);
            _redo = AddButton(tools, "ui.build.redo", 384, 0, 120, 60, 20, UiKit.WoodLight, _controller.Redo);
            _fill = AddButton(tools, "ui.build.fill", 512, 0, 120, 60, 20, UiKit.WoodLight, _controller.FillFloor);
            _fillLabel = _fill.GetComponentInChildren<Text>();
            _storeAll = AddButton(tools, "ui.build.store_all", 640, 0, 120, 60, 18, UiKit.Wood, _controller.StoreAll);

            // Status / hint line above the bar, left side.
            var statusBox = UiKit.Box(safe, "Status", new Color(0, 0, 0, 0), new Color(0.15f, 0.1f, 0.07f, 0.7f), 0f);
            statusBox.anchorMin = statusBox.anchorMax = statusBox.pivot = new Vector2(0, 0);
            statusBox.anchoredPosition = new Vector2(12, BarHeight + 22); statusBox.sizeDelta = new Vector2(900, 64);
            _status = UiKit.Label(statusBox, "Text", 14, 4, 872, 56, 20, TextAnchor.MiddleLeft, Color.white, true);

            // Floating rotate / store buttons beside a piece held on a touch screen.
            _hand = UiKit.NewRect("HandButtons", safe);
            _hand.anchorMin = _hand.anchorMax = new Vector2(0, 0); _hand.pivot = new Vector2(0.5f, 0);
            _hand.sizeDelta = new Vector2(260, 84);
            _handRotate = AddButton(_hand, "ui.build.rotate_touch", 0, 0, 124, 84, 24, UiKit.WoodLight, _controller.Rotate);
            _handStore = AddButton(_hand, "ui.build.store", 136, 0, 124, 84, 24, UiKit.Red, _controller.Remove);
            _hand.gameObject.SetActive(false);

            Loc.LanguageChanged += RefreshLanguage;
            RefreshLanguage(); SetVisible(false);
        }

        private static void AnchorRight(RectTransform rect, float right, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-right, -top); rect.sizeDelta = new Vector2(width, height);
        }

        private Button TabButton(Transform parent, string key, float x, Action action, float width = 132, bool small = false)
        {
            var button = AddButton(parent, key, x, 10, width, small ? 50 : 56, small ? 18 : 22, UiKit.WoodLight, action);
            return button;
        }

        private Button AddButton(Transform parent, string key, float x, float y, float w, float h, int size, Color color, Action action)
        {
            var button = UiKit.TextButton(parent, key, color, size, action, out var text);
            button.gameObject.AddComponent<CanvasGroup>();
            UiKit.Place((RectTransform)button.transform, x, y, w, h);
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 12; text.resizeTextMaxSize = size;
            _labels.Add((text, key)); return button;
        }

        public void SetVisible(bool visible) { _root.gameObject.SetActive(visible); if (visible) RefreshState(); }

        /// <summary>True when a screen point is over the shop bar (dropping a held piece there stores it).</summary>
        public bool IsOverShopBar(Vector2 screen) =>
            _bar != null && _root.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(_bar, screen, null);

        /// <summary>What choosing the card under <paramref name="screen"/> does, or null when it isn't over a card.</summary>
        public Action CardAt(Vector2 screen)
        {
            if (!IsOverShopBar(screen)) return null;
            foreach (var (card, select) in _cardPicks)
                if (card != null && card.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(card, screen, null))
                    return select;
            return null;
        }

        /// <summary>Screen height of the shop bar's top edge (0 when hidden).</summary>
        public float ShopBarTop()
        {
            if (_bar == null || !_root.gameObject.activeInHierarchy) return 0f;
            var corners = new Vector3[4];
            _bar.GetWorldCorners(corners);
            return corners[1].y; // screen-space overlay canvas: world corners are screen pixels
        }

        /// <summary>Shows the floating hand buttons just above <paramref name="screen"/> (the held piece's top).</summary>
        public void SetHandButtons(bool visible, Vector2 screen, bool freezePosition = false)
        {
            if (_hand == null) return;
            if (_hand.gameObject.activeSelf != visible) _hand.gameObject.SetActive(visible);
            if (!visible) return;
            SetEnabled(_handRotate, _controller.CanRotate);
            SetEnabled(_handStore, _controller.CanRemove);
            if (freezePosition) return;
            var parent = (RectTransform)_hand.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, null, out var local))
            {
                var position = local - parent.rect.min + new Vector2(0, 12);
                position.x = Mathf.Clamp(position.x, _hand.sizeDelta.x * .5f, Mathf.Max(_hand.sizeDelta.x * .5f, parent.rect.width - _hand.sizeDelta.x * .5f));
                position.y = Mathf.Clamp(position.y, BarHeight + 90, Mathf.Max(BarHeight + 90, parent.rect.height - _hand.sizeDelta.y));
                _hand.anchoredPosition = position;
            }
        }

        private void ShowStorage(bool storage)
        {
            _storage = storage;
            if (storage && (_controller.Category == BuildCategory.Floor || _controller.Category == BuildCategory.Wallpaper || _controller.Category == BuildCategory.Land))
                _controller.SetCategory(BuildCategory.Table);
            else RefreshCatalog();
        }

        public void RefreshCatalog()
        {
            foreach (Transform child in _cards) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            _cardRims.Clear(); _cardPicks.Clear(); _shopCards.Clear(); _cards.anchoredPosition = Vector2.zero;
            if (_storage)
            {
                // Storage: every owned piece not placed, whatever its category.
                var owned = _controller.Owned;
                var placed = _controller.PlacedInDraft();
                foreach (var f in _content.Pack.Furniture)
                    if (FurnitureOwnership.InStorage(owned, placed, f.Id) > 0)
                        AddShopCard(f);
            }
            else if (_controller.Category == BuildCategory.Floor)
            {
                foreach (var floor in _content.Theme.floors)
                    Card(floor.id, floor.nameKey, floor.sprite, Loc.Get("ui.build.floor_size"), () => _controller.SelectFloor(floor));
            }
            else if (_controller.Category == BuildCategory.Wallpaper)
            {
                foreach (var paper in _content.Theme.wallpapers)
                    Card(paper.id, paper.nameKey, paper.sprite, Loc.Get("ui.build.wallpaper_info"), () => _controller.SelectWallpaper(paper));
            }
            else if (_controller.Category == BuildCategory.Land)
            {
                for (int i = 0; i < 2; i++)
                {
                    bool alongX = i == 0;
                    bool available = _controller.ExpansionQuote(alongX, out long cost);
                    var button = Card("land" + i, alongX ? "ui.build.land_right" : "ui.build.land_left", "floor_pattern",
                        available ? Loc.Format("ui.build.land_price", cost) : Loc.Get("ui.build.land_max"), () => _controller.Expand(alongX));
                    button.interactable = available;
                }
            }
            else
            {
                // Cheapest and earliest-unlocked first, so a section reads like a shop shelf.
                var pieces = new List<FurnitureDef>();
                foreach (var f in _content.Pack.Furniture)
                    if (CategoryFor(f) == _controller.Category) pieces.Add(f);
                pieces.Sort((a, b) => a.UnlockLevel != b.UnlockLevel ? a.UnlockLevel.CompareTo(b.UnlockLevel) : a.Price.CompareTo(b.Price));
                foreach (var f in pieces) AddShopCard(f);
            }
            RefreshState();
        }

        private void AddShopCard(FurnitureDef f)
        {
            var button = Card(f.Id, f.NameKey, _content.ArtForFurniture(f.Id).Sprite, "", () => _controller.SelectFurniture(f));
            var card = button.transform;
            _shopCards.Add((f, card.Find("Info").GetComponent<Text>(), card.Find("Icon").GetComponent<Image>()));
        }

        private BuildCategory CategoryFor(FurnitureDef f)
        {
            // What a piece does decides its section first; decor pieces may name theirs (e.g. Outdoor, Kitchen).
            switch (f.Kind)
            {
                case FurnitureKind.WallDecor: return BuildCategory.Wall;
                case FurnitureKind.Table: return BuildCategory.Table;
                case FurnitureKind.Chair: return BuildCategory.Chair;
                case FurnitureKind.Toilet:
                case FurnitureKind.Entertainment:
                case FurnitureKind.Sink: return BuildCategory.Functional;
                case FurnitureKind.Stove:
                case FurnitureKind.DrinkMachine:
                case FurnitureKind.Counter:
                case FurnitureKind.ServingCounter: return BuildCategory.Kitchen;
            }
            if (Enum.TryParse(_content.ArtForFurniture(f.Id).Category, out BuildCategory category) && category != BuildCategory.Land)
                return category;
            return BuildCategory.Decoration;
        }

        private Button Card(string id, string key, string sprite, string info, Action select)
        {
            var card = UiKit.Box(_cards, id, UiKit.CardRim, UiKit.CardFill, 4, out var rim, out _);
            card.sizeDelta = new Vector2(150, 0);
            var button = UiKit.AddButton(card, () => { select(); RefreshState(); });
            var icon = UiKit.AddImage(UiKit.NewRect("Icon", card), _art.Get(sprite));
            icon.rectTransform.anchorMin = new Vector2(0, 0.42f); icon.rectTransform.anchorMax = new Vector2(1, 1);
            icon.rectTransform.offsetMin = new Vector2(12, 0); icon.rectTransform.offsetMax = new Vector2(-12, -8);
            var name = UiKit.AddText(UiKit.NewRect("Name", card), 18, TextAnchor.MiddleCenter, UiKit.DarkText);
            name.rectTransform.anchorMin = new Vector2(0, 0.17f); name.rectTransform.anchorMax = new Vector2(1, 0.42f);
            name.rectTransform.offsetMin = new Vector2(6, 0); name.rectTransform.offsetMax = new Vector2(-6, 0);
            // Long names (e.g. "White Cloth Table") wrap onto two lines and shrink to fit, never past the card.
            name.horizontalOverflow = HorizontalWrapMode.Wrap; name.verticalOverflow = VerticalWrapMode.Truncate;
            name.lineSpacing = 0.85f;
            name.resizeTextForBestFit = true; name.resizeTextMinSize = 10; name.resizeTextMaxSize = 18;
            name.text = Loc.Get(key);
            var infoText = UiKit.AddText(UiKit.NewRect("Info", card), 15, TextAnchor.MiddleCenter, UiKit.SoftText);
            infoText.rectTransform.anchorMin = new Vector2(0, 0); infoText.rectTransform.anchorMax = new Vector2(1, 0.18f);
            infoText.rectTransform.offsetMin = new Vector2(4, 4); infoText.rectTransform.offsetMax = new Vector2(-4, 0);
            infoText.resizeTextForBestFit = true; infoText.resizeTextMinSize = 10; infoText.resizeTextMaxSize = 15;
            infoText.text = info;
            _cardRims.Add((id, rim));
            _cardPicks.Add((card, select));
            return button;
        }

        /// <summary>Mouse players get hints that name the mouse buttons and keys.</summary>
        private static string MouseSuffix => Application.isMobilePlatform ? "" : "_mouse";

        public void RefreshState()
        {
            if (_controller.Editor == null) return;
            SetEnabled(_rotate, _controller.CanRotate);
            SetEnabled(_store, _controller.CanRemove);
            SetEnabled(_undo, _controller.Editor.CanUndo);
            SetEnabled(_redo, _controller.Editor.CanRedo);
            SetEnabled(_fill, _controller.HasFloor);
            _fillLabel.text = Loc.Get(_controller.HasWallpaper ? "ui.build.fill_walls" : "ui.build.fill");
            ShowToolChip(_controller.PaintTool);
            SetEnabled(_storeAll, _controller.Editor.Draft.Furniture.Count + _controller.Editor.Draft.WallItems.Count > 0);

            long furniture = _controller.FurnitureCost;
            if (_controller.Category == BuildCategory.Land || _controller.ExpansionCost > 0)
                _status.text = Loc.Format("ui.build.land_status", _controller.Editor.Draft.Grid.Width, _controller.Editor.Draft.Grid.Height,
                    _controller.Balance, _controller.TotalCost);
            else if (furniture > 0)
                _status.text = Loc.Format("ui.build.shop_status", _controller.Balance, furniture);
            else
                _status.text = Loc.Get(_controller.HasSelection ? "ui.build.holding_hint" + MouseSuffix :
                    _controller.HasWallpaper ? "ui.build.wallpaper_hint" : _controller.HasFloor ? "ui.build.floor_hint" : "ui.build.pick_hint" + MouseSuffix);
            if (_controller.TotalCost > _controller.Balance) _status.text += "  " + Loc.Get("ui.build.over_budget");

            var owned = _controller.Owned;
            var placed = _shopCards.Count > 0 ? _controller.PlacedInDraft() : null;
            foreach (var (def, info, icon) in _shopCards)
            {
                bool locked = def.UnlockLevel > _controller.PlayerLevel;
                int storage = FurnitureOwnership.InStorage(owned, placed, def.Id);
                info.text = locked ? Loc.Format("ui.build.locked_short", def.UnlockLevel)
                    : storage > 0 ? Loc.Format("ui.build.in_storage", storage)
                    : def.Price == 0 ? Loc.Get("ui.build.free") : Loc.Format("ui.build.buy_price", def.Price);
                info.color = locked ? UiKit.Disabled : storage > 0 ? UiKit.Green : UiKit.DarkText;
                icon.color = locked ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            }
            UiKit.SetButtonColor(_shopTab, _storage ? UiKit.WoodLight : UiKit.Green);
            UiKit.SetButtonColor(_storageTab, _storage ? UiKit.Green : UiKit.WoodLight);
            foreach (var (category, button) in _filters)
            {
                button.gameObject.SetActive(!_storage);
                UiKit.SetButtonColor(button, category == _controller.Category ? UiKit.Gold : UiKit.WoodLight);
            }
            foreach (var (id, rim) in _cardRims) rim.color = id == _controller.SelectedId ? UiKit.Gold : UiKit.CardRim;
        }

        private void ShowToolChip(FloorArt tool)
        {
            if (_chip == null) return;
            _chip.gameObject.SetActive(tool != null);
            if (tool == null) return;
            _chipIcon.sprite = _art.Get(tool.sprite);
            _chipText.text = Loc.Format("ui.build.laying", Loc.Get(tool.nameKey));
        }

        public void ShowMessage(string key, bool raw = false) { _status.text = raw ? key : Loc.Get(key); }

        private static void SetEnabled(Button button, bool enabled)
        { button.interactable = enabled; button.GetComponent<CanvasGroup>().alpha = enabled ? 1f : 0.4f; }

        private void RefreshLanguage()
        {
            foreach (var label in _labels) label.label.text = Loc.Get(label.key);
            if (_controller.Editor != null) RefreshCatalog();
        }

        private void OnDestroy() => Loc.LanguageChanged -= RefreshLanguage;
    }
}
