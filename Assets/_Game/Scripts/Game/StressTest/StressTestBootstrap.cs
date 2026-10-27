using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Controls;
using DemoRestaurant.Game.Audio;
using DemoRestaurant.Game.DebugTools;
using DemoRestaurant.Game.Online;
using DemoRestaurant.Game.Persistence;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Game.UI;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Online;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DemoRestaurant.Game.StressTest
{
    /// <summary>
    /// Demo / engine-test scene. Builds the furnished test restaurant and a <see cref="RestaurantSession"/>, then
    /// hooks up the renderer and UI.
    /// - Game mode: customers arrive by popularity level and daily limit; staff caps apply.
    /// - Stress mode: a fixed number of customers is kept in the room and caps are off, to measure FPS on device.
    /// </summary>
    public sealed class StressTestBootstrap : MonoBehaviour
    {
        public enum Mode
        {
            Game,
            Stress,
        }

        // The test layout below is hand-placed for this size (a multiple of the 3-cell wall panel).
        private const int RoomWidth = 15;
        private const int RoomHeight = 12;

        [SerializeField] private string contentPack = "malaysia";
        [SerializeField] private Mode mode = Mode.Game;
        [Header("Stress mode")]
        [SerializeField, Range(0, 300)] private int targetCustomers = 40;
        [SerializeField] private float spawnPerSecond = 6f;
        [SerializeField, Min(0)] private int stressChefs = 3;
        [SerializeField, Min(0)] private int stressWaiters = 3;
        [Header("General")]
        [SerializeField] private int seed = 12345;
        [SerializeField] private int targetFrameRate = 60;
        [Tooltip("Confirmed balance at start. In the real game this comes from the server at login.")]
        [SerializeField] private long startingCoins = 0;
        [SerializeField] private bool showDebugHud = true;
        [Tooltip("Camera orthographic size at start (half the visible height in world units). 5 = whole room (1x), 4 = 1.25x, 3.13 = 1.6x, 2.5 = 2x.")]
        [SerializeField, Range(1.6f, 6f)] private float cameraStartSize = 3.196383f;
        [Tooltip("World position the camera starts at. Use the debug panel's \"Copy view\" to capture a view you like.")]
        [SerializeField] private Vector2 cameraStartPosition = new Vector2(0.7262582f, -2.778744f);
        [Tooltip("Shown on the profile card. Later this comes from the player's save.")]
        [SerializeField] private string restaurantName = "NOMU WORLD";
        [Tooltip("Game mode saves to the device and resumes with idle progress. Stress mode never saves.")]
        [SerializeField] private bool useSaveGame = true;
        [SerializeField, Min(5f)] private float autosaveSeconds = 30f;

        // Where idle staff wait: chefs in the kitchen aisle behind the counter, waiters in front of it.
        private static readonly GridPos[] ChefHomes =
            { new GridPos(6, 1), new GridPos(8, 1), new GridPos(2, 1), new GridPos(5, 1), new GridPos(7, 1), new GridPos(10, 1) };
        private static readonly GridPos[] WaiterHomes =
            { new GridPos(9, 3), new GridPos(10, 3), new GridPos(11, 3), new GridPos(2, 3), new GridPos(13, 3), new GridPos(1, 3) };
        // Waiting area beside the visible back-left doorway, front of the line first.
        private static readonly GridPos[] WaitingArea =
            { new GridPos(1, 10), new GridPos(1, 9), new GridPos(1, 8), new GridPos(0, 9), new GridPos(0, 8), new GridPos(0, 11) };

        private LoadedContentPack _content;
        private RestaurantSession _session;
        private RestaurantView _view;
        private StaffController _staff;
        private EarningsHud _earningsHud;
        private GameUi _ui;
        private RestaurantEditorController _builder;
        private RestaurantMusic _music;
        private float _spawnBudget;
        private LocalSaveStore _store;
        private OnlineSession _online;
        private float _autosaveTimer;
        private System.DateTime? _pausedAtUtc;
        private bool _resetting;

        private bool UsesSave => useSaveGame && mode == Mode.Game && !_resetting;

        // Restaurant layout save (renovations and land), kept in step with the game save above.
        private bool _autoSaveAllowed;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            // Device preferences: language (or the device's), frame rate (or targetFrameRate), music volume.
            GameSettings.Apply(targetFrameRate);

            _content = ContentLoader.Load(contentPack);
            // A new restaurant starts at the level-1 room size and grows with its level (the stress test uses the fixed showroom).
            var growth = mode == Mode.Stress ? null : _content.Pack.Rules.RoomGrowth;
            var (roomWidth, roomHeight) = growth != null ? growth.SizeFor(1) : (RoomWidth, RoomHeight);
            var layout = new RestaurantLayout(roomWidth, roomHeight, new GridPos(_content.Theme.entranceX, _content.Theme.entranceY));
            // A new restaurant starts small (one kitchen, two tables); the stress test uses the full showroom.
            if (mode == Mode.Stress) FurnishShowroom(layout, _content.Pack);
            else FurnishStarter(layout, _content.Pack);
            layout.SetWaitingArea(System.Linq.Enumerable.Take(WaitingArea, _content.Pack.Rules.WaitingSpots));
            HangStarterWalls(layout, _content.Pack);
            bool loaded = LayoutSaveStore.TryLoad(layout, _content, out var savedCoins, out var ownedFurniture);
            // Saves from before wall pieces: hang the starter set (menu holder and windows) and own it.
            if (loaded && layout.WallItems.Count == 0)
            {
                HangStarterWalls(layout, _content.Pack);
                if (ownedFurniture != null)
                    foreach (var w in layout.WallItems)
                        ownedFurniture[w.Def.Id] = (ownedFurniture.TryGetValue(w.Def.Id, out int n) ? n : 0) + 1;
            }
            // Saves from before the rules dropped (or shrank) the waiting area keep only as many spots as the rules allow.
            if (layout.WaitingSpots.Count > _content.Pack.Rules.WaitingSpots)
                layout.SetWaitingArea(System.Linq.Enumerable.Take(new System.Collections.Generic.List<GridPos>(layout.WaitingSpots), _content.Pack.Rules.WaitingSpots));
            _autoSaveAllowed = loaded || !System.IO.File.Exists(LayoutSaveStore.PathFor(_content));

            // The player's display name starts as the restaurant name; the profile dialog renames it.
            _session = new RestaurantSession(_content.Pack, layout, seed, savedCoins ?? startingCoins, playerName: restaurantName);
            if (_content.Exterior != null) _session.ConfigureExterior(_content.Exterior);
            // Furniture is owned: the starter pieces (or, for older saves, whatever is placed); more is bought in the shop.
            (_session.Economy as ILandPurchaseServer)?.RestoreOwnedFurniture(ownedFurniture ?? FurnitureOwnership.CountPlaced(layout));
            _staff = new StaffController(_session, ChefHomes, WaiterHomes);

            // Offline-first: resume from the device save if there is one (staff, coins, recipes, popularity...).
            _store = new LocalSaveStore(contentPack);
            System.DateTime? savedAt = null;
            // Internal testing: once "Level +1" has gone past the playable cap, saves keep their higher level.
            if (Debug.isDebugBuild && PlayerPrefs.GetInt(DebugLevelCapKey, 0) == 1) _session.Progress.IgnorePlayableCap = true;
            if (UsesSave)
            {
                var save = _store.Load();
                if (save != null && save.contentPack == contentPack)
                    savedAt = _session.ApplySave(save);
            }
            if (mode != Mode.Stress) _session.GrowRoom(); // the room is as big as the restaurant level allows

            // One painter for the player's avatar everywhere: in the restaurant, on the profile card and dialog.
            var avatars = new AvatarPainter(_content.Pack.Avatar);
            _view = new GameObject("RestaurantView").AddComponent<RestaurantView>();
            _view.Avatars = avatars;
            _view.RecipeLevel = dish => _session.Recipes.LevelOf(dish);
            _view.OwnerLook = () => _session.Profile.Avatar;
            _view.OwnerCustomized = () => _session.Profile.HasCustomAvatar;
            _session.Profile.Changed += _view.RefreshOwner;
            _view.Bind(_session.Sim, _content, windowSegments: new int[0]); // windows are wall pieces now
            // The farm behind the back-right wall: tapping it opens the garden panel (not while decorating).
            var farm = new GameObject("FarmView").AddComponent<FarmView>();
            farm.transform.SetParent(_view.transform, false);
            farm.Bind(_session, _content, () => _builder != null && _builder.IsEditing, plot => _ui.GardenPanel.Show(plot));
            var trash = new GameObject("GroundTrash").AddComponent<TrashView>();
            trash.transform.SetParent(_view.transform, false);
            trash.Bind(_session, ArtLibrary.For(_content.Theme), () => _builder != null && _builder.IsEditing);
            // Wall pieces with a tap action (the menu holder opens the menu).
            var wallTaps = new GameObject("WallTaps").AddComponent<WallTapHandler>();
            wallTaps.transform.SetParent(_view.transform, false);
            wallTaps.Bind(() => _session.Layout, _content, () => _builder != null && _builder.IsEditing,
                action => { if (action == "menu") _ui.MenuPanel.Toggle(); });
            // Tapping a broken functional fixture repairs it (as in the reference game).
            wallTaps.FloorTap = world =>
            {
                if (trash.TryTap(world)) return true;
                if (_view.TryTapDirtyTable(world)) return true;
                var piece = _view.PickFurniture(world);
                if (piece == null || !_session.Sim.Fix(piece)) return false;
                _ui.Toast(Loc.Get(piece.Def.Kind == FurnitureKind.Entertainment ? "ui.entertainment.fixed" : "ui.toilet.fixed"));
                return true;
            };

            SetUpCamera(layout);
            _music = gameObject.AddComponent<RestaurantMusic>();
            _music.Bind(_content.Theme);

            var art = ArtLibrary.For(_content.Theme);
            _earningsHud = new GameObject("EarningsHud").AddComponent<EarningsHud>();
            _earningsHud.Bind(_session.Wallet, _session.Earnings, () => _session.Time, Camera.main, art.Get(_content.Theme.coinIcon));
            _session.TrashCollected += (piece, coins, xp) =>
            {
                if (coins > 0) _earningsHud.ShowEarned(IsoProjection.GridToWorld(piece.x + .5f, piece.y + .5f), coins);
            };

            _ui = new GameObject("GameUi").AddComponent<GameUi>();
            _ui.Bind(_session, _content, _staff, avatars, targetFrameRate);
            // The terrace for-sale sign: buying saves the enlarged layout with the approved balance.
            _ui.PersistLayout = (snapshot, balance) => !UsesSave || LayoutSaveStore.TrySave(snapshot, _content, balance);
            var sign = new GameObject("TerraceSign").AddComponent<TerraceSignView>();
            sign.transform.SetParent(_view.transform, false);
            sign.Bind(_session, _content, () => _builder != null && _builder.IsEditing, () => _ui.TerraceSale.Show());
            if (_content.Exterior != null)
            {
                var exterior = new GameObject("ExteriorStreet").AddComponent<ExteriorStreetView>();
                exterior.Bind(_ui);
                exterior.Persist = save => !UsesSave || _store.TrySave(save);
                exterior.Saved += () => { _autoSaveAllowed = true; SaveNow(); };
                _ui.Exterior = exterior;
            }
            _ui.DeleteAllData = () =>
            {
                GameSettings.ClearAll();
                ResetSave();
            };
            _builder = gameObject.AddComponent<RestaurantEditorController>();
            _builder.Bind(_session, _content, _view, _ui);
            _ui.Builder = _builder;
            StartOnline();
            _builder.Saved += () => { _autoSaveAllowed = true; SaveNow(); };
            _session.Economy.SaleSettled += _ => PersistRestaurant();
            HookFeedback();

            ApplyMode(mode, initial: savedAt == null);
            if (showDebugHud)
                SetUpDebugHud();

            // Idle progress for the time the game was closed.
            if (savedAt.HasValue)
            {
                _ui.ShowWelcomeBack(_session.ApplyOfflineProgress(savedAt.Value));
                SaveNow();
            }
        }

        private void SaveNow()
        {
            if (UsesSave && _session != null)
            {
                _store.Save(_session.CaptureSave());
                PersistRestaurant();
                _online?.SaveSync();
            }
            _autosaveTimer = autosaveSeconds;
        }

        /// <summary>
        /// Mobile: the app went to the background (save everything) or came back (idle progress for the gap).
        /// Both saves are written together so the layout (land, renovations) and the coins that paid for it agree.
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (_session == null || _ui == null)
                return;
            if (paused)
            {
                _pausedAtUtc = _session.UtcNow;
                PersistAll();
                if (_online != null && mode == Mode.Game) _ = _online.SyncNow();
                return;
            }
            if (!UsesSave)
                return;
            // Back from the background: retry an unfinished online start-up (e.g. the network was down).
            if (_online != null && _online.State == Logic.Online.OnlineState.Unreachable)
                _ = _online.Run();
            if (_pausedAtUtc.HasValue)
            {
                _ui.ShowWelcomeBack(_session.ApplyOfflineProgress(_pausedAtUtc.Value));
                _pausedAtUtc = null;
                SaveNow();
            }
        }

        private void OnApplicationQuit() => PersistAll();

        /// <summary>
        /// Online start-up (Game mode with saves only): sign-in screen while signed out, unless the player chose to
        /// play offline. Runs alongside local play; nothing here blocks the restaurant.
        /// </summary>
        private async void StartOnline()
        {
            if (!UsesSave) return;
            _online = new OnlineSession(OnlineConfig.Load(), _session, contentPack, LoadServerIds(contentPack),
                _content.Theme.wallpapers.Length > 0 ? _content.Theme.wallpapers[0].id : null);
            _ui.BindOnline(_online);
            _online.AdoptionNeeded += AdoptServerState;
            _online.AskKeepDeviceRestaurant = change => change == Logic.Online.AccountChange.AskKeepDeviceLayout
                ? _ui.Ask("ui.online.choose_layout.title", "ui.online.choose_layout.body",
                    "ui.online.choose_layout.keep", "ui.online.choose_layout.account")
                : _ui.Ask("ui.online.choose.title", "ui.online.choose.body", "ui.online.choose.keep", "ui.online.choose.fresh");
            if (!_online.IsConfigured) return;
            _online.StateChanged += () =>
            {
                if (_online.State != Logic.Online.OnlineState.Online) return;
                _ui.Toast(s_showAdopted ? Loc.Get("ui.online.synced") : Loc.Format("ui.online.connected", _session.Wallet.DisplayCoins), 2.6f);
                s_showAdopted = false;
            };
            await _online.Run();
            if (_online.State == Logic.Online.OnlineState.SignedOut || _online.State == Logic.Online.OnlineState.NeedsVerification)
                _ui.OpenLogin();
        }

        /// <summary>Local content id → backend id for the few that differ (Content/&lt;pack&gt;/server_ids.json).</summary>
        private static ServerIdMap LoadServerIds(string pack)
        {
            var text = Resources.Load<TextAsset>($"Content/{pack}/server_ids");
            if (text == null) return new ServerIdMap();
            return new ServerIdMap(JsonUtility.FromJson<ServerIdMap.File>(text.text)?.ids);
        }

        // The last adoption: the server restaurant and the local layout it replaced (it survives the scene reload).
        // Being asked to adopt the same server state onto the same local layout again means the server's can't be
        // shown here, so the local one is kept instead of reloading in a loop. Any change on either side (another
        // account, a later renovation, a room grown by test mode) is adopted as usual.
        private static string s_lastAdoption;
        private static bool s_showAdopted;

        // Play mode starts with domain reload off, so statics would otherwise outlive the previous session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_lastAdoption = null;
            s_showAdopted = false;
        }

        /// <summary>
        /// The server's restaurant differs from ours in a way that can't be patched in place (another device, a
        /// rejected hire or renovation): write it as the device saves and start the scene again from them. The queued
        /// actions stay in the sync save and go out on the next round.
        /// </summary>
        private void AdoptServerState(JsonValue state, string reason)
        {
            if (!UsesSave || _builder == null || _builder.IsEditing || (_ui.Exterior != null && _ui.Exterior.IsEditing))
            {
                Debug.Log($"Online: the server restaurant differs ({reason}); it loads after the editor closes.");
                return;
            }
            string local = _online.Sync.LocalFingerprint();
            string detail = reason == "layout" && state["layout"].Kind == JsonKind.Object
                ? ": " + ServerLayoutCodec.Difference(local, ServerLayoutCodec.Fingerprint(state["layout"])) : "";
            string key = AdoptionKey(state) + "#" + local;
            if (key == s_lastAdoption)
            {
                Debug.LogWarning($"Online: the server restaurant was already loaded but still differs ({reason}{detail}); keeping the local one.");
                return;
            }
            var ids = _online.Sync.Ids;
            var floors = new System.Collections.Generic.HashSet<string>();
            foreach (var f in _content.Theme.floors) floors.Add(f.id);
            var wallpapers = new System.Collections.Generic.HashSet<string>();
            foreach (var w in _content.Theme.wallpapers) wallpapers.Add(w.id);
            var snapshot = ServerLayoutCodec.Decode(state["layout"], ids, floors.Contains, wallpapers.Contains);
            snapshot.SetOwned(_session.MergeOwned((_session.Economy as ILandPurchaseServer)?.OwnedFurniture, state, ids));
            var save = _session.SaveFromServer(state);
            // Write the server layout, then check it loads like a start-up would; otherwise put ours back.
            var previous = LayoutSnapshot.Capture(_session.Layout);
            previous.SetOwned((_session.Economy as ILandPurchaseServer)?.OwnedFurniture);
            if (!LayoutSaveStore.TrySave(snapshot, _content, save.coins))
            {
                Debug.LogWarning("Online: could not write the server restaurant to this device.");
                return;
            }
            if (!LayoutSaveStore.TryLoad(new RestaurantLayout(snapshot.width, snapshot.height, new GridPos(0, 1)), _content))
            {
                Debug.LogWarning($"Online: the server restaurant ({reason}) doesn't load on this device; keeping the local one.");
                LayoutSaveStore.TrySave(previous, _content, _session.Wallet.ConfirmedCoins);
                s_lastAdoption = key;
                return;
            }
            s_lastAdoption = key;
            _store.Save(save);
            _online.Sync.Adopted(state);
            _online.SaveSync();
            Debug.Log($"Online: adopted the server restaurant ({reason}{detail}).");
            s_showAdopted = true;
            _resetting = true; // nothing else may overwrite the adopted saves before the reload
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private static string AdoptionKey(JsonValue state)
        {
            var staff = new System.Collections.Generic.List<string>();
            foreach (var s in state["staff"].Items) staff.Add(s["id"].AsString() + ":" + s["role"].AsString());
            staff.Sort(string.CompareOrdinal);
            return state["restaurantId"].AsString() + "|" + string.Join(",", staff) + "|" +
                   (state["layout"].Kind == JsonKind.Object ? ServerLayoutCodec.Fingerprint(state["layout"]) : "");
        }

        /// <summary>Writes the restaurant layout save and the game save (progress, staff, offline ledger).</summary>
        private void PersistAll()
        {
            PersistRestaurant();
            SaveNow();
        }


        private void Update()
        {
            if (_session == null)
            {
                // Scripts recompiled during Play mode: Unity wipes non-serialized state and doesn't re-run Awake.
                Debug.LogWarning("Scripts were recompiled while playing. Stop and press Play again.");
                enabled = false;
                return;
            }

            _music.Play(_builder.IsEditing || (_ui.Exterior != null && _ui.Exterior.IsEditing) ? MusicScene.Editor
                : _ui.Exterior != null && _ui.Exterior.IsOpen ? MusicScene.Street : MusicScene.Restaurant);
            if (_builder.IsEditing || (_ui.Exterior != null && _ui.Exterior.IsEditing))
            { _session.TickEconomy(Time.deltaTime); return; }
            float dt = Time.deltaTime;
            _session.Tick(dt);

            _autosaveTimer -= Time.unscaledDeltaTime;
            if (_autosaveTimer <= 0f)
                SaveNow();
            if (mode == Mode.Game) _online?.Tick(Time.unscaledDeltaTime);
            if (mode == Mode.Stress)
                BalanceCustomers(dt);
        }

        private void PersistRestaurant()
        {
            // Live layout stays unchanged while editing; never persist the unpaid draft here.
            if (_session != null && UsesSave && _autoSaveAllowed)
            {
                var snapshot = LayoutSnapshot.Capture(_session.Layout);
                snapshot.SetOwned((_session.Economy as ILandPurchaseServer)?.OwnedFurniture);
                LayoutSaveStore.TrySave(snapshot, _content, _session.Wallet.ConfirmedCoins);
            }
        }


        private void ApplyMode(Mode newMode, bool initial = false)
        {
            if (!initial && newMode == Mode.Stress)
                SaveNow(); // keep the real progress safe before the stress test changes staff
            mode = newMode;
            _session.ArrivalsEnabled = mode == Mode.Game;
            _staff.IgnoreCaps = mode == Mode.Stress;

            int chefs = mode == Mode.Stress ? stressChefs : 1;
            int waiters = mode == Mode.Stress ? stressWaiters : 1;
            if (initial || mode == Mode.Stress)
            {
                // A new restaurant starts with one free chef and one free waiter (economy sheet: starting staff).
                while (_session.StaffCount(StaffRole.Chef) < chefs && _staff.Hire(StaffRole.Chef, free: true) == HireResult.Hired) { }
                while (_session.StaffCount(StaffRole.Waiter) < waiters && _staff.Hire(StaffRole.Waiter, free: true) == HireResult.Hired) { }
            }
            if (mode == Mode.Game)
            {
                // Back to normal rules: trim staff above the level's slots (cleaners first, keep a chef and a waiter).
                while (_session.HiredCount > _session.StaffSlots)
                {
                    if (_session.StaffCount(StaffRole.Cleaner) > 0) _staff.DismissLatest(StaffRole.Cleaner);
                    else if (_session.StaffCount(StaffRole.Waiter) > 1) _staff.DismissLatest(StaffRole.Waiter);
                    else if (_session.StaffCount(StaffRole.Chef) > 1) _staff.DismissLatest(StaffRole.Chef);
                    else break;
                }
            }
            // The player's own avatar works in the restaurant too (free, outside the caps).
            _staff.EnsureOwner();
        }

        /// <summary>Player feedback for session events: coin pop-ups, angry customers, level-ups.</summary>
        private void HookFeedback()
        {
            float headHeight = _content.Theme.characterHeight + 0.15f;
            Vector3 Head(Customer c) => (Vector3)(IsoProjection.GridToWorld(c.Position, c.Z) + new Vector2(0, headHeight));

            _session.CustomerServed += (c, coins, xp) => _earningsHud.ShowEarned(Head(c), coins);
            _session.EntertainmentEarned += (item, coins) => _earningsHud.ShowEarned(IsoProjection.GridToWorld(item.Origin.X + .5f, item.Origin.Y + .5f, item.Def.Height), coins);
            _session.CustomerAngered += (c, ratingLost) =>
                _earningsHud.ShowText(Head(c), $"{Loc.Get("ui.angry")} ★{ratingLost:0.00}", new Color(1f, 0.45f, 0.4f));
            // The level-up toast itself comes from GameUi.
        }

        /// <summary>Stress mode: keep the room at the target customer count regardless of popularity.</summary>
        private void BalanceCustomers(float dt)
        {
            var sim = _session.Sim;
            if (sim.Customers.Count > targetCustomers)
            {
                for (int i = sim.Customers.Count - 1; i >= 0 && sim.Customers.Count > targetCustomers; i--)
                    sim.Despawn(sim.Customers[i]);
                _spawnBudget = 0;
                return;
            }

            _spawnBudget += dt * spawnPerSecond;
            while (_spawnBudget >= 1f && sim.Customers.Count < targetCustomers)
            {
                _spawnBudget -= 1f;
                sim.SpawnCustomer(_session.Arrivals.PickType() ?? _content.Pack.CustomerTypes[0]);
            }
            _spawnBudget = Mathf.Min(_spawnBudget, 1f);
        }

        /// <summary>
        /// Fixed test layout: kitchen line along the back-right wall, shelves on the back-left wall,
        /// two rows of 1x1 dining tables (20 seats), and tall plants as occluders in the walkways.
        /// Exercises depth sorting against multi-cell objects. Unrotated pieces face SouthWest (+Y).
        /// </summary>
        private static void FurnishShowroom(RestaurantLayout layout, ContentPack pack)
        {
            void Place(string id, int x, int y, Facing facing = Facing.SouthWest) => PlaceOrWarn(layout, pack, id, x, y, facing);

            // Back-right wall: the kitchen line. Chefs stand on the cell in front of each stove; plates wait on the stove.
            Place("bread_shelf", 0, 0);
            Place("fridge", 2, 0);
            Place("clay_oven", 3, 0);
            Place("gas_stove", 5, 0);
            Place("gas_stove", 7, 0);
            Place("sink", 9, 0);
            Place("jar_cabinet", 11, 0);
            Place("potted_palm", 14, 0);

            // Back-left wall: shelf turned to run along Y.
            Place("bread_shelf", 0, 3, Facing.SouthEast);
            Place("potted_palm", 0, 7);

            // Dining: 1x1 tables, each with a chair behind and in front of it.
            foreach (int tx in new[] { 3, 5, 7, 9, 11 })
            {
                foreach (int ty in new[] { 5, 9 })
                {
                    Place("dining_table", tx, ty);
                    Place("wooden_chair", tx, ty - 1, Facing.SouthWest);
                    Place("wooden_chair", tx, ty + 1, Facing.NorthEast);
                }
            }

            // Decor and tall occluders in the walkways.
            Place("menu_board", 14, 3);
            Place("potted_palm", 13, 7);
            Place("potted_palm", 9, 4);
            Place("potted_palm", 12, 4);
        }

        /// <summary>
        /// What a new player owns: one stove and two dining tables with two chairs each. Everything else is bought in
        /// the build shop and placed by the player.
        /// </summary>
        private static void FurnishStarter(RestaurantLayout layout, ContentPack pack)
        {
            PlaceOrWarn(layout, pack, "gas_stove", 5, 0, Facing.SouthWest);
            foreach (int tx in new[] { 4, 7 })
            {
                const int ty = 6;
                PlaceOrWarn(layout, pack, "dining_table", tx, ty, Facing.SouthWest);
                PlaceOrWarn(layout, pack, "wooden_chair", tx, ty - 1, Facing.SouthWest);
                PlaceOrWarn(layout, pack, "wooden_chair", tx, ty + 1, Facing.NorthEast);
            }
        }

        /// <summary>
        /// The pieces every restaurant starts with on its walls (as in the reference game): the menu holder, which opens
        /// the menu when tapped, and two windows.
        /// </summary>
        private static void HangStarterWalls(RestaurantLayout layout, ContentPack pack)
        {
            void Hang(string id, WallSide side, int index)
            {
                if (!pack.HasFurniture(id) || !layout.TryHang(pack.GetFurniture(id), side, index, out _))
                    Debug.LogWarning($"Restaurant layout: could not hang {id} on the {side} wall at {index}.");
            }
            if (layout.WallItems.Count > 0) return;
            Hang("menu_board", WallSide.Right, 2);
            Hang("window_basic", WallSide.Right, 6);
            Hang("window_basic", WallSide.Left, 4);
        }

        private static void PlaceOrWarn(RestaurantLayout layout, ContentPack pack, string id, int x, int y, Facing facing)
        {
            if (!layout.TryPlace(pack.GetFurniture(id), new GridPos(x, y), facing, out _))
                Debug.LogWarning($"Restaurant layout: could not place {id} at ({x},{y}).");
        }

        private void SetUpCamera(RestaurantLayout layout)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.orthographic = true;
            bool hasListener = false;
            foreach (var existingListener in Object.FindObjectsByType<AudioListener>())
                if (existingListener.isActiveAndEnabled) { hasListener = true; break; }
            if (!hasListener)
            {
                if (!cam.TryGetComponent<AudioListener>(out var listener)) listener = cam.gameObject.AddComponent<AudioListener>();
                listener.enabled = true;
            }
            // Start about 1.6x zoomed in so the art reads clearly; players can zoom out to the whole room (about 5).
            cam.orthographicSize = cameraStartSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ArtLibrary.For(_content.Theme).SceneColor(m => m.backgroundColor, new Color(0.55f, 0.78f, 0.62f));

            cam.transform.position = new Vector3(cameraStartPosition.x, cameraStartPosition.y, -10f);

            if (!cam.TryGetComponent(out CameraController controller))
                controller = cam.gameObject.AddComponent<CameraController>();
            controller.SetRoomBounds(layout);
            layout.BoundsChanged += () => controller.SetRoomBounds(layout); // the room grows on level-up
        }

        /// <summary>Developer overlay: FPS, memory, kitchen state, and the stress/game mode switch.</summary>
        /// <summary>Debug overlay: every staff member's job, cell and whether their character is drawn.</summary>
        private string StaffStats()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var s in _session.Sim.Staff)
                parts.Add($"{s.Type.Id} {s.State}{(s.IsResting ? " (rest)" : "")} ({s.Position.X:0.#},{s.Position.Y:0.#}) {_view.DescribeCharacter(s)}");
            return "Staff: " + string.Join(" | ", parts);
        }

        private const string DebugLevelCapKey = "debug.ignoreLevelCap";

        private void SetUpDebugHud()
        {
            var hud = gameObject.AddComponent<PerfHud>();
            hud.ChangeTargetCount = delta => targetCustomers = Mathf.Clamp(targetCustomers + delta, 0, 300);
            hud.ExtraStats = () =>
                (mode == Mode.Stress ? $"Customers {_session.Sim.Customers.Count}/{targetCustomers}" : $"Customers {_session.Sim.Customers.Count}") +
                $"   queue {_session.Sim.Queue.Count}   sort {_view.LastSortMs:0.00} ms\n" +
                $"Lv {_session.Progress.Level} XP {_session.Progress.Xp}/{_session.Progress.XpToNext}   demand {_session.Demand.Value:0}   guest every {_session.Arrivals.IntervalSeconds:0.0}s\n" +
                KitchenStats() + "\n" +
                StaffStats() + "\n" +
                CameraLine() + "\n" +
                $"Save: {(UsesSave ? "on" : "off")}   offline ledger: {_session.Ledger.Entries.Count} entries waiting to sync";
            hud.AddButton(() => $"Mode: {mode}", () => ApplyMode(mode == Mode.Game ? Mode.Stress : Mode.Game));
            // Internal testing (debug overlay only): jump a level (its reward, room growth and unlocks apply as usual)
            // and add coins. Offline the local economy server shares the wallet's confirmed balance.
            if (Debug.isDebugBuild) // the editor and development builds only, never a release
            {
                hud.AddButton(() => $"Level +1 (now {_session.Progress.Level})", () =>
                {
                    // Past the phase-1 cap too (the economy sheet's Levels tab goes further), remembered on this device.
                    if (!_session.Progress.IgnorePlayableCap)
                    {
                        _session.Progress.IgnorePlayableCap = true;
                        PlayerPrefs.SetInt(DebugLevelCapKey, 1);
                        PlayerPrefs.Save();
                    }
                    if (!_session.Progress.IsMaxLevel) _session.Progress.Add(_session.Progress.XpToNext - _session.Progress.Xp);
                });
                hud.AddButton(() => "Coins +10,000", () => _session.Wallet.SetConfirmed(_session.Wallet.ConfirmedCoins + 10000));
                hud.AddButton(() => "Ingredients +10 each", () => _session.DebugAddIngredients(10));
                hud.AddButton(() => "Learn all recipes", () => _ui.Toast($"Learned {_session.DebugLearnAllRecipes()} recipes"));
                // Everyone back to full energy and on duty (resting staff return to work at once).
                hud.AddButton(() => "Staff energy full", () =>
                {
                    foreach (var s in _session.Sim.Staff) _session.Sim.SetEnergy(s, StaffEnergyRules.Max, false);
                });
            }
            hud.AddButton(() => "Garden +6h", () => _session.Garden.DebugGrow(System.TimeSpan.FromHours(6), _session.UtcNow));
            hud.AddButton(() => "+1h away", () => _ui.ShowWelcomeBack(_session.ApplyOfflineProgress(_session.UtcNow - System.TimeSpan.FromHours(1))));
            hud.AddButton(() => "Reset save", ResetSave);
            hud.AddButton(() => "Copy view", CopyView);
        }

        private static string CameraLine()
        {
            var cam = Camera.main;
            if (cam == null) return "Camera: none";
            var p = cam.transform.position;
            return $"Camera: size {cam.orthographicSize:0.###} at ({p.x:0.###}, {p.y:0.###})   screen {Screen.width}x{Screen.height}";
        }

        /// <summary>
        /// Developer: logs the current zoom and position and copies them to the clipboard (works on the phone too),
        /// so a view found by pinching can become the default (Camera Start Size + Camera Start Position).
        /// </summary>
        private void CopyView()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var p = cam.transform.position;
            string text = $"Camera Start Size {cam.orthographicSize:0.######}, Camera Start Position ({p.x:0.######}, {p.y:0.######}), screen {Screen.width}x{Screen.height}";
            GUIUtility.systemCopyBuffer = text;
            Debug.Log(text);
            _ui.Toast(text, 4f);
        }

        /// <summary>Developer: wipe the device save and restart the scene from a fresh restaurant.</summary>
        private void ResetSave()
        {
            _resetting = true;
            _store.Delete();
            // The layout save (renovations, bought land) goes too, so the restaurant really starts fresh.
            _autoSaveAllowed = false;
            try { System.IO.File.Delete(LayoutSaveStore.PathFor(_content)); }
            catch (System.Exception e) { Debug.LogWarning($"Could not delete layout save: {e.Message}"); }
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private string KitchenStats()
        {
            int queued = 0, cooking = 0, ready = 0, delivering = 0;
            foreach (var t in _session.Sim.Tickets)
            {
                switch (t.State)
                {
                    case TicketState.Queued: queued++; break;
                    case TicketState.Cooking: case TicketState.Plating: cooking++; break;
                    case TicketState.Ready: ready++; break;
                    case TicketState.Delivering: delivering++; break;
                }
            }
            return $"Kitchen: {queued} queued, {cooking} cooking, {ready} on counter, {delivering} delivering";
        }
    }
}
