using System;
using System.IO;
using DemoRestaurant.Logic.Online;
using DemoRestaurant.Logic.Session;
using UnityEngine;

namespace DemoRestaurant.Game.Online
{
    /// <summary>
    /// Runs the v2 online start-up (sign-in → bootstrap → <c>GET /config</c> → <c>POST /sync/ledger</c>) and then keeps
    /// the restaurant in step with the server: queued actions are uploaded every few seconds, on pause and on quit,
    /// and each answer folds the server state back into the session (<see cref="OnlineSync"/>). Every failure leaves
    /// the game playable offline; nothing here blocks play.
    /// </summary>
    public sealed class OnlineSession
    {
        private const string OfflinePrefsKey = "dr_play_offline";
        public const float SyncSeconds = 15f;

        private readonly RestaurantSession _game;
        private readonly string _packId;
        private StartupFacts _facts;
        private bool _running;
        private float _syncTimer = SyncSeconds;

        public SupabaseAuth Auth { get; }
        public GameApiClient Api { get; }
        public OnlineSync Sync { get; }
        public OnlineState State { get; private set; } = OnlineState.Offline;
        public bool IsConfigured => _facts.Configured;

        /// <summary>The player's display name from the server profile, once bootstrapped.</summary>
        public string DisplayName { get; private set; }

        /// <summary>Raised on every state change (on the main thread).</summary>
        public event Action StateChanged;

        /// <summary>
        /// The server's restaurant differs from the local one in a way that can't be patched in place (another
        /// device, a rejected hire or renovation): the game writes <c>RestaurantSession.SaveFromServer</c> and reloads.
        /// </summary>
        public event Action<JsonValue, string> AdoptionNeeded;

        /// <summary>Actions the server turned down in the last round (type and reason), for a toast or the log.</summary>
        public event Action<string, string> ActionRejected;

        /// <summary>
        /// First sign-in of a brand-new account on a device that already has play: true keeps this device's
        /// restaurant (its queued actions and layout go to the account), false starts from the account's.
        /// Null (no UI) loads the account's.
        /// </summary>
        public Func<Awaitable<bool>> AskKeepDeviceRestaurant { get; set; }

        public OnlineSession(OnlineConfig config, RestaurantSession game, string packId, ServerIdMap ids, string defaultWallpaper = null)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _packId = packId;
            Sync = new OnlineSync(game, ids) { DefaultWallpaper = defaultWallpaper };
            _facts.Configured = config != null && config.IsConfigured;
            _facts.PlayOffline = PlayerPrefs.GetInt(OfflinePrefsKey, 0) == 1;
            LoadSyncSave();
            if (!_facts.Configured) return;
            var http = new SupabaseHttp(config);
            Auth = new SupabaseAuth(http);
            Api = new GameApiClient(http, Auth);
            Auth.SignedOut += OnSignedOut;
        }

        /// <summary>The player chose "Play offline" on the sign-in screen; asked again only from the profile.</summary>
        public void ChooseOffline()
        {
            PlayerPrefs.SetInt(OfflinePrefsKey, 1);
            PlayerPrefs.Save();
            _facts.PlayOffline = true;
            SetState(OnlineState.Offline);
        }

        public async Awaitable<AuthResult> SignIn(string email, string password)
        {
            RequireConfigured();
            var result = await Auth.SignIn(email.Trim(), password);
            if (result.Ok) await ResumeOnline();
            return result;
        }

        public async Awaitable<AuthResult> SignUp(string email, string password)
        {
            RequireConfigured();
            var result = await Auth.SignUp(email.Trim(), password);
            if (result.Ok && Auth.IsSignedIn) await ResumeOnline();
            return result;
        }

        /// <summary>"I've verified": refresh the session so the token carries the confirmed email.</summary>
        public async Awaitable<bool> CheckVerified()
        {
            RequireConfigured();
            if (!Auth.IsSignedIn) return false;
            await Auth.Refresh();
            await Run();
            return Auth.IsEmailVerified;
        }

        public async Awaitable SignOut()
        {
            if (Auth == null) return;
            SaveSync();
            await Auth.SignOut(); // raises SignedOut → OnSignedOut
        }

        /// <summary>Starts or continues the flow; also used after "Play offline" was undone from the profile.</summary>
        public async Awaitable ResumeOnline()
        {
            if (_facts.PlayOffline)
            {
                PlayerPrefs.DeleteKey(OfflinePrefsKey);
                PlayerPrefs.Save();
                _facts.PlayOffline = false;
            }
            await Run();
        }

