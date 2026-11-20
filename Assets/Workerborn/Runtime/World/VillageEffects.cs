using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class VillageEffects : MonoBehaviour
    {
        [SerializeField] private GameObject click;
        [SerializeField] private GameObject itemLanding;
        [SerializeField] private GameObject itemPickup;
        [SerializeField] private GameObject levelUp;
        [SerializeField] private LootFlightTrail lootTrail;
        [SerializeField, Min(.1f)] private float lifetime = 3f;
        [Header("Pool prewarm")]
        [SerializeField, Min(0)] private int dropPrewarm = 24;
        [SerializeField, Min(0)] private int feedbackPrewarm = 8;

        private ParticlePool clicks, landings, pickups, levels;
        private PrefabPool<LootFlightTrail> trails;
        private Action<Vector3> playPickup;
        private readonly List<ParticlePlayback> playing = new List<ParticlePlayback>();
        private readonly List<LootFlightTrail> flying = new List<LootFlightTrail>();

        private void Awake()
        {
            clicks = CreateParticlePool(click, feedbackPrewarm);
            landings = CreateParticlePool(itemLanding, dropPrewarm);
            pickups = CreateParticlePool(itemPickup, feedbackPrewarm);
            levels = CreateParticlePool(levelUp, Mathf.Min(2, feedbackPrewarm));
            playPickup = PlayPickup;
            if (lootTrail != null)
            {
                trails = new PrefabPool<LootFlightTrail>(lootTrail, transform);
                trails.Prewarm(dropPrewarm + feedbackPrewarm);
            }
        }

        public void PlayClick(Vector3 position) => Play(clicks, position);
        public void PlayLanding(Vector3 position) => Play(landings, position);
        public void PlayPickup(Vector3 position) => Play(pickups, position);
        public void PlayLevelUp(Vector3 position) => Play(levels, position);

        public void PlayDropTrail(GroundPickup pickup)
        {
            if (isActiveAndEnabled && trails != null)
                StartTrail(pickup.VisualPosition).FollowDrop(pickup);
        }

        public void PlayPickupTrail(Vector3 position, Transform worker)
        {
            if (!isActiveAndEnabled || worker == null)
                return;
            if (trails != null)
                StartTrail(position).FlyTo(worker, playPickup);
            else
                PlayPickup(worker.position);
        }

        private ParticlePool CreateParticlePool(GameObject prefab, int prewarm) =>
            prefab != null ? new ParticlePool(prefab.transform, transform, prewarm) : null;

        private void Play(ParticlePool pool, Vector3 position)
        {
            if (isActiveAndEnabled && pool != null)
                playing.Add(new ParticlePlayback(pool, pool.Play(position), Time.time + lifetime));
        }

        private LootFlightTrail StartTrail(Vector3 position)
        {
            var instance = trails.Rent();
            instance.transform.SetPositionAndRotation(position, Quaternion.identity);
            instance.gameObject.SetActive(true);
            flying.Add(instance);
            return instance;
        }

        private void LateUpdate()
        {
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var trail = flying[i];
                if (trail != null && !trail.Advance(Time.deltaTime))
                    continue;
                ReturnTrail(trail);
                flying.RemoveAt(i);
            }

            float now = Time.time;
            for (int i = playing.Count - 1; i >= 0; i--)
            {
                var effect = playing[i];
                if (now < effect.EndsAt && effect.Instance != null)
                    continue;
                effect.Pool.Return(effect.Instance);
                playing.RemoveAt(i);
            }
        }

        internal void Clear()
        {
            foreach (var trail in flying)
                ReturnTrail(trail);
            flying.Clear();
            foreach (var effect in playing)
                effect.Pool.Return(effect.Instance);
            playing.Clear();
        }

        private void OnDisable() => Clear();

        private void ReturnTrail(LootFlightTrail instance)
        {
            if (instance == null)
                return;
            instance.ResetTrail();
            trails.Return(instance);
        }

        private readonly struct ParticlePlayback
        {
            public readonly ParticlePool Pool;
            public readonly Transform Instance;
            public readonly float EndsAt;

            public ParticlePlayback(ParticlePool pool, Transform instance, float endsAt)
            {
                Pool = pool;
                Instance = instance;
                EndsAt = endsAt;
            }
        }

        private sealed class ParticlePool
        {
            private readonly PrefabPool<Transform> instances;
            private readonly Dictionary<Transform, ParticleSystem[]> systems = new Dictionary<Transform, ParticleSystem[]>();

            public ParticlePool(Transform prefab, Transform parent, int prewarm)
            {
                instances = new PrefabPool<Transform>(prefab, parent, Prepare);
                instances.Prewarm(prewarm);
            }

            private void Prepare(Transform instance)
            {
                var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var particle in particles)
                {
                    var main = particle.main;
                    if (main.stopAction == ParticleSystemStopAction.Destroy || main.stopAction == ParticleSystemStopAction.Disable)
                        main.stopAction = ParticleSystemStopAction.None;
                }
                systems.Add(instance, particles);
            }

            public Transform Play(Vector3 position)
            {
                var instance = instances.Rent();
                instance.SetPositionAndRotation(position, Quaternion.identity);
                instance.gameObject.SetActive(true);
                foreach (var particle in systems[instance])
                    if (particle.gameObject.activeInHierarchy && particle.main.playOnAwake)
                        particle.Play(false);
                return instance;
            }

            public void Return(Transform instance)
            {
                if (instance == null)
                {
                    if (!ReferenceEquals(instance, null))
                        systems.Remove(instance);
                    return;
                }
                foreach (var particle in systems[instance])
                    if (particle != null)
                        particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                instances.Return(instance);
            }
        }
    }
}
