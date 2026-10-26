using System;
using UnityEngine;

namespace DemoRestaurant.Game.Online
{
    public enum AuthStatus { Ok, InvalidCredentials, EmailNotVerified, AlreadyRegistered, WeakPassword, RateLimited, Network, Failed }

    public readonly struct AuthResult
    {
        public readonly AuthStatus Status;
        /// <summary>Server error code for logs/support (never shown raw to players).</summary>
        public readonly string Code;
        /// <summary>Server's explanation, for the log only.</summary>
        public readonly string Message;

        public AuthResult(AuthStatus status, string code = null, string message = null)
        {
            Status = status;
            Code = code;
            Message = message;
        }

        public bool Ok => Status == AuthStatus.Ok;
    }

    /// <summary>
    /// Supabase Auth (email + password): sign-up, sign-in, refresh, sign-out, password recovery.
    /// Separate from the game routes; the access token it holds is what the game API sends as Bearer.
    /// </summary>
    public sealed class SupabaseAuth
    {
        // TODO(launch): move the refresh token to Keychain (iOS) / Keystore-backed storage (Android).
        private const string PrefsKey = "dr_auth_session";

        [Serializable]
        private sealed class Credentials { public string email; public string password; }

        [Serializable]
        private sealed class RefreshBody { public string refresh_token; }

        [Serializable]
        private sealed class EmailBody { public string email; }

        [Serializable]
        private sealed class UserDto { public string id; public string email; public string email_confirmed_at; }

        [Serializable]
        private sealed class SessionDto
        {
            public string access_token;
            public string refresh_token;
            public long expires_at;
            public int expires_in;
            public UserDto user;
        }

        [Serializable]
        private sealed class ErrorDto { public string error; public string error_code; public string msg; public string message; public string error_description; }

        [Serializable]
        private sealed class StoredSession
        {
            public string accessToken;
            public string refreshToken;
            public long expiresAtUnix;
            public string userId;
            public string email;
            public bool emailVerified;
        }

        private readonly SupabaseHttp _http;
        private StoredSession _session;

        public SupabaseAuth(SupabaseHttp http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _session = LoadStored();
        }

        public bool IsSignedIn => _session != null && !string.IsNullOrEmpty(_session.refreshToken);
        public bool IsEmailVerified => _session != null && _session.emailVerified;
        public string Email => _session?.email;
        public string UserId => _session?.userId;
        public string AccessToken => _session?.accessToken;

        /// <summary>Raised when the session is gone (signed out or refresh rejected): return to sign-in.</summary>
        public event Action SignedOut;

        public bool AccessTokenExpiresSoon(int marginSeconds = 60) =>
            _session == null || DateTimeOffset.UtcNow.ToUnixTimeSeconds() + marginSeconds >= _session.expiresAtUnix;

        public async Awaitable<AuthResult> SignUp(string email, string password)
        {
            var r = await _http.Send("POST", _http.Config.AuthBase + "/signup",
                JsonUtility.ToJson(new Credentials { email = email, password = password }), null);
            if (!r.IsSuccess) return Log("sign-up", r, Fail(r));
            // With email confirmation on, no session is returned until the link is opened.
            var session = Parse(r.Body);
            if (session != null && !string.IsNullOrEmpty(session.access_token)) Store(session);
            return Log("sign-up", r, new AuthResult(AuthStatus.Ok));
        }

        public async Awaitable<AuthResult> SignIn(string email, string password)
        {
            var r = await _http.Send("POST", _http.Config.AuthBase + "/token?grant_type=password",
                JsonUtility.ToJson(new Credentials { email = email, password = password }), null);
            if (!r.IsSuccess) return Log("sign-in", r, Fail(r));
            Store(Parse(r.Body));
            return Log("sign-in", r, IsEmailVerified ? new AuthResult(AuthStatus.Ok) : new AuthResult(AuthStatus.EmailNotVerified));
        }

        /// <summary>Exchanges the refresh token. A definite rejection signs the player out.</summary>
        public async Awaitable<AuthResult> Refresh()
        {
            if (!IsSignedIn) return new AuthResult(AuthStatus.InvalidCredentials);
            var r = await _http.Send("POST", _http.Config.AuthBase + "/token?grant_type=refresh_token",
                JsonUtility.ToJson(new RefreshBody { refresh_token = _session.refreshToken }), null);
            if (r.IsSuccess)
            {
                Store(Parse(r.Body));
                return Log("refresh", r, new AuthResult(AuthStatus.Ok));
            }
            var fail = Log("refresh", r, Fail(r));
            // Only a definite rejection of the refresh token ends the session; outages and 5xx keep it.
            if (fail.Status == AuthStatus.InvalidCredentials && r.Status >= 400 && r.Status < 500) ClearLocal();
            return fail;
        }

