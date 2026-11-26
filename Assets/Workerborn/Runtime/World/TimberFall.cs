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
        private Action<TimberPose, Vector3> onLanded;
        private Vector3? lastContactPoint;

        internal static TimberFall Begin(TimberSite site, float yaw, Mesh bark, Transform parent, Action<TimberPose> onSettled,
            Action<TimberPose, Vector3> onLanded = null, TimberPose resumeAt = default)
        {
            float length = site.Section(TimberPart.Trunk).Length;
            var pivot = new Vector3(site.Point.x, site.BaseHeight + site.CutHeight, site.Point.z);
            Quaternion orientation = Quaternion.Euler(0f, yaw, 0f);
            var pose = resumeAt.IsValid ? resumeAt : TimberLocation.Pose(pivot + Vector3.up * (length * .5f), orientation);
            var fall = Create(site.BaseHeight, site.BarkRadius, pose, yaw, bark, length, 0f, parent, 12f, onSettled);
            if (resumeAt.IsValid)
                return fall;
            fall.onLanded = onLanded;
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
            var fall = Create(site.BaseHeight, site.BarkRadius, pose, cutYaw, bark, section.Length, meshOffset, parent, 3f, onSettled);
            Vector3 alongCut = Vector3.ProjectOnPlane(fall.transform.up, Vector3.up);
            alongCut = alongCut.sqrMagnitude > .01f ? alongCut.normalized : Quaternion.Euler(0f, cutYaw, 0f) * Vector3.forward;
            fall.body.AddForce(alongCut * (side * SeparationSpeed), ForceMode.VelocityChange);
            return fall;
        }

        internal static TimberFall Throw(TimberPose release, Vector3 aim, float radius, float length, Mesh bark, float meshOffset,
            float cutYaw, Transform parent, Action<TimberPose> settled, Action<TimberPose, Vector3> landed)
        {
            var fall = Create(Mathf.Min(release.height, aim.y), radius, release, cutYaw, bark, length, meshOffset, parent, 7f, settled);
            fall.onLanded = landed;
            Vector3 delta = aim - TimberLocation.Position(release);
            Vector3 horizontal = Vector3.ProjectOnPlane(delta, Vector3.up);
            float flight = Mathf.Clamp(.54f + horizontal.magnitude * .075f, .62f, 1.15f);
            fall.body.AddForce(delta / flight - Physics.gravity * (.5f * flight), ForceMode.VelocityChange);
            Vector3 spin = horizontal.sqrMagnitude > .001f ? horizontal.normalized : Vector3.forward;
            fall.body.AddTorque(spin * 5.4f + Vector3.up * 1.1f, ForceMode.VelocityChange);
            return fall;
        }

        private static TimberFall Create(float height, float radius, TimberPose pose, float cutYaw, Mesh bark, float length, float meshOffset,
            Transform parent, float settleLimit, Action<TimberPose> onSettled)
        {
            var prop = new GameObject("Moving timber collider") { layer = 2 };
            prop.transform.SetParent(parent, false);
            prop.transform.SetPositionAndRotation(TimberLocation.Position(pose), TimberLocation.Rotation(pose));
            var fall = prop.AddComponent<TimberFall>();
            fall.onSettled = onSettled;
            fall.settleLimit = settleLimit;
            fall.lowestHeight = height - 16f;
            fall.trunk = AddTrunkCollider(prop, bark, cutYaw, radius, length, meshOffset);
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
            RememberContact(collision);
            if (onLanded == null)
                return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                if (contact.normal.y < .55f ||
                    Mathf.Max(Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal)), collision.impulse.magnitude / body.mass) < .5f)
                    continue;
                AnnounceLanding(TimberLocation.Pose(body.position, body.rotation), contact.point + contact.normal * .025f);
                break;
            }
        }

        private void OnCollisionStay(Collision collision) => RememberContact(collision);

        private void RememberContact(Collision collision)
        {
            lastContact = Time.fixedTime;
            if (onLanded != null && collision.contactCount > 0)
            {
                var contact = collision.GetContact(0);
                lastContactPoint = contact.point + contact.normal * .025f;
            }
        }

        private void AnnounceLanding(TimberPose pose, Vector3 contact)
        {
            var landed = onLanded;
            onLanded = null;
            landed?.Invoke(pose, contact);
        }

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
            // A trunk can come to rest propped against another tree without a downward impact.
            if (lastContactPoint is Vector3 contact)
                AnnounceLanding(restingPose, contact);
            onSettled?.Invoke(restingPose);
        }
    }
}
