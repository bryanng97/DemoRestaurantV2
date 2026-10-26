using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>EventSystem setup for uGUI on the Input System, and "is this point over UI?" for world input.</summary>
    public static class UiInput
    {
        private static readonly List<RaycastResult> Results = new List<RaycastResult>();
        private static PointerEventData _eventData;
        private static EventSystem _eventDataOwner;

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        /// <summary>True when a raycast-target UI element is under the screen position.</summary>
        public static bool IsPointerOverUi(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;
            if (_eventData == null || _eventDataOwner != eventSystem)
            {
                _eventData = new PointerEventData(eventSystem);
                _eventDataOwner = eventSystem;
            }
            _eventData.position = screenPosition;
            Results.Clear();
            eventSystem.RaycastAll(_eventData, Results);
            return Results.Count > 0;
        }
    }
}
