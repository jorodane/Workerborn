using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Workerborn
{
    public readonly struct WorldContextAction
    {
        public readonly string Label;
        public readonly System.Action Select;

        public WorldContextAction(string label, System.Action select)
        {
            Label = label;
            Select = select;
        }
    }

    // Shared click/selection contract. Subclasses own their gameplay behavior.
    public abstract class WorldInteractable : MonoBehaviour
    {
        private static readonly List<WorldInteractable> active = new List<WorldInteractable>();
        public static IReadOnlyList<WorldInteractable> Active => active;

        [SerializeField]
        private Vector3 nameOffset = Vector3.up * 1.9f;
        [SerializeField]
        private UnityEvent onInteractionRequested = new UnityEvent();
        [SerializeField]
        private string persistentId;
        [SerializeField]
        private UnityEvent onInteractionCompleted = new UnityEvent();
        public string PersistentId => persistentId;
        public abstract string DisplayName { get; }

        [SerializeField, Min(0f), Tooltip("0 uses this interaction type's default distance.")]
        private float interactionDistance;
        protected virtual float DefaultDistance => 1.8f;
        public virtual float InteractionDistance => Mathf.Max(.1f, interactionDistance > 0 ? interactionDistance : DefaultDistance);
        public virtual bool CanTarget => false;
        public virtual bool CanInteract => isActiveAndEnabled;
        public virtual string InteractionVerb => "살펴보기";

        // A capsule center stays steady while individual skinned vertices animate.
        public virtual Vector3 TargetCenter
        {
            get
            {
                var capsule = GetComponent<CapsuleCollider>();
                return capsule != null ? transform.TransformPoint(capsule.center) : transform.position + Vector3.up * .9f;
            }
        }

        public virtual Vector3 NamePosition => transform.position + nameOffset;

        protected virtual void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
        }

        protected virtual void OnDisable() => active.Remove(this);
        public void RequestInteraction(HarvestWorker worker)
        {
            if (!BeginInteraction(worker))
                return;
            Interact(worker);
        }

        protected bool BeginInteraction(HarvestWorker worker)
        {
            if (worker == null || !worker.AcceptsInteractionCommands || !CanInteract)
                return false;
            onInteractionRequested.Invoke();
            return true;
        }

        public virtual void AddContextActions(VillagePrototype village, List<WorldContextAction> actions)
        {
            actions.Add(new WorldContextAction(InteractionVerb, () => RequestInteraction(village.Worker)));
        }

        public void Arrive(VillagePrototype village, HarvestWorker worker, System.Action arrival = null)
        {
            if (!CanInteract)
                return;
            NotifyInteractionCompleted();
            if (arrival != null)
                arrival();
            else
                OnArrived(village, worker);
        }

        protected void NotifyInteractionCompleted() => onInteractionCompleted.Invoke();
        protected virtual void OnArrived(VillagePrototype village, HarvestWorker worker)
        {
        }

        protected abstract void Interact(HarvestWorker worker);
    }
}
