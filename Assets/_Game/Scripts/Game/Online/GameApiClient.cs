using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Online;
using UnityEngine;

namespace DemoRestaurant.Game.Online
{
    /// <summary>
    /// The v2 player API (<c>player-api/v1</c>, mobile-api-v2.md) with the contract's retry rules: refresh once on
    /// 401, bootstrap on PLAYER_NOT_INITIALIZED, Retry-After on 429, bounded jittered backoff on 5xx/network, and
    /// persisted Idempotency-Keys (same key + body on every retry, across restarts) for keyed single writes.
    /// Gameplay actions don't go through single writes: they are ledger entries (<see cref="SyncLedger"/>), each with
    /// its own key inside the body. Writes carry <c>X-Ruleset-Version</c> once the ruleset is known.
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

        /// <summary>The ruleset version the client validated against (sent on writes; 0 = not known yet).</summary>
        public int RulesetVersion { get; set; }

        /// <summary>The <c>X-Ruleset-Version</c> of the newest response: the player's pinned ruleset.</summary>
        public int ServerRulesetVersion { get; private set; }

        /// <summary>Keyed operations that never got a definite answer; retry them with <see cref="Resume"/>.</summary>
        public PendingOperationStore Pending
        {
            get { LoadPendingFor(_auth.UserId); return _pending; }
        }

        // ---- start-up and sync

        /// <summary>Creates the save once (idempotent) and returns the full state.</summary>
        public Awaitable<ApiJson> Bootstrap() => Call("POST", "/players/bootstrap", "{}", false);

        /// <summary>The ruleset (<c>{ rulesetId, version, etag, body }</c>); <see cref="ApiJson.NotModified"/> when
        /// <paramref name="etag"/> is still current.</summary>
        public Awaitable<ApiJson> GetConfig(string etag) =>
            Call("GET", "/config", null, false, headers: string.IsNullOrEmpty(etag) ? null : new Dictionary<string, string> { ["If-None-Match"] = etag });

        /// <summary>Replays queued actions (<c>{ entries }</c>, at most 200) and settles time away. Returns
        /// <c>{ state, settlement, results }</c>. Safe to resend: every entry carries its own key.</summary>
        public Awaitable<ApiJson> SyncLedger(JsonValue body) => Call("POST", "/sync/ledger", body.ToString(), false);

        public Awaitable<ApiJson> GetState() => Call("GET", "/state", null, false);

        // ---- profile, rewards and social (shapes unchanged from v1)

        public Awaitable<ApiJson> GetMe() => Call("GET", "/me", null, false);
        public Awaitable<ApiJson> GetDailyReward() => Call("GET", "/rewards/daily", null, false);
        public Awaitable<ApiJson> ClaimDailyReward() => Call("POST", "/rewards/daily/claims", "{}", true);

        public Awaitable<ApiJson> Discover(int limit = 20, string cursor = null) =>
            Call("GET", "/restaurants/discover?limit=" + limit + (string.IsNullOrEmpty(cursor) ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), null, false);

        public Awaitable<ApiJson> Visit(string restaurantId) =>
            Call("POST", "/restaurants/" + Uri.EscapeDataString(restaurantId) + "/visits", "{}", false);

        /// <summary>A friend's restaurant: layout and facade snapshots for a visit.</summary>
        public Awaitable<ApiJson> GetRestaurantLayout(string restaurantId) =>
            Call("GET", "/restaurants/" + Uri.EscapeDataString(restaurantId) + "/layout", null, false);

        public Awaitable<ApiJson> GetRestaurantExterior(string restaurantId) =>
            Call("GET", "/restaurants/" + Uri.EscapeDataString(restaurantId) + "/exterior", null, false);

        /// <summary>Re-sends a pending keyed operation with its original key and body.</summary>
        public Awaitable<ApiJson> Resume(PendingOperation op) => Call(op.method, op.path, op.body, true);

        // ---- core

        private async Awaitable<ApiJson> Call(string method, string path, string body, bool keyed,
            bool retryUncertain = true, IReadOnlyDictionary<string, string> headers = null)
        {
            if (!_auth.IsSignedIn)
                return new ApiJson { Status = 401, Code = "UNAUTHENTICATED", Action = ApiAction.Fail };

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

                var sent = WithRuleset(method, headers);
                var http = await _http.Send(method, _http.Config.GameBase + path, body, _auth.AccessToken, op?.key, sent);
                if (int.TryParse(http.RulesetVersion, out int pinned)) ServerRulesetVersion = pinned;
                if (http.Status == 304)
                    return new ApiJson { Ok = true, NotModified = true, Status = 304, ETag = http.ETag };
                if (http.IsSuccess)
                {
                    if (op != null) _pending.Complete(op.key);
                    return Success(http);
                }

                var result = Failure(http);
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

        private IReadOnlyDictionary<string, string> WithRuleset(string method, IReadOnlyDictionary<string, string> headers)
        {
            if (method == "GET" || RulesetVersion <= 0) return headers;
            var all = new Dictionary<string, string>();
            if (headers != null) foreach (var pair in headers) all[pair.Key] = pair.Value;
            all["X-Ruleset-Version"] = RulesetVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return all;
        }

        private static ApiJson Success(HttpResult http)
        {
            if (!JsonValue.TryParse(http.Body, out var envelope))
                // A 2xx we can't read: the operation happened, so treat it as uncertain and reload state.
                return new ApiJson { Status = http.Status, Code = "UNREADABLE_RESPONSE", Action = ApiAction.Reload };
            return new ApiJson
            {
                Ok = true, Data = envelope["data"], Status = http.Status, RequestId = envelope["requestId"].AsString(), ETag = http.ETag,
            };
        }

        private static ApiJson Failure(HttpResult http)
        {
            // Gateway failures may return non-JSON content.
            JsonValue.TryParse(http.Body, out var envelope);
            string code = envelope["error"]["code"].AsString();
            return new ApiJson
            {
                Status = http.Status,
                Code = code,
                Details = envelope["error"]["details"],
                RequestId = envelope["requestId"].AsString(),
                Action = ApiErrorPolicy.Classify(http.Status, code),
            };
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

    /// <summary>Result of one game API call: <see cref="Data"/> (the envelope's <c>data</c>) or an error with the
    /// action to take.</summary>
    public sealed class ApiJson
    {
        public bool Ok;
        public JsonValue Data = JsonValue.Missing;
        public int Status;
        public string Code;
        public JsonValue Details = JsonValue.Missing;
        public string RequestId;
        public string ETag;
        /// <summary>304: the cached copy (<c>/config</c>) is still current.</summary>
        public bool NotModified;
        public ApiAction Action;

        /// <summary>The outcome is unknown (network/5xx after all retries). Keyed operations stay pending.</summary>
        public bool Uncertain => !Ok && Action == ApiAction.Backoff;
    }
}