        public async Awaitable SignOut()
        {
            if (IsSignedIn)
                await _http.Send("POST", _http.Config.AuthBase + "/logout", "{}", _session.accessToken);
            ClearLocal();
        }

        /// <summary>Sends the password-reset email. The app redirect URL is configured in the Supabase dashboard.</summary>
        public async Awaitable<AuthResult> RecoverPassword(string email)
        {
            var r = await _http.Send("POST", _http.Config.AuthBase + "/recover",
                JsonUtility.ToJson(new EmailBody { email = email }), null);
            return Log("recover", r, r.IsSuccess ? new AuthResult(AuthStatus.Ok) : Fail(r));
        }

        [Serializable]
        private sealed class ResendBody { public string type; public string email; }

        /// <summary>Sends the sign-up confirmation email again.</summary>
        public async Awaitable<AuthResult> ResendVerification(string email)
        {
            var r = await _http.Send("POST", _http.Config.AuthBase + "/resend",
                JsonUtility.ToJson(new ResendBody { type = "signup", email = email }), null);
            return Log("resend", r, r.IsSuccess ? new AuthResult(AuthStatus.Ok) : Fail(r));
        }

        private static SessionDto Parse(string body)
        {
            try { return string.IsNullOrEmpty(body) ? null : JsonUtility.FromJson<SessionDto>(body); }
            catch (Exception) { return null; }
        }

        private void Store(SessionDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.access_token)) return;
            long expires = dto.expires_at > 0 ? dto.expires_at
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(60, dto.expires_in);
            _session = new StoredSession
            {
                accessToken = dto.access_token,
                refreshToken = dto.refresh_token,
                expiresAtUnix = expires,
                userId = dto.user?.id,
                email = dto.user?.email,
                emailVerified = !string.IsNullOrEmpty(dto.user?.email_confirmed_at),
            };
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(_session));
            PlayerPrefs.Save();
        }

        private void ClearLocal()
        {
            bool had = _session != null;
            _session = null;
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            if (had) SignedOut?.Invoke();
        }

        private static StoredSession LoadStored()
        {
            string json = PlayerPrefs.GetString(PrefsKey, null);
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<StoredSession>(json); }
            catch (Exception) { return null; }
        }

        /// <summary>One line per auth call for debugging: operation, outcome, HTTP status and error code. Never the email,
        /// password or tokens.</summary>
        private static AuthResult Log(string operation, HttpResult r, AuthResult result)
        {
            if (result.Ok) Debug.Log($"Auth {operation}: ok (HTTP {r.Status})");
            else Debug.LogWarning($"Auth {operation}: {result.Status} (HTTP {r.Status}, code {result.Code ?? "none"})" +
                                  (string.IsNullOrEmpty(result.Message) ? "" : $": {result.Message}"));
            return result;
        }

        private static AuthResult Fail(HttpResult r)
        {
            ErrorDto e = null;
            try
            {
                if (!string.IsNullOrEmpty(r.Body) && r.Body.TrimStart().StartsWith("{"))
                    e = JsonUtility.FromJson<ErrorDto>(r.Body);
            }
            catch (Exception) { }
            string code = NullIfEmpty(e?.error_code) ?? NullIfEmpty(e?.error);
            string message = NullIfEmpty(e?.msg) ?? NullIfEmpty(e?.message) ?? NullIfEmpty(e?.error_description);

            if (r.Status == 0) return new AuthResult(AuthStatus.Network, code, message);
            if (r.Status == 429 || code == "over_email_send_rate_limit" || code == "over_request_rate_limit")
                return new AuthResult(AuthStatus.RateLimited, code, message);
            if (r.Status >= 500) return new AuthResult(AuthStatus.Failed, code, message);
            switch (code)
            {
                case "email_not_confirmed": return new AuthResult(AuthStatus.EmailNotVerified, code, message);
                case "user_already_exists":
                case "email_exists": return new AuthResult(AuthStatus.AlreadyRegistered, code, message);
                case "weak_password": return new AuthResult(AuthStatus.WeakPassword, code, message);
                case "invalid_grant":
                case "invalid_credentials":
                case "refresh_token_not_found":
                case "refresh_token_already_used":
                case "session_not_found": return new AuthResult(AuthStatus.InvalidCredentials, code, message);
            }
            // A bad project key is a setup problem, not the player's password.
            if (r.Status == 401 && code == null) return new AuthResult(AuthStatus.Failed, "invalid_api_key", message);
            return new AuthResult(r.Status == 400 || r.Status == 401 ? AuthStatus.InvalidCredentials : AuthStatus.Failed, code, message);
        }

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
