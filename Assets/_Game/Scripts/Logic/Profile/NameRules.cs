using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DemoRestaurant.Logic.Profile
{
    public enum NameCheck
    {
        Ok,
        TooShort,
        TooLong,
        InvalidCharacters,
        NotAllowed,
        /// <summary>Renamed too recently (see the cooldown).</summary>
        Cooldown,
        Unchanged,
    }

    /// <summary>
    /// Display-name rules: 3-16 characters after trimming; letters of any script (Malay, Chinese, English...),
    /// digits, spaces and a few separators. A small local word list catches obvious abuse offline; the server's
    /// filter is the real gate once names sync.
    /// </summary>
    public static class NameRules
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        private static readonly string[] Blocked = { "admin", "moderator", "nomuofficial", "fuck", "shit", "bitch", "cibai", "babi", "pukimak" };

        /// <summary>Trims, collapses runs of spaces, and normalises to NFC.</summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder();
            bool space = false;
            foreach (char ch in name.Trim().Normalize(NormalizationForm.FormC))
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!space) sb.Append(' ');
                    space = true;
                }
                else
                {
                    sb.Append(ch);
                    space = false;
                }
            }
            return sb.ToString();
        }

        /// <summary>Length in user-visible characters (a Chinese character or an emoji counts as one).</summary>
        public static int VisibleLength(string s) => new StringInfo(s ?? "").LengthInTextElements;

        public static NameCheck Validate(string name)
        {
            string n = Normalize(name);
            int len = VisibleLength(n);
            if (len < MinLength) return NameCheck.TooShort;
            if (len > MaxLength) return NameCheck.TooLong;
            foreach (char ch in n)
            {
                if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '\'')
                    continue;
                var cat = char.GetUnicodeCategory(ch);
                // Combining marks are part of some scripts' letters.
                if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.SpacingCombiningMark)
                    continue;
                return NameCheck.InvalidCharacters;
            }
            string squashed = n.Replace(" ", "").Replace("_", "").Replace("-", "").Replace(".", "").ToLowerInvariant();
            foreach (var word in Blocked)
                if (squashed.Contains(word))
                    return NameCheck.NotAllowed;
            return NameCheck.Ok;
        }
    }
}
