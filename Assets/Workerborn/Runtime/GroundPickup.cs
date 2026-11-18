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
        private static long nextOrder;
        private Vector3 origin, landing, visualOrigin;
        private float age;
        private bool collected, initialized;
        public long CreationOrder { get; private set; }
        public CropDefinition Definition => definition;
        public string ItemId => item != null ? item.Id : definition != null ? definition.ItemId : string.Empty;
        public int Quantity => quantity;
        public override string DisplayName => item != null ? item.DisplayName : definition != null ? definition.DisplayName : name;
        protected override float DefaultDistance => 1f;
        public bool CanCollect => !collected && isActiveAndEnabled;
        public override bool CanInteract => CanCollect;
        public override string InteractionVerb => "줍기";
        public Vector3 LandingPosition => initialized ? landing : transform.position;

        protected override void Interact(HarvestWorker worker) => worker.CommandPickup(this);
        protected override void OnEnable()
        {
            base.OnEnable();
            if (CreationOrder == 0)
                CreationOrder = ++nextOrder;
        }

        private void Start()
        {
            if (!initialized)
                Initialize(definition, visual, transform.position, transform.position, true);
        }

        public void SetQuantity(int amount) => quantity = Mathf.Max(1, amount);
        public void Initialize(CropDefinition crop, Vector3 start, Vector3 end, bool settled = false) => Initialize(crop, visual, start, end, settled);
        public void Initialize(CropDefinition crop, Transform model, Vector3 start, Vector3 end, bool settled = false)
        {
            initialized = true;
            definition = crop;
            visual = model;
            visualOrigin = visual != null ? visual.localPosition : Vector3.zero;
            origin = start;
            landing = end;
            age = settled ? flightSeconds : 0f;
            transform.position = start;
        }

        private void Update()
        {
            if (!initialized)
                return;
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / flightSeconds);
            transform.position = Vector3.Lerp(origin, landing, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.25f);
            if (visual == null)
                return;
            visual.localPosition = visualOrigin + Vector3.up * (t < 1f ? 0f : .04f + Mathf.Sin(age * 3f) * .035f);
            visual.Rotate(Vector3.up, Time.deltaTime * 28f, Space.World);
        }

        public bool TryCollect(System.Func<bool> receive = null)
        {
            if (!CanCollect)
                return false;
            if (receive != null && !receive())
                return false;
            collected = true;
            NotifyInteractionCompleted();
            gameObject.SetActive(false);
            Destroy(gameObject);
            return true;
        }
    }
}
