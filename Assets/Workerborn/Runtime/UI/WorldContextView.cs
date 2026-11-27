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
        private WorldTargetReference target;
        private Ray pointerRay;
        private NpcActivity shownActivity;
        private int shownDay;
        private bool shownJobHours;
        internal WorldInteractable Target => target.Target;

        internal bool Show(WorldInteractable subject, Ray pointerRay)
        {
            target = new WorldTargetReference(subject);
            this.pointerRay = pointerRay;
            if (subject is VillageInteractable npc)
            {
                shownActivity = npc.CurrentActivity;
                shownDay = Game.State.Day;
                shownJobHours = Game.State.JobWindowOpen;
            }
            var shownTarget = target;
            targetName.text = subject.DisplayName;
            InventoryWindowView.Clear(actions);
            choices.Clear();
            subject.AddContextActions(Game, choices, pointerRay);
            if (choices.Count == 0)
                return false;
            foreach (var choice in choices)
            {
                var selected = choice;
                Instantiate(Hud.Library.ActionButton, actions).Bind(selected.Label, () =>
                {
                    Hud.CloseWorldContext();
                    if (shownTarget.Target != null)
                        selected.Select();
                });
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(actions);
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, headerHeight + LayoutUtility.GetPreferredHeight(actions));
            return true;
        }

        private void LateUpdate()
        {
            var subject = target.Target;
            if (subject == null || Game.ControlsBlocked)
                Hud.CloseWorldContext();
            else if (subject is VillageInteractable npc &&
                (npc.CurrentActivity != shownActivity || Game.State.Day != shownDay || Game.State.JobWindowOpen != shownJobHours))
                Show(npc, pointerRay);
        }
    }
}
