using System;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Layout;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace DemoRestaurant.Game.Render
{
    /// <summary>
    /// Outside decorate mode, a short tap on a wall piece runs its tap action (furniture.json "tapAction"): the menu
    /// holder opens the menu, as in the reference game. Drags (camera pans) and taps on the UI are ignored.
    /// </summary>
    public sealed class WallTapHandler : MonoBehaviour
    {
        private Func<RestaurantLayout> _layout;
        private LoadedContentPack _content;
        private Func<bool> _blocked;
        private Action<string> _run;
        /// <summary>Optional: handles a tap on the floor first (e.g. fixing a broken toilet); true = handled.</summary>
        public Func<Vector2, bool> FloorTap { get; set; }
        private Vector2 _pressAt;
        private float _pressTime;
        private bool _pressed;
        private int _finger = -1;

        private void OnEnable() => EnhancedTouchSupport.Enable();
        private void OnDisable() { EnhancedTouchSupport.Disable(); _pressed = false; _finger = -1; }

        /// <param name="blocked">True while taps mean something else (decorate mode).</param>
        /// <param name="run">Runs a tap action by name.</param>
        public void Bind(Func<RestaurantLayout> layout, LoadedContentPack content, Func<bool> blocked, Action<string> run)
        {
            _layout = layout; _content = content; _blocked = blocked; _run = run;
        }

        private void Update()
        {
            var touches = Touch.activeTouches;
            if (touches.Count > 1) { _pressed = false; return; }
            if (touches.Count == 1)
            {
                var touch = touches[0];
                bool began = touch.phase == UnityEngine.InputSystem.TouchPhase.Began;
                if (began) _finger = touch.touchId;
                if (_finger != touch.touchId) return;
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled) { _pressed = false; _finger = -1; return; }
                HandlePointer(touch.screenPosition, began, touch.phase == UnityEngine.InputSystem.TouchPhase.Ended);
                return;
            }
            var pointer = Mouse.current;
            if (pointer == null) return;
            var pos = pointer.position.ReadValue();
            HandlePointer(pos, pointer.leftButton.wasPressedThisFrame, pointer.leftButton.wasReleasedThisFrame);
        }

        private void HandlePointer(Vector2 pos, bool down, bool up)
        {
            if (_run == null) return;
            if (down)
            {
                _pressed = !UI.UiInput.IsPointerOverUi(pos) && (_blocked == null || !_blocked());
                _pressAt = pos;
                _pressTime = Time.unscaledTime;
            }
            if (!up || !_pressed) return;
            _pressed = false;
            if ((_blocked != null && _blocked()) || UI.UiInput.IsPointerOverUi(pos)) return;
            if ((pos - _pressAt).sqrMagnitude > 20f * 20f || Time.unscaledTime - _pressTime > 0.5f || Camera.main == null) return;
            var layout = _layout();
            var world = (Vector2)Camera.main.ScreenToWorldPoint(pos);
            if (FloorTap != null && FloorTap(world)) return;
            Vector2 local = transform.InverseTransformPoint(world);
            if (!RestaurantView.WallCellAt(local, layout, out var side, out int index)) return;
            var item = layout.WallItemAt(side, index);
            var action = item != null ? _content.ArtForFurniture(item.Def.Id).TapAction : null;
            if (!string.IsNullOrEmpty(action)) _run(action);
        }
    }
}
