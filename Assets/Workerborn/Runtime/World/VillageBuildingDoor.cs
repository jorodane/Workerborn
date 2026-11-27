using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class VillageBuildingDoor : WorldInteractable
    {
        [SerializeField, Tooltip("Permanent ID for this building. A construction plot also includes its own ID.")]
        private string buildingId;
        [SerializeField] private string displayName = "출입문";
        [SerializeField] private VillageInteriorMap interiorPrefab;
        [SerializeField, Tooltip("The walkable point to approach and return to on this side of the door.")]
        private Transform approach;
        [SerializeField] private Transform doorLeaf;
        [SerializeField, Tooltip("Optional original door kept visible in the editor.")]
        private Transform originalDoor;
        [SerializeField] private GameObject originalDoorCollider;
        [SerializeField] private GameObject hingedModel;
        [SerializeField, Range(-150f, 150f)] private float openAngle = 95f;
        [SerializeField, Range(0, 23)] private int opensAt = 6;
        [SerializeField, Range(1, 24)] private int closesAt = 20;

        private VillagePrototype village;
        private Quaternion closedRotation;
        private float fractionOpen;
        internal string BuildingId => buildingId;
        internal VillageInteriorMap InteriorPrefab => interiorPrefab;
        internal VillageInteriorMap Interior { get; private set; }
        internal Transform Approach => approach;
        internal bool IsExit => Interior != null && Interior.Exit == this;
        internal bool IsOpen => Interior != null && (IsExit || IsWithinHours(village.State.Minute, opensAt, closesAt));
        public override string DisplayName => IsExit && Interior.Lobby != null ? "여관 로비" : displayName;
        public override bool ShowNameByDefault => true;
        public override Vector3 NamePosition => transform.TransformPoint(NameOffset);
        public override bool CanInteract => Interior != null && base.CanInteract;
        public override string InteractionVerb => IsExit ? (Interior.Lobby != null ? "로비로 나가기" : "밖으로 나가기") :
            IsOpen ? "들어가기" : "문 닫힘";
        internal override InteractionCursor CursorFor(HarvestWorker worker) =>
            IsOpen ? InteractionCursor.Enter : InteractionCursor.Inspect;
        public override float InteractionDistance => approach != null
            ? Mathf.Max(base.InteractionDistance, Vector3.Distance(approach.position, transform.position) + .9f)
            : base.InteractionDistance;
        public override Vector3 TargetCenter => transform.TransformPoint(GetComponent<BoxCollider>().center);

        internal static bool IsWithinHours(float minute, int openHour, int closeHour)
        {
            int start = openHour * 60;
            int end = closeHour * 60;
            return start <= end ? minute >= start && minute < end : minute >= start || minute < end;
        }

        internal void Bind(VillagePrototype game, VillageInteriorMap interior)
        {
            village = game;
            Interior = interior;
            if (originalDoor != null)
                originalDoor.gameObject.SetActive(false);
            if (originalDoorCollider != null)
                originalDoorCollider.SetActive(false);
            if (hingedModel != null)
                hingedModel.SetActive(true);
            if (doorLeaf == null)
                return;
            closedRotation = doorLeaf.localRotation;
            SetOpenFraction(IsOpen ? 1f : 0f);
        }

        internal void Unbind() => Interior = null;

        private void LateUpdate()
        {
            if (Interior == null || doorLeaf == null)
                return;
            float next = Mathf.MoveTowards(fractionOpen, IsOpen ? 1f : 0f, Time.deltaTime * 4f);
            if (next != fractionOpen)
                SetOpenFraction(next);
        }

        private void SetOpenFraction(float value)
        {
            fractionOpen = value;
            float angle = openAngle * Mathf.SmoothStep(0f, 1f, value);
            doorLeaf.localRotation = closedRotation * Quaternion.Euler(0f, angle, 0f);
        }

        protected override void Interact(HarvestWorker worker)
        {
            if (!worker.SharesMap(transform.position))
                return;
            if (!IsOpen)
            {
                ShowClosedNotice(worker);
                return;
            }

            Vector3 point = approach.position;
            point = village.SurfacePosition(point.x, point.z);
            if (worker.CurrentMount == null && worker.InReach(point, .55f))
                Arrive(village, worker);
            else
                worker.CommandApproachAt(this, point);
        }

        protected override void OnArrived(VillagePrototype game, HarvestWorker worker)
        {
            if (!IsOpen)
            {
                ShowClosedNotice(worker);
                return;
            }
            game.UseBuildingDoor(worker, this);
        }

        private void ShowClosedNotice(HarvestWorker worker)
        {
            if (worker.IsLocallyControlled)
                village.Hud.ShowToast($"문이 닫혀 있어요. {opensAt:00}:00에 열어요.");
        }
    }
}
