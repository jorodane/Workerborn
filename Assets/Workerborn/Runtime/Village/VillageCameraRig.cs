using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Workerborn
{
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(Camera))]
    public sealed class VillageCameraRig : MonoBehaviour
    {
        [SerializeField, Range(25f, 85f)] private float fieldOfView = 50f;
        [SerializeField, Min(0.2f)] private float pivotHeight = 1.1f;
        [SerializeField, Min(1f)] private float initialDistance = 7.5f;
        [SerializeField, Min(0.5f)] private float minimumDistance = 1.8f;
        [SerializeField, Min(2f)] private float maximumDistance = 22f;
        [SerializeField] private float initialYaw;
        [SerializeField, Range(-10f, 80f)] private float initialPitch = 18f;
        [SerializeField] private Vector2 pitchLimits = new Vector2(-5f, 75f);
        [SerializeField, Range(0.02f, 1f)] private float orbitSensitivity = 0.18f;
        [SerializeField, Range(0.05f, 0.5f)] private float zoomSensitivity = 0.18f;
        [SerializeField, Min(0f)] private float followSmoothness = 10f;
        [SerializeField] private LayerMask collisionMask = 1 << 8;
        private Transform target;
        private VillagePrototype game;
        private Camera cameraComponent;
        private Vector3 focus;
        private float yaw;
        private float pitch;
        private float distance;
        private bool rightPressed;
        private bool rightDragged;
        private Vector2 pressPoint;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        public bool CancelClickThisFrame { get; private set; }
        public float Distance => distance;

        public void Initialize(Transform follow, VillagePrototype owner = null)
        {
            target = follow; game = owner; cameraComponent = GetComponent<Camera>();
            cameraComponent.orthographic = false;
            cameraComponent.fieldOfView = fieldOfView;
            cameraComponent.nearClipPlane = 0.08f;
            cameraComponent.farClipPlane = 180f;
            yaw = initialYaw; pitch = initialPitch;
            distance = Mathf.Clamp(initialDistance, minimumDistance, maximumDistance);
            focus = target.position + Vector3.up * pivotHeight;
            UpdatePose(true);
        }

        private bool OverUi(Vector2 position)
        {
            if (EventSystem.current == null) return false;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            return uiHits.Count > 0;
        }

        private void Update()
        {
            CancelClickThisFrame = false;
            if (target == null || Mouse.current == null) return;
            var mouse = Mouse.current;
            Vector2 position = mouse.position.ReadValue();
            bool blocked = game != null && game.ControlsBlocked;
            bool overUi = OverUi(position);
            if (mouse.rightButton.wasPressedThisFrame && !blocked && !overUi)
            {
                rightPressed = true; rightDragged = false; pressPoint = position;
            }
            if (rightPressed && mouse.rightButton.isPressed)
            {
                if ((position - pressPoint).sqrMagnitude > 25f) rightDragged = true;
                if (rightDragged)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yaw += delta.x * orbitSensitivity;
                    pitch = Mathf.Clamp(pitch - delta.y * orbitSensitivity, pitchLimits.x, pitchLimits.y);
                }
            }
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                CancelClickThisFrame = rightPressed && !rightDragged && !blocked && !overUi;
                rightPressed = false;
            }
            if (!blocked && !overUi)
                distance = OrbitZoom.Apply(distance, mouse.scroll.ReadValue().y, minimumDistance, maximumDistance, zoomSensitivity);
        }

        private void LateUpdate() { if (target != null) UpdatePose(false); }

        private void UpdatePose(bool instant)
        {
            Vector3 desiredFocus = target.position + Vector3.up * pivotHeight;
            focus = instant ? desiredFocus : Vector3.Lerp(focus, desiredFocus, 1f - Mathf.Exp(-Time.unscaledDeltaTime * followSmoothness));
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 direction = rotation * Vector3.back;
            float resolved = distance;
            if (Physics.SphereCast(focus, 0.18f, direction, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
                resolved = Mathf.Max(0.35f, hit.distance - 0.08f);
            Vector3 position = focus + direction * resolved;
            position.y = Mathf.Max(position.y, target.position.y + 0.2f);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
        }
    }
}
