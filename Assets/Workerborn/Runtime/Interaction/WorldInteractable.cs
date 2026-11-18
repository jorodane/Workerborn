using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Workerborn
{
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
        public virtual Vector3 NamePosition => transform.position + nameOffset;

        protected virtual void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
        }

        protected virtual void OnDisable() => active.Remove(this);
        public void RequestInteraction(HarvestWorker worker)
        {
            if (worker == null || !worker.AcceptsInteractionCommands || !isActiveAndEnabled)
                return;
            onInteractionRequested.Invoke();
            Interact(worker);
        }

        public void Arrive(VillagePrototype village, HarvestWorker worker)
        {
            if (!isActiveAndEnabled)
                return;
            NotifyInteractionCompleted();
            OnArrived(village, worker);
        }

        protected void NotifyInteractionCompleted() => onInteractionCompleted.Invoke();
        protected virtual void OnArrived(VillagePrototype village, HarvestWorker worker)
        {
        }

        protected abstract void Interact(HarvestWorker worker);
    }
}
