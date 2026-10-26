using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace DemoRestaurant.Game.DebugTools
{
    /// <summary>
    /// Developer-only overlay for the engine test: FPS, worst frame, memory, depth-sort cost,
    /// and buttons to change the customer count. Not player-facing, so its labels are not localised.
    /// </summary>
    public sealed class PerfHud : MonoBehaviour
    {
        private const float SampleWindow = 2f;

        private readonly float[] _frameTimes = new float[600];
        private int _frameIndex;
        private int _frameCount;
        private float _refreshTimer;
        private string _text = "";
        private GUIStyle _label, _button, _tag;
        private float _fps;
        /// <summary>Collapsed to a small FPS tag by default; tap the tag for the full panel.</summary>
        private bool _expanded;
        private float _scale = 1f;

        /// <summary>Returns the extra line(s) of stats from the scene (customer count, sort time...).</summary>
        public Func<string> ExtraStats { get; set; }
        public Action<int> ChangeTargetCount { get; set; }

        /// <summary>Seconds after start to ignore: scene load and art setup land in the first frames.</summary>
        private const float WarmUpSeconds = 2f;
        private float _warmUp = WarmUpSeconds;

        private void Update()
        {
            if (_warmUp > 0f)
            {
                _warmUp -= Time.unscaledDeltaTime;
                _text = "Warming up...";
                return;
            }

            _frameTimes[_frameIndex] = Time.unscaledDeltaTime;
            _frameIndex = (_frameIndex + 1) % _frameTimes.Length;
            _frameCount = Mathf.Min(_frameCount + 1, _frameTimes.Length);

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 0.5f;
                _text = BuildText();
            }
        }

        private string BuildText()
        {
            // Average FPS and worst frame over the last SampleWindow seconds.
            float total = 0f, worst = 0f;
            int n = 0;
            for (int i = 0; i < _frameCount && total < SampleWindow; i++)
            {
                float dt = _frameTimes[(_frameIndex - 1 - i + _frameTimes.Length) % _frameTimes.Length];
                total += dt;
                worst = Mathf.Max(worst, dt);
                n++;
            }
            float fps = total > 0 ? n / total : 0f;
            _fps = fps;
            long allocated = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
            long reserved = Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
            long mono = Profiler.GetMonoUsedSizeLong() / (1024 * 1024);

            return $"FPS {fps:0.0}   worst {worst * 1000f:0.0} ms\n" +
                   $"Mem alloc {allocated} MB / reserved {reserved} MB / managed {mono} MB\n" +
                   (ExtraStats != null ? ExtraStats() + "\n" : "") +
                   $"{SystemInfo.deviceModel} | {SystemInfo.graphicsDeviceName} | {Screen.width}x{Screen.height}";
        }

        private void OnGUI()
        {
            if (_label == null)
            {
                _scale = Mathf.Max(1f, Screen.dpi > 0 ? Screen.dpi / 160f : 1f);
                _label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false, wordWrap = true };
                _label.normal.textColor = Color.white;
                _button = new GUIStyle(GUI.skin.button) { fontSize = 14 };
                _tag = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
            }

            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));

            // Top-right of the safe area (clear of cameras and curved edges), below the coin counter.
            // IMGUI measures y from the top, Screen.safeArea from the bottom.
            var safe = UI.SafeArea.Current();
            float screenWidth = safe.xMax / _scale;
            float top = (Screen.height - safe.yMax) / _scale + safe.height / _scale * 0.17f;

            // Collapsed: a small FPS tag coloured by health. Tap to expand / collapse.
            const float tagWidth = 104f, tagHeight = 30f;
            float tagLeft = screenWidth - tagWidth - 12f;
            var tagColor = _fps >= 50f ? new Color(0.35f, 0.85f, 0.4f) : _fps >= 30f ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.4f, 0.35f);
            GUI.backgroundColor = new Color(0, 0, 0, 0.85f);
            _tag.normal.textColor = _tag.hover.textColor = _tag.active.textColor = tagColor;
            string tagText = _warmUp > 0f ? "FPS ..." : $"FPS {_fps:0}";
            if (GUI.Button(new Rect(tagLeft, top, tagWidth, tagHeight), _expanded ? tagText + "  x" : tagText, _tag))
                _expanded = !_expanded;
            GUI.backgroundColor = Color.white;

            if (_expanded)
                DrawPanel(screenWidth, top + tagHeight + 4f);

            GUI.matrix = oldMatrix;
        }

        private Vector2 _panelScroll;

        /// <summary>
        /// The expanded panel, sized to the screen: as wide as fits (at most 560), buttons wrap onto as many rows as
        /// they need, and the whole panel scrolls when it is taller than the space below the tag.
        /// </summary>
        private void DrawPanel(float screenWidth, float top)
        {
            const float rowHeight = 36f, buttonHeight = 30f, gap = 6f, pad = 6f;
            var safe = UI.SafeArea.Current();
            float width = Mathf.Min(560f, screenWidth - safe.xMin / _scale - 24f);
            float left = screenWidth - width - 12f;
            float inner = width - 2f * pad - 16f; // room for the scroll bar
            float textHeight = _label.CalcHeight(new GUIContent(_text), inner) + 4f;

            // Lay the buttons out first (flowing left to right, wrapping), so the panel knows its height.
            _layout.Clear();
            float x = 0f, y = textHeight + 2f;
            void Flow(string text, float w, Action click)
            {
                w = Mathf.Min(w, inner);
                if (x > 0f && x + w > inner) { x = 0f; y += rowHeight; }
                _layout.Add((new Rect(pad + x, y, w, buttonHeight), text, click));
                x += w + gap;
            }
            if (ChangeTargetCount != null)
            {
                foreach (int delta in new[] { -50, -10, +10, +50 })
                {
                    int d = delta;
                    Flow(d > 0 ? $"+{d}" : d.ToString(), 56f, () => ChangeTargetCount(d));
                }
                x = 0f; y += rowHeight;
            }
            foreach (var (label, onClick) in _buttons)
            {
                string text = label();
                Flow(text, Mathf.Max(56f, _button.CalcSize(new GUIContent(text)).x + 12f), onClick);
            }
            float contentHeight = (x > 0f ? y + rowHeight : y) + pad;

            float bottom = (Screen.height - safe.yMin) / _scale - 8f;
            float viewHeight = Mathf.Max(rowHeight * 2f, Mathf.Min(contentHeight, bottom - top));
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(left, top, width, viewHeight), Texture2D.whiteTexture);
            GUI.color = Color.white;

            _panelScroll = GUI.BeginScrollView(new Rect(left, top, width, viewHeight), _panelScroll,
                new Rect(0f, 0f, width - 16f, contentHeight), false, false);
            GUI.Label(new Rect(pad, 2f, inner, textHeight), _text, _label);
            foreach (var (rect, text, click) in _layout)
                if (GUI.Button(rect, text, _button)) click();
            GUI.EndScrollView();
        }

        private readonly System.Collections.Generic.List<(Rect rect, string text, Action click)> _layout =
            new System.Collections.Generic.List<(Rect, string, Action)>();
        private readonly System.Collections.Generic.List<(Func<string> label, Action onClick)> _buttons =
            new System.Collections.Generic.List<(Func<string>, Action)>();

        /// <summary>Adds a button to the second row (label is re-evaluated every frame).</summary>
        public void AddButton(Func<string> label, Action onClick) => _buttons.Add((label, onClick));
    }
}
