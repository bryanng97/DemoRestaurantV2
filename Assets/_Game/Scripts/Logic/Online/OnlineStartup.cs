namespace DemoRestaurant.Logic.Online
{
    /// <summary>Where the online start-up is. The game is playable offline in every state.</summary>
    public enum OnlineState
    {
        /// <summary>No backend configured, or the player chose to play offline.</summary>
        Offline,
        /// <summary>Waiting for the player to sign in or create an account.</summary>
        SignedOut,
        /// <summary>Signed in but the email link has not been opened yet.</summary>
        NeedsVerification,
        /// <summary>Talking to the server (bootstrap, loading the restaurant).</summary>
        Connecting,
        /// <summary>Profile, wallet, inventory, restaurant and layout are loaded.</summary>
        Online,
        /// <summary>The server could not be reached; local play continues and the flow retries later.</summary>
        Unreachable,
    }

    /// <summary>The next start-up call (mobile-integration.md, "Recommended mobile implementation order").</summary>
    public enum StartupStep { None, ShowSignIn, ShowVerifyEmail, Bootstrap, LoadRestaurants, CreateRestaurant, LoadState, Done }

    /// <summary>What the client knows so far. Each completed call sets one flag.</summary>
    public struct StartupFacts
    {
        public bool Configured;
        public bool PlayOffline;
        public bool SignedIn;
        public bool EmailVerified;
        public bool Bootstrapped;
        public bool RestaurantsLoaded;
        public bool HasRestaurant;
        public bool StateLoaded;
    }

    public static class StartupPlanner
    {
        public static StartupStep Next(StartupFacts f)
        {
            if (!f.Configured || f.PlayOffline) return StartupStep.None;
            if (!f.SignedIn) return StartupStep.ShowSignIn;
            if (!f.EmailVerified) return StartupStep.ShowVerifyEmail;
            if (!f.Bootstrapped) return StartupStep.Bootstrap;
            if (!f.RestaurantsLoaded) return StartupStep.LoadRestaurants;
            if (!f.HasRestaurant) return StartupStep.CreateRestaurant;
            if (!f.StateLoaded) return StartupStep.LoadState;
            return StartupStep.Done;
        }

        public static OnlineState StateFor(StartupStep step)
        {
            switch (step)
            {
                case StartupStep.None: return OnlineState.Offline;
                case StartupStep.ShowSignIn: return OnlineState.SignedOut;
                case StartupStep.ShowVerifyEmail: return OnlineState.NeedsVerification;
                case StartupStep.Done: return OnlineState.Online;
                default: return OnlineState.Connecting;
            }
        }
    }

    public enum CredentialCheck { Ok, EmailInvalid, PasswordTooShort }

    /// <summary>Client-side checks before calling Supabase Auth (the server still decides).</summary>
    public static class CredentialRules
    {
        public const int MinPasswordLength = 8;

        public static CredentialCheck Check(string email, string password, bool newAccount)
        {
            if (!LooksLikeEmail(email)) return CredentialCheck.EmailInvalid;
            if (string.IsNullOrEmpty(password) || (newAccount && password.Length < MinPasswordLength))
                return CredentialCheck.PasswordTooShort;
            return CredentialCheck.Ok;
        }

        public static bool LooksLikeEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            email = email.Trim();
            int at = email.IndexOf('@');
            if (at <= 0 || at != email.LastIndexOf('@') || email.IndexOf(' ') >= 0) return false;
            int dot = email.LastIndexOf('.');
            return dot > at + 1 && dot < email.Length - 1;
        }
    }
}
