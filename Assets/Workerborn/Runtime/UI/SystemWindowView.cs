using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class SystemWindowView : VillageWindowView
    {
        [SerializeField]
        private Button save;
        [SerializeField]
        private Button character;
        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            save.onClick.AddListener(() => game.SaveGame(true));
            character.onClick.AddListener(() => hud.ToggleWindow("character"));
        }
    }
}
