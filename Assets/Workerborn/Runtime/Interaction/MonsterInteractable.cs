using System;
using UnityEngine;

namespace Workerborn
{
    public class MonsterInteractable : WorldInteractable
    {
        [SerializeField]
        private string displayName = "몬스터";
        [SerializeField, Min(.5f)]
        private float reach = 1.8f;
        [SerializeField] private AnimalCombatProfile combat;
        [SerializeField] private string idleState = "Base Layer.Idle_A";
        [SerializeField] private string attackState = "Base Layer.Attack";
        [SerializeField] private string defeatedState = "Base Layer.Death";
        private const float VisibleHealthSeconds = 60f;
        private const float PursuitUpdateSeconds = .18f;
        private Animator animator;
        private AnimalWander wander;
        private Collider[] livingColliders = Array.Empty<Collider>();
        private HarvestWorker opponent;
        private VillagePrototype corpseVillage;
        private float nextCounterAt;
        private float nextPursuitAt;
        private float despawnAt;
        private bool counterAnimation;
        private bool lootTaken;
        private bool corpseHidden;
        private bool returning;
        private Vector3 home;

        public override string DisplayName => displayName;
        public override bool CanTarget => !IsDefeated;
        public override bool ShowNameByDefault => combat != null;
        public override float InteractionDistance => reach;
        public override bool CanInteract => base.CanInteract && !IsDefeated;
        public override string InteractionVerb => combat == null ? "다가가기" : "공격하기";
        public AnimalCombatProfile Combat => combat;
        public int Health { get; private set; }
        public bool IsDefeated => combat != null && Health == 0;
        public bool LootTaken => lootTaken;
        internal float HealthVisibleUntil { get; private set; }
        internal bool WasRecentlyHit => Time.unscaledTime < HealthVisibleUntil;
        internal override bool CanReceiveStrike => combat != null && CanInteract;

        private void Awake()
        {
            CharacterActor.SuppressBodyCollision(gameObject);
            if (combat == null)
                return;
            animator = GetComponentInChildren<Animator>(true);
            wander = GetComponent<AnimalWander>();
            home = wander != null ? wander.Home : transform.position;
            livingColliders = Array.FindAll(GetComponentsInChildren<Collider>(true), collider => collider.enabled);
            if (animator != null)
                animator.applyRootMotion = false;
            ResetEncounter();
        }

        public void ResetEncounter()
        {
            if (combat == null)
                return;
            RestoreEncounter(combat.Health, false);
        }

        internal void RestoreEncounter(int health, bool gathered)
        {
            if (combat == null)
                return;
            Health = Mathf.Clamp(health, 0, combat.Health);
            HealthVisibleUntil = 0f;
            lootTaken = IsDefeated && gathered;
            opponent = null;
            returning = false;
            wander?.StopCombatMovement();
            counterAnimation = false;
            corpseVillage = null;
            SetBodyAlive(!IsDefeated);
            if (IsDefeated)
            {
                HideCorpse();
                return;
            }
            if (corpseHidden)
            {
                corpseHidden = false;
                gameObject.SetActive(true);
            }
            Play(idleState);
        }

        private void SetBodyAlive(bool alive)
        {
            if (alive)
                wander?.SetSuspended(false);
            else
                wander?.StopForDefeat();
            foreach (var collider in livingColliders)
                if (collider != null)
                    collider.enabled = alive;
        }

        private void HideCorpse()
        {
            if (!gameObject.activeSelf)
                return;
            corpseHidden = true;
            gameObject.SetActive(false);
        }

        protected override void Interact(HarvestWorker worker)
        {
            if (combat == null)
                worker.CommandApproach(this);
            else
                worker.CommandAttack(this);
        }

        internal bool TryDropLoot(VillagePrototype village)
        {
            if (!IsDefeated || lootTaken || village == null)
                return false;
            lootTaken = true;
            int count = village.State.ScaledYield(SkillId.Carving, combat.LootCount);
            if (village.DropStrikeLoot(transform, combat.Loot, count))
                return true;
            lootTaken = false;
            return false;
        }

