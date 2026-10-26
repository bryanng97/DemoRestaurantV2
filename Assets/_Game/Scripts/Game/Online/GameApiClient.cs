using System;
using DemoRestaurant.Logic.Online;
using UnityEngine;

namespace DemoRestaurant.Game.Online
{
    /// <summary>
    /// The sixteen game operations with the contract's retry rules: refresh once on 401, bootstrap on
    /// PLAYER_NOT_INITIALIZED, Retry-After on 429, bounded jittered backoff on 5xx/network, and persisted
    /// Idempotency-Keys (same key + body on every retry, across restarts) for create/purchase/claim.
    /// </summary>
    public sealed class GameApiClient
    {
        private const string PendingPrefsKey = "dr_pending_ops_";

        private readonly SupabaseHttp _http;
        private readonly SupabaseAuth _auth;
        private readonly PendingOperationStore _pending = new PendingOperationStore();
        private readonly System.Random _random = new System.Random();
        private string _pendingOwner;

        public GameApiClient(SupabaseHttp http, SupabaseAuth auth)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _pending.Changed += SavePending;
        }

        /// <summary>Keyed operations that never got a definite answer; retry them with <see cref="Resume"/>.</summary>
        public PendingOperationStore Pending
        {
            get { LoadPendingFor(_auth.UserId); return _pending; }
        }

        // ---- players

        public Awaitable<ApiResult<ApiBootstrap>> Bootstrap() => Call<ApiBootstrap>("POST", "/players/bootstrap", "{}", false);
        public Awaitable<ApiResult<ApiProfile>> GetMe() => Call<ApiProfile>("GET", "/me", null, false);
        public Awaitable<ApiResult<ApiWallet>> GetWallet() => Call<ApiWallet>("GET", "/wallet", null, false);

        // ---- catalog and inventory

        public Awaitable<ApiResult<Page<ApiCatalogItem>>> GetCatalog(int limit = 50, string cursor = null, string category = null) =>
            Call<Page<ApiCatalogItem>>("GET", "/catalog" + Query(limit, cursor, category), null, false);

        public Awaitable<ApiResult<Page<ApiInventoryEntry>>> GetInventory(int limit = 50, string cursor = null) =>
            Call<Page<ApiInventoryEntry>>("GET", "/inventory" + Query(limit, cursor, null), null, false);

        // ---- restaurants

        public Awaitable<ApiResult<Page<ApiRestaurant>>> GetMyRestaurants() =>
            Call<Page<ApiRestaurant>>("GET", "/restaurants/me", null, false);

        public Awaitable<ApiResult<ApiRestaurant>> CreateRestaurant(string name) =>
            Call<ApiRestaurant>("POST", "/restaurants", JsonUtility.ToJson(new CreateRestaurantBody { name = name }), true);

        public Awaitable<ApiResult<ApiRestaurant>> GetRestaurant(string id) =>
            Call<ApiRestaurant>("GET", "/restaurants/" + Uri.EscapeDataString(id), null, false);

        /// <summary>Pass null for a setting that should not change.</summary>
        public Awaitable<ApiResult<ApiRestaurant>> UpdateRestaurant(string id, string name, string visibility)
        {
            string body;
            if (name != null && visibility != null) body = JsonUtility.ToJson(new RenameAndVisibilityBody { name = name, visibility = visibility });
            else if (name != null) body = JsonUtility.ToJson(new RenameBody { name = name });
            else if (visibility != null) body = JsonUtility.ToJson(new VisibilityBody { visibility = visibility });
            else throw new ArgumentException("Nothing to update");
            return Call<ApiRestaurant>("PATCH", "/restaurants/" + Uri.EscapeDataString(id), body, false);
        }

        public Awaitable<ApiResult<ApiLayoutSnapshot>> GetLayout(string id) =>
            Call<ApiLayoutSnapshot>("GET", "/restaurants/" + Uri.EscapeDataString(id) + "/layout", null, false);

        /// <summary>Not auto-retried on uncertain outcomes: use <see cref="LayoutSync"/> recovery instead.</summary>
        public Awaitable<ApiResult<ApiLayoutSnapshot>> SaveLayout(string id, int expectedVersion, ServerLayout layout) =>
            Call<ApiLayoutSnapshot>("PUT", "/restaurants/" + Uri.EscapeDataString(id) + "/layout",
                JsonUtility.ToJson(new SaveLayoutBody { expectedVersion = expectedVersion, layout = layout }), false,
                retryUncertain: false);

        // ---- economy

        public Awaitable<ApiResult<ApiPurchase>> Purchase(string itemId) =>
            Call<ApiPurchase>("POST", "/purchases", JsonUtility.ToJson(new PurchaseBody { itemId = itemId }), true);

        public Awaitable<ApiResult<ApiDailyEligibility>> GetDailyReward() =>
            Call<ApiDailyEligibility>("GET", "/rewards/daily", null, false);

        public Awaitable<ApiResult<ApiDailyClaim>> ClaimDailyReward() =>
            Call<ApiDailyClaim>("POST", "/rewards/daily/claims", "{}", true);

        // ---- social

