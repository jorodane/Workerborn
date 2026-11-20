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
        private Animator animator;
        private HarvestWorker opponent;
        private float nextCounterAt;
        private bool counterAnimation;
        private bool lootTaken;

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
        internal override bool CanReceiveStrike => combat != null && CanInteract;

        private void Awake()
        {
            if (combat == null)
                return;
            animator = GetComponentInChildren<Animator>(true);
            if (animator != null)
                animator.applyRootMotion = false;
            ResetEncounter();
        }

        public void ResetEncounter()
        {
            if (combat == null)
                return;
            RestoreEncounter(combat.Health, false, false);
        }

        internal void RestoreEncounter(int health, bool gathered, bool keepDefeatedPose = true)
        {
            if (combat == null)
                return;
            Health = Mathf.Clamp(health, 0, combat.Health);
            lootTaken = IsDefeated && gathered;
            opponent = null;
            counterAnimation = false;
            if (IsDefeated)
                Play(defeatedState, keepDefeatedPose ? 1f : 0f);
            else
                Play(idleState);
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
            if (village.DropStrikeLoot(transform, combat.Loot, combat.LootCount))
                return true;
            lootTaken = false;
            return false;
        }

        internal override void ReceiveStrike(HarvestWorker worker, int damage)
        {
            if (!CanReceiveStrike || worker.Village == null)
                return;
            int applied = Mathf.Min(Health, damage);
            Health -= applied;
            worker.Village.Hud.ShowFloatingText(NamePosition + Vector3.up * .2f, "-" + applied, new Color(1f, .85f, .56f));
            if (IsDefeated)
            {
                RestoreEncounter(0, false, false);
                TryDropLoot(worker.Village);
                worker.Village.SaveGame(false);
            }
            else if (opponent != worker)
            {
                opponent = worker;
                nextCounterAt = Time.time + combat.CounterDelay;
            }
        }

        private void Update()
        {
            if (counterAnimation && animator != null)
            {
                var playing = animator.GetCurrentAnimatorStateInfo(0);
                if (playing.IsName(attackState) && playing.normalizedTime >= 1f)
                {
                    counterAnimation = false;
                    Play(idleState);
                }
            }
            if (opponent == null || IsDefeated || Time.time < nextCounterAt ||
                !opponent.InReach(transform.position, InteractionDistance))
                return;
            var village = opponent.Village;
            if (village == null || village.Hud.HasModal || village.State.Energy <= 0)
                return;

            nextCounterAt = Time.time + combat.CounterDelay;
            Vector3 facing = opponent.transform.position - transform.position;
            facing.y = 0;
            if (facing.sqrMagnitude > .001f)
                transform.rotation = Quaternion.LookRotation(facing);
            counterAnimation = Play(attackState);
            village.ReceiveAnimalHit(combat.CounterDamage);
        }

        private bool Play(string state, float progress = 0)
        {
            if (animator == null || animator.runtimeAnimatorController == null || !animator.isActiveAndEnabled)
                return false;
            int hash = Animator.StringToHash(state);
            if (!animator.HasState(0, hash))
                return false;
            animator.Play(hash, 0, progress);
            return true;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            opponent = null;
        }
    }
}
