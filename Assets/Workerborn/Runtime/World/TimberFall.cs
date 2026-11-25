using System;
using UnityEngine;

namespace Workerborn
{
    internal sealed class TimberFall : MonoBehaviour
    {
        private Rigidbody body;
        private CapsuleCollider trunk;
        private SphereCollider canopy;
        private Vector3 axis;
        private float elapsed, stillFor;
        private Action<float> onSettled;

        private float Tilt(Quaternion rotation) => Vector3.SignedAngle(Vector3.up, rotation * Vector3.up, axis);
        internal float Angle => Mathf.Clamp(Tilt(transform.rotation), 0f, TimberWork.MaximumFallAngle);

        internal static TimberFall Begin(TimberSite site, float yaw, Transform parent, Action<float> onSettled)
        {
            var prop = new GameObject("Falling timber collider") { layer = 2 };
            prop.transform.SetParent(parent, false);
            var fall = prop.AddComponent<TimberFall>();
            fall.onSettled = onSettled;
            float length = site.Section(TimberPart.Trunk).Length;
            var pivot = new Vector3(site.Point.x, site.BaseHeight + site.CutHeight, site.Point.z);
            Quaternion orientation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 arm = orientation * (Vector3.up * (length * .5f));
            prop.transform.SetPositionAndRotation(pivot + arm, orientation);
            fall.axis = orientation * Vector3.right;

            fall.trunk = prop.AddComponent<CapsuleCollider>();
            fall.trunk.radius = Mathf.Min(length * .23f, site.BarkRadius);
            fall.trunk.height = length;
            fall.canopy = prop.AddComponent<SphereCollider>();
            fall.canopy.center = Vector3.down * (length * .23f);
            fall.canopy.radius = length * .17f;
            fall.body = prop.AddComponent<Rigidbody>();
            fall.body.mass = 120f * length;
            fall.body.interpolation = RigidbodyInterpolation.Interpolate;
            fall.body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            fall.body.solverIterations = 12;

            // The remaining fibers act as a hinge at the cut. Gravity and contacts determine when the tree comes to rest.
            var hinge = prop.AddComponent<HingeJoint>();
            hinge.autoConfigureConnectedAnchor = false;
            hinge.anchor = Vector3.down * (length * .5f);
            hinge.connectedAnchor = pivot;
            hinge.axis = Vector3.right;
            Vector3 push = fall.axis * .65f;
            fall.body.AddTorque(push, ForceMode.VelocityChange);
            fall.body.AddForce(Vector3.Cross(push, arm), ForceMode.VelocityChange);
            return fall;
        }

        private void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;
            bool still = body.angularVelocity.sqrMagnitude < .01f;
            stillFor = still ? stillFor + Time.fixedDeltaTime : 0f;
            float settled = Tilt(body.rotation);
            if (elapsed < 5f && settled < TimberWork.MaximumFallAngle && (elapsed < .45f || !body.IsSleeping() && stillFor < .28f))
                return;

            // Record the physics pose. Cut pieces are interactive only after the authoritative result is applied.
            enabled = false;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            trunk.enabled = false;
            canopy.enabled = false;
            onSettled?.Invoke(settled);
        }
    }
}
