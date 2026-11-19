using UnityEngine;

namespace Workerborn
{
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(Camera))]
    public sealed class VillageCameraRig : MonoBehaviour
    {
        [SerializeField, Range(25f, 85f)]
        private float fieldOfView = 50f;
        [SerializeField, Min(0.2f)]
        private float pivotHeight = 1.1f;
        [SerializeField, Min(1f)]
        private float initialDistance = 7.5f;
        [SerializeField, Min(0.5f)]
        private float minimumDistance = 1.8f;
        [SerializeField, Min(2f)]
        private float maximumDistance = 22f;
        [SerializeField]
        private float initialYaw;
        [SerializeField, Range(-10f, 80f)]
        private float initialPitch = 18f;
        [SerializeField]
        private Vector2 pitchLimits = new Vector2(-5f, 75f);
        [SerializeField, Range(0.02f, 1f)]
        private float orbitSensitivity = 0.18f;
        [SerializeField, Range(0.05f, 0.5f)]
        private float zoomSensitivity = 0.18f;
        private Transform target;
        private Transform viewOverride;
        private VillagePrototype game;
        private Camera cameraComponent;
        private Vector3 focus;
        private float yaw;
        private float pitch;
        private float distance;
        private bool rightPressed;
        private bool rightDragged;
        private bool rightViewMoved;
        private bool middlePressed;
        private Vector2 pressPoint;
        public bool RightClickThisFrame { get; private set; }
        public float Distance => distance;

        public void SetViewOverride(Transform pose)
        {
            viewOverride = pose;
            rightPressed = middlePressed = false;
            if (target != null)
                UpdatePose();
        }

        public void Initialize(Transform follow, VillagePrototype owner = null)
        {
            target = follow;
            game = owner;
            cameraComponent = GetComponent<Camera>();
            cameraComponent.orthographic = false;
            cameraComponent.fieldOfView = fieldOfView;
            cameraComponent.nearClipPlane = 0.08f;
            cameraComponent.farClipPlane = 180f;
            yaw = initialYaw;
            pitch = initialPitch;
            distance = Mathf.Clamp(initialDistance, minimumDistance, maximumDistance);
            focus = target.position + Vector3.up * pivotHeight;
            UpdatePose();
        }

        private void Update()
        {
            RightClickThisFrame = false;
            if (target == null || WorkerbornInput.Current == null)
                return;
            Vector2 position = WorkerbornInput.Point;
            bool blocked = game != null && game.ControlsBlocked;
            var ui = game?.Targeting?.UiAt(position);
            bool overUi = ui != null && ui.GetComponentInParent<WorldNameplateView>() == null;
            if (WorkerbornInput.Pressed("Camera/OrbitRight") && !blocked && !overUi)
            {
                rightPressed = true;
                rightDragged = false;
                rightViewMoved = false;
                pressPoint = position;
            }

            if (WorkerbornInput.Pressed("Camera/OrbitMiddle") && !blocked && !overUi)
                middlePressed = true;
            if (WorkerbornInput.Released("Camera/OrbitMiddle"))
                middlePressed = false;
            float previousYaw = yaw, previousPitch = pitch, previousDistance = distance;
            if ((rightPressed && WorkerbornInput.Held("Camera/OrbitRight")) || (middlePressed && WorkerbornInput.Held("Camera/OrbitMiddle")))
            {
                if (rightPressed && (position - pressPoint).sqrMagnitude > 25f)
                    rightDragged = true;
                if ((rightDragged || middlePressed) && !blocked)
                {
                    Vector2 delta = WorkerbornInput.Look;
                    yaw += delta.x * orbitSensitivity;
                    pitch = Mathf.Clamp(pitch - delta.y * orbitSensitivity, pitchLimits.x, pitchLimits.y);
                }
            }

            if (!blocked && !overUi)
                distance = OrbitZoom.Apply(distance, WorkerbornInput.Scroll, minimumDistance, maximumDistance, zoomSensitivity);
            if (rightPressed && (yaw != previousYaw || pitch != previousPitch || distance != previousDistance))
                rightViewMoved = true;

            if (WorkerbornInput.Released("Camera/OrbitRight"))
            {
                RightClickThisFrame = rightPressed && !rightViewMoved && !blocked && !overUi;
                rightPressed = false;
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                rightPressed = false;
                middlePressed = false;
            }
        }

        private void LateUpdate()
        {
            if (target != null)
                UpdatePose();
        }

        private void UpdatePose()
        {
            if (viewOverride != null)
            {
                transform.SetPositionAndRotation(viewOverride.position, viewOverride.rotation);
                return;
            }
            focus = target.position + Vector3.up * pivotHeight;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 direction = rotation * Vector3.back;
            Vector3 position = focus + direction * distance;
            float floor = target.position.y + .2f;
            if (game != null && game.GroundTerrain != null)
                floor = game.GroundTerrain.SampleHeight(position) + game.GroundTerrain.transform.position.y + cameraComponent.nearClipPlane + .2f;
            position.y = Mathf.Max(position.y, floor);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
        }
    }
}