        /// <summary>Advances the start-up as far as it can. Safe to call repeatedly (e.g. when the app resumes).</summary>
        public async Awaitable Run()
        {
            if (_running) return;
            _running = true;
            try
            {
                while (true)
                {
                    _facts.SignedIn = Auth != null && Auth.IsSignedIn;
                    _facts.EmailVerified = Auth != null && Auth.IsEmailVerified;
                    var step = StartupPlanner.Next(_facts);
                    SetState(StartupPlanner.StateFor(step));
                    if (step == StartupStep.None || step == StartupStep.ShowSignIn ||
                        step == StartupStep.ShowVerifyEmail || step == StartupStep.Done)
                        return;
                    if (!await Do(step))
                        return;
                }
            }
            finally
            {
                _running = false;
            }
        }

        /// <summary>Call every frame: uploads queued actions every <see cref="SyncSeconds"/> while online.</summary>
        public void Tick(float unscaledDt)
        {
            if (State == OnlineState.Unreachable)
            {
                // Retry the start-up now and then; the game keeps playing meanwhile.
                _syncTimer -= unscaledDt;
                if (_syncTimer <= 0f) { _syncTimer = SyncSeconds * 2; _ = Run(); }
                return;
            }
            if (State != OnlineState.Online) return;
            _syncTimer -= unscaledDt;
            if (_syncTimer > 0f) return;
            _syncTimer = SyncSeconds;
            _ = SyncNow();
        }

        /// <summary>Uploads everything queued now (pause, quit, after a renovation). Returns false when offline or failed.</summary>
        public async Awaitable<bool> SyncNow()
        {
            if (State != OnlineState.Online || _running) return false;
            _running = true;
            try { return await SyncAll(false); }
            finally { _running = false; }
        }

        /// <summary>Writes the action log and instance ids (call with the game save).</summary>
        public void SaveSync()
        {
            var data = Sync.Capture(Auth?.UserId ?? _savedPlayer);
            WriteAtomic(SyncPath, JsonUtility.ToJson(data));
        }

        // ---------------------------------------------------------------- steps

        private async Awaitable<bool> Do(StartupStep step)
        {
            switch (step)
            {
                case StartupStep.Bootstrap:
                {
                    var r = await Api.Bootstrap();
                    if (!Check(r)) return false;
                    await ClaimOrForget(r.Data);
                    DisplayName = r.Data["profile"]["displayName"].AsString();
                    _game.ServerRestaurantId = r.Data["restaurantId"].AsString() ?? _game.ServerRestaurantId;
                    _facts.Bootstrapped = true;
                    return true;
                }
                case StartupStep.LoadConfig:
                {
                    if (!await LoadConfig()) return false;
                    _facts.ConfigLoaded = true;
                    return true;
                }
                case StartupStep.Sync:
                {
                    // The first round settles the time away even with nothing queued.
                    if (!await SyncAll(true)) return false;
                    _facts.Synced = true;
                    _syncTimer = SyncSeconds;
                    return true;
                }
                default:
                    return false;
            }
        }

        private async Awaitable<bool> LoadConfig()
        {
            string cached = ReadText(ConfigPath);
            JsonValue config = null;
            if (cached != null && JsonValue.TryParse(cached, out var parsed) && parsed["etag"].AsString() != null) config = parsed;
            var r = await Api.GetConfig(config?["etag"].AsString());
            if (!r.Ok)
            {
                // Without the network a cached ruleset is still good enough to build requests later.
                if (config == null) return Check(r);
                Check(r);
                return false;
            }
            if (!r.NotModified)
            {
                config = r.Data;
                WriteAtomic(ConfigPath, config.ToString());
            }
            Sync.Catalog = ServerCatalog.FromConfig(config);
            Api.RulesetVersion = Sync.Catalog.RulesetVersion;
            return true;
        }

