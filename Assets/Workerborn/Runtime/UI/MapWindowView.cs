using UnityEngine;

namespace Workerborn
{
    public sealed class MapWindowView : VillageWindowView
    {
        [SerializeField]
        private VillageMapView map;
        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            map.Bind(game);
        }
    }
}
