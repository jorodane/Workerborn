using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class SystemWindowView : VillageWindowView
    {
        [SerializeField] private TMP_Text session;
        [SerializeField] private Button save;
        [SerializeField] private Button character;
        [SerializeField] private Button era;
        [SerializeField] private Button administration;
        [SerializeField] private Button invite;
        [SerializeField] private Button title;

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            save.onClick.AddListener(() => game.SaveGame(true));
            character.onClick.AddListener(() => hud.ToggleWindow("character"));
            era.onClick.AddListener(() => hud.ToggleWindow("era"));
            administration.onClick.AddListener(() => hud.ToggleWindow("server-admin"));
            invite.onClick.AddListener(game.InviteFriends);
            title.onClick.AddListener(game.ReturnToMenu);
        }

        public override void Refresh()
        {
            session.text = Game.SessionName;
            era.gameObject.SetActive(Game.IsOnline);
            administration.gameObject.SetActive(Game.CanManageServer);
            invite.gameObject.SetActive(Game.CanInviteFriends);
        }
    }
}