        /// <summary>Sends batches until the log is empty (or only retries are left). One round settles even if empty.</summary>
        private async Awaitable<bool> SyncAll(bool evenIfEmpty)
        {
            for (int round = 0; round < 50; round++)
            {
                var body = Sync.NextRequest(out var batch, evenIfEmpty && round == 0);
                if (body == null) return true;
                var r = await Api.SyncLedger(body);
                if (!r.Ok)
                {
                    SaveSync(); // the batch is marked sent: it goes again unchanged
                    if (r.Action == ApiAction.RefreshConfig && await LoadConfig()) continue;
                    return Check(r);
                }
                var outcome = Sync.ApplyResponse(r.Data);
                SaveSync();
                foreach (var rejected in outcome.Outcome.Rejected)
                {
                    Debug.Log($"Online: {rejected.Key.type} rejected ({rejected.Value})");
                    ActionRejected?.Invoke(rejected.Key.type, rejected.Value);
                }
                if (outcome.NeedsAdoption)
                {
                    AdoptionNeeded?.Invoke(Sync.LastState, outcome.AdoptReason);
                    return true;
                }
                // Retries mean the server is busy (rate limit): stop and try again next tick.
                if (outcome.Outcome.Retry.Count > 0 || batch.Count < ServerActionLog.MaxBatch) return true;
            }
            return true;
        }

        /// <summary>Maps a failed call to a state; true when the call succeeded.</summary>
        private bool Check(ApiJson r)
        {
            if (r.Ok) return true;
            switch (r.Action)
            {
                case ApiAction.VerifyEmail: SetState(OnlineState.NeedsVerification); break;
                case ApiAction.RefreshSession: SetState(Auth.IsSignedIn ? OnlineState.Unreachable : OnlineState.SignedOut); break;
                default:
                    if (State == OnlineState.Online) _facts.Synced = false; // re-run the start-up later
                    SetState(OnlineState.Unreachable);
                    break;
            }
            return false;
        }

        // ---------------------------------------------------------------- local files

        private string _savedPlayer;

        private string SyncPath => Path.Combine(Application.persistentDataPath, $"online_sync_{_packId}.json");
        private string ConfigPath => Path.Combine(Application.persistentDataPath, $"online_config_{_packId}.json");

        private void LoadSyncSave()
        {
            string text = ReadText(SyncPath);
            if (text == null) return;
            try
            {
                var save = JsonUtility.FromJson<OnlineSyncSave>(text);
                _savedPlayer = save?.playerId;
                Sync.Restore(save);
            }
            catch (Exception e) { Debug.LogWarning($"Online: could not read the sync save ({e.Message})."); }
        }

        /// <summary>
        /// The device's restaurant belongs to whoever synced it last. When another account signs in:
        /// an account that already has progress always wins (the device's queued actions are dropped and the
        /// first sync loads the account's restaurant); a brand-new account on a device with guest play asks the
        /// player whether to keep this device's restaurant.
        /// </summary>
        private async Awaitable ClaimOrForget(JsonValue state)
        {
            string user = Auth.UserId;
            var change = OnlineSync.OnSignIn(_savedPlayer, user, state, Sync.DeviceHasPlay);
            if (change == AccountChange.SameAccount) return;
            if (change == AccountChange.AskKeepDevice && AskKeepDeviceRestaurant != null && await AskKeepDeviceRestaurant())
            {
                Debug.Log("Online: keeping this device's restaurant for the new account.");
                Sync.KeepDevice();
            }
            else
            {
                Debug.Log(string.IsNullOrEmpty(_savedPlayer)
                    ? "Online: loading the account's restaurant; this device's unsent play is dropped."
                    : "Online: a different account signed in; the previous account's unsent actions are dropped.");
                Sync.ForgetDevice();
            }
            _savedPlayer = user;
            SaveSync();
        }

        private static string ReadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception) { return null; }
        }

        private static void WriteAtomic(string path, string text)
        {
            try
            {
                string temp = path + ".tmp";
                File.WriteAllText(temp, text);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (Exception e) { Debug.LogWarning($"Online: could not write {Path.GetFileName(path)} ({e.Message})."); }
        }

        private void OnSignedOut()
        {
            _facts = new StartupFacts { Configured = _facts.Configured };
            DisplayName = null;
            SetState(OnlineState.SignedOut);
        }

        private void SetState(OnlineState state)
        {
            if (State == state) return;
            State = state;
            Debug.Log($"Online: {state}" + (state == OnlineState.Online ? $" ({Sync.Log.Count} actions waiting)" : ""));
            StateChanged?.Invoke();
        }

        private void RequireConfigured()
        {
            if (Auth == null) throw new InvalidOperationException("Online is not configured");
        }
    }
}
