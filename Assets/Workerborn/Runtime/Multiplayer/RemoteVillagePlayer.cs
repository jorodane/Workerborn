using UnityEngine;

namespace Workerborn
{
    public sealed class RemoteVillagePlayer : MonoBehaviour
    {
        [SerializeField] private Transform visual;
        [SerializeField] private WorkerAnimation animationDriver;
        [SerializeField, Min(0f)] private float labelHeight = 1.9f;
        private PlayerOverheadView label;
        private Transform head;
        private VillageHud hud;
        private Vector3 destination;
        private Vector3 seatPosition;
        private Quaternion yaw;
        private bool hasPose;
        private bool hasSeatPose;
        private float seatLift;
        private SessionAvatar afterStanding;
        private Transform modularModel;
        private int lookSelection;
        private Vector3 visualBase;
        private Quaternion visualRotation;
        private VillageMount mount;
        internal bool IsUsingMountAttack { get; private set; }
        private float jumpOffset, shownJumpOffset;
        private TimberAssets timber;
        private TimberSwing timberSwing;

        internal void Bind(CharacterDefinition appearance, VillageHud display, string name, CharacterLook look)
        {
            hud = display;
            gameObject.name = name;
            visualBase = visual != null ? visual.localPosition : Vector3.zero;
            visualRotation = visual != null ? visual.localRotation : Quaternion.identity;
            if (visual != null && appearance != null && appearance.BasePrefab != null)
            {
                var model = CharacterAssembler.Build(appearance, visual, look);
                if (appearance.UsesP09)
                {
                    modularModel = model.transform;
                    lookSelection = look.ToSelection();
                }
                var animator = model.GetComponentInChildren<Animator>();
                animationDriver?.Initialize(animator, appearance.Idle, appearance.Run, appearance.Work, appearance.Walk);
                if (animator != null && animator.avatar != null && animator.avatar.isValid && animator.isHuman)
                    head = animator.GetBoneTransform(HumanBodyBones.Head);
            }
            CharacterActor.SuppressBodyCollision(gameObject);
            label = hud.CreatePlayerOverhead(name);
        }

        internal void Say(string message) => label?.Say(message);

        internal void ShowMount(VillageMount next)
        {
            if (ReferenceEquals(mount, next))
                return;
            mount = next;
            animationDriver?.SetRiding(mount, visual);
        }
        internal void ShowTimberCarry(TimberAssets assets, bool carrying)
        {
            timber = assets;
            animationDriver?.ConfigureTimberToss(assets?.Toss);
            animationDriver?.SetCarrying(assets?.Carry, carrying);
        }
        internal bool OccupiesSeat(Vector3 position) => hasPose && animationDriver != null &&
            hasSeatPose && (animationDriver.CurrentAction == (int)WorkerMotion.SeatSit || animationDriver.IsStandingUp) &&
            (position - seatPosition).sqrMagnitude < .04f;

        internal void SetPose(SessionAvatar avatar, float surfaceY)
        {
            if (modularModel != null && lookSelection != avatar.look.ToSelection())
            {
                lookSelection = avatar.look.ToSelection();
                P09Wardrobe.Apply(modularModel, avatar.look.Validated());
            }
            float height = Mathf.Abs(avatar.y - surfaceY) < 3f ? avatar.y : surfaceY;
            destination = new Vector3(avatar.x, height, avatar.z);
            yaw = Quaternion.Euler(0, avatar.yaw, 0);
            bool standing = animationDriver?.IsStandingUp == true;
            bool enteringSeat = avatar.motion == (int)WorkerMotion.SeatSit &&
                animationDriver?.CurrentAction != (int)WorkerMotion.SeatSit && !standing;
            bool teleport = hasPose && Vector3.SqrMagnitude(destination - transform.position) > 64f;
            if (!hasPose || teleport || enteringSeat)
                transform.SetPositionAndRotation(destination, yaw);
            hasPose = true;

            if (animationDriver != null && animationDriver.CurrentAction >= (int)WorkerMotion.GroundSit &&
                avatar.motion < (int)WorkerMotion.GroundSit)
                animationDriver.SetAction((int)WorkerMotion.Idle);
            if (teleport)
                animationDriver?.SkipStandUp();
            if (animationDriver?.IsStandingUp == true)
            {
                afterStanding = avatar;
                return;
            }
            afterStanding = null;
            PresentMotion(avatar);
        }

        private void PresentMotion(SessionAvatar avatar)
        {
            hasSeatPose = avatar.motion == (int)WorkerMotion.SeatSit;
            seatLift = avatar.hasSeat ? avatar.seatLift : 0f;
            seatPosition = avatar.hasSeat ? new Vector3(avatar.seatX, avatar.seatY, avatar.seatZ) : destination;
            IsUsingMountAttack = avatar.motion == (int)WorkerMotion.Work;
            PresentTimberSwing(avatar);
            animationDriver?.SetAction(avatar.motion);
            animationDriver?.SetOvereating(avatar.overeating);
            if (animationDriver != null && float.IsFinite(avatar.gestureProgress))
                animationDriver.SetGesture((WorkerGesture)avatar.gesture, avatar.gestureProgress);
            jumpOffset = animationDriver?.Gesture != WorkerGesture.Jump && float.IsFinite(avatar.jumpOffset)
                ? Mathf.Clamp(avatar.jumpOffset, 0f, .6f) : 0f;
        }

        private void PresentTimberSwing(SessionAvatar avatar)
        {
            if (animationDriver == null || timber == null)
                return;
            var swing = (TimberSwing)avatar.timberSwing;
            if (swing != timberSwing)
            {
                timberSwing = swing;
                switch (swing)
                {
                    case TimberSwing.Fell:
                        animationDriver.BeginStrike(timber.Felling.WorkAnimation?.Loop);
                        break;
                    case TimberSwing.Split:
                    case TimberSwing.Strike:
                        animationDriver.BeginStrike(swing == TimberSwing.Split ? timber.Split : timber.Strike);
                        break;
                    default:
                        if (avatar.motion == (int)WorkerMotion.Work)
                            animationDriver.BeginWork(null, 1f);
                        break;
                }
            }
            if (swing != TimberSwing.None)
                animationDriver.SampleStrike(avatar.timberProgress);
        }

        private void Update()
        {
            if (!hasPose || animationDriver?.IsStandingUp == true)
                return;
            if (afterStanding != null)
            {
                PresentMotion(afterStanding);
                afterStanding = null;
            }
            float factor = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, destination, factor),
                Quaternion.Slerp(transform.rotation, yaw, factor));
            shownJumpOffset = Mathf.Lerp(shownJumpOffset, jumpOffset, factor);
        }

        private void LateUpdate()
        {
            if (!hasPose)
                return;
            if (visual != null)
            {
                if (mount != null)
                    mount.PlaceRider(visual, visualBase, visualRotation);
                else
                {
                    float seated = seatLift * (animationDriver?.SeatLiftWeight ?? 0f);
                    visual.localPosition = visualBase + Vector3.up * (shownJumpOffset + seated);
                    visual.localRotation = visualRotation;
                }
            }
            if (label != null && hud != null)
            {
                Vector3 namePosition = head != null ? head.position + Vector3.up * .32f
                    : (visual != null ? visual.position : transform.position) + Vector3.up * labelHeight;
                hud.PositionPlayerOverhead(label, transform.position, namePosition);
            }
        }

        private void OnDestroy()
        {
            if (label != null)
                Destroy(label.gameObject);
        }
    }
}
