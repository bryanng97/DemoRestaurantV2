using System;
using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Controls;
using DemoRestaurant.Game.Online;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Session;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// The player-facing UI around the restaurant, laid out like the concept screen:
    /// - top-left: profile card (player level) and popularity card;
    /// - collapsible left drawer: Build, Menu, Staff, Recipes, Events;
    /// - collapsible bottom-right drawer: Street, Garden, Market, Friends;
    /// - bottom-centre: today's dishes; top-right: language (settings) button;
    /// - slide-in panels (one open at a time) and toast messages.
    /// Buttons for features that don't exist yet show "Coming soon!" so the layout already matches.
    /// </summary>
    public sealed class GameUi : MonoBehaviour
    {
        private HudDrawer _actions, _places;

        private readonly List<SlidePanel> _panels = new List<SlidePanel>();
        private readonly List<(Text label, string key)> _labels = new List<(Text, string)>();
        private RestaurantHud _hud;
        private WelcomeBackPopup _welcome;
        private ProfileDialog _profile;
        private SettingsDialog _settings;
        private LoginDialog _login;
        private RectTransform _canvas;
        private Text _toast, _language;
        private CanvasGroup _toastGroup;
        private float _toastTime;
        private ArtLibrary _art;
        private RectTransform _canvasRoot;
        public RestaurantEditorController Builder { get; set; }
        public ExteriorStreetView Exterior { get; set; }
        public void SetGameplayVisible(bool visible) => _canvasRoot.gameObject.SetActive(visible);

        public RestaurantSession Session { get; private set; }
        public LoadedContentPack Content { get; private set; }
        public StaffController Staff { get; private set; }
        public Sprite Coin { get; private set; }
        /// <summary>The for-sale sign's dialog (buy the next terrace).</summary>
        public TerraceSaleDialog TerraceSale { get; private set; }
        /// <summary>Saves the restaurant layout with a confirmed balance (set by the game; null = nothing is saved).</summary>
        public Func<Logic.Layout.LayoutSnapshot, long, bool> PersistLayout { get; set; }

        /// <summary>Wipes every save and preference on this device and restarts (Settings → Delete account and data).</summary>
        public Action DeleteAllData { set => _settings.DeleteAllData = value; }

        /// <summary>Renders the player's avatar (profile card and dialog).</summary>
        public AvatarPainter Avatars { get; private set; }

        /// <summary>The farm panel (toolbar, or tapping the farm behind the restaurant).</summary>
        public GardenPanel GardenPanel { get; private set; }
        public MenuPanel MenuPanel { get; private set; }

        /// <summary>Online backend session; null until <see cref="BindOnline"/>.</summary>
        public OnlineSession Online { get; private set; }

        /// <summary>Adds the sign-in dialog. Call after <see cref="Bind"/>; skipped when online is not configured.</summary>
        public void BindOnline(OnlineSession online)
        {
            Online = online;
            if (online == null || !online.IsConfigured) return;
            _login = new LoginDialog(this, _canvas, online);
            _toastGroup.transform.SetAsLastSibling();
        }

        /// <summary>Sign-in screen (start-up while signed out, or the profile's account button).</summary>
        public void OpenLogin()
        {
            if (_login == null) return;
            _profile.Close();
            _login.Show();
            _toastGroup.transform.SetAsLastSibling();
        }

        public void Bind(RestaurantSession session, LoadedContentPack content, StaffController staff, AvatarPainter avatars,
            int defaultFrameRate)
        {
            Session = session;
            Content = content;
            Staff = staff;
            _art = ArtLibrary.For(content.Theme);
            Coin = _art.Get(content.Theme.coinIcon);
            UiInput.EnsureEventSystem();

            var canvas = UiKit.CreateCanvas(transform, "GameCanvas", 20);
            _canvas = canvas;
            // Hidden as a whole while the restaurant editor is open (SetGameplayVisible).
            _canvasRoot = canvas;
            // Everything the player taps lives inside the safe area (clear of notches, punch-hole cameras,
            // curved edges and the iOS home bar). Only the full-screen dim of the welcome popup uses the whole canvas.
            var root = SafeArea.Create(canvas);
            Avatars = avatars;
            var fallbackPortrait = _art.Get("avatar_chef");
            // A skin's baked portrait of the owner wins (the prototype skin's 3D-style head), then the drawn avatar.
            var ownerType = content.Pack.OwnerType(Logic.Content.StaffRole.Chef);
            var bakedOwner = ownerType != null ? BakedPortrait(ownerType.Id) : null;
            Func<Sprite> portrait = () => bakedOwner != null ? bakedOwner
                : content.Pack.Avatar.IsEmpty ? fallbackPortrait : Avatars.Head(session.Profile.Avatar);
            _hud = new RestaurantHud(root, session.Profile, portrait, OpenProfile, _art.Get("icon_goal"),
                session.Progress, session.Demand, session.Arrivals);

            var menu = AddPanel<MenuPanel>(root);
            MenuPanel = menu;
            var staffPanel = AddPanel<StaffPanel>(root);
            var recipes = AddPanel<RecipePanel>(root);
            var market = AddPanel<MarketPanel>(root);
            GardenPanel = AddPanel<GardenPanel>(root);
            Action comingSoon = () => Toast(Loc.Get("ui.coming_soon"), 1.6f);

            // Left column, top to bottom.
            var left = new (string key, string icon, Action onClick)[]
            {
                // One decorate mode, as in the reference game (building and decorating are the same screen).
                ("ui.toolbar.build", "icon_build", () => Builder.Begin(BuildCategory.Table)),
                ("ui.toolbar.menu", "icon_menu", menu.Toggle),
                ("ui.toolbar.staff", "icon_staff", staffPanel.Toggle),
                ("ui.toolbar.recipes", "food_soup", recipes.Toggle),
                ("ui.toolbar.events", "icon_events", comingSoon),
            };
            _actions = HudDrawer.Create(root, "Actions", "ui.toolbar.actions", false);
            foreach (var item in left)
                _actions.Add(ToolbarButton(_actions.Content, item.key, item.icon, () =>
                { _actions.SetExpanded(false); item.onClick(); }));

            // Bottom-right row, left to right.
            var right = new (string key, string icon, Action onClick)[]
            {
                ("exterior.title", "icon_worldmap", () => Exterior?.Show()),
                ("ui.toolbar.garden", "icon_decor", GardenPanel.Toggle), // placeholder icon until farm art exists
                ("ui.toolbar.market", "icon_shop", market.Toggle),
                ("ui.toolbar.friends", "icon_friends", comingSoon),
            };
            _places = HudDrawer.Create(root, "Places", "ui.toolbar.places", true);
            foreach (var item in right)
                _places.Add(ToolbarButton(_places.Content, item.key, item.icon, () =>
                { _places.SetExpanded(false); item.onClick(); }));
            _actions.Opened += () => _places.SetExpanded(false);
            _places.Opened += () => _actions.SetExpanded(false);

            BuildLanguageButton(root);
            _welcome = new WelcomeBackPopup(canvas, Coin);
            TerraceSale = new TerraceSaleDialog(canvas, session, Coin, (snapshot, balance) => PersistLayout != null && PersistLayout(snapshot, balance), message => Toast(message));
            _profile = new ProfileDialog(this, canvas, Avatars);
            _settings = new SettingsDialog(this, canvas, defaultFrameRate);
            _profile.SettingsRequested += OpenSettings;
            BuildToast(root);

            // Panels sit above the HUD and buttons; the toast sits above everything, dialogs included,
            // so it moves from the safe area to the canvas (it is centred well clear of the screen edges).
            foreach (var p in _panels) p.Root.SetAsLastSibling();
            _toastGroup.transform.SetParent(canvas, false);
            _toastGroup.transform.SetAsLastSibling();

            session.LevelUp += (level, coins) => Toast(Loc.Format("ui.player.levelup", level, coins), 3f);
            Loc.LanguageChanged += RefreshTexts;
            RefreshTexts();
        }

        private void OnDestroy()
        {
            _settings?.Dispose(); _profile?.Dispose(); _login?.Dispose();
            Loc.LanguageChanged -= RefreshTexts;
        }

        public Sprite IconFor(DishDef dish) => dish == null ? null : _art.Get(Content.IconForDish(dish.Id));

        public Sprite ItemIcon(ItemDef item) =>
            item != null && Content.ItemIcons.TryGetValue(item.Id, out var key) ? _art.Get(key) : null;

        /// <summary>The skin's baked full-figure owner (standing, facing the viewer), or null.</summary>
        public Sprite BakedOwnerFigure()
        {
            var owner = Content.Pack.OwnerType(Logic.Content.StaffRole.Chef);
            var key = owner != null ? Content.ArtForStaff(owner.Id)?.AnimKey : null;
            return string.IsNullOrEmpty(key) ? null : _art.GetOptional(key + "_idle_f_00");
        }

        /// <summary>The skin's baked head-and-shoulders portrait for a staff type ({animKey}_portrait), or null.</summary>
        private Sprite BakedPortrait(string staffTypeId)
        {
            var key = Content.ArtForStaff(staffTypeId)?.AnimKey;
            return string.IsNullOrEmpty(key) ? null : _art.GetOptional(key + "_portrait");
        }

        /// <summary>Staff list portrait: your own avatar, a drawn look (cleaners), or the staff type's sprite.</summary>
        public Sprite PortraitFor(Logic.Simulation.Staff staff)
        {
            if (staff == null) return null;
            var baked = BakedPortrait(staff.Type.Id);
            if (baked != null) return baked;
            if (staff.IsOwner && !Content.Pack.Avatar.IsEmpty) return Avatars.Head(Session.Profile.Avatar);
            var art = Content.ArtForStaff(staff.Type.Id);
            return art.Avatar != null ? Avatars.Head(art.Avatar) : _art.Get(art.Front);
        }

        /// <summary>Shows the idle-progress summary (skipped when nothing worth reporting happened).</summary>
        public void ShowWelcomeBack(Logic.Save.OfflineReport report)
        {
            if (report == null || report.AwaySeconds < 60)
                return;
            foreach (var p in _panels) p.SetOpen(false);
            _welcome.Show(report, Session.Market?.ReadyBaskets ?? 0, Session.Settings.offlineCapHours);
            _toastGroup.transform.SetAsLastSibling();
        }

        /// <summary>Top-left profile card: name, account linking, avatar and outfit.</summary>
        public void OpenProfile()
        {
            if (_welcome.IsShowing) return;
            foreach (var p in _panels) p.SetOpen(false);
            _profile.Show();
            _toastGroup.transform.SetAsLastSibling();
        }

        public void OpenSettings()
        {
            foreach (var p in _panels) p.SetOpen(false);
            _settings.Show();
            _toastGroup.transform.SetAsLastSibling();
        }

        public void Toast(string message, float seconds = 2.6f)
        {
            _toast.text = message;
            _toastTime = seconds;
            _toastGroup.alpha = 1f;
        }

        private void Update()
        {
            if (Session == null)
                return;
            _hud.Update(Time.unscaledDeltaTime);
            _profile.Update();
            if (_toastTime > 0f)
            {
                _toastTime -= Time.unscaledDeltaTime;
                _toastGroup.alpha = Mathf.Clamp01(_toastTime / 0.4f);
            }
        }

        private T AddPanel<T>(RectTransform root) where T : SlidePanel
        {
            var panel = new GameObject(typeof(T).Name).AddComponent<T>();
            panel.transform.SetParent(transform, false);
            panel.Build(this, root);
            panel.Opened += opened =>
            {
                foreach (var other in _panels)
                    if (other != opened) other.SetOpen(false);
            };
            _panels.Add(panel);
            return panel;
        }

        private RectTransform ToolbarButton(RectTransform root, string key, string icon, Action onClick)
        {
            var button = UiKit.ConceptButton(root, key, _art.Get(icon), onClick, out var label);
            _labels.Add((label, key));
            return button;
        }

        /// <summary>Top-right settings button; for now it switches the language (EN / BM / 中文).</summary>
        private void BuildLanguageButton(RectTransform root)
        {
            var button = UiKit.ConceptCard(root, "Language");
            button.anchorMin = button.anchorMax = button.pivot = new Vector2(1f, 1f);
            button.anchoredPosition = new Vector2(-24f, -24f);
            button.sizeDelta = new Vector2(84f, 76f);
            UiKit.AddButton(button, CycleLanguage);
            _language = UiKit.AddText(UiKit.NewRect("Label", button), 26, TextAnchor.MiddleCenter, UiKit.DarkText);
            UiKit.Stretch(_language.rectTransform, 4, 4, 4, 4);
        }

        private void CycleLanguage()
        {
            var langs = Loc.SupportedLanguages;
            int next = (Array.IndexOf(langs, Loc.Language) + 1) % langs.Length;
            GameSettings.SetLanguage(langs[next]); // remembered on this device
            Toast(Loc.Get("ui.language.name"), 1.4f);
        }

        private void BuildToast(RectTransform root)
        {
            var toast = UiKit.Box(root, "Toast", new Color(0.15f, 0.1f, 0.07f, 0.9f), new Color(0, 0, 0, 0), 0f, out var bg, out _);
            bg.raycastTarget = false;
            toast.anchorMin = toast.anchorMax = toast.pivot = new Vector2(0.5f, 0f);
            toast.anchoredPosition = new Vector2(0f, 150f);
            toast.sizeDelta = new Vector2(980f, 84f);
            _toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.alpha = 0f;
            _toastGroup.blocksRaycasts = false;
            _toast = UiKit.AddText(UiKit.NewRect("Message", toast), 30, TextAnchor.MiddleCenter, Color.white);
            UiKit.Stretch(_toast.rectTransform, 20, 0, 20, 0);
            _toast.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void RefreshTexts()
        {
            foreach (var (label, key) in _labels)
                label.text = Loc.Get(key);
            if (_language != null)
                _language.text = Loc.Language == "zh" ? "中文" : Loc.Language == "ms" ? "BM" : "EN";
        }
    }
}
