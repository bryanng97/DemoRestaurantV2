using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Online;
using UnityEngine;

namespace DemoRestaurant.Game.Online
{
    /// <summary>
    /// Runs the online start-up (sign-in → bootstrap → own restaurant, created if missing → wallet, inventory,
    /// layout) and holds what the server returned. Every failure leaves the game playable offline.
    /// </summary>
    public sealed class OnlineSession
    {
        private const string OfflinePrefsKey = "dr_play_offline";

        private readonly Func<string> _restaurantName;
        private StartupFacts _facts;
        private bool _running;

        public SupabaseAuth Auth { get; }
        public GameApiClient Api { get; }
        public OnlineCoins Coins { get; } = new OnlineCoins();
        public OnlineState State { get; private set; } = OnlineState.Offline;
        public bool IsConfigured => _facts.Configured;

        public ApiProfile Profile { get; private set; }
        public ApiRestaurant Restaurant { get; private set; }
        public ApiLayoutSnapshot Layout { get; private set; }
        /// <summary>Owned quantity per server item id (includes placed copies).</summary>
        public Dictionary<string, int> Inventory { get; } = new Dictionary<string, int>();

        /// <summary>Raised on every state change (on the main thread).</summary>
        public event Action StateChanged;

        public OnlineSession(OnlineConfig config, Func<string> restaurantName)
        {
            _restaurantName = restaurantName ?? (() => "My Restaurant");
            _facts.Configured = config != null && config.IsConfigured;
            _facts.PlayOffline = PlayerPrefs.GetInt(OfflinePrefsKey, 0) == 1;
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

        private async Awaitable<bool> Do(StartupStep step)
        {
            switch (step)
            {
                case StartupStep.Bootstrap:
                {
                    var r = await Api.Bootstrap();
                    if (!Check(r)) return false;
                    Profile = r.Data.profile;
                    Coins.ApplyServer(r.Data.wallet.softCurrency, r.Data.wallet.updatedAt);
                    _facts.Bootstrapped = true;
                    return true;
                }
                case StartupStep.LoadRestaurants:
                {
                    var r = await Api.GetMyRestaurants();
                    if (!Check(r)) return false;
                    Restaurant = r.Data.items != null && r.Data.items.Length > 0 ? r.Data.items[0] : null;
                    _facts.HasRestaurant = Restaurant != null;
                    _facts.RestaurantsLoaded = true;
                    return true;
                }
                case StartupStep.CreateRestaurant:
                {
                    var r = await Api.CreateRestaurant(RestaurantName());
                    if (r.Ok) Restaurant = r.Data;
                    else if (r.Code == "RESTAURANT_LIMIT_REACHED") _facts.RestaurantsLoaded = false; // load the existing one
                    else return Check(r);
                    _facts.HasRestaurant = Restaurant != null;
                    return true;
                }
                case StartupStep.LoadState:
                    if (!await LoadState()) return false;
                    _facts.StateLoaded = true;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Wallet, inventory (all pages) and the saved layout.</summary>
        public async Awaitable<bool> LoadState()
        {
            var wallet = await Api.GetWallet();
            if (!Check(wallet)) return false;
            Coins.ApplyServer(wallet.Data.softCurrency, wallet.Data.updatedAt);

            var owned = new Dictionary<string, int>();
            string cursor = null;
            do
            {
                var page = await Api.GetInventory(100, cursor);
                if (!Check(page)) return false;
                foreach (var e in page.Data.items ?? new ApiInventoryEntry[0])
                    owned[e.itemId] = e.quantity;
                cursor = page.Data.nextCursor;
            } while (!string.IsNullOrEmpty(cursor));
            Inventory.Clear();
            foreach (var pair in owned) Inventory[pair.Key] = pair.Value;

            var layout = await Api.GetLayout(Restaurant.id);
            if (!Check(layout)) return false;
            Layout = layout.Data;
            return true;
        }

        /// <summary>Maps a failed call to a state; true when the call succeeded.</summary>
        private bool Check<T>(ApiResult<T> r)
        {
            if (r.Ok) return true;
            switch (r.Action)
            {
                case ApiAction.VerifyEmail: SetState(OnlineState.NeedsVerification); break;
                case ApiAction.RefreshSession: SetState(Auth.IsSignedIn ? OnlineState.Unreachable : OnlineState.SignedOut); break;
                case ApiAction.NotFound:
                    // Our restaurant vanished (deleted elsewhere): start over from the restaurant list.
                    _facts.RestaurantsLoaded = _facts.HasRestaurant = _facts.StateLoaded = false;
                    SetState(OnlineState.Unreachable);
                    break;
                default: SetState(OnlineState.Unreachable); break;
            }
            return false;
        }

        private string RestaurantName()
        {
            string name = (_restaurantName() ?? "").Trim();
            if (name.Length == 0) name = "My Restaurant";
            return name.Length > 40 ? name.Substring(0, 40) : name;
        }

        private void OnSignedOut()
        {
            _facts = new StartupFacts { Configured = _facts.Configured };
            Profile = null;
            Restaurant = null;
            Layout = null;
            Inventory.Clear();
            SetState(OnlineState.SignedOut);
        }

        private void SetState(OnlineState state)
        {
            if (State == state) return;
            State = state;
            Debug.Log($"Online: {state}" + (state == OnlineState.Online ? $" (server coins {Coins.ServerCoins})" : ""));
            StateChanged?.Invoke();
        }

        private void RequireConfigured()
        {
            if (Auth == null) throw new InvalidOperationException("Online is not configured");
        }
    }
}
