using System;
using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>Large touch targets in a compact two-column drawer inside the safe area.</summary>
    public sealed class HudDrawer : MonoBehaviour
    {
        public const float TileWidth = 156f, TileHeight = 148f;
        private const float Gap = 12f, HandleHeight = 76f;
        private readonly List<RectTransform> _buttons = new List<RectTransform>();
        private string _key;
        private bool _right;
        private Text _label;
        public RectTransform Content { get; private set; }
        public bool Expanded { get; private set; }
        public event Action Opened;

        public static HudDrawer Create(RectTransform parent, string name, string key, bool right)
        {
            var rect = UiKit.NewRect(name + "Drawer", parent);
            var drawer = rect.gameObject.AddComponent<HudDrawer>();
            drawer._key = key; drawer._right = right;
            rect.anchorMin = rect.anchorMax = rect.pivot = right ? new Vector2(1, 0) : new Vector2(0, 1);
            rect.anchoredPosition = right ? new Vector2(-24, 24) : new Vector2(30, -210);
            rect.sizeDelta = new Vector2(TileWidth * 2 + Gap, HandleHeight);
            var handle = UiKit.TextButton(rect, "Toggle", UiKit.Wood, 30,
                () => drawer.SetExpanded(!drawer.Expanded), out drawer._label);
            var h = (RectTransform)handle.transform;
            drawer._label.resizeTextForBestFit = true;
            drawer._label.resizeTextMinSize = 20; drawer._label.resizeTextMaxSize = 30;
            h.anchorMin = h.anchorMax = h.pivot = right ? new Vector2(1, 0) : new Vector2(0, 1);
            h.anchoredPosition = Vector2.zero; h.sizeDelta = new Vector2(212, HandleHeight);
            drawer.Content = UiKit.NewRect("Buttons", rect);
            drawer.Content.anchorMin = drawer.Content.anchorMax = drawer.Content.pivot = h.pivot;
            drawer.Content.anchoredPosition = new Vector2(0, right ? HandleHeight + Gap : -HandleHeight - Gap);
            drawer.Content.sizeDelta = new Vector2(TileWidth * 2 + Gap, 0);
            drawer.SetExpanded(false);
            Loc.LanguageChanged += drawer.RefreshLabel;
            return drawer;
        }

        public void Add(RectTransform button)
        {
            int index = _buttons.Count;
            button.anchorMin = button.anchorMax = button.pivot = _right ? new Vector2(1, 0) : new Vector2(0, 1);
            button.anchoredPosition = new Vector2((_right ? -1 : 1) * (index % 2) * (TileWidth + Gap),
                (_right ? 1 : -1) * (index / 2) * (TileHeight + Gap));
            button.sizeDelta = new Vector2(TileWidth, TileHeight);
            var icon = (RectTransform)button.Find("Icon");
            icon.sizeDelta = new Vector2(94, 92);
            var text = button.Find("Label").GetComponent<Text>();
            text.rectTransform.sizeDelta = new Vector2(-12, 38);
            text.resizeTextMaxSize = 28; text.resizeTextMinSize = 20; text.fontSize = 28;
            _buttons.Add(button);
        }

        public void SetExpanded(bool expanded)
        {
            Expanded = expanded; Content.gameObject.SetActive(expanded); RefreshLabel();
            if (expanded) Opened?.Invoke();
        }

        private void RefreshLabel() => _label.text = Loc.Get(_key) + "  " +
            (Expanded ? Loc.Get("ui.toolbar.collapse") : "+");
        private void OnDestroy() => Loc.LanguageChanged -= RefreshLabel;
    }
}
