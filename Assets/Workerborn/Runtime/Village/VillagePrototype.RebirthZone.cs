using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        [Header("Rebirth map")]
        [SerializeField] private RebirthZone rebirthZone;

        internal CharacterLook IncarnationLook { get; private set; }

        public bool InRebirthZone { get; private set; }
        internal RebirthZone RebirthArea => rebirthZone;

        private void EnterClouds()
        {
            if (!State.AwaitingIncarnation)
                return;
            player.ShowEquipment(State.Equipment, settings);
            IncarnationLook = player.CurrentLook.Validated();
            if (rebirthZone != null && rebirthZone.Spawn != null && Travel(rebirthZone.Spawn.position, toClouds: true))
            {
                SaveGame(false);
                return;
            }
            Debug.LogError("The rebirth area needs a walkable cloud surface and an arrival point.", this);
        }

        private void EnterVillage()
        {
            if (State.AwaitingIncarnation)
                return;
            player.ApplyProfileLook(State.LifeLook ?? IncarnationLook);
            if (!Travel(spawnPoint.position, toClouds: false))
                Debug.LogError("The village arrival point must be on walkable ground.", this);
        }

        internal void PreviewIncarnationLook(CharacterLook look)
        {
            if (!InRebirthZone || !State.AwaitingIncarnation || !hud.IsSpeakingWith(rebirthZone.Guide))
                return;
            IncarnationLook = look.Validated();
            player.ApplyProfileLook(IncarnationLook);
        }

        internal void ConfirmIncarnation() => PerformWorld(CampaignAction.Incarnate, value: IncarnationLook.ToSelection());

        private bool Travel(Vector3 destination, bool toClouds)
        {
            if (State.AwaitingIncarnation != toClouds)
                return false;
            if (!TryWarpToMap(destination, toClouds: toClouds))
                return false;
            if (toClouds)
                worker.transform.rotation = rebirthZone.Spawn.rotation;
            RefreshAfterTravel();
            return true;
        }

        private void RefreshAfterTravel()
        {
            hud.CloseDialogue();
            hud.CloseWorldContext();
            hud.CancelItemDrag();
            hud.HideItemTooltip();
            hud.SetIncarnationPresentation();
            targeting?.RefreshStaticGeometry();
            RefreshSharedGroundPickups();
            cameraRig.SnapToTarget();
        }
    }
}
