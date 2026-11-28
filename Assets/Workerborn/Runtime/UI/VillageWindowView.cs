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
        private float expandedHeight;
        private CanvasGroup bodyInput;
        private WindowTransition transition;
        protected VillagePrototype Game;
        protected VillageHud Hud;
        public string Id => windowId;
        public RectTransform Rect => (RectTransform)transform;
        internal bool HasSavedPosition => dragHandle != null && dragHandle.HasSavedPosition;
        internal bool IsFloating => dragHandle != null && Id != "tooltip" && Id != "itemcontext";
        internal bool IsClosing => transition != null && transition.IsClosing;
        internal bool IsOpen => gameObject.activeSelf && !IsClosing;

        public virtual void Bind(VillagePrototype game, VillageHud hud)
        {
            Game = game;
            Hud = hud;
            expandedHeight = Rect.sizeDelta.y;
            transition = GetComponent<WindowTransition>();
            if (windowBody != null)
                bodyInput = windowBody.GetComponent<CanvasGroup>();
            if (minimizeButton != null && windowBody != null)
                minimizeButton.onClick.AddListener(() => SetBodyExpanded(!windowBody.activeSelf));
            if (IsFloating)
                dragHandle.Bind(Rect, Id == "inventory" ? "inventory-top" : Id);
            if (closeButton != null)
                closeButton.onClick.AddListener(() => hud.CloseWindow(Id));
        }

        public virtual void Refresh()
        {
        }

        internal void SetBodyInputEnabled(bool enabled)
        {
            if (bodyInput == null)
                return;
            bodyInput.interactable = enabled;
            bodyInput.blocksRaycasts = enabled;
        }

        protected void SetBodyExpanded(bool expanded)
        {
            windowBody.SetActive(expanded);
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, expanded ? expandedHeight : 30f);
            WindowDragHandle.KeepOnScreen(Rect);
        }

        internal void PlayOpening() => transition?.PlayOpening();

        internal void Hide(bool animate)
        {
            dragHandle?.FinishDrag();
            if (!(animate && transition != null && transition.PlayClosing()))
                gameObject.SetActive(false);
        }

        protected void SetExpandedHeight(float height)
        {
            if (expandedHeight == height)
                return;
            expandedHeight = height;
            if (windowBody != null && !windowBody.activeSelf)
                return;
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            if (IsFloating)
                WindowDragHandle.KeepOnScreen(Rect);
        }
    }
}
