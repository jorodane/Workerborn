using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Workerborn
{
    public sealed class VillageCameraRig : MonoBehaviour
    {
        private Transform target;
        private Camera cameraComponent;
        private Vector3 focus;
        private float yaw = 30f;
        private float zoom = 10.5f;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        public void Initialize(Transform follow)
        {
            target = follow;
            cameraComponent = GetComponent<Camera>();
            focus = follow.position;
            UpdatePose(true);
        }

        private void LateUpdate()
        {
            if (target == null) return;
            var mouse = Mouse.current;
            bool overUi = false;
            if (mouse != null && EventSystem.current != null)
            {
                uiHits.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = mouse.position.ReadValue() }, uiHits);
                overUi = uiHits.Count > 0;
            }
            if (mouse != null && !overUi)
            {
                zoom = Mathf.Clamp(zoom - mouse.scroll.ReadValue().y / 120f, 7f, 19f);
                if (mouse.middleButton.isPressed) yaw += mouse.delta.ReadValue().x * 0.2f;
            }
            UpdatePose(false);
        }

        private void UpdatePose(bool instant)
        {
            Vector3 destination = target.position + Vector3.forward * 1.5f;
            focus = instant ? destination : Vector3.Lerp(focus, destination, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f));
            Quaternion rotation = Quaternion.Euler(52f, yaw, 0f);
            transform.SetPositionAndRotation(focus + rotation * Vector3.back * 35f, rotation);
            cameraComponent.orthographicSize = Mathf.Max(zoom, zoom * 1.35f / Mathf.Max(0.5f, cameraComponent.aspect));
        }
    }
}
