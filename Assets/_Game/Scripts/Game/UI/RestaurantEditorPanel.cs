using System;
using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Controls;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Layout;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>Touch-sized decoration dock with a left-side catalog and contextual furniture actions.</summary>
    public sealed class RestaurantEditorPanel : MonoBehaviour
    {
        private const float DockHeight = 116f;
        private GridLayoutGroup _cardGrid;
        private RectTransform _cardsViewport;
        private ScrollRect _categoryScroll;
        private RectTransform _categoryNavigation, _categoryTrack, _categoryThumb;
        private Button _categoryPrevious, _categoryNext;
        private RectTransform _dock, _more, _statusBox;
        private Button _moreButton;
        private bool _catalogOpen = true, _lastSelection;
        private FloorArt _lastPaintTool;

        private RestaurantEditorController _controller;
        private LoadedContentPack _content;
        private ArtLibrary _art;
        private RectTransform _root, _bar, _cards;
        private Text _status;
        private Button _rotate, _store, _undo, _redo, _fill, _storeAll;
        private Text _fillLabel;
        // "Laying: <floor or wallpaper>" with its picture and a close button, so the chosen tool is always clear.
        private RectTransform _chip;
        private Image _chipIcon;
        private Text _chipText;
        private Button _shopTab, _storageTab;
        private bool _storage;
        private readonly List<(Text label, string key)> _labels = new List<(Text, string)>();
        private readonly List<(BuildCategory category, string code, Button button)> _filters = new List<(BuildCategory, string, Button)>();
        private RectTransform _categoryButtonsRoot;
        private int _categoryVersion = -1;
        // Category tabs are sized so the viewport always shows whole tabs; the strip rests on a tab boundary.
        private float _tabStep = 136f;
        private bool _categoryDragging;
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

            // A separate decoration scale keeps the touch controls readable without enlarging the game HUD.
            canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280, 720);
            _dock = UiKit.Box(safe, "DecorationDock", UiKit.Wood, UiKit.Cream, 4f);
            _dock.anchorMin = Vector2.zero; _dock.anchorMax = new Vector2(1, 0); _dock.pivot = Vector2.zero;
            _dock.offsetMin = new Vector2(8, 8); _dock.offsetMax = new Vector2(-8, DockHeight);
            // The decorative fill must stay stretched behind the buttons, outside the layout row.
            _dock.Find("Fill").gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var dockRow = _dock.gameObject.AddComponent<HorizontalLayoutGroup>();
            dockRow.padding = new RectOffset(8, 8, 6, 6); dockRow.spacing = 12;
            dockRow.childControlWidth = dockRow.childControlHeight = true;
            dockRow.childForceExpandWidth = true; dockRow.childForceExpandHeight = true;
            _shopTab = DockButton("ui.build.tab_shop", UiKit.WoodLight, () => ToggleCatalog(false));
            _storageTab = DockButton("ui.build.tab_storage", UiKit.WoodLight, () => ToggleCatalog(true));
            _undo = DockButton("ui.build.undo", UiKit.WoodLight, _controller.Undo);
            _rotate = DockButton("ui.build.rotate", UiKit.WoodLight, _controller.Rotate);
            _store = DockButton("ui.build.store", UiKit.Red, _controller.Remove);
            _moreButton = DockButton("ui.build.more", UiKit.WoodLight, () => {
                bool open = !_more.gameObject.activeSelf;
                if (open) SetCatalogOpen(false);
                SetMoreOpen(open);
            });
            DockButton("ui.build.done", UiKit.Green, _controller.Save);

            _bar = UiKit.Box(safe, "ShopBar", UiKit.Wood, UiKit.Cream, 4f);
            _bar.anchorMin = Vector2.zero; _bar.anchorMax = new Vector2(.3f, 1); _bar.pivot = Vector2.zero;
            _bar.offsetMin = new Vector2(8, DockHeight + 8);
            _bar.offsetMax = new Vector2(-8, -8);
            var categoriesView = UiKit.NewRect("CategoriesViewport", _bar);
            categoriesView.anchorMin = new Vector2(0, 1); categoriesView.anchorMax = Vector2.one;
            categoriesView.pivot = new Vector2(0, 1);
            categoriesView.offsetMin = new Vector2(52, -88); categoriesView.offsetMax = new Vector2(-52, -8);
            categoriesView.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, .01f);
            categoriesView.gameObject.AddComponent<RectMask2D>();
            var categoriesScroll = categoriesView.gameObject.AddComponent<ScrollRect>();
            var categoriesRow = UiKit.NewRect("Categories", categoriesView);
            categoriesRow.anchorMin = Vector2.zero; categoriesRow.anchorMax = new Vector2(0, 1);
            categoriesRow.pivot = new Vector2(0, .5f); categoriesRow.sizeDelta = Vector2.zero;
            _categoryButtonsRoot = categoriesRow;
            RebuildCategoryTabs();
            categoriesScroll.viewport = categoriesView; categoriesScroll.content = categoriesRow;
            categoriesScroll.vertical = false; categoriesScroll.movementType = ScrollRect.MovementType.Clamped;
            _categoryScroll = categoriesScroll;
            var dragWatch = categoriesView.gameObject.AddComponent<EventTrigger>();
            AddTrigger(dragWatch, EventTriggerType.BeginDrag, () => _categoryDragging = true);
            AddTrigger(dragWatch, EventTriggerType.EndDrag, () => _categoryDragging = false);
            _categoryNavigation = UiKit.NewRect("CategoryNavigation", _bar);
            _categoryNavigation.anchorMin = new Vector2(0, 1); _categoryNavigation.anchorMax = Vector2.one;
            _categoryNavigation.pivot = new Vector2(0, 1);
            _categoryNavigation.offsetMin = new Vector2(8, -96); _categoryNavigation.offsetMax = new Vector2(-8, -8);
            _categoryPrevious = CategoryArrow(_categoryNavigation, "ui.build.category_previous", () => ScrollCategories(-1));
            _categoryNext = CategoryArrow(_categoryNavigation, "ui.build.category_next", () => ScrollCategories(1));
            var nextRect = (RectTransform)_categoryNext.transform;
            nextRect.anchorMin = nextRect.anchorMax = nextRect.pivot = new Vector2(1, 1);
            nextRect.anchoredPosition = Vector2.zero;
            _categoryTrack = UiKit.NewRect("ScrollTrack", _categoryNavigation);
            _categoryTrack.anchorMin = new Vector2(0, 0); _categoryTrack.anchorMax = new Vector2(1, 0);
            _categoryTrack.pivot = Vector2.zero;
            _categoryTrack.offsetMin = new Vector2(44, 0); _categoryTrack.offsetMax = new Vector2(-44, 4);
            var trackImage = _categoryTrack.gameObject.AddComponent<Image>();
            trackImage.color = new Color(.69f, .55f, .4f, .2f); trackImage.raycastTarget = false;
            _categoryThumb = UiKit.NewRect("ScrollThumb", _categoryTrack);
            var thumbImage = _categoryThumb.gameObject.AddComponent<Image>();
            thumbImage.color = UiKit.Gold; thumbImage.raycastTarget = false;
            categoriesScroll.onValueChanged.AddListener(_ => RefreshCategoryNavigation());

            var viewport = UiKit.NewRect("CardsViewport", _bar);
            _cardsViewport = viewport;
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(8, 8); viewport.offsetMax = new Vector2(-8, -104);
            viewport.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, .01f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _cards = UiKit.NewRect("Cards", viewport);
            _cards.anchorMin = new Vector2(0, 1); _cards.anchorMax = Vector2.one; _cards.pivot = new Vector2(0, 1);
            _cards.sizeDelta = Vector2.zero;
            scroll.viewport = viewport; scroll.content = _cards; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            _cardGrid = _cards.gameObject.AddComponent<GridLayoutGroup>();
            _cardGrid.spacing = new Vector2(12, 12); _cardGrid.padding = new RectOffset(4, 4, 4, 4);
            _cardGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; _cardGrid.constraintCount = 2;
            _cardGrid.cellSize = new Vector2(164, 208);
            _cards.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _more = UiKit.Box(safe, "MoreActions", UiKit.Wood, UiKit.Cream, 4f);
            _more.anchorMin = _more.anchorMax = _more.pivot = new Vector2(1, 0);
            _more.anchoredPosition = new Vector2(-8, DockHeight + 8); _more.sizeDelta = new Vector2(480, 332);
            _redo = AddButton(_more, "ui.build.redo", 12, 12, 222, 96, 30, UiKit.WoodLight, () => MoreAction(_controller.Redo));
            _fill = AddButton(_more, "ui.build.fill", 246, 12, 222, 96, 30, UiKit.WoodLight, () => MoreAction(_controller.FillFloor));
            _fillLabel = _fill.GetComponentInChildren<Text>();
            _storeAll = AddButton(_more, "ui.build.store_all", 12, 120, 456, 96, 30, UiKit.Wood, () => MoreAction(_controller.StoreAll));
            AddButton(_more, "ui.build.cancel", 12, 228, 456, 92, 30, UiKit.Red, _controller.Cancel);
            SetMoreOpen(false);

            _statusBox = UiKit.Box(safe, "Status", Color.clear, new Color(.15f, .1f, .07f, .8f), 0);
            _statusBox.anchorMin = Vector2.zero; _statusBox.anchorMax = new Vector2(1, 0); _statusBox.pivot = Vector2.zero;
            _status = UiKit.AddText(UiKit.NewRect("Text", _statusBox), 26, TextAnchor.MiddleLeft, Color.white);
            _status.rectTransform.anchorMin = Vector2.zero; _status.rectTransform.anchorMax = Vector2.one;
            _status.rectTransform.offsetMin = new Vector2(12, 2); _status.rectTransform.offsetMax = new Vector2(-12, -2);
            _status.resizeTextForBestFit = true; _status.resizeTextMinSize = 22; _status.resizeTextMaxSize = 26;

            _chip = UiKit.Box(safe, "ToolChip", UiKit.Gold, UiKit.GoldFill, 4f);
            _chip.anchorMin = _chip.anchorMax = _chip.pivot = new Vector2(.5f, 1);
            _chip.anchoredPosition = new Vector2(0, -12); _chip.sizeDelta = new Vector2(560, 96);
            _chipIcon = UiKit.AddImage(UiKit.Place(UiKit.NewRect("Icon", _chip), 8, 8, 80, 80), null);
            _chipText = UiKit.Label(_chip, "Text", 100, 8, 344, 80, 28, TextAnchor.MiddleLeft, UiKit.DarkText, true);
            var close = AddButton(_chip, "ui.build.deselect", 456, 0, 104, 96, 26, UiKit.WoodLight,
                () => { _controller.ClearSelection(); RefreshState(); });
            _chip.gameObject.SetActive(false);
            SetCatalogOpen(true);

            Loc.LanguageChanged += RefreshLanguage;
            RefreshLanguage(); SetVisible(false);
        }

        private Button DockButton(string key, Color color, Action action)
        {
            var button = AddButton(_dock, key, 0, 0, 144, 96, 30, color, action);
            var layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 96; layout.preferredWidth = 144; layout.flexibleWidth = 1;
            return button;
        }

        private void SetMoreOpen(bool open)
        {
            _more.gameObject.SetActive(open);
            if (_statusBox != null) _statusBox.gameObject.SetActive(!open);
            UiKit.SetButtonColor(_moreButton, open ? UiKit.Gold : UiKit.WoodLight);
        }

        private void MoreAction(Action action) { SetMoreOpen(false); action(); }

        private void ToggleCatalog(bool storage)
        {
            bool open = !_catalogOpen || _storage != storage;
            SetMoreOpen(false);
            ShowStorage(storage);
            SetCatalogOpen(open);
        }

        public void SetCatalogOpen(bool open)
        {
            _catalogOpen = open;
            _bar.gameObject.SetActive(open);
            float top = DockHeight + 8;
            _statusBox.anchorMin = new Vector2(open ? .3f : 0, 0);
            _statusBox.offsetMin = new Vector2(8, top);
            _statusBox.offsetMax = new Vector2(-8, top + 48);
            if (_shopTab != null) UiKit.SetButtonColor(_shopTab, open && !_storage ? UiKit.Green : UiKit.WoodLight);
            if (_storageTab != null) UiKit.SetButtonColor(_storageTab, open && _storage ? UiKit.Green : UiKit.WoodLight);
        }

        private Button AddButton(Transform parent, string key, float x, float y, float w, float h, int size, Color color, Action action)
        {
            var button = UiKit.TextButton(parent, key, color, size, action, out var text);
            button.gameObject.AddComponent<CanvasGroup>();
            UiKit.Place((RectTransform)button.transform, x, y, w, h);
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 26; text.resizeTextMaxSize = size;
            text.text = Loc.Get(key);
            _labels.Add((text, key)); return button;
        }

        public void SetVisible(bool visible)
        {
            _root.gameObject.SetActive(visible);
            if (visible) { _lastSelection = false; _lastPaintTool = null; SetMoreOpen(false); SetCatalogOpen(true); RefreshState(); }
        }

        /// <summary>True when a screen point is over the shop bar (dropping a held piece there stores it).</summary>
        public bool IsOverShopBar(Vector2 screen) =>
            _bar != null && _bar.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(_bar, screen, null);

        /// <summary>What choosing the card under <paramref name="screen"/> does, or null when it isn't over a card.</summary>
        public Action CardAt(Vector2 screen)
        {
            if (!IsOverShopBar(screen) || !RectTransformUtility.RectangleContainsScreenPoint(_cardsViewport, screen, null)) return null;
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
            _dock.GetWorldCorners(corners);
            return corners[1].y; // screen-space overlay canvas: world corners are screen pixels
        }

        /// <summary>Only leaving the right edge hands a catalog gesture over to world placement.</summary>
        public bool IsRightOfCatalog(Vector2 screen)
        {
            var corners = new Vector3[4];
            _bar.GetWorldCorners(corners);
            return screen.x > corners[2].x;
        }

        private void LateUpdate()
        {
            if (_cardGrid == null || !_catalogOpen) return;
            FitCategoryTabs();
            RefreshCategoryNavigation();
            float width = Mathf.Max(1, (_cardsViewport.rect.width - 20) * .5f);
            var size = new Vector2(width, 208);
            if ((_cardGrid.cellSize - size).sqrMagnitude > .01f) _cardGrid.cellSize = size;
        }

        private Button CategoryArrow(Transform parent, string key, Action action)
        {
            var rect = UiKit.Place(UiKit.NewRect(key, parent), 0, 0, 44, 80);
            rect.gameObject.AddComponent<Image>().color = Color.clear;
            var button = UiKit.AddButton(rect, action);
            button.gameObject.AddComponent<CanvasGroup>();
            var label = UiKit.AddText(UiKit.NewRect("Label", rect), 32, TextAnchor.MiddleCenter, UiKit.SoftText);
            UiKit.Stretch(label.rectTransform, 0, 0, 0, 0);
            _labels.Add((label, key));
            return button;
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action());
            trigger.triggers.Add(entry);
        }

        private void ScrollCategories(int direction)
        {
            float overflow = _categoryScroll.content.rect.width - _categoryScroll.viewport.rect.width;
            if (overflow <= 1f) return;
            _categoryScroll.StopMovement();
            int tab = Mathf.RoundToInt(-_categoryScroll.content.anchoredPosition.x / _tabStep) + direction;
            SetCategoryOffset(Mathf.Clamp(tab * _tabStep, 0f, overflow));
            RefreshCategoryNavigation();
        }

        private void SetCategoryOffset(float offset)
        {
            var content = _categoryScroll.content;
            content.anchoredPosition = new Vector2(-offset, content.anchoredPosition.y);
        }

        /// <summary>
        /// Widens the tabs so a whole number of them fills the viewport, and lets the strip settle on a tab
        /// boundary after a swipe, so no tab label is ever cut in half at the edge.
        /// </summary>
        private void FitCategoryTabs()
        {
            if (_categoryScroll == null || _categoryButtonsRoot.childCount == 0) return;
            float view = _categoryScroll.viewport.rect.width;
            if (view <= 1f) return;
            int perView = Mathf.Max(1, Mathf.FloorToInt((view + 8f) / 136f));
            float step = view / perView;
            if (Mathf.Abs(step - _tabStep) > .5f || Mathf.Abs(_categoryButtonsRoot.sizeDelta.x - step * _filters.Count) > .5f)
            {
                _tabStep = step;
                for (int i = 0; i < _filters.Count; i++)
                    UiKit.Place((RectTransform)_filters[i].button.transform, i * step, 0, step - 8f, 80);
                _categoryButtonsRoot.sizeDelta = new Vector2(step * _filters.Count, 0);
            }
            float overflow = _categoryButtonsRoot.sizeDelta.x - view;
            if (_categoryDragging || overflow <= 1f || Mathf.Abs(_categoryScroll.velocity.x) > 300f) return;
            float offset = -_categoryScroll.content.anchoredPosition.x;
            float target = Mathf.Clamp(Mathf.Round(offset / _tabStep) * _tabStep, 0f, overflow);
            if (Mathf.Abs(target - offset) < .5f) return;
            _categoryScroll.StopMovement();
            SetCategoryOffset(Mathf.Lerp(offset, target, Mathf.Min(1f, Time.unscaledDeltaTime * 14f)));
        }

        private void RefreshCategoryNavigation()
        {
            if (_categoryNavigation == null) return;
            bool overflow = _categoryScroll.content.rect.width > _categoryScroll.viewport.rect.width + 1f;
            bool show = !_storage && overflow;
            _categoryNavigation.gameObject.SetActive(show);
            if (_cardsViewport != null)
                _cardsViewport.offsetMax = new Vector2(-8, _storage ? -8 : -104);
            if (_storage || !overflow) return;
            float position = Mathf.Clamp01(_categoryScroll.horizontalNormalizedPosition);
            float fraction = Mathf.Clamp(_categoryScroll.viewport.rect.width / _categoryScroll.content.rect.width, .08f, 1f);
            _categoryThumb.anchorMin = new Vector2(position * (1f - fraction), 0);
            _categoryThumb.anchorMax = new Vector2(position * (1f - fraction) + fraction, 1);
            _categoryThumb.offsetMin = _categoryThumb.offsetMax = Vector2.zero;
            SetEnabled(_categoryPrevious, position > .001f);
            SetEnabled(_categoryNext, position < .999f);
        }

        /// <summary>Hand actions remain in the dock, away from the finger and the furniture preview.</summary>
        public void SetHandButtons(bool visible, Vector2 screen, bool freezePosition = false) { }

        private void RebuildCategoryTabs()
        {
            if (_categoryButtonsRoot == null || _categoryVersion == _content.Pack.ConfigurationVersion) return;
            _categoryVersion = _content.Pack.ConfigurationVersion;
            _labels.RemoveAll(entry => entry.label != null && entry.label.transform.IsChildOf(_categoryButtonsRoot));
            var oldTabs = new List<GameObject>();
            foreach (Transform child in _categoryButtonsRoot) oldTabs.Add(child.gameObject);
            foreach (var tab in oldTabs)
            { tab.SetActive(false); if (Application.isPlaying) Destroy(tab); else DestroyImmediate(tab); }
            _filters.Clear();
            var categories = new List<(BuildCategory category, string key, string code)>();
            if (_content.Pack.ShopCategories.Count > 0)
            {
                foreach (var row in _content.Pack.ShopCategories)
                {
                    if (!row.Active || row.Code == "Floor" || row.Code == "Wallpaper" || row.Code == "Land") continue;
                    bool known = Enum.TryParse(row.Code, out BuildCategory category);
                    categories.Add((known ? category : BuildCategory.Decoration, "catalog.category." + row.Code, known ? null : row.Code));
                }
            }
            else
            {
                foreach (var category in new[] { BuildCategory.Kitchen, BuildCategory.Table, BuildCategory.Chair,
                    BuildCategory.Decoration, BuildCategory.Functional, BuildCategory.Outdoor, BuildCategory.Wall })
                    categories.Add((category, "ui.build." + category.ToString().ToLowerInvariant(), null));
            }
            categories.Add((BuildCategory.Wallpaper, "ui.build.wallpaper", null));
            categories.Add((BuildCategory.Floor, "ui.build.floor", null));
            if (_content.Pack.Rules.Expansion != null) categories.Add((BuildCategory.Land, "ui.build.land", null));
            int x = 0;
            foreach (var (category, key, code) in categories)
            {
                var tab = AddButton(_categoryButtonsRoot, key, x, 0, 128, 80, 26, UiKit.Cream,
                    () => { _storage = false; if (code == null) _controller.SetCategory(category); else _controller.SetConfiguredCategory(code); });
                _filters.Add((category, code, tab));
                foreach (var outline in tab.GetComponentsInChildren<Outline>()) outline.enabled = false;
                var text = tab.GetComponentInChildren<Text>();
                text.color = UiKit.DarkText; text.resizeTextMinSize = 20; text.horizontalOverflow = HorizontalWrapMode.Wrap;
                var selectedMark = UiKit.NewRect("SelectedMark", tab.transform);
                selectedMark.anchorMin = Vector2.zero; selectedMark.anchorMax = new Vector2(1, 0);
                selectedMark.offsetMin = new Vector2(12, 5); selectedMark.offsetMax = new Vector2(-12, 11);
                var mark = selectedMark.gameObject.AddComponent<Image>(); mark.color = UiKit.Wood; mark.raycastTarget = false;
                x += 136;
            }
            _categoryButtonsRoot.sizeDelta = new Vector2(x, 0);
        }

        private bool MatchesCategory(FurnitureDef piece)
        {
            if (_controller.ConfiguredCategoryCode != null) return piece.ShopCategory == _controller.ConfiguredCategoryCode;
            if (_content.Pack.ShopCategories.Count > 0 && !string.IsNullOrEmpty(piece.ShopCategory))
                return piece.ShopCategory == _controller.Category.ToString();
            return CategoryFor(piece) == _controller.Category;
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
            RebuildCategoryTabs();
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
                    if (f.Available && MatchesCategory(f)) pieces.Add(f);
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
                case FurnitureKind.Bed:
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
            card.sizeDelta = new Vector2(200, 0);
            Action choose = () => {
                select();
                if (_controller.HasSelection || _controller.HasFloor) SetCatalogOpen(false);
                RefreshState();
            };
            var button = UiKit.AddButton(card, choose);
            var icon = UiKit.AddImage(UiKit.NewRect("Icon", card), _art.Get(sprite));
            icon.rectTransform.anchorMin = new Vector2(0, 0); icon.rectTransform.anchorMax = new Vector2(1, 1);
            icon.rectTransform.offsetMin = new Vector2(12, 96); icon.rectTransform.offsetMax = new Vector2(-12, -8);
            var name = UiKit.AddText(UiKit.NewRect("Name", card), 28, TextAnchor.MiddleCenter, UiKit.DarkText);
            name.rectTransform.anchorMin = Vector2.zero; name.rectTransform.anchorMax = new Vector2(1, 0);
            name.rectTransform.offsetMin = new Vector2(8, 42); name.rectTransform.offsetMax = new Vector2(-8, 96);
            // Long names (e.g. "White Cloth Table") wrap onto two lines and shrink to fit, never past the card.
            name.horizontalOverflow = HorizontalWrapMode.Wrap; name.verticalOverflow = VerticalWrapMode.Truncate;
            name.lineSpacing = 1f;
            name.resizeTextForBestFit = true; name.resizeTextMinSize = 24; name.resizeTextMaxSize = 28;
            name.text = Loc.Get(key);
            var infoText = UiKit.AddText(UiKit.NewRect("Info", card), 26, TextAnchor.MiddleCenter, UiKit.SoftText);
            infoText.rectTransform.anchorMin = Vector2.zero; infoText.rectTransform.anchorMax = new Vector2(1, 0);
            infoText.rectTransform.offsetMin = new Vector2(8, 8); infoText.rectTransform.offsetMax = new Vector2(-8, 38);
            infoText.horizontalOverflow = HorizontalWrapMode.Wrap; infoText.verticalOverflow = VerticalWrapMode.Truncate;
            infoText.resizeTextForBestFit = true; infoText.resizeTextMinSize = 24; infoText.resizeTextMaxSize = 26;
            infoText.text = info;
            _cardRims.Add((id, rim));
            _cardPicks.Add((card, choose));
            return button;
        }

        /// <summary>Mouse players get hints that name the mouse buttons and keys.</summary>
        private static string MouseSuffix => Application.isMobilePlatform ? "" : "_mouse";

        public void RefreshState()
        {
            if (_controller.Editor == null) return;
            bool selected = _controller.HasSelection;
            if ((selected && !_lastSelection) || (_controller.PaintTool != null && _controller.PaintTool != _lastPaintTool))
            {
                SetCatalogOpen(false);
                SetMoreOpen(false);
            }
            _lastSelection = selected; _lastPaintTool = _controller.PaintTool;
            _rotate.gameObject.SetActive(selected);
            _store.gameObject.SetActive(selected);
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
            // The held piece's permanent effects (they count once it is placed and saved).
            if (_controller.HasSelection && _content.Pack.HasFurniture(_controller.SelectedId))
            {
                string effects = FurnitureEffectText.For(_content.Pack.GetFurniture(_controller.SelectedId));
                if (effects.Length > 0) _status.text = effects + "\n" + _status.text;
            }

            var owned = _controller.Owned;
            var placed = _shopCards.Count > 0 ? _controller.PlacedInDraft() : null;
            foreach (var (def, info, icon) in _shopCards)
            {
                // A download/replacement can finish while this Shop remains open.
                var currentArt = _art.GetOptional(_content.ArtForFurniture(def.Id).Sprite);
                if (icon.sprite != currentArt) icon.sprite = currentArt;
                bool locked = def.UnlockLevel > _controller.PlayerLevel;
                int storage = FurnitureOwnership.InStorage(owned, placed, def.Id);
                info.text = locked ? Loc.Format("ui.build.locked_short", def.UnlockLevel)
                    : storage > 0 ? Loc.Format("ui.build.in_storage", storage)
                    : def.Price == 0 ? Loc.Get("ui.build.free") : Loc.Format("ui.build.buy_price", def.Price);
                info.color = locked ? UiKit.Disabled : storage > 0 ? UiKit.Green : UiKit.DarkText;
                icon.color = locked ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            }
            UiKit.SetButtonColor(_shopTab, _catalogOpen && !_storage ? UiKit.Green : UiKit.WoodLight);
            UiKit.SetButtonColor(_storageTab, _catalogOpen && _storage ? UiKit.Green : UiKit.WoodLight);
            foreach (var (category, code, button) in _filters)
            {
                button.gameObject.SetActive(!_storage);
                UiKit.SetButtonColor(button, category == _controller.Category && code == _controller.ConfiguredCategoryCode ? UiKit.Gold : UiKit.Cream);
                button.GetComponent<Image>().color = Color.clear;
                button.transform.Find("SelectedMark").gameObject.SetActive(category == _controller.Category && code == _controller.ConfiguredCategoryCode);
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
