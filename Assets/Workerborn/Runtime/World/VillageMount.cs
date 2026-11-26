using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Workerborn
{
    [DefaultExecutionOrder(150), DisallowMultipleComponent]
    public sealed class VillageMount : WorldInteractable
    {
        [SerializeField] private MountDefinition definition;
        [SerializeField, Tooltip("Optional name for this individual mount.")]
        private string displayName;
        [Header("Presentation")]
        [SerializeField] private Transform model;
        [SerializeField, Tooltip("Defaults to the first collider on or under the mount root.")]
        private Collider interactionVolume;
        [SerializeField, Tooltip("Optional set of renderers used for hover. Empty uses the model's renderers.")]
        private Renderer[] pointerRenderers = System.Array.Empty<Renderer>();
        [SerializeField, FormerlySerializedAs("saddleBone")] private Transform attachmentBone;
        [SerializeField, FormerlySerializedAs("saddle")] private Transform riderAssembly;
        [SerializeField] private Transform riderRoot;
        [SerializeField] private AnimationClip riderPose;
        [SerializeField, FormerlySerializedAs("leftStirrup")] private Transform leftFootrest;
        [SerializeField, FormerlySerializedAs("rightStirrup")] private Transform rightFootrest;
        [Header("Animator States (Full Paths)")]
        [SerializeField] private string idleState;
        [SerializeField] private string walkState;
        [SerializeField] private string runState;
        [SerializeField] private string attackState;
        private int idle, walk, run, attack;
        private Animator animator;
        private Transform driver;
        private HarvestWorker localDriver;
        private RemoteVillagePlayer remoteDriver;
        private Vector3 previous;
        private int motion;
        private bool attackRequested;
        private float attackEndsAt;

        public override string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName :
            definition != null ? definition.DisplayName : "탑승물";
        protected override float DefaultDistance => definition != null ? definition.BoardingReach : base.DefaultDistance;
        public override bool ShowNameByDefault => string.IsNullOrEmpty(Rider);
        public override Vector3 NamePosition => transform.TransformPoint(NameOffset);
        public override Vector3 TargetCenter => interactionVolume != null ? interactionVolume.bounds.center : base.TargetCenter;
        public override string InteractionVerb => string.IsNullOrEmpty(Rider) ? "타기 (C)" : "탑승 중";
        internal override InteractionCursor CursorFor(HarvestWorker worker) => InteractionCursor.Enter;
        internal MountDefinition Definition => definition;
        internal string Rider { get; private set; }
        internal AnimationClip RiderPose => riderPose;
        internal float NavigationRadius => definition != null ? definition.NavigationRadius : .32f;
        internal float DismountDistance => definition != null ? definition.DismountDistance : .95f;
        internal bool CanAttack => AttackDamage > 0;
        internal int AttackDamage => definition != null ? definition.AttackDamage : 0;
        internal float AttackReach => definition != null ? definition.AttackReach : 0f;
        internal float AttackImpact => definition != null ? definition.AttackImpact : .5f;
        internal float AttackSeconds => definition != null ? definition.AttackSeconds : .45f;
        internal float Speed(bool walk) => definition != null ? definition.Speed(walk) : walk ? 1.65f : 3.7f;

        internal bool CoversPointer(Ray ray)
        {
            bool hasGeometry = false;
            foreach (var renderer in pointerRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    renderer is not (MeshRenderer or SkinnedMeshRenderer))
                    continue;
                hasGeometry = true;
                if (renderer.bounds.IntersectRay(ray))
                    return true;
            }
            return !hasGeometry && interactionVolume != null && interactionVolume.enabled && interactionVolume.bounds.IntersectRay(ray);
        }

        internal void PlaceRider(Transform visual, Vector3 basePosition, Quaternion baseRotation)
        {
            if (riderRoot == null)
                return;
            Quaternion relative = Quaternion.Inverse(transform.rotation);
            Vector3 offset = relative * (riderRoot.position - transform.position);
            Quaternion facing = relative * riderRoot.rotation;
            visual.SetLocalPositionAndRotation(basePosition + offset, facing * baseRotation);
        }

        internal bool TryGetFootrests(out Vector3 left, out Vector3 right)
        {
            left = right = default;
            if (riderRoot == null || leftFootrest == null || rightFootrest == null)
                return false;
            Quaternion relative = Quaternion.Inverse(riderRoot.rotation);
            left = relative * (leftFootrest.position - riderRoot.position);
            right = relative * (rightFootrest.position - riderRoot.position);
            return true;
        }

        internal MountSave Capture()
        {
            var pose = driver != null ? driver : transform;
            return new MountSave { id = PersistentId, x = pose.position.x, z = pose.position.z, yaw = pose.eulerAngles.y };
        }

        private void Awake()
        {
            var visuals = model != null ? model : transform;
            if (pointerRenderers == null || pointerRenderers.Length == 0)
                pointerRenderers = visuals.GetComponentsInChildren<Renderer>(true);
            if (interactionVolume == null)
                interactionVolume = GetComponentInChildren<Collider>();
            animator = visuals.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                idle = StateHash(idleState);
                walk = StateHash(walkState);
                run = StateHash(runState);
                attack = StateHash(attackState);
                // A shared gait can drive mounts that only have one locomotion clip.
                if (walk == 0)
                    walk = run;
                if (run == 0)
                    run = walk;
            }
            if (riderAssembly != null && attachmentBone != null)
                riderAssembly.SetParent(attachmentBone, worldPositionStays: true);
            previous = transform.position;
        }

        private static int StateHash(string path) => string.IsNullOrWhiteSpace(path) ? 0 : Animator.StringToHash(path);

        internal void Present(string rider, Transform follow, Vector3 point, float yaw, bool immediate)
        {
            Rider = rider;
            if (driver == follow && driver != null)
                return;
            driver = follow;
            localDriver = driver != null ? driver.GetComponent<HarvestWorker>() : null;
            remoteDriver = driver != null && localDriver == null ? driver.GetComponent<RemoteVillagePlayer>() : null;
            attackRequested = false;
            attackEndsAt = 0f;
            if (driver != null)
                transform.SetPositionAndRotation(driver.position, driver.rotation);
            else
                transform.SetPositionAndRotation(point, Quaternion.Euler(0, yaw, 0));
            if (immediate || driver == null)
                previous = transform.position;
        }

        public override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions)
        {
            if (village.Worker.CurrentMount == this)
                actions.Add(new WorldContextAction("내리기", village.Worker.RequestDismount));
            else if (village.Worker.CanMount && village.IsRegisteredMount(this) && string.IsNullOrEmpty(Rider))
                actions.Add(new WorldContextAction("타기", () => RequestInteraction(village.Worker)));
        }

        protected override void Interact(HarvestWorker worker) => worker.InteractWithMount(this);

        private void Update()
        {
            if (driver != null)
                transform.SetPositionAndRotation(driver.position, driver.rotation);
            float stride = Vector3.ProjectOnPlane(transform.position - previous, Vector3.up).magnitude;
            previous = transform.position;
            if (animator == null)
                return;

            bool attacking = CanAttack && (localDriver != null ? localDriver.Action == WorkerAction.Attacking :
                remoteDriver != null && remoteDriver.IsUsingMountAttack);
            if (attacking && !attackRequested && attack != 0 && animator.HasState(0, attack))
            {
                motion = attack;
                attackEndsAt = Time.time + AttackSeconds;
                animator.Play(attack, 0, 0f);
            }
            attackRequested = attacking;
            if (Time.time < attackEndsAt)
                return;

            float walkSpeed = Speed(true);
            float runSpeed = Speed(false);
            float elapsed = Time.deltaTime;
            float speed = elapsed > 0f && stride < Mathf.Max(2f, runSpeed * elapsed * 2f) ? stride / elapsed : 0f;
            float idleThreshold = Mathf.Min(.15f, walkSpeed * .3f);
            int next = speed < idleThreshold ? idle : speed < (walkSpeed + runSpeed) * .5f ? walk : run;
            if (next != 0 && next != motion && animator.HasState(0, next))
            {
                motion = next;
                animator.CrossFadeInFixedTime(next, .12f, 0);
            }
        }

        private void LateUpdate()
        {
            if (localDriver != null)
            {
                transform.SetPositionAndRotation(driver.position, driver.rotation);
                localDriver.AlignToMount();
            }
        }
    }
}
