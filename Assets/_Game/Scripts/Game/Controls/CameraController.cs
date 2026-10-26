using DemoRestaurant.Game.UI;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Game.Render;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace DemoRestaurant.Game.Controls
{
    /// <summary>
    /// Orthographic camera: one-finger / left-mouse drag pans, pinch / scroll wheel zooms around the
    /// gesture point. Panning is clamped to the room bounds.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraController : MonoBehaviour
    {
        // Orthographic size = half the visible height in world units: smaller is more zoomed in.
        [SerializeField] private float minSize = 1.6f;
        [SerializeField] private float maxSize = 6f;

        /// <summary>Sets the zoom limits (e.g. so the whole room fits at max zoom-out).</summary>
        public void SetZoomLimits(float min, float max)
        {
            minSize = Mathf.Max(0.1f, min);
            maxSize = Mathf.Max(minSize, max);
            if (_camera != null)
                _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize, minSize, maxSize);
        }
        [SerializeField] private float scrollZoomStep = 0.1f;

        private Camera _camera;
        private Rect _bounds = new Rect(-10, -10, 20, 20);
        private Vector2 _lastMouse;
        private bool _mouseDragging;
        private float _lastPinchDistance;
        private Vector2 _lastPinchMid;
        private int _lastTouchCount;
        private bool _touchGestureOnUi;
        public bool Editing { get; set; }
        public System.Func<bool> CanPanInEditor { get; set; }

        /// <summary>World-space rectangle the camera centre is kept inside.</summary>
        public void SetBounds(Rect bounds)
        {
            _bounds = bounds;
            Clamp();
        }

        public void SetRoomBounds(RestaurantLayout layout)
        {
            // The room, the sidewalk and some road beyond the back-left wall, the garden in front, and the fenced farm
            // top-right.
            var field = Logic.Progression.Garden.FieldOrigin(layout.RoomWidth);
            int minX = RestaurantLayout.StreetOuterX - 3;
            int maxX = Mathf.Max(layout.Grid.Width, field.X + Logic.Progression.Garden.FieldSize + 1);
            int minY = Mathf.Min(layout.StreetMinY, field.Y - 1);
            int maxY = layout.StreetMaxY + 1;
            Vector2 left = IsoProjection.GridToWorld(minX, maxY);
            Vector2 right = IsoProjection.GridToWorld(maxX, minY);
            Vector2 top = IsoProjection.GridToWorld(minX, minY, 3f);
            Vector2 bottom = IsoProjection.GridToWorld(maxX, maxY);
            maxSize = Mathf.Max(9f, (top.y - bottom.y) * 0.65f);
            SetBounds(Rect.MinMaxRect(left.x, bottom.y, right.x, top.y));
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
        }

        private void OnEnable() => EnhancedTouchSupport.Enable();
        private void OnDisable() => EnhancedTouchSupport.Disable();

        private void Update()
        {
            if (Touch.activeTouches.Count > 0)
                HandleTouches();
            else
                HandleMouse();
            _lastTouchCount = Touch.activeTouches.Count;
        }

        private void HandleTouches()
        {
            var touches = Touch.activeTouches;

            // A gesture that starts on a UI panel belongs to the UI until every finger lifts.
            if (_lastTouchCount == 0)
                _touchGestureOnUi = UiInput.IsPointerOverUi(touches[0].screenPosition);
            if (_touchGestureOnUi)
                return;

            if (touches.Count == 1)
            {
                if (Editing && (CanPanInEditor == null || !CanPanInEditor())) return;
                var t = touches[0];
                // Skip the first frame after the finger count changes so pans don't jump.
                if (_lastTouchCount == 1 && t.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                    PanByScreenDelta(t.screenPosition - t.delta, t.screenPosition);
                return;
            }

            Vector2 a = touches[0].screenPosition, b = touches[1].screenPosition;
            float distance = Vector2.Distance(a, b);
            Vector2 mid = (a + b) * 0.5f;
            if (_lastTouchCount >= 2 && _lastPinchDistance > 1f)
            {
                ZoomAround(mid, _lastPinchDistance / distance);
                PanByScreenDelta(_lastPinchMid, mid);
            }
            _lastPinchDistance = distance;
            _lastPinchMid = mid;
        }

        private void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            Vector2 pos = mouse.position.ReadValue();
            bool overUi = UiInput.IsPointerOverUi(pos);
            if ((!Editing && mouse.leftButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame ||
                Editing && mouse.rightButton.wasPressedThisFrame) && !overUi)
            {
                _mouseDragging = true;
                _lastMouse = pos;
            }
            if (!(Editing ? mouse.rightButton.isPressed : mouse.leftButton.isPressed) && !mouse.middleButton.isPressed)
                _mouseDragging = false;

            if (_mouseDragging && pos != _lastMouse)
            {
                PanByScreenDelta(_lastMouse, pos);
                _lastMouse = pos;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !overUi)
                ZoomAround(pos, scroll > 0 ? 1f - scrollZoomStep : 1f + scrollZoomStep);
        }

        /// <summary>Moves the view by a world-space offset (e.g. edge-scrolling while dragging a piece), kept in bounds.</summary>
        public void PanWorld(Vector2 delta)
        {
            transform.position += (Vector3)delta;
            Clamp();
        }

        private void PanByScreenDelta(Vector2 from, Vector2 to)
        {
            Vector3 worldFrom = _camera.ScreenToWorldPoint(from);
            Vector3 worldTo = _camera.ScreenToWorldPoint(to);
            transform.position += worldFrom - worldTo;
            Clamp();
        }

        private void ZoomAround(Vector2 screenPoint, float factor)
        {
            Vector3 before = _camera.ScreenToWorldPoint(screenPoint);
            _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize * factor, minSize, maxSize);
            Vector3 after = _camera.ScreenToWorldPoint(screenPoint);
            transform.position += before - after;
            Clamp();
        }

        private void Clamp()
        {
            Vector3 p = transform.position;
            p.x = Mathf.Clamp(p.x, _bounds.xMin, _bounds.xMax);
            p.y = Mathf.Clamp(p.y, _bounds.yMin, _bounds.yMax);
            transform.position = p;
        }
    }
}
