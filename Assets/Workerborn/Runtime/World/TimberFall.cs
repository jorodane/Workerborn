using System;
using UnityEngine;

namespace Workerborn
{
    internal sealed class TimberFall : MonoBehaviour
    {
        private const float ReleaseTilt = 72f;
        private const float UnbalancedAngularSpeed = .16f;
        private const float SeparationSpeed = 1.05f;
        private Rigidbody body;
        private Collider trunk;
        private HingeJoint hinge;
        private float elapsed, stillFor, lastContact = float.NegativeInfinity;
        private float lowestHeight, settleLimit;
        private Action<TimberPose> onSettled;
        private Action<Vector3> onGroundImpact;

        internal static TimberFall Begin(TimberSite site, float yaw, Mesh bark, Transform parent, Action<TimberPose> onSettled,
            Action<Vector3> onGroundImpact = null)
        {
            float length = site.Section(TimberPart.Trunk).Length;
            var pivot = new Vector3(site.Point.x, site.BaseHeight + site.CutHeight, site.Point.z);
            Quaternion orientation = Quaternion.Euler(0f, yaw, 0f);
            var pose = TimberLocation.Pose(pivot + Vector3.up * (length * .5f), orientation);
            var fall = Create(site, pose, yaw, bark, length, 0f, parent, 12f, onSettled);
            fall.onGroundImpact = onGroundImpact;
            // The pivot lets gravity accelerate the initial tip before the severed trunk is released.
            fall.hinge = fall.gameObject.AddComponent<HingeJoint>();
            fall.hinge.autoConfigureConnectedAnchor = false;
            fall.hinge.anchor = Vector3.down * (length * .5f);
            fall.hinge.connectedAnchor = pivot;
            fall.hinge.axis = Vector3.right;
            Vector3 nudge = orientation * Vector3.right * UnbalancedAngularSpeed;
            fall.body.AddTorque(nudge, ForceMode.VelocityChange);
            fall.body.AddForce(Vector3.Cross(nudge, fall.body.worldCenterOfMass - pivot), ForceMode.VelocityChange);
            return fall;
        }

        internal static TimberFall Separate(TimberSite site, TimberSection section, TimberPose pose, float cutYaw, Mesh bark,
            float meshOffset, int side, Transform parent, Action<TimberPose> onSettled)
        {
            var fall = Create(site, pose, cutYaw, bark, section.Length, meshOffset, parent, 3f, onSettled);
            Vector3 alongCut = Vector3.ProjectOnPlane(fall.transform.up, Vector3.up);
            alongCut = alongCut.sqrMagnitude > .01f ? alongCut.normalized : Quaternion.Euler(0f, cutYaw, 0f) * Vector3.forward;
            fall.body.AddForce(alongCut * (side * SeparationSpeed), ForceMode.VelocityChange);
            return fall;
        }

        private static TimberFall Create(TimberSite site, TimberPose pose, float cutYaw, Mesh bark, float length, float meshOffset,
            Transform parent, float settleLimit, Action<TimberPose> onSettled)
        {
            var prop = new GameObject("Moving timber collider") { layer = 2 };
            prop.transform.SetParent(parent, false);
            prop.transform.SetPositionAndRotation(TimberLocation.Position(pose), TimberLocation.Rotation(pose));
            var fall = prop.AddComponent<TimberFall>();
            fall.onSettled = onSettled;
            fall.settleLimit = settleLimit;
            fall.lowestHeight = site.BaseHeight - 16f;
            fall.trunk = AddTrunkCollider(prop, bark, cutYaw, site.BarkRadius, length, meshOffset);
            fall.body = prop.AddComponent<Rigidbody>();
            fall.body.mass = 120f * length;
            fall.body.interpolation = RigidbodyInterpolation.Interpolate;
            fall.body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            fall.body.solverIterations = 12;
            fall.body.solverVelocityIterations = 6;
            return fall;
        }

        private static Collider AddTrunkCollider(GameObject prop, Mesh bark, float yaw, float radius, float length, float meshOffset)
        {
            if (bark != null && bark.vertexCount >= 4)
            {
                var collision = new GameObject("Trunk collision") { layer = prop.layer };
                collision.transform.SetParent(prop.transform, false);
                collision.transform.localRotation = Quaternion.Euler(0f, -yaw, 0f);
                collision.transform.localPosition = Vector3.up * meshOffset;
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

        private void OnCollisionEnter(Collision collision)
        {
            lastContact = Time.fixedTime;
            if (onGroundImpact == null)
                return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                if (contact.normal.y < .55f ||
                    Mathf.Max(Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal)), collision.impulse.magnitude / body.mass) < .5f)
                    continue;
                var play = onGroundImpact;
                onGroundImpact = null;
                play(contact.point + contact.normal * .025f);
                break;
            }
        }
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
            if (stillFor < .30f && !body.IsSleeping() && elapsed < settleLimit && body.position.y > lowestHeight)
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
