using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Workerborn
{
    // One editable action asset drives gameplay, camera and EventSystem input.
    [DefaultExecutionOrder(-500)]
    public sealed class WorkerbornInput : MonoBehaviour
    {
        [SerializeField]
        private InputActionAsset actions;
        private InputActionAsset runtime;
        // Pointer-following UI can stay at the last usable position while input settles.
        private Vector2 lastPointerPosition;
        private readonly List<InputActionReference> uiReferences = new List<InputActionReference>();
        public static WorkerbornInput Current { get; private set; }
        public InputActionAsset Actions => runtime;

        private InputAction Find(string name) => runtime != null ? runtime.FindAction(name, false) : null;
        public static bool Pressed(string name) => Current?.Find(name)?.WasPressedThisFrame() ?? false;
        public static bool Released(string name) => Current?.Find(name)?.WasReleasedThisFrame() ?? false;
        public static bool Held(string name) => Current?.Find(name)?.IsPressed() ?? false;
        public static Vector2 Move => Current?.Find("Gameplay/Move")?.ReadValue<Vector2>() ?? Vector2.zero;

        public static Vector2 Point
        {
            get
            {
                TryGetPointerPosition(out var position);
                return position;
            }
        }

        public static Vector2 Look
        {
            get
            {
                var delta = Current?.Find("Camera/Look")?.ReadValue<Vector2>() ?? Vector2.zero;
                return IsFinite(delta) ? delta : Vector2.zero;
            }
        }

        public static float Scroll
        {
            get
            {
                float delta = Current?.Find("Camera/Zoom")?.ReadValue<float>() ?? 0f;
                return float.IsFinite(delta) ? delta : 0f;
            }
        }

        internal static bool TryGetPointerPosition(out Vector2 position)
        {
            var input = Current;
            position = input != null ? input.lastPointerPosition : Vector2.zero;
            var action = input?.Find("Gameplay/Point");
            if (action == null)
                return false;
            Vector2 sample = action.ReadValue<Vector2>();
            if (!IsFinite(sample))
                return false;
            position = input.lastPointerPosition = sample;
            return true;
        }

        internal static bool IsFinite(Vector2 point) => float.IsFinite(point.x) && float.IsFinite(point.y);

        internal static bool IsInView(Camera camera, Vector2 point) =>
            camera != null && IsFinite(point) && camera.pixelRect.Contains(point);

        private void Awake()
        {
            if (actions == null)
            {
                Debug.LogError("Assign the Workerborn input actions asset.", this);
                enabled = false;
                return;
            }

            runtime = Instantiate(actions);
        }

        private void OnEnable()
        {
            Current = this;
            runtime?.Enable();
        }

        private void OnDisable()
        {
            runtime?.Disable();
            if (Current == this)
                Current = null;
        }

        public void BindUi(InputSystemUIInputModule module)
        {
            if (module == null || runtime == null)
                return;
            module.actionsAsset = runtime;
            module.point = Reference("UI/Point", module.point);
            module.leftClick = Reference("UI/Click", module.leftClick);
            module.rightClick = Reference("UI/RightClick", module.rightClick);
            module.middleClick = Reference("UI/MiddleClick", module.middleClick);
            module.scrollWheel = Reference("UI/ScrollWheel", module.scrollWheel);
            module.move = Reference("UI/Move", module.move);
            module.submit = Reference("UI/Submit", module.submit);
            module.cancel = Reference("UI/Cancel", module.cancel);
        }

        private InputActionReference Reference(string name, InputActionReference existing)
        {
            var action = runtime.FindAction(name, true);
            var reference = existing != null && existing.action == action ? existing : InputActionReference.Create(action);
            if (!uiReferences.Contains(reference))
                uiReferences.Add(reference);
            return reference;
        }

        private void OnDestroy()
        {
            foreach (var reference in uiReferences)
                if (reference != null)
                    Destroy(reference);
            if (runtime != null)
                Destroy(runtime);
        }
    }
}
