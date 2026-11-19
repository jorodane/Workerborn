using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class WorldContextView : VillageWindowView
    {
        [SerializeField] private TMP_Text targetName;
        [SerializeField] private RectTransform actions;
        [SerializeField, Min(0)] private float headerHeight = 28f;
        private readonly List<WorldContextAction> choices = new List<WorldContextAction>();
        private WorldInteractable target;

        public void Show(WorldInteractable subject)
        {
            target = subject;
            targetName.text = subject.DisplayName;
            InventoryWindowView.Clear(actions);
            choices.Clear();
            subject.AddContextActions(Game, choices);
            foreach (var choice in choices)
            {
                var selected = choice;
                Instantiate(Hud.Library.ActionButton, actions).Bind(selected.Label, () =>
                {
                    Hud.CloseWorldContext();
                    if (subject != null && subject.CanInteract)
                    {
                        Game.ShowClick(subject.transform.position);
                        selected.Select();
                    }
                });
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(actions);
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, headerHeight + LayoutUtility.GetPreferredHeight(actions));
        }

        private void LateUpdate()
        {
            if (target == null || !target.CanInteract || Game.ControlsBlocked)
                Hud.CloseWorldContext();
        }
    }
}
