using System;
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
        private Vector3 origin, landing, visualOrigin;
        private float age;
        private bool collected, initialized, respawnable;
        private Vector3 authoredPosition;
        private int authoredQuantity;
        private Action<GroundPickup> returnToPool;
        private Vector3? pooledNameOffset;
        public override bool ShowNameByDefault => true;
        public CropDefinition Definition => definition;
        internal Transform Visual => visual;
        internal GroundPickup AppearanceSource { get; set; }
        internal bool IsPooled => returnToPool != null;
        public string ItemId => item != null ? item.Id : definition != null ? definition.ItemId : string.Empty;
        public int Quantity => quantity;
        public override string DisplayName => item != null ? item.DisplayName : definition != null ? definition.DisplayName : name;
        protected override float DefaultDistance => 1.5f;
        public bool CanCollect => !collected && isActiveAndEnabled;
        public bool IsInFlight => initialized && age < flightSeconds;
        public override bool CanInteract => CanCollect;
        public override string InteractionVerb => "줍기";
        public Vector3 LandingPosition => initialized ? landing : transform.position;
        public Vector3 VisualPosition => visual != null ? visual.position : transform.position;
        public override Vector3 NamePosition => pooledNameOffset.HasValue ? transform.position + pooledNameOffset.Value : base.NamePosition;
        public event Action<Vector3> Landed;

        protected override void Interact(HarvestWorker worker) => worker.CommandPickup(this);
        protected override void OnEnable()
        {
            if (returnToPool != null)
                RenewNameplateOrder();
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

        public void SetQuantity(int amount) => quantity = Mathf.Max(1, amount);
        public void Initialize(CropDefinition crop, Vector3 start, Vector3 end, bool settled = false) => Initialize(crop, visual, start, end, settled);
        public void Initialize(CropDefinition crop, Transform model, Vector3 start, Vector3 end, bool settled = false)
        {
            initialized = true;
            collected = false;
            definition = crop;
            visual = model;
            visualOrigin = visual != null ? visual.localPosition : Vector3.zero;
            origin = start;
            landing = end;
            age = settled ? flightSeconds : 0f;
            transform.position = settled ? end : start;
        }

        private void Update()
        {
            if (!initialized)
                return;
            bool inFlight = age < flightSeconds;
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / flightSeconds);
            if (inFlight)
                transform.position = t < 1f ? Vector3.Lerp(origin, landing, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.25f) : landing;
            if (visual != null)
            {
                visual.localPosition = visualOrigin + Vector3.up * (t < 1f ? 0f : .04f + Mathf.Sin(age * 3f) * .035f);
                visual.Rotate(Vector3.up, Time.deltaTime * 28f, Space.World);
            }
            if (inFlight && t >= 1f)
                Landed?.Invoke(landing);
        }

        public bool TryCollect(Func<bool> receive = null)
        {
            if (!CanCollect)
                return false;
            if (receive != null && !receive())
                return false;
            collected = true;
            NotifyInteractionCompleted();
            Despawn();
            return true;
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
