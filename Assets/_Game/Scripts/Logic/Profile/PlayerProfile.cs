using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Profile
{
    /// <summary>Sign-in providers a guest account can be linked to.</summary>
    public enum AccountProvider
    {
        GooglePlayGames,
        GameCenter,
        SignInWithApple,
    }

    /// <summary>
    /// Who the player is: a stable player id (guest until linked), display name, avatar and linked accounts.
    /// Names and looks are client-side while offline and go through the offline ledger; the server re-checks
    /// names (its own word filter) and owns account links once online.
    /// </summary>
    public sealed class PlayerProfile
    {
        private readonly List<AccountProvider> _linked = new List<AccountProvider>();

        public string PlayerId { get; private set; }
        public string DisplayName { get; private set; }
        public AvatarLook Avatar { get; private set; }
        /// <summary>When the name was last changed by the player (null = never; the first rename is free).</summary>
        public DateTime? LastRenameUtc { get; private set; }
        public IReadOnlyList<AccountProvider> LinkedAccounts => _linked;
        public bool IsGuest => _linked.Count == 0;

        public event Action Changed;

        public PlayerProfile(string playerId, string displayName, AvatarLook avatar)
        {
            PlayerId = string.IsNullOrEmpty(playerId) ? Guid.NewGuid().ToString("N") : playerId;
            DisplayName = NameRules.Normalize(displayName);
            Avatar = avatar ?? throw new ArgumentNullException(nameof(avatar));
        }

        /// <summary>When the player may rename next (null = now).</summary>
        public DateTime? NextRenameUtc(TimeSpan cooldown) =>
            LastRenameUtc.HasValue && cooldown > TimeSpan.Zero ? LastRenameUtc.Value + cooldown : (DateTime?)null;

        public NameCheck CheckRename(string name, DateTime nowUtc, TimeSpan cooldown)
        {
            string n = NameRules.Normalize(name);
            if (n == DisplayName) return NameCheck.Unchanged;
            var check = NameRules.Validate(n);
            if (check != NameCheck.Ok) return check;
            var next = NextRenameUtc(cooldown);
            if (next.HasValue && nowUtc < next.Value) return NameCheck.Cooldown;
            return NameCheck.Ok;
        }

        public NameCheck TryRename(string name, DateTime nowUtc, TimeSpan cooldown)
        {
            var check = CheckRename(name, nowUtc, cooldown);
            if (check != NameCheck.Ok) return check;
            DisplayName = NameRules.Normalize(name);
            LastRenameUtc = nowUtc;
            Changed?.Invoke();
            return NameCheck.Ok;
        }

        public void SetAvatar(AvatarLook look)
        {
            Avatar = look ?? throw new ArgumentNullException(nameof(look));
            Changed?.Invoke();
        }

        /// <summary>Records a successful link reported by the platform/backend.</summary>
        public void MarkLinked(AccountProvider provider)
        {
            if (_linked.Contains(provider)) return;
            _linked.Add(provider);
            Changed?.Invoke();
        }

        public void Restore(string playerId, string displayName, DateTime? lastRenameUtc, IEnumerable<AccountProvider> linked)
        {
            if (!string.IsNullOrEmpty(playerId)) PlayerId = playerId;
            if (!string.IsNullOrEmpty(displayName)) DisplayName = NameRules.Normalize(displayName);
            LastRenameUtc = lastRenameUtc;
            _linked.Clear();
            if (linked != null) _linked.AddRange(linked);
        }
    }
}
