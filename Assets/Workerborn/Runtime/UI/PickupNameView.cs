namespace Workerborn
{
    public sealed class PickupNameView : WorldNameplateView
    {
        public void Bind(GroundPickup item, HarvestWorker owner)
        {
            Bind(item, owner.Targeting, item.DisplayName + (item.Quantity > 1 ? " ×" + item.Quantity : ""));
        }
    }
}
