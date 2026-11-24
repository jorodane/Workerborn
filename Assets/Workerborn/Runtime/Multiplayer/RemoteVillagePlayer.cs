using UnityEngine;

namespace Workerborn
{
    public sealed class RemoteVillagePlayer : MonoBehaviour
    {
        [SerializeField] private Transform visual;
        [SerializeField] private WorkerAnimation animationDriver;
        [SerializeField, Min(0f)] private float labelHeight = 1.9f;
        private PlayerOverheadView label;
        private VillageHud hud;
        private Vector3 destination;
        private Quaternion yaw;
        private bool hasPose;
        private Transform modularModel;
        private int lookSelection;

        internal void Bind(CharacterDefinition appearance, VillageHud display, string name, CharacterLook look)
        {
            hud = display;
            gameObject.name = name;
            if (visual != null && appearance != null && appearance.BasePrefab != null)
            {
                var model = CharacterAssembler.Build(appearance, visual, look);
                if (appearance.UsesP09)
                {
                    modularModel = model.transform;
                    lookSelection = look.ToSelection();
                }
                animationDriver?.Initialize(model.GetComponentInChildren<Animator>(), appearance.Idle, appearance.Run, appearance.Work, appearance.Walk);
            }
            label = hud.CreatePlayerOverhead(name);
        }

        internal void Say(string message) => label?.Say(message);
        internal bool OccupiesSeat(Vector3 position) => hasPose && animationDriver != null &&
            animationDriver.CurrentAction == (int)WorkerMotion.SeatSit && (position - destination).sqrMagnitude < .04f;

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
            if (!hasPose || Vector3.SqrMagnitude(destination - transform.position) > 64f)
                transform.SetPositionAndRotation(destination, yaw);
            hasPose = true;
            animationDriver?.SetAction(avatar.motion);
        }

        private void LateUpdate()
        {
            if (!hasPose)
                return;
            float factor = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, destination, factor),
                Quaternion.Slerp(transform.rotation, yaw, factor));
            if (label != null && hud != null)
                hud.PositionPlayerOverhead(label, transform.position, transform.position + Vector3.up * labelHeight);
        }

        private void OnDestroy()
        {
            if (label != null)
                Destroy(label.gameObject);
        }
    }
}
