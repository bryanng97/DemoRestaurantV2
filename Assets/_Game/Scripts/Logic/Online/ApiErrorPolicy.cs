using System;

namespace DemoRestaurant.Logic.Online
{
    /// <summary>What the client does after a failed game API call (mobile-integration.md, "Retry behavior").</summary>
    public enum ApiAction
    {
        /// <summary>Show the failure; retrying the same request can't help.</summary>
        Fail,
        /// <summary>Refresh the session once and retry; a second 401 returns to sign-in.</summary>
        RefreshSession,
        /// <summary>The player must verify their email first.</summary>
        VerifyEmail,
        /// <summary>Call bootstrap, then retry.</summary>
        Bootstrap,
        /// <summary>Resource is missing or not ours.</summary>
        NotFound,
        /// <summary>Server state moved on (layout version, existing restaurant, funds): reload, then decide.</summary>
        Reload,
        /// <summary>Wait the server's Retry-After, then retry with the same key/body.</summary>
        RetryAfter,
        /// <summary>Uncertain outcome (5xx, network): bounded backoff, same key/body.</summary>
        Backoff,
    }

    public static class ApiErrorPolicy
    {
        /// <summary>
        /// Decides from HTTP status and error code only, never the message text.
        /// <paramref name="status"/> 0 means the request never got a response (network failure, timeout).
        /// </summary>
        public static ApiAction Classify(int status, string code)
        {
            if (status == 0 || status >= 500) return ApiAction.Backoff;
            if (status == 429) return ApiAction.RetryAfter;
            if (status == 401) return ApiAction.RefreshSession;
            if (status == 403) return code == "EMAIL_NOT_VERIFIED" ? ApiAction.VerifyEmail : ApiAction.Fail;
            if (status == 404) return ApiAction.NotFound;
            if (status == 409)
            {
                switch (code)
                {
                    case "PLAYER_NOT_INITIALIZED": return ApiAction.Bootstrap;
                    case "RESTAURANT_LIMIT_REACHED":
                    case "LAYOUT_VERSION_CONFLICT":
                    case "ITEM_NOT_OWNED":
                    case "INSUFFICIENT_FUNDS":
                    case "ITEM_NOT_AVAILABLE":
                    case "REWARD_NOT_READY":
                        return ApiAction.Reload;
                    default: return ApiAction.Fail; // IDEMPOTENCY_KEY_REUSED, SELF_VISIT_NOT_ALLOWED, limit codes
                }
            }
            return ApiAction.Fail; // 400, 413, 415 and anything unexpected
        }
    }

    /// <summary>Bounded exponential backoff with full jitter for uncertain outcomes.</summary>
    public static class RetryBackoff
    {
        public const int MaxAttempts = 4;
        public const double BaseSeconds = 1;
        public const double CapSeconds = 20;

        /// <summary>Delay before retry number <paramref name="attempt"/> (1-based). <paramref name="random01"/> in [0,1).</summary>
        public static double DelaySeconds(int attempt, double random01)
        {
            if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
            double ceiling = Math.Min(CapSeconds, BaseSeconds * Math.Pow(2, attempt - 1));
            return ceiling * Math.Max(0, Math.Min(1, random01));
        }

        public static bool ShouldRetry(int attemptsSoFar) => attemptsSoFar < MaxAttempts;

        /// <summary>Retry-After header in seconds; falls back to <paramref name="fallback"/> when missing or bad.</summary>
        public static double ParseRetryAfter(string header, double fallback = 5)
        {
            return int.TryParse(header, out int s) && s >= 0 ? Math.Min(s, 300) : fallback;
        }
    }
}
