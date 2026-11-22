using UnityEngine;

namespace Workerborn
{
    public sealed class MapWindowView : VillageWindowView
    {
        [SerializeField]
        private VillageMapView map;
        [SerializeField] private VillageClockView clock;
        [SerializeField] private PartTimeScheduleView schedule;
        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            map.Bind(game);
            clock.Bind(game);
            schedule.Bind(game);
        }
    }
}
