using UnityEngine;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Fits its RectTransform to the device's safe area (<see cref="Screen.safeArea"/>): inside notches, punch-hole
    /// cameras, rounded corners and the iOS home indicator. Adds a small extra margin for curved screen edges, which
    /// the OS doesn't report. Re-fits whenever the safe area, resolution or orientation changes.
    /// Put HUD buttons and panels under this; full-screen dims and world-anchored pop-ups stay on the canvas root.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeArea : MonoBehaviour
    {
        /// <summary>Extra inset on the left/right as a fraction of screen width (curved edges).</summary>
        public static float ExtraHorizontal = 0.012f;
        /// <summary>Extra inset on the top/bottom as a fraction of screen height.</summary>
        public static float ExtraVertical = 0.01f;

        private RectTransform _rect;
        private Rect _lastSafe;
        private Vector2Int _lastSize;
        private ScreenOrientation _lastOrientation;

        public static RectTransform Create(RectTransform canvasRoot)
        {
            var rect = UiKit.NewRect("SafeArea", canvasRoot);
            rect.gameObject.AddComponent<SafeArea>().Apply();
            return rect;
        }

        /// <summary>The safe area plus the extra margins, in screen pixels.</summary>
        public static Rect Current()
        {
            var safe = Screen.safeArea;
            float dx = Screen.width * ExtraHorizontal, dy = Screen.height * ExtraVertical;
            return new Rect(safe.x + dx, safe.y + dy, Mathf.Max(1f, safe.width - 2f * dx), Mathf.Max(1f, safe.height - 2f * dy));
        }

        private void Awake() => Apply();

        private void Update()
        {
            if (Screen.safeArea != _lastSafe || Screen.width != _lastSize.x || Screen.height != _lastSize.y
                || Screen.orientation != _lastOrientation)
                Apply();
        }

        private void Apply()
        {
            _rect ??= GetComponent<RectTransform>();
            _lastSafe = Screen.safeArea;
            _lastSize = new Vector2Int(Screen.width, Screen.height);
            _lastOrientation = Screen.orientation;
            if (Screen.width <= 0 || Screen.height <= 0)
                return;

            var area = Current();
            _rect.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            _rect.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            _rect.offsetMin = _rect.offsetMax = Vector2.zero;
            _rect.pivot = new Vector2(0.5f, 0.5f);
        }
    }
}
