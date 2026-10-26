using System;
using System.Collections.Generic;
using System.Globalization;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Economy;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Player-facing earnings display: coin balance in the top bar (counts up smoothly), the restaurant's
    /// coins-per-minute and customers served, and "+N" coin pop-ups over customers when they pay.
    /// Reads <see cref="Wallet"/> and <see cref="EarningsTracker"/>; holds no economy state of its own.
    /// </summary>
    public sealed class EarningsHud : MonoBehaviour
    {
        private const float PopupSeconds = 1.2f;
        private const float PopupRise = 90f;   // canvas pixels
        private const int PopupPoolSize = 48;

        private static readonly Color Gold = new Color(1f, 0.85f, 0.3f);

        private Wallet _wallet;
        private EarningsTracker _tracker;
        private Func<float> _clock;
        private Camera _camera;
        private Sprite _coin;

        private RectTransform _canvasRect;
        private Text _balance, _rate;
        private RectTransform _coinIcon;
        private double _shownCoins;
        private double _lastTarget;
        private float _pulse;
        private float _rateRefresh;

        private readonly List<Popup> _popups = new List<Popup>();
        private readonly Stack<Popup> _freePopups = new Stack<Popup>();

        public void Bind(Wallet wallet, EarningsTracker tracker, Func<float> clock, Camera worldCamera, Sprite coin)
        {
            _wallet = wallet;
            _tracker = tracker;
            _clock = clock;
            _camera = worldCamera;
            _coin = coin;
            _shownCoins = _lastTarget = wallet.DisplayCoins;

            BuildCanvas();
            for (int i = 0; i < PopupPoolSize; i++)
                _freePopups.Push(CreatePopup());

            Loc.LanguageChanged += RefreshRate;
            RefreshRate();
        }

        private void OnDestroy() => Loc.LanguageChanged -= RefreshRate;

        /// <summary>Shows "+coins" rising from a world position (e.g. above the paying customer).</summary>
        public void ShowEarned(Vector3 worldPosition, int coins)
        {
            if (_freePopups.Count == 0)
                return; // a busy moment; dropping a pop-up beats allocating mid-frame
            var p = _freePopups.Pop();
            p.World = worldPosition;
            p.Age = 0f;
            p.Label.text = "+" + coins.ToString(CultureInfo.InvariantCulture);
            p.Label.color = Gold;
            p.Icon.SetActive(true);
            p.Label.rectTransform.offsetMin = new Vector2(54f, 0f);
            p.Label.alignment = TextAnchor.MiddleLeft;
            p.Root.gameObject.SetActive(true);
            _popups.Add(p);
        }

        /// <summary>Shows a short text rising from a world position (e.g. "Too slow!" over an angry customer).</summary>
        public void ShowText(Vector3 worldPosition, string text, Color color)
        {
            if (_freePopups.Count == 0)
                return;
            var p = _freePopups.Pop();
            p.World = worldPosition;
            p.Age = 0f;
            p.Label.text = text;
            p.Label.color = color;
            p.Icon.SetActive(false);
            p.Label.rectTransform.offsetMin = new Vector2(-120f, 0f);
            p.Label.alignment = TextAnchor.MiddleCenter;
            p.Root.gameObject.SetActive(true);
            _popups.Add(p);
        }

        private void LateUpdate()
        {
            if (_wallet == null)
                return;
            float dt = Time.unscaledDeltaTime;

            // Balance counts toward the wallet value; faster when far behind.
            double target = _wallet.DisplayCoins;
            if (target > _lastTarget)
                _pulse = 1f; // new money: bump the coin once
            _lastTarget = target;
            if (Math.Abs(target - _shownCoins) > 0.5)
            {
                double step = Math.Max(20.0, Math.Abs(target - _shownCoins) * 6.0) * dt;
                _shownCoins = target > _shownCoins ? Math.Min(target, _shownCoins + step) : Math.Max(target, _shownCoins - step);
            }
            else
            {
                _shownCoins = target;
            }
            _balance.text = ((long)Math.Round(_shownCoins)).ToString("N0", CultureInfo.InvariantCulture);

            _pulse = Mathf.Max(0f, _pulse - dt * 4f);
            float s = 1f + 0.18f * Mathf.Sin(_pulse * Mathf.PI);
            _coinIcon.localScale = new Vector3(s, s, 1f);

            _rateRefresh -= dt;
            if (_rateRefresh <= 0f)
            {
                _rateRefresh = 0.5f;
                RefreshRate();
            }

            UpdatePopups(dt);
        }

        private void RefreshRate()
        {
            if (_rate == null)
                return;
            int perMinute = Mathf.RoundToInt(_tracker.CoinsPerMinute(_clock()));
            _rate.text = Loc.Format("hud.coins_per_minute", perMinute.ToString("N0", CultureInfo.InvariantCulture)) +
                         "   " + Loc.Format("hud.customers_served", _tracker.CustomersServed);
        }

        private void UpdatePopups(float dt)
        {
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                p.Age += dt;
                float t = p.Age / PopupSeconds;
                if (t >= 1f)
                {
                    p.Root.gameObject.SetActive(false);
                    _popups.RemoveAt(i);
                    _freePopups.Push(p);
                    continue;
                }

                Vector3 screen = _camera.WorldToScreenPoint(p.World);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out Vector2 local);
                float rise = PopupRise * (1f - (1f - t) * (1f - t)); // ease-out
                p.Root.anchoredPosition = local + new Vector2(0, rise);

                float pop = t < 0.15f ? Mathf.Lerp(0.6f, 1.15f, t / 0.15f) : Mathf.Lerp(1.15f, 1f, Mathf.Min(1f, (t - 0.15f) / 0.15f));
                p.Root.localScale = new Vector3(pop, pop, 1f);
                p.Group.alpha = t < 0.65f ? 1f : 1f - (t - 0.65f) / 0.35f;
            }
        }

        // ------------------------------------------------------------ construction

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("EarningsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // landscape: scale by height
            _canvasRect = (RectTransform)canvasGo.transform;
            // Coin pill and rate sit inside the safe area; pop-ups follow customers, so they use the full canvas.
            var safe = SafeArea.Create(_canvasRect);

            // Popups first so the top bar draws over them.
            var popupLayer = NewRect("Popups", _canvasRect);
            Stretch(popupLayer);
            _popupLayer = popupLayer;

            // Top-right coin pill.
            var pill = NewRect("CoinPill", safe);
            pill.anchorMin = pill.anchorMax = new Vector2(1f, 1f);
            pill.pivot = new Vector2(1f, 1f);
            pill.anchoredPosition = new Vector2(-124f, -28f);
            pill.sizeDelta = new Vector2(330f, 76f);
            var pillImage = pill.gameObject.AddComponent<Image>();
            pillImage.sprite = UiArt.Pill();
            pillImage.type = Image.Type.Sliced;
            pillImage.raycastTarget = false;

            _coinIcon = NewRect("Coin", pill);
            _coinIcon.anchorMin = _coinIcon.anchorMax = new Vector2(0f, 0.5f);
            _coinIcon.anchoredPosition = new Vector2(10f, 0f);
            _coinIcon.sizeDelta = new Vector2(92f, 92f);
            AddImage(_coinIcon, _coin, Gold);

            _balance = AddText(NewRect("Balance", pill), 44, TextAnchor.MiddleRight, new Color32(78, 48, 28, 255));
            Destroy(_balance.GetComponent<Outline>()); // dark text on the cream pill needs no outline
            var balanceRect = _balance.rectTransform;
            Stretch(balanceRect);
            balanceRect.offsetMin = new Vector2(70f, 0f);
            balanceRect.offsetMax = new Vector2(-28f, 0f);

            _rate = AddText(NewRect("Rate", safe), 28, TextAnchor.UpperRight, new Color(1f, 0.97f, 0.88f));
            var rateRect = _rate.rectTransform;
            rateRect.anchorMin = rateRect.anchorMax = new Vector2(1f, 1f);
            rateRect.pivot = new Vector2(1f, 1f);
            rateRect.anchoredPosition = new Vector2(-134f, -112f);
            rateRect.sizeDelta = new Vector2(600f, 40f);
        }

        private RectTransform _popupLayer;

        private Popup CreatePopup()
        {
            var root = NewRect("CoinPopup", _popupLayer);
            root.sizeDelta = new Vector2(160f, 48f);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var icon = NewRect("Coin", root);
            icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
            icon.anchoredPosition = new Vector2(28f, 0f);
            icon.sizeDelta = new Vector2(44f, 44f);
            AddImage(icon, _coin, Gold);

            var label = AddText(NewRect("Amount", root), 36, TextAnchor.MiddleLeft, Gold);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(54f, 0f);

            root.gameObject.SetActive(false);
            return new Popup { Root = root, Group = group, Label = label, Icon = icon.gameObject };
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void AddImage(RectTransform rect, Sprite sprite, Color fallback)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            if (sprite != null) image.sprite = sprite;
            else image.color = fallback;
        }

        private static Text AddText(RectTransform rect, int size, TextAnchor anchor, Color color)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = UiArt.Font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.14f, 0.07f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        private sealed class Popup
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Text Label;
            public GameObject Icon;
            public Vector3 World;
            public float Age;
        }
    }
}
