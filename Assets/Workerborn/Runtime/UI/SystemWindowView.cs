using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class SystemWindowView : VillageWindowView
    {
        [SerializeField] private TMP_Text session;
        [SerializeField] private Button save;
        [SerializeField] private Button load;
        [SerializeField] private Button settings;
        [SerializeField] private Button era;
        [SerializeField] private Button administration;
        [SerializeField] private Button invite;
        [SerializeField] private Button title;
        [SerializeField] private GameObject loadConfirmation;
        [SerializeField] private Button confirmLoad;
        [SerializeField] private Button cancelLoad;
        private Button[] menuButtons;

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            save.onClick.AddListener(() => game.SaveGame(true));
            load.onClick.AddListener(() => loadConfirmation.SetActive(true));
            settings.onClick.AddListener(() => hud.ToggleWindow("settings"));
            era.onClick.AddListener(() => hud.ToggleWindow("era"));
            administration.onClick.AddListener(() => hud.ToggleWindow("server-admin"));
            invite.onClick.AddListener(game.InviteFriends);
            title.onClick.AddListener(game.ReturnToMenu);
            confirmLoad.onClick.AddListener(() =>
            {
                loadConfirmation.SetActive(false);
                game.ReloadSavedGame();
            });
            cancelLoad.onClick.AddListener(() => loadConfirmation.SetActive(false));
            menuButtons = new[] { save, load, settings, era, administration, invite, title };
        }

        private void OnDisable() => loadConfirmation.SetActive(false);

        public override void Refresh()
        {
            session.text = Game.SessionName;
            load.interactable = Game.CanReloadSavedGame;
            era.gameObject.SetActive(Game.IsOnline);
            administration.gameObject.SetActive(Game.CanManageServer);
            invite.gameObject.SetActive(Game.CanInviteFriends);
            int row = 0;
            foreach (var button in menuButtons)
            {
                if (!button.gameObject.activeSelf)
                    continue;
                var rect = (RectTransform)button.transform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -37f - 38f * row++);
            }
            SetExpandedHeight(72f + 38f * row);
        }
    }
}
