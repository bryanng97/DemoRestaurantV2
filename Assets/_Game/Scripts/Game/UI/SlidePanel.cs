using System;
using DemoRestaurant.Game.Content;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// A panel that slides in from the left next to the toolbar, with a title and a close button.
    /// Subclasses build their content once and refresh it while visible.
    /// </summary>
    public abstract class SlidePanel : MonoBehaviour
    {
        private const float SlideSeconds = 0.22f;
        protected const float Width = 840f;
        protected const float Height = 960f;
        private const float OpenX = 150f; // just right of the left button column

        private bool _open;
        private float _slide;
        private float _refreshTimer;
        private Text _title;

        protected RectTransform Panel { get; private set; }
        /// <summary>Area below the title, in panel coordinates (use UiKit.Place relative to it).</summary>
        protected RectTransform Content { get; private set; }
        protected GameUi Ui { get; private set; }

        public bool IsOpen => _open;
        /// <summary>The panel's UI rectangle (for draw-order changes).</summary>
        public RectTransform Root => Panel;
        public event Action<SlidePanel> Opened;

        protected abstract string TitleKey { get; }
        protected abstract void BuildContent(RectTransform content);
        /// <summary>Called on open, on language change and every 0.25 s while visible.</summary>
        protected abstract void Refresh();

        public void Build(GameUi ui, RectTransform root)
        {
            Ui = ui;
            Panel = UiKit.Box(root, GetType().Name, UiKit.Wood, UiKit.Cream, 6f);
            Panel.anchorMin = Panel.anchorMax = Panel.pivot = new Vector2(0f, 0.5f);
            Panel.sizeDelta = new Vector2(Width, Height);
            Panel.gameObject.SetActive(false);

            _title = UiKit.Label(Panel, "Title", 36, 24, 600, 60, 46, TextAnchor.UpperLeft, UiKit.DarkText);
            var close = UiKit.TextButton(Panel, "Close", UiKit.Wood, 40, () => SetOpen(false), out var x);
            x.text = "X";
            UiKit.Place((RectTransform)close.transform, Width - 110, 22, 84, 72);

            Content = UiKit.NewRect("Content", Panel);
            UiKit.Stretch(Content, 30, 100, 30, 30);
            BuildContent(Content);

            Loc.LanguageChanged += OnLanguageChanged;
            OnLanguageChanged();
        }

        protected virtual void OnDestroy() => Loc.LanguageChanged -= OnLanguageChanged;

        private void OnLanguageChanged()
        {
            if (_title != null) _title.text = Loc.Get(TitleKey);
            Refresh();
        }

        public void Toggle() => SetOpen(!_open);

        public void SetOpen(bool open)
        {
            if (open == _open)
                return;
            _open = open;
            if (open)
            {
                Refresh();
                Opened?.Invoke(this);
            }
        }

        /// <summary>Shrinks the panel uniformly if the safe area is shorter or narrower than it (tablets, home bars).</summary>
        private void FitToParent()
        {
            var parent = Panel.parent as RectTransform;
            if (parent == null)
                return;
            var size = parent.rect.size;
            float scale = Mathf.Min(1f, (size.y - 20f) / Height, (size.x - OpenX - 10f) / Width);
            Panel.localScale = new Vector3(scale, scale, 1f);
        }

        protected virtual void Update()
        {
            if (Panel == null)
                return;
            _slide = Mathf.MoveTowards(_slide, _open ? 1f : 0f, Time.unscaledDeltaTime / SlideSeconds);
            float eased = 1f - (1f - _slide) * (1f - _slide);
            Panel.anchoredPosition = new Vector2(Mathf.Lerp(-Width - 20f, OpenX, eased), 0f);
            FitToParent();
            Panel.gameObject.SetActive(_slide > 0f);

            if (_open)
            {
                _refreshTimer -= Time.unscaledDeltaTime;
                if (_refreshTimer <= 0f)
                {
                    _refreshTimer = 0.25f;
                    Refresh();
                }
            }
        }
    }
}
