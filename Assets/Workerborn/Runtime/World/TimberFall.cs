using System;
using UnityEngine;

namespace Workerborn
{
    internal sealed class TimberFall : MonoBehaviour
    {
        private const float ReleaseTilt = 72f;
        private const float UnbalancedAngularSpeed = .16f;
        private Rigidbody body;
        private Collider trunk;
        private HingeJoint hinge;
        private float elapsed, stillFor, lastContact = float.NegativeInfinity;
        private float lowestHeight;
        private Action<TimberPose> onSettled;

        internal static TimberFall Begin(TimberSite site, float yaw, Mesh bark, Transform parent, Action<TimberPose> onSettled)
        {
            var prop = new GameObject("Falling timber collider") { layer = 2 };
            prop.transform.SetParent(parent, false);
            var fall = prop.AddComponent<TimberFall>();
            fall.onSettled = onSettled;
            fall.lowestHeight = site.BaseHeight - 16f;
            float length = site.Section(TimberPart.Trunk).Length;
            var pivot = new Vector3(site.Point.x, site.BaseHeight + site.CutHeight, site.Point.z);
            Quaternion orientation = Quaternion.Euler(0f, yaw, 0f);
            prop.transform.SetPositionAndRotation(pivot + Vector3.up * (length * .5f), orientation);

            fall.trunk = AddTrunkCollider(prop, bark, yaw, site.BarkRadius, length);
            fall.body = prop.AddComponent<Rigidbody>();
            fall.body.mass = 120f * length;
            fall.body.interpolation = RigidbodyInterpolation.Interpolate;
            fall.body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            fall.body.solverIterations = 12;
            fall.body.solverVelocityIterations = 6;

            // The pivot lets gravity accelerate the initial tip before the severed trunk is released.
            fall.hinge = prop.AddComponent<HingeJoint>();
            fall.hinge.autoConfigureConnectedAnchor = false;
            fall.hinge.anchor = Vector3.down * (length * .5f);
            fall.hinge.connectedAnchor = pivot;
            fall.hinge.axis = Vector3.right;
            Vector3 nudge = orientation * Vector3.right * UnbalancedAngularSpeed;
            fall.body.AddTorque(nudge, ForceMode.VelocityChange);
            fall.body.AddForce(Vector3.Cross(nudge, fall.body.worldCenterOfMass - pivot), ForceMode.VelocityChange);
            return fall;
        }

        private static Collider AddTrunkCollider(GameObject prop, Mesh bark, float yaw, float radius, float length)
        {
            if (bark != null && bark.vertexCount >= 4)
            {
                var collision = new GameObject("Trunk collision") { layer = prop.layer };
                collision.transform.SetParent(prop.transform, false);
                collision.transform.localRotation = Quaternion.Euler(0f, -yaw, 0f);
                var hull = collision.AddComponent<MeshCollider>();
                hull.convex = true;
                hull.sharedMesh = bark;
                return hull;
            }

            var capsule = prop.AddComponent<CapsuleCollider>();
            capsule.radius = Mathf.Min(length * .23f, radius);
            capsule.height = length;
            return capsule;
        }

        private void OnCollisionEnter() => lastContact = Time.fixedTime;
        private void OnCollisionStay() => lastContact = Time.fixedTime;

        private void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;
            bool inContact = Time.fixedTime - lastContact <= Time.fixedDeltaTime * 2f;
            if (hinge != null)
            {
                float tilt = Vector3.Angle(Vector3.up, body.rotation * Vector3.up);
                if (tilt >= ReleaseTilt || (elapsed >= .25f && inContact) || elapsed >= 3.5f)
                {
                    Destroy(hinge);
                    hinge = null;
                }
                return;
            }

            Vector3 velocity = body.GetPointVelocity(body.worldCenterOfMass);
            bool still = inContact && body.angularVelocity.sqrMagnitude < .016f && velocity.sqrMagnitude < .0144f;
            stillFor = still ? stillFor + Time.fixedDeltaTime : 0f;
            if (stillFor < .30f && !body.IsSleeping() && elapsed < 12f && body.position.y > lowestHeight)
                return;

            var restingPose = TimberLocation.Pose(body.position, body.rotation);
            enabled = false;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            trunk.enabled = false;
            onSettled?.Invoke(restingPose);
        }
    }
}
