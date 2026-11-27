using UnityEngine;

namespace Workerborn
{
    internal sealed partial class NpcRoutine
    {
        private const float CheckoutDelay = .85f;
        private const float ConversationSeconds = 1.9f;
        private VillageInteractable merchant;
        private WorldInteractable shoppingStep;
        private NpcRoutine customer;
        private float shopRetryAt;
        private float checkoutAt, conversationEndsAt;
        private bool checkedOut;
        internal Vector3? ReturnPoint { get; set; }

        private bool AtCheckout => merchant != null && merchant.Routine != null && merchant.Routine.customer == this;
        private bool AvailableToTrade => IsAvailableResident && !character.IsEating && !character.IsStandingUp &&
            (character.Action is WorkerAction.Idle or WorkerAction.Sitting) && !AtCheckout && !HoldingConversation && !PlayerEngaged;

        private bool PlanFoodShopping()
        {
            float nearest = float.PositiveInfinity;
            foreach (var subject in WorldInteractable.Active)
            {
                if (subject is not VillageInteractable seller || seller == npc || !seller.CanInteract ||
                    seller.gameObject.scene != npc.gameObject.scene || seller.Routine == null || !seller.Routine.IsAvailableResident ||
                    FindFoodOffer(seller) == null || !TryShopStep(seller, out _, out float distance) || distance >= nearest)
                    continue;
                merchant = seller;
                nearest = distance;
            }
            if (merchant == null)
                return false;

            ReturnPoint ??= character.StandingPosition;
            planned = false;
            goal = default;
            ContinueShopping();
            return true;
        }

        private ItemSpec FindFoodOffer(VillageInteractable seller)
        {
            var state = character.CharacterState;
            string crop = state.Rules.JobItemId;
            long budget = state.Gold;
            if (state.CanSell(crop) && !ItemCatalog.Get(crop).IsFood)
                budget += (long)state.Inventory.Count(crop) * ItemCatalog.Get(crop).Price;
            ItemSpec cheapest = null;
            if (seller.Definition?.ShopItems == null)
                return null;
            foreach (var item in seller.Definition.ShopItems)
            {
                if (item == null || !ItemCatalog.Contains(item.Id))
                    continue;
                var spec = ItemCatalog.Get(item.Id);
                if (spec.IsFood && spec.Price <= budget && (cheapest == null || spec.Price < cheapest.Price) &&
                    (spec.Price > state.Gold || state.Inventory.CanAdd(spec.Id, 1) || state.Gold == spec.Price && spec.Price > 0))
                    cheapest = spec;
            }
            return cheapest;
        }

        private bool TryShopStep(VillageInteractable seller, out WorldInteractable step, out float distance)
        {
            step = game.ShopApproach(character, seller);
            distance = 0f;
            if (step == null || !step.CanInteract)
                return false;
            var door = step as VillageBuildingDoor;
            Vector3 target = door != null ? door.Approach.position : seller.transform.position;
            float offset = door != null ? 0f : seller.InteractionDistance * .7f;
            if (!character.TryGetReachablePoint(target, offset, out var approach))
                return false;
            distance = (approach - character.StandingPosition).sqrMagnitude;
            return true;
        }

        private bool ContinueShopping()
        {
            if (ReferenceEquals(merchant, null))
                return false;
            if (merchant == null || !merchant.CanInteract || !character.CharacterState.NpcMealDue ||
                merchant.Routine == null || !merchant.Routine.IsAvailableResident || game.IsWorldTransitioning)
            {
                StopShopping();
                return false;
            }

            if (AtCheckout)
            {
                if (!merchant.Routine.AvailableToTrade || !character.SharesMap(merchant.transform.position) ||
                    !character.InReach(merchant.transform.position, merchant.InteractionDistance))
                {
                    StopShopping();
                    return false;
                }
                if (!checkedOut && Time.time >= checkoutAt)
                {
                    checkedOut = true;
                    BuyMeal();
                }
                if (Time.time >= conversationEndsAt)
                    StopShopping();
                return true;
            }
            if (character.IsStandingUp || character.Action == WorkerAction.Moving || Time.time < shopRetryAt)
                return true;
            if (FindFoodOffer(merchant) == null || !TryShopStep(merchant, out var next, out _))
            {
                StopShopping();
                return false;
            }
            shopRetryAt = Time.time + RetryDelay;
            if (next == merchant && (!merchant.Routine.AvailableToTrade || merchant.Routine.customer != null))
                return true;
            shoppingStep = next;
            next.RequestInteraction(character);
            return true;
        }

        private void ArrivedAtShop(VillageInteractable seller)
        {
            if (seller != merchant || seller == null || !character.CharacterState.NpcMealDue ||
                seller.Routine == null || !seller.Routine.AvailableToTrade || seller.Routine.customer != null)
                return;
            seller.Routine.customer = this;
            conversation?.End();
            seller.Routine.conversation?.End();
            seller.Routine.character.FaceConversationPartner(character.StandingPosition);
            shoppingStep = null;
            checkoutAt = Time.time + CheckoutDelay;
            conversationEndsAt = Time.time + ConversationSeconds;
            checkedOut = false;
            game.Hud.ShowNpcShopConversation(seller, npc);
        }

        private void BuyMeal()
        {
            var food = FindFoodOffer(merchant);
            if (food == null)
                return;
            var state = character.CharacterState;
            string crop = state.Rules.JobItemId;
            if (state.Gold < food.Price && state.CanSell(crop) && !ItemCatalog.Get(crop).IsFood)
            {
                int price = ItemCatalog.Get(crop).Price;
                if (price > 0)
                    while (state.Gold < food.Price)
                    {
                        InventoryEntry bundle = null;
                        foreach (var entry in state.Inventory.Entries)
                            if (entry.ItemId == crop)
                            {
                                bundle = entry;
                                break;
                            }
                        long needed = ((long)food.Price - state.Gold + price - 1) / price;
                        if (bundle == null || !game.SellTo(character, merchant, bundle.Id, (int)System.Math.Min(bundle.Quantity, needed)))
                            return;
                    }
            }
            game.BuyFrom(character, merchant, food.Id, 1);
        }

        private void StopShopping()
        {
            if (AtCheckout)
            {
                merchant.Routine.customer = null;
                game.Hud.HideNpcConversationBubble(merchant);
            }
            merchant = null;
            if (shoppingStep != null && character.IsInteractingWith(shoppingStep))
                character.Cancel();
            shoppingStep = null;
            shopRetryAt = 0f;
        }

        private bool ReturnToActivity()
        {
            if (ReturnPoint is not Vector3 origin)
                return false;
            if (character.Action == WorkerAction.Moving || character.IsStandingUp || Time.time < shopRetryAt)
                return true;
            shopRetryAt = Time.time + RetryDelay;
            if (!character.SharesMap(origin))
            {
                game.DoorToward(character.StandingPosition, origin)?.RequestInteraction(character);
                return true;
            }

            if (character.InReach(origin, .8f) || !character.TryGetReachablePoint(origin, 0f, out var point))
            {
                ReturnPoint = null;
                return false;
            }
            character.CommandMove(point, walk: true);
            return true;
        }
    }
}