        internal override void ReceiveStrike(HarvestWorker worker, int damage)
        {
            if (!CanReceiveStrike || worker.Village == null)
                return;
            int applied = Mathf.Min(Health, Mathf.Max(0, damage));
            if (applied == 0)
                return;
            Health -= applied;
            worker.Village.Hud.ShowFloatingText(NamePosition + Vector3.up * .2f, "-" + applied, new Color(1f, .85f, .56f));
            if (IsDefeated)
            {
                opponent = null;
                counterAnimation = false;
                SetBodyAlive(false);
                Play(defeatedState);
                corpseVillage = worker.Village;
                despawnAt = Time.time + combat.CorpseSeconds;
                TryDropLoot(worker.Village);
                worker.Village.SaveGame(false);
            }
            else if (opponent != worker)
            {
                opponent = worker;
                returning = false;
                wander?.SetSuspended(true);
                nextCounterAt = Time.time + combat.CounterDelay;
                nextPursuitAt = 0f;
            }
            HealthVisibleUntil = Time.unscaledTime + VisibleHealthSeconds;
            worker.Village.Hud.ShowPlayerHealthUntil(HealthVisibleUntil);
        }

        private void Update()
        {
            if (IsDefeated)
            {
                if (Time.time >= despawnAt)
                {
                    corpseVillage?.PlayCorpseDespawn(transform.position);
                    corpseVillage = null;
                    HideCorpse();
                }
                return;
            }
            if (counterAnimation && animator != null)
            {
                var playing = animator.GetCurrentAnimatorStateInfo(0);
                if (playing.IsName(attackState) && playing.normalizedTime >= 1f)
                {
                    counterAnimation = false;
                    Play(idleState);
                }
            }
            if (combat == null)
                return;
            if (opponent == null)
            {
                if (returning && (WithinHome(transform.position, .55f) || wander.CombatMovementComplete))
                    FinishReturn();
                return;
            }

            var village = opponent.Village;
            Vector3 destination = opponent.StandingPosition;
            if (village == null || !village.SharesMap(transform.position) || village.State.AwaitingIncarnation ||
                village.State.Health <= 0 || village.State.Energy <= 0 ||
                !WithinHome(destination, combat.ChaseRadius) || !WithinHome(transform.position, combat.ChaseRadius + .2f))
            {
                ReturnHome();
                return;
            }
            if (!opponent.InReach(transform.position, InteractionDistance))
            {
                if (Time.time < nextPursuitAt)
                    return;
                nextPursuitAt = Time.time + PursuitUpdateSeconds;
                counterAnimation = false;
                if (wander != null && !wander.MoveForCombat(destination, combat.ChaseSpeed, InteractionDistance * .75f))
                    ReturnHome();
                return;
            }

            wander?.StopCombatMovement();
            if (Time.time < nextCounterAt || village.Hud.WorldActionsLocked)
                return;
            nextCounterAt = Time.time + combat.CounterDelay;
            Vector3 facing = destination - transform.position;
            facing.y = 0;
            if (facing.sqrMagnitude > .001f)
                transform.rotation = Quaternion.LookRotation(facing);
            counterAnimation = Play(attackState);
            village.ReceiveAnimalHit(combat.CounterDamage);
        }

        private bool WithinHome(Vector3 point, float radius)
        {
            Vector3 delta = point - home;
            delta.y = 0f;
            return delta.sqrMagnitude <= radius * radius;
        }

        private void ReturnHome()
        {
            opponent = null;
            counterAnimation = false;
            returning = !WithinHome(transform.position, .55f) &&
                wander?.MoveForCombat(home, combat.ChaseSpeed, .2f) == true;
            if (!returning)
                FinishReturn();
        }

        private void FinishReturn()
        {
            returning = false;
            wander?.SetSuspended(false);
            Play(idleState);
        }

        private bool Play(string state)
        {
            if (animator == null || animator.runtimeAnimatorController == null || !animator.isActiveAndEnabled)
                return false;
            int hash = Animator.StringToHash(state);
            if (!animator.HasState(0, hash))
                return false;
            animator.Play(hash, 0, 0f);
            return true;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            opponent = null;
            returning = false;
        }
    }
}
