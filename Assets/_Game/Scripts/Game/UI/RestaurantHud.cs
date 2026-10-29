using System;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Profile;
using DemoRestaurant.Logic.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Top-left HUD in the concept's layout:
    /// - Profile card: the player's avatar, name and the restaurant level with an XP bar. Tapping it opens the
    ///   profile dialog.
    /// - A slim strip below: demand (popularity) as a bar and its value; tapping it switches to how often guests
    ///   arrive, and tapping again back to the bar. Pulses when demand rises, flashes red when it drops.
    /// </summary>
    public sealed class RestaurantHud
    {
        private static readonly Color XpBlue = new Color32(108, 182, 230, 255);
        private static readonly Color StarGold = new Color32(240, 182, 60, 255);

        private readonly RestaurantProgress _progress;
        private readonly RestaurantDemand _demand;
        private readonly CustomerArrivals _arrivals;
        private readonly PlayerProfile _profile;
        private readonly Func<Sprite> _portrait;
        private readonly Text _name, _level, _ratingTitle, _guests, _stars;
        private readonly Image _face;
        private readonly Image _xpFill, _ratingFill;
        private readonly RectTransform _avatar, _ratingCard;
        private float _shownXp, _shownRating, _xpPulse, _ratingPulse, _hurt;
        /// <summary>Tapping the strip switches between the bar and the arrival rate.</summary>
        private bool _showDetail;

        public RestaurantHud(RectTransform root, PlayerProfile profile, Func<Sprite> portrait, Action onProfileTap, Sprite goalIcon,
            RestaurantProgress progress, RestaurantDemand demand, CustomerArrivals arrivals)
        {
            _profile = profile;
            _portrait = portrait;
            _progress = progress;
            _demand = demand;
            _arrivals = arrivals;

            // Profile card, with the round avatar overlapping its left end.
            var card = UiKit.ConceptCard(root, "Profile");
            UiKit.Place(card, 104, 26, 300, 96);
            UiKit.AddButton(card, onProfileTap);
            var name = _name = UiKit.Label(card, "Name", 52, 8, 236, 42, 30, TextAnchor.MiddleLeft, UiKit.DarkText);
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 18;
            name.resizeTextMaxSize = 30;
            _level = UiKit.Label(card, "Level", 52, 52, 96, 34, 26, TextAnchor.MiddleLeft, UiKit.DarkText);
            _xpFill = UiKit.Bar(card, "Xp", 148, 56, 136, 26, XpBlue, UiKit.CardRimSoft);

            _avatar = UiKit.Place(UiKit.NewRect("Avatar", root), 16, 12, 124, 124);
            var ring = _avatar.gameObject.AddComponent<Image>();
            ring.sprite = UiArt.Circle();
            ring.color = UiKit.CardRimSoft;
            ring.raycastTarget = true;
            UiKit.AddButton(_avatar, onProfileTap);
            var face = UiKit.NewRect("Face", _avatar);
            UiKit.Stretch(face, 5, 5, 5, 5);
            var disc = face.gameObject.AddComponent<Image>();
            disc.sprite = UiArt.Circle();
            disc.color = UiKit.GoldFill;
            disc.raycastTarget = false;
            face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _face = UiKit.AddImage(UiKit.NewRect("Portrait", face), null);
            UiKit.Stretch(_face.rectTransform);

            // Demand: one slim strip under the profile card (icon, bar, value). Tap to switch between the bar and how often guests come.
            _ratingCard = UiKit.ConceptCard(root, "Rating");
            UiKit.Place(_ratingCard, 20, 146, 300, 50);
            UiKit.AddButton(_ratingCard, () => _showDetail = !_showDetail);
            UiKit.AddImage(UiKit.Place(UiKit.NewRect("Icon", _ratingCard), 10, 5, 34, 40), goalIcon);
            _ratingFill = UiKit.Bar(_ratingCard, "Bar", 50, 13, 150, 24, StarGold, UiKit.CardRimSoft);
            _ratingTitle = UiKit.Label(_ratingCard, "Title", 54, 13, 142, 24, 16, TextAnchor.MiddleCenter, UiKit.DarkText);
            _stars = UiKit.Label(_ratingCard, "Stars", 206, 10, 72, 30, 22, TextAnchor.MiddleLeft, UiKit.DarkText);
            _guests = UiKit.Label(_ratingCard, "Guests", 50, 4, 228, 42, 18, TextAnchor.MiddleLeft, UiKit.DarkText);
            _guests.resizeTextForBestFit = true; _guests.resizeTextMinSize = 12; _guests.resizeTextMaxSize = 18;

            progress.XpGained += _ => _xpPulse = 1f;
            progress.LevelUp += (_, __) => { _xpPulse = 1f; _shownXp = 0f; };
            demand.Changed += delta => { if (delta > 0) _ratingPulse = 1f; else _hurt = 1f; };
            _shownXp = progress.Progress;
            _shownRating = RatingFill;

            profile.Changed += RefreshProfile;
            RefreshProfile();
        }

        private float RatingFill => _demand.Fill;

        public void RefreshProfile()
        {
            _name.text = _profile.DisplayName;
            _face.sprite = _portrait();
            _face.enabled = _face.sprite != null;
        }

        public void Update(float dt)
        {
            _shownXp = Mathf.MoveTowards(_shownXp, _progress.Progress, dt * 1.5f);
            _shownRating = Mathf.MoveTowards(_shownRating, RatingFill, dt * 1.5f);
            _xpFill.fillAmount = _shownXp;
            _ratingFill.fillAmount = _shownRating;

            _xpPulse = Mathf.Max(0f, _xpPulse - dt * 3f);
            _ratingPulse = Mathf.Max(0f, _ratingPulse - dt * 3f);
            _hurt = Mathf.Max(0f, _hurt - dt * 2f);
            float a = 1f + 0.08f * Mathf.Sin(_xpPulse * Mathf.PI);
            _avatar.localScale = new Vector3(a, a, 1f);
            float p = 1f + 0.04f * Mathf.Sin(_ratingPulse * Mathf.PI);
            _ratingCard.localScale = new Vector3(p, p, 1f);
            _ratingFill.color = Color.Lerp(StarGold, UiKit.Red, _hurt);

            _level.text = Loc.Format("ui.hud.level", _progress.Level);
            bool detail = _showDetail;
            _ratingFill.transform.parent.gameObject.SetActive(!detail);
            _ratingTitle.enabled = _stars.enabled = !detail;
            _guests.enabled = detail;
            _ratingTitle.text = Loc.Get("ui.hud.demand");
            _guests.text = _arrivals.DailyCapReached ? Loc.Get("ui.hud.guests_capped")
                : Loc.Format("ui.hud.guests", _arrivals.IntervalSeconds.ToString("0.0"));
            _stars.text = _demand.Display.ToString("0.0");
        }
    }
}