        public Awaitable<ApiResult<Page<ApiRestaurant>>> Discover(int limit = 20, string cursor = null) =>
            Call<Page<ApiRestaurant>>("GET", "/restaurants/discover" + Query(limit, cursor, null), null, false);

        public Awaitable<ApiResult<ApiVisit>> Visit(string restaurantId) =>
            Call<ApiVisit>("POST", "/restaurants/" + Uri.EscapeDataString(restaurantId) + "/visits", "{}", false);

        /// <summary>Re-sends a pending keyed operation with its original key and body.</summary>
        public Awaitable<ApiResult<T>> Resume<T>(PendingOperation op) => Call<T>(op.method, op.path, op.body, true);

        // ---- core

        private async Awaitable<ApiResult<T>> Call<T>(string method, string path, string body, bool keyed,
            bool retryUncertain = true)
        {
            if (!_auth.IsSignedIn)
                return new ApiResult<T> { Status = 401, Code = "UNAUTHENTICATED", Action = ApiAction.Fail };

            PendingOperation op = keyed ? Pending.Begin(method, path, body, DateTime.UtcNow) : null;
            bool refreshed = false, bootstrapped = false;
            int attempts = 0;

            while (true)
            {
                attempts++;
                if (_auth.AccessTokenExpiresSoon() && !refreshed)
                {
                    refreshed = true;
                    await _auth.Refresh();
                }

                var http = await _http.Send(method, _http.Config.GameBase + path, body, _auth.AccessToken, op?.key);
                if (http.IsSuccess)
                {
                    if (op != null) _pending.Complete(op.key);
                    return Success<T>(http);
                }

                var result = Failure<T>(http);
                switch (result.Action)
                {
                    case ApiAction.RefreshSession when !refreshed:
                        refreshed = true;
                        if ((await _auth.Refresh()).Ok) continue;
                        break;
                    case ApiAction.Bootstrap when !bootstrapped && path != "/players/bootstrap":
                        bootstrapped = true;
                        if ((await Bootstrap()).Ok) continue;
                        break;
                    case ApiAction.RetryAfter when RetryBackoff.ShouldRetry(attempts):
                        await Awaitable.WaitForSecondsAsync((float)RetryBackoff.ParseRetryAfter(http.RetryAfter));
                        continue;
                    case ApiAction.Backoff when retryUncertain && RetryBackoff.ShouldRetry(attempts):
                        await Awaitable.WaitForSecondsAsync((float)RetryBackoff.DelaySeconds(attempts, _random.NextDouble()));
                        continue;
                }

                // A definite answer ends the operation; an uncertain one keeps its key for a later Resume.
                if (op != null && !result.Uncertain) _pending.Complete(op.key);
                Debug.LogWarning($"Game API {method} {StripQuery(path)} failed: {result.Status} {result.Code} (request {result.RequestId})");
                return result;
            }
        }

        private static ApiResult<T> Success<T>(HttpResult http)
        {
            try
            {
                var env = JsonUtility.FromJson<Envelope<T>>(http.Body);
                return new ApiResult<T> { Ok = true, Data = env.data, Status = http.Status, RequestId = env.requestId };
            }
            catch (Exception)
            {
                // A 2xx we can't read: the operation happened, so treat it as uncertain and reload state.
                return new ApiResult<T> { Status = http.Status, Code = "UNREADABLE_RESPONSE", Action = ApiAction.Reload };
            }
        }

        private static ApiResult<T> Failure<T>(HttpResult http)
        {
            ApiErrorEnvelope env = null;
            try
            {
                if (!string.IsNullOrEmpty(http.Body) && http.Body.TrimStart().StartsWith("{"))
                    env = JsonUtility.FromJson<ApiErrorEnvelope>(http.Body);
            }
            catch (Exception) { } // gateway failures may return non-JSON content

            string code = env?.error?.code;
            return new ApiResult<T>
            {
                Status = http.Status,
                Code = code,
                Details = env?.error?.details,
                RequestId = env?.requestId,
                Action = ApiErrorPolicy.Classify(http.Status, code),
            };
        }

        private static string Query(int limit, string cursor, string category)
        {
            string q = "?limit=" + limit;
            if (!string.IsNullOrEmpty(category)) q += "&category=" + Uri.EscapeDataString(category);
            if (!string.IsNullOrEmpty(cursor)) q += "&cursor=" + Uri.EscapeDataString(cursor);
            return q;
        }

        private static string StripQuery(string path)
        {
            int i = path.IndexOf('?');
            return i < 0 ? path : path.Substring(0, i);
        }

        // Pending keys belong to one account: a different player never inherits them.
        private void LoadPendingFor(string userId)
        {
            if (userId == _pendingOwner) return;
            _pendingOwner = userId;
            var list = string.IsNullOrEmpty(userId) ? null
                : JsonUtility.FromJson<PendingOperationList>(PlayerPrefs.GetString(PendingPrefsKey + userId, "{}"));
            _pending.Restore(list);
        }

        private void SavePending()
        {
            if (string.IsNullOrEmpty(_pendingOwner)) return;
            PlayerPrefs.SetString(PendingPrefsKey + _pendingOwner, JsonUtility.ToJson(_pending.ToList()));
            PlayerPrefs.Save();
        }
    }
}
