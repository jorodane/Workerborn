using System;
using System.Collections.Generic;

namespace Workerborn
{
    internal sealed partial class VillageCampaign
    {
        private readonly GroundDropPiles groundDrops = new GroundDropPiles();
        internal IEnumerable<GroundDropSave> SharedGroundDrops => groundDrops.Active;

        internal bool TryGetGroundDrop(string dropId, out GroundDropSave drop) => groundDrops.TryGet(dropId, out drop);

        internal void ReleaseExpiredDrops(long now, Action<string> changed) => groundDrops.ReleaseExpired(now, changed);

        internal bool TryPlaceGroundDrop(string identity, int entryId, GroundDropSave draft, long now,
            out GroundDropSave drop, out string message)
        {
            drop = null;
            message = "그 위치에 아이템을 놓을 수 없어요.";
            if (!ConnectedProfile(identity, out var profile) || !ValidGroundDrop(draft) || !InPickupRange(profile.character, draft))
                return false;

            var state = VillageState.Restore(profile.character, rules);
            var entry = state.CarriedItem(entryId);
            if (entry == null || entry.ItemId != draft.itemId || entry.Quantity != draft.quantity ||
                !state.TryRemoveCarriedItem(entryId, draft.quantity))
            {
                message = "놓으려던 아이템이 바뀌었어요. 다시 확인해 주세요.";
                return false;
            }
            drop = AddGroundDrop(draft, profile.id, now);
            SaveCharacter(profile, state, teleport: false);
            message = null;
            return true;
        }

        internal bool TryPublishGatheredDrops(string identity, VillageSave character, GroundDropSave[] drafts, long now,
            out GroundDropSave[] drops)
        {
            drops = null;
            if (!ConnectedProfile(identity, out var profile) || drafts == null || drafts.Length == 0 ||
                drafts.Length > GroundDropSave.MaximumGatheredDrops)
                return false;
            foreach (var draft in drafts)
                if (!ValidGroundDrop(draft))
                    return false;
            if (!Checkpoint(identity, character))
                return false;

            drops = new GroundDropSave[drafts.Length];
            for (int i = 0; i < drops.Length; i++)
                drops[i] = AddGroundDrop(drafts[i], profile.id, now);
            return true;
        }

        internal bool TryCollectGroundDrop(string identity, string dropId, long now, out int quantity, out string message)
        {
            quantity = 0;
            message = "지금 이 아이템을 주울 수 없어요.";
            if (!ConnectedProfile(identity, out var profile) || string.IsNullOrEmpty(dropId) ||
                !groundDrops.TryGet(dropId, out var drop) || !drop.IsVisibleTo(profile.id) || !InPickupRange(profile.character, drop))
                return false;
            if (!drop.CanCollect(profile.id, now))
            {
                message = "아직 소유자만 주울 수 있는 아이템입니다.";
                return false;
            }

            var state = VillageState.Restore(profile.character, rules);
            quantity = drop.combined ? state.Inventory.TryAddUpTo(drop.itemId, drop.quantity) :
                state.Inventory.TryAdd(drop.itemId, drop.quantity) ? drop.quantity : 0;
            if (quantity == 0)
            {
                message = "소지품 공간이 부족합니다.";
                return false;
            }
            SaveCharacter(profile, state, teleport: false);
            if (quantity == drop.quantity)
                groundDrops.Remove(dropId);
            else
                drop.quantity -= quantity;
            message = null;
            return true;
        }

        private bool ConnectedProfile(string identity, out CampaignProfile profile) =>
            profiles.TryGetValue(identity, out profile) && connected.Contains(identity) && !profile.character.awaitingIncarnation;

        private bool ValidGroundDrop(GroundDropSave drop) => drop != null && drop.quantity > 0 && ItemCatalog.Contains(drop.itemId) &&
            FiniteCoordinate(drop.x) && FiniteCoordinate(drop.z) && isCloudPosition?.Invoke(drop.x, drop.z) != true &&
            FiniteCoordinate(drop.interior.localX) && FiniteCoordinate(drop.interior.localZ) &&
            (!drop.hasDropOrigin || FiniteCoordinate(drop.originX) && FiniteCoordinate(drop.originY) && FiniteCoordinate(drop.originZ));

        private static bool InPickupRange(VillageSave character, GroundDropSave drop)
        {
            string roomId = string.IsNullOrEmpty(character.interior.id) ? null : character.interior.id;
            if (roomId != drop.RoomId)
                return false;
            float x = (roomId == null ? character.playerX : character.interior.localX) - drop.MapX;
            float z = (roomId == null ? character.playerZ : character.interior.localZ) - drop.MapZ;
            return x * x + z * z <= GroundDropSave.PickupDistance * GroundDropSave.PickupDistance;
        }

        private GroundDropSave AddGroundDrop(GroundDropSave draft, string owner, long now)
        {
            var drop = new GroundDropSave
            {
                id = Guid.NewGuid().ToString("N"), itemId = draft.itemId, quantity = draft.quantity,
                x = draft.x, z = draft.z, interior = draft.interior, hasDropOrigin = draft.hasDropOrigin,
                originX = draft.originX, originY = draft.originY, originZ = draft.originZ
            };
            drop.ReserveFor(owner, now);
            groundDrops.Add(drop);
            return drop;
        }

        private void RestoreGroundDrops(GroundDropSave[] saved)
        {
            if (saved == null)
                return;
            foreach (var drop in saved)
                if (ValidGroundDrop(drop) && Guid.TryParseExact(drop.id, "N", out _) && !string.IsNullOrEmpty(drop.ownerId))
                    groundDrops.Add(drop);
        }

        private void LoadSoloGroundDrops(VillageSave save)
        {
            if (save.drops == null || save.drops.Length == 0)
                return;
            var personalDrops = new List<GroundDropSave>();
            foreach (var drop in save.drops)
                if (ValidGroundDrop(drop) && Guid.TryParseExact(drop.id, "N", out _) && !string.IsNullOrEmpty(drop.ownerId))
                    groundDrops.Add(drop);
                else
                    personalDrops.Add(drop);
            save.drops = personalDrops.ToArray();
        }
    }
}
