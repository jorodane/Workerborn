using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class VillageEffects : MonoBehaviour
    {
        [SerializeField] private VillageEffectLibrary library;

        private readonly Dictionary<VillageEffectKind, (ParticlePool Pool, float Lifetime)> bursts =
            new Dictionary<VillageEffectKind, (ParticlePool, float)>();
        private readonly Dictionary<VillageEffectKind, PrefabPool<LootFlightTrail>> trails =
            new Dictionary<VillageEffectKind, PrefabPool<LootFlightTrail>>();
        private Action<Vector3> playPickup;
        private readonly List<(ParticlePool Pool, Transform Instance, float EndsAt)> playing =
            new List<(ParticlePool, Transform, float)>();
        private readonly List<(PrefabPool<LootFlightTrail> Pool, LootFlightTrail Instance)> flying =
            new List<(PrefabPool<LootFlightTrail>, LootFlightTrail)>();

        private void Awake()
        {
            playPickup = PlayPickup;
            if (library == null)
                return;

            var particlePools = new Dictionary<GameObject, ParticlePool>();
            var trailPools = new Dictionary<GameObject, PrefabPool<LootFlightTrail>>();
            var capacities = new Dictionary<GameObject, int>();
            var registered = new HashSet<VillageEffectKind>();
            foreach (var entry in library.Effects)
            {
                if (entry.prefab == null || !registered.Add(entry.effect))
                    continue;
                capacities.TryGetValue(entry.prefab, out int capacity);
                capacity += Mathf.Max(0, entry.prewarm);
                capacities[entry.prefab] = capacity;

                if (VillageEffectLibrary.UsesTrail(entry.effect))
                {
                    if (entry.prefab.GetComponent<LootFlightTrail>() is not LootFlightTrail template)
                    {
                        Debug.LogWarning($"{entry.effect} needs a LootFlightTrail on the effect prefab root.", library);
                        continue;
                    }
                    if (!trailPools.TryGetValue(entry.prefab, out var pool))
                    {
                        pool = new PrefabPool<LootFlightTrail>(template, transform);
                        trailPools.Add(entry.prefab, pool);
                    }
                    pool.Prewarm(capacity);
                    trails.Add(entry.effect, pool);
                }
                else
                {
                    if (!particlePools.TryGetValue(entry.prefab, out var pool))
                    {
                        pool = new ParticlePool(entry.prefab.transform, transform);
                        particlePools.Add(entry.prefab, pool);
                    }
                    pool.Prewarm(capacity);
                    bursts.Add(entry.effect, (pool, Mathf.Max(.05f, entry.lifetime)));
                }
            }
        }

        public void PlayClick(Vector3 position) => Play(VillageEffectKind.WorldClick, position);
        public void PlayLanding(Vector3 position) => Play(VillageEffectKind.DropLanding, position);
        public void PlayPickup(Vector3 position) => Play(VillageEffectKind.PickupArrival, position);
        public void PlayLevelUp(Vector3 position) => Play(VillageEffectKind.LevelUp, position);
        public void PlayCorpseDespawn(Vector3 position) => Play(VillageEffectKind.CorpseDespawn, position);

        public void PlayDropTrail(GroundPickup pickup)
        {
            if (isActiveAndEnabled && trails.TryGetValue(VillageEffectKind.DropTrail, out var pool))
                StartTrail(pool, pickup.VisualPosition).FollowDrop(pickup);
        }

        public void PlayPickupTrail(Vector3 position, Transform worker)
        {
            if (!isActiveAndEnabled || worker == null)
                return;
            if (trails.TryGetValue(VillageEffectKind.PickupTrail, out var pool))
                StartTrail(pool, position).FlyTo(worker, playPickup);
            else
                PlayPickup(worker.position);
        }

        private void Play(VillageEffectKind effect, Vector3 position)
        {
            if (isActiveAndEnabled && bursts.TryGetValue(effect, out var burst))
                playing.Add((burst.Pool, burst.Pool.Play(position), Time.time + burst.Lifetime));
        }

        private LootFlightTrail StartTrail(PrefabPool<LootFlightTrail> pool, Vector3 position)
        {
            var instance = pool.Rent();
            instance.transform.SetPositionAndRotation(position, Quaternion.identity);
            instance.gameObject.SetActive(true);
            flying.Add((pool, instance));
            return instance;
        }

        private void LateUpdate()
        {
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var flight = flying[i];
                if (flight.Instance != null && !flight.Instance.Advance(Time.deltaTime))
                    continue;
                ReturnTrail(flight.Pool, flight.Instance);
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
            foreach (var flight in flying)
                ReturnTrail(flight.Pool, flight.Instance);
            flying.Clear();
            foreach (var effect in playing)
                effect.Pool.Return(effect.Instance);
            playing.Clear();
        }

        private void OnDisable() => Clear();

        private static void ReturnTrail(PrefabPool<LootFlightTrail> pool, LootFlightTrail instance)
        {
            if (instance == null)
                return;
            instance.ResetTrail();
            pool.Return(instance);
        }

        private sealed class ParticlePool
        {
            private readonly PrefabPool<Transform> instances;
            private readonly Dictionary<Transform, ParticleSystem[]> systems = new Dictionary<Transform, ParticleSystem[]>();

            public ParticlePool(Transform prefab, Transform parent)
            {
                instances = new PrefabPool<Transform>(prefab, parent, Prepare);
            }

            public void Prewarm(int count) => instances.Prewarm(count);

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
