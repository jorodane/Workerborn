using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public class VillageWindowView : MonoBehaviour
    {
        [SerializeField]
        private string windowId;
        [SerializeField]
        private Button closeButton;
        [SerializeField]
        private WindowDragHandle dragHandle;
        [SerializeField]
        private Button minimizeButton;
        [SerializeField]
        private GameObject windowBody;
        [SerializeField] private bool docked;
        private float expandedHeight;
        protected VillagePrototype Game;
        protected VillageHud Hud;
        public string Id => windowId;
        public RectTransform Rect => (RectTransform)transform;

        public virtual void Bind(VillagePrototype game, VillageHud hud)
        {
            Game = game;
            Hud = hud;
            expandedHeight = Rect.sizeDelta.y;
            if (minimizeButton != null && windowBody != null)
                minimizeButton.onClick.AddListener(() =>
                {
                    windowBody.SetActive(!windowBody.activeSelf);
                    Rect.sizeDelta = new Vector2(Rect.sizeDelta.x, windowBody.activeSelf ? expandedHeight : 30f);
                });
            if (dragHandle != null)
                dragHandle.Window = docked ? null : Rect;
            if (closeButton != null)
                closeButton.onClick.AddListener(() => hud.CloseWindow(Id));
        }

        public virtual void Refresh()
        {
        }
    }
}
