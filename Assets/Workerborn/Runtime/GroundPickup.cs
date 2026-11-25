using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class GroundPickup : WorldInteractable
    {
        [SerializeField]
        private ItemDefinition item;
        [SerializeField]
        private CropDefinition definition;
        [SerializeField]
        private Transform visual;
        [SerializeField, Min(1)]
        private int quantity = 1;
        [SerializeField, Min(.01f)]
        private float flightSeconds = .48f;
        private Vector3 origin, landing;
        private float age;
        private int landedFrame = -1;
        private bool collected, initialized, respawnable;
        private Vector3 authoredPosition;
        private int authoredQuantity;
        private Action<GroundPickup> returnToPool;
        private Vector3? pooledNameOffset;
        public override bool ShowNameByDefault => true;
        public CropDefinition Definition => definition;
        internal Transform Visual => visual;
        internal GroundPickup AppearanceSource { get; set; }
        internal GroundDropSave DropRecord { get; private set; }
        internal EquipmentCondition Condition => DropRecord?.condition ?? default;
        internal string SharedDropId { get; private set; }
        internal bool IsPooled => returnToPool != null;
        public string ItemId => item != null ? item.Id : definition != null ? definition.ItemId : string.Empty;
        public int Quantity => quantity;
        internal string ItemDisplayName => item != null ? item.DisplayName : definition != null ? definition.DisplayName : name;
        public override string DisplayName => Quantity > 1 ? $"{ItemDisplayName}({Quantity:N0})" : ItemDisplayName;
        protected override float DefaultDistance => HarvestWorker.DefaultPickupDistance;
        public bool CanCollect => !collected && isActiveAndEnabled;
        public bool IsInFlight => initialized && age < flightSeconds;
        internal bool ReadyForAutoPickup => !IsInFlight && landedFrame != Time.frameCount;
        internal int FlightMilliseconds => Mathf.CeilToInt(flightSeconds * 1_000f);
        public override bool CanInteract => CanCollect;
        public override string InteractionVerb => "줍기";
        public override string InteractionVerbFor(HarvestWorker worker) => CanCollectFor(worker) ? InteractionVerb : "소유자 보호 중";
        internal override InteractionCursor CursorFor(HarvestWorker worker) =>
            CanCollectFor(worker) ? InteractionCursor.PickUp : InteractionCursor.Inspect;
        public Vector3 LandingPosition => initialized ? landing : transform.position;
        public Vector3 VisualPosition => visual != null ? visual.position : transform.position;
        public override Vector3 NamePosition => pooledNameOffset.HasValue ? transform.position + pooledNameOffset.Value : base.NamePosition;
        public event Action<Vector3> Landed;

        public override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions)
        {
            if (CanCollectFor(village.Worker))
                base.AddContextActions(village, actions);
            string id = ItemId;
            int count = Quantity;
            var condition = Condition;
            actions.Add(new WorldContextAction("자세히 보기", () => village.Hud.InspectGroundItem(id, count, condition)));
        }

        protected override void Interact(HarvestWorker worker) => worker.CommandPickup(this);
        protected override void OnEnable()
        {
            if (returnToPool != null)
                RenewInteractionGeneration();
            base.OnEnable();
        }

        private void Start()
        {
            if (!initialized)
                Initialize(definition, visual, transform.position, transform.position, true);
        }

        internal void BindPool(Action<GroundPickup> release) => returnToPool = release;

        internal void ApplyDefinition(GroundPickup source)
        {
            item = source.item;
            flightSeconds = source.flightSeconds;
            pooledNameOffset = source.NamePosition - source.transform.position;
        }

        internal void BindDrop(GroundDropSave record, bool shared = false)
        {
            DropRecord = record;
            SharedDropId = shared ? record.id : null;
        }

        internal bool CanCollectFor(HarvestWorker worker) => CanCollect &&
            (DropRecord == null || DropRecord.CanCollect(worker?.PlayerId, worker?.Village?.PickupUtcNow ?? GroundDropSave.UtcNow));

        public void SetQuantity(int amount) => quantity = Mathf.Max(1, amount);
        public void Initialize(CropDefinition crop, Vector3 start, Vector3 end, bool settled = false) => Initialize(crop, visual, start, end, settled);
        public void Initialize(CropDefinition crop, Transform model, Vector3 start, Vector3 end, bool settled = false)
        {
            DropRecord = null;
            SharedDropId = null;
            initialized = true;
            collected = false;
            definition = crop;
            visual = model;
            origin = start;
            landing = end;
            age = settled ? flightSeconds : 0f;
            landedFrame = -1;
            transform.position = settled ? end : start;
        }

        private void Update()
        {
            if (!IsInFlight)
                return;
            age = Mathf.Min(age + Time.deltaTime, flightSeconds);
            float t = age / flightSeconds;
            transform.position = t < 1f ? Vector3.Lerp(origin, landing, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.25f) : landing;
            if (t >= 1f)
            {
                landedFrame = Time.frameCount;
                Landed?.Invoke(landing);
            }
        }

        public bool TryCollect(Func<bool> receive = null) => TryCollect(null, receive);

        internal bool TryCollect(HarvestWorker worker, Func<bool> receive = null)
        {
            if (SharedDropId != null || !CanCollectFor(worker))
                return false;
            if (receive != null && !receive())
                return false;
            CompleteCollection();
            return true;
        }

        internal void CompleteCollection()
        {
            collected = true;
            NotifyInteractionCompleted();
            Despawn();
        }

        internal void KeepForRespawn()
        {
            respawnable = true;
            authoredPosition = transform.position;
            authoredQuantity = quantity;
        }

        internal void RespawnAuthored()
        {
            if (!respawnable)
                return;
            SetQuantity(authoredQuantity);
            Initialize(definition, authoredPosition, authoredPosition, settled: true);
            gameObject.SetActive(true);
        }

        internal void Despawn()
        {
            if (!gameObject.activeSelf)
                return;
            Landed = null;
            if (returnToPool != null)
            {
                initialized = false;
                definition = null;
                item = null;
                returnToPool(this);
                return;
            }
            gameObject.SetActive(false);
            if (!respawnable)
                Destroy(gameObject);
        }
    }
}
