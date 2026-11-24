using System;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace Workerborn
{
    [DefaultExecutionOrder(-20)]
    [DisallowMultipleComponent, RequireComponent(typeof(NavMeshAgent))]
    public sealed class AnimalWander : MonoBehaviour
    {
        [SerializeField, Min(.75f)] private float radius = 3f;
        [SerializeField, Tooltip("Minimum and maximum seconds spent resting between walks.")]
        private Vector2 restSeconds = new Vector2(4f, 8f);

        private const float MaximumWalkSeconds = 18f;
        private static readonly int Idle = Animator.StringToHash("Base Layer.Idle_A");
        private static readonly int Walk = Animator.StringToHash("Base Layer.Walk");
        private NavMeshAgent agent;
        private NavMeshPath path;
        private Animator animator;
        private Vector3 home;
        private float walkAfter;
        private float stopWalkingAt;
        private bool suspended;

        internal bool IsWalking { get; private set; }
        internal event Action StartedWalking;

        private void Awake()
        {
            home = transform.position;
            agent = GetComponent<NavMeshAgent>();
            path = new NavMeshPath();
            animator = GetComponentInChildren<Animator>(true);
            if (animator != null)
                animator.applyRootMotion = false;
        }

        private void OnEnable() => Rest();
        private void OnDisable() => StopWalking();

        internal void SetSuspended(bool value)
        {
            if (suspended == value)
                return;
            suspended = value;
            if (value)
                StopWalking();
            else
                Rest();
        }

        private void Rest()
        {
            float minimum = Mathf.Max(.1f, restSeconds.x);
            walkAfter = Time.time + Random.Range(minimum, Mathf.Max(minimum, restSeconds.y));
        }

        private void Update()
        {
            if (suspended)
                return;
            if (IsWalking)
            {
                if (Time.time >= stopWalkingAt || !agent.isOnNavMesh ||
                    (!agent.pathPending && (!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete ||
                        agent.remainingDistance <= agent.stoppingDistance + .04f)))
                    StopWalking();
                return;
            }
            if (Time.time >= walkAfter && !TryWalk())
                Rest();
        }

        private bool TryWalk()
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!BindToNavigation(filter))
                return false;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                var probe = home + new Vector3(offset.x, 0f, offset.y);
                if (!NavMesh.SamplePosition(probe, out var goal, .65f, filter) ||
                    HorizontalDistanceSquared(transform.position, goal.position) < .6f * .6f ||
                    !agent.CalculatePath(goal.position, path) || path.status != NavMeshPathStatus.PathComplete ||
                    !StaysNearHome(path) || !agent.SetPath(path))
                    continue;

                agent.isStopped = false;
                IsWalking = true;
                stopWalkingAt = Time.time + MaximumWalkSeconds;
                Play(Walk);
                StartedWalking?.Invoke();
                return true;
            }
            return false;
        }

        private bool BindToNavigation(NavMeshQueryFilter filter)
        {
            if (agent.isOnNavMesh)
                return true;
            if (!NavMesh.SamplePosition(transform.position, out var ground, .8f, filter) ||
                HorizontalDistanceSquared(home, ground.position) > radius * radius)
                return false;

            agent.enabled = false;
            transform.position = ground.position;
            agent.enabled = true;
            return agent.isOnNavMesh;
        }

        private bool StaysNearHome(NavMeshPath candidate)
        {
            foreach (Vector3 corner in candidate.corners)
                if (HorizontalDistanceSquared(home, corner) > radius * radius)
                    return false;
            return true;
        }

        private static float HorizontalDistanceSquared(Vector3 a, Vector3 b)
        {
            Vector3 offset = a - b;
            return offset.x * offset.x + offset.z * offset.z;
        }

        private void StopWalking()
        {
            if (!IsWalking)
                return;
            IsWalking = false;
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            Play(Idle);
            Rest();
        }

        private void Play(int state)
        {
            if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null &&
                animator.HasState(0, state))
                animator.CrossFadeInFixedTime(state, .12f);
        }
    }
}
