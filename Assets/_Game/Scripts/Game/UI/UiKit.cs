using System;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Shared uGUI building blocks in the concept's style (dark wood frames, cream panels).
    /// All layout is in reference pixels of a 1920x1080 canvas scaled by height.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Wood = new Color32(92, 58, 34, 255);
        public static readonly Color WoodLight = new Color32(140, 94, 58, 255);
        public static readonly Color Cream = new Color32(250, 241, 224, 255);
        public static readonly Color CardRim = new Color32(201, 165, 122, 255);
        public static readonly Color CardFill = new Color32(255, 250, 240, 255);
        public static readonly Color Gold = new Color32(232, 163, 58, 255);
        public static readonly Color GoldFill = new Color32(255, 243, 200, 255);
        public static readonly Color Green = new Color32(86, 160, 74, 255);
        public static readonly Color Red = new Color32(204, 72, 56, 255);
        public static readonly Color Disabled = new Color32(170, 160, 150, 255);
        public static readonly Color DarkText = new Color32(78, 48, 28, 255);
        public static readonly Color SoftText = new Color32(115, 82, 56, 255);
        // The concept's HUD style: cream cards and buttons with a soft brown rim and a drop shadow.
        public static readonly Color CardRimSoft = new Color32(176, 140, 102, 255);
        public static readonly Color CardCream = new Color32(253, 247, 234, 255);

        public static RectTransform CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // landscape: scale by height
            return (RectTransform)go.transform;
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Positions a rect by its top-left corner relative to the parent's top-left.</summary>
        public static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>Rounded box: a rim image (raycast target) with an inset fill image.</summary>
        public static RectTransform Box(Transform parent, string name, Color rim, Color fill, float rimWidth,
            out Image rimImage, out Image fillImage)
        {
            var rect = NewRect(name, parent);
            rimImage = rect.gameObject.AddComponent<Image>();
            rimImage.sprite = UiArt.Rounded();
            rimImage.type = Image.Type.Sliced;
            rimImage.color = rim;

            var inner = NewRect("Fill", rect);
            Stretch(inner, rimWidth, rimWidth, rimWidth, rimWidth);
            fillImage = inner.gameObject.AddComponent<Image>();
            fillImage.sprite = UiArt.Rounded();
            fillImage.type = Image.Type.Sliced;
            fillImage.color = fill;
            fillImage.raycastTarget = false;
            return rect;
        }

        public static RectTransform Box(Transform parent, string name, Color rim, Color fill, float rimWidth) =>
            Box(parent, name, rim, fill, rimWidth, out _, out _);

        public static Button AddButton(RectTransform rect, Action onClick)
        {
            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.6f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>A rounded button with a centred label.</summary>
        public static Button TextButton(Transform parent, string name, Color color, int fontSize, Action onClick, out Text label)
        {
            var rect = Box(parent, name, Darken(color, 0.75f), color, 4f);
            var button = AddButton(rect, onClick);
            label = AddText(NewRect("Label", rect), fontSize, TextAnchor.MiddleCenter, Color.white);
            Stretch(label.rectTransform, 8, 0, 8, 0);
            AddOutline(label);
            return button;
        }

        public static Image AddImage(RectTransform rect, Sprite sprite, bool preserveAspect = true)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = preserveAspect;
            image.raycastTarget = false;
            image.enabled = sprite != null;
            return image;
        }

        public static Text AddText(RectTransform rect, int size, TextAnchor anchor, Color color, bool bold = true)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = UiArt.Font;
            text.fontSize = size;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Text Label(Transform parent, string name, float x, float y, float width, float height, int size,
            TextAnchor anchor, Color color, bool wrap = false)
        {
            var text = AddText(Place(NewRect(name, parent), x, y, width, height), size, anchor, color);
            if (wrap)
            {
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
            }
            return text;
        }

        public static void AddOutline(Text text)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.14f, 0.07f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);
        }

        /// <summary>Cream card with a soft brown rim and a shadow, like the concept's profile and goal cards.</summary>
        public static RectTransform ConceptCard(Transform parent, string name)
        {
            var rect = Box(parent, name, CardRimSoft, CardCream, 4f, out var rim, out _);
            var shadow = rim.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.2f, 0.12f, 0.05f, 0.35f);
            shadow.effectDistance = new Vector2(0f, -5f);
            return rect;
        }

        /// <summary>Square HUD button: icon above a label, cream card style (the concept's Build/Menu/Staff buttons).</summary>
        public static RectTransform ConceptButton(Transform parent, string name, Sprite icon, Action onClick, out Text label)
        {
            var rect = ConceptCard(parent, name);
            rect.sizeDelta = new Vector2(104f, 100f);
            AddButton(rect, onClick);
            var iconRect = NewRect("Icon", rect);
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.anchoredPosition = new Vector2(0f, -8f);
            iconRect.sizeDelta = new Vector2(64f, 58f);
            AddImage(iconRect, icon);
            label = AddText(NewRect("Label", rect), 20, TextAnchor.MiddleCenter, DarkText);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 0f);
            label.rectTransform.pivot = new Vector2(0.5f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(0f, 6f);
            label.rectTransform.sizeDelta = new Vector2(-8f, 28f);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = 20;
            return rect;
        }

        /// <summary>Horizontal fill bar; returns the fill image (set fillAmount 0..1).</summary>
        public static Image Bar(Transform parent, string name, float x, float y, float width, float height, Color fill) =>
            Bar(parent, name, x, y, width, height, fill, Wood);

        public static Image Bar(Transform parent, string name, float x, float y, float width, float height, Color fill, Color rim)
        {
            var back = Box(Place(NewRect(name, parent), x, y, width, height), "Back", rim, new Color(0.88f, 0.84f, 0.78f), 3f);
            back.anchorMin = Vector2.zero;
            back.anchorMax = Vector2.one;
            back.offsetMin = back.offsetMax = Vector2.zero;
            var fillRect = NewRect("Fill", back);
            Stretch(fillRect, 5, 5, 5, 5);
            var image = fillRect.gameObject.AddComponent<Image>();
            image.sprite = UiArt.Pixel();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.color = fill;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A round coloured chip with a short label: the stand-in icon for an ingredient.</summary>
        public static RectTransform Chip(Transform parent, string name, Color color, string label, float size, out Text text)
        {
            var rect = NewRect(name, parent);
            rect.sizeDelta = new Vector2(size, size);
            var ring = rect.gameObject.AddComponent<Image>();
            ring.sprite = UiArt.Circle();
            ring.color = Darken(color, 0.6f);
            var inner = NewRect("Fill", rect);
            Stretch(inner, 4, 4, 4, 4);
            var fill = inner.gameObject.AddComponent<Image>();
            fill.sprite = UiArt.Circle();
            fill.color = color;
            fill.raycastTarget = false;
            text = AddText(NewRect("Label", rect), Mathf.RoundToInt(size * 0.2f), TextAnchor.MiddleCenter, DarkText);
            Stretch(text.rectTransform, 2, 0, 2, 0);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = Mathf.RoundToInt(size * 0.22f);
            text.text = label;
            return rect;
        }

        /// <summary>
        /// Full-screen dim (blocks taps on everything behind) with a centred concept card. Starts hidden.
        /// </summary>
        public static RectTransform Modal(RectTransform canvas, string name, Vector2 size, out GameObject root)
        {
            var dim = NewRect(name, canvas);
            Stretch(dim);
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            root = dim.gameObject;
            var card = ConceptCard(dim, "Card");
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = size;
            root.SetActive(false);
            return card;
        }

        /// <summary>Round red close button for a dialog's top-right corner.</summary>
        public static Button CloseButton(RectTransform card, Action onClick)
        {
            var button = TextButton(card, "Close", Red, 34, onClick, out var x);
            x.text = "X";
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-18f, -18f);
            rect.sizeDelta = new Vector2(64f, 64f);
            return button;
        }

        /// <summary>Single-line text field (opens the on-screen keyboard on phones).</summary>
        public static InputField TextField(Transform parent, string name, float x, float y, float width, float height,
            int fontSize, int characterLimit)
        {
            var rect = Box(parent, name, CardRimSoft, Color.white, 3f, out var rim, out _);
            Place(rect, x, y, width, height);
            var text = AddText(NewRect("Text", rect), fontSize, TextAnchor.MiddleLeft, DarkText, bold: false);
            Stretch(text.rectTransform, 18, 4, 18, 4);
            text.supportRichText = false;
            var placeholder = AddText(NewRect("Placeholder", rect), fontSize, TextAnchor.MiddleLeft,
                new Color(SoftText.r, SoftText.g, SoftText.b, 0.55f), bold: false);
            Stretch(placeholder.rectTransform, 18, 4, 18, 4);
            placeholder.fontStyle = FontStyle.Italic;

            var input = rect.gameObject.AddComponent<InputField>();
            input.targetGraphic = rim;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = characterLimit;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        /// <summary>Horizontal 0..1 slider: gold fill and a round wooden handle.</summary>
        public static Slider Slider(Transform parent, string name, float x, float y, float width, float height, Action<float> onChanged)
        {
            var rect = Place(NewRect(name, parent), x, y, width, height);
            var back = Box(rect, "Background", CardRimSoft, new Color(0.88f, 0.84f, 0.78f), 3f);
            Stretch(back, 0, height * 0.3f, 0, height * 0.3f);

            var fillArea = NewRect("FillArea", rect);
            Stretch(fillArea, 6, height * 0.3f + 5, 6, height * 0.3f + 5);
            var fill = NewRect("Fill", fillArea);
            fill.sizeDelta = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = Rounded();
            fillImage.type = Image.Type.Sliced;
            fillImage.color = Gold;
            fillImage.raycastTarget = false;

            var handleArea = NewRect("HandleArea", rect);
            Stretch(handleArea, height * 0.5f, 0, height * 0.5f, 0);
            var handle = NewRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(height, 0f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = UiArt.Circle();
            handleImage.color = Wood;

            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        private static Sprite Rounded() => UiArt.Rounded();

        public static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        public static void SetButtonColor(Button button, Color color)
        {
            var rim = button.GetComponent<Image>();
            var fill = button.transform.Find("Fill")?.GetComponent<Image>();
            if (rim != null) rim.color = Darken(color, 0.75f);
            if (fill != null) fill.color = color;
        }
    }
}
