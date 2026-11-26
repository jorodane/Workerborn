using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    internal sealed class SkillShortcutDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal VillageHud Hud { get; set; }
        internal SkillId Skill { get; set; }
        private bool dragging;

        public void OnBeginDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left)
                dragging = Hud.BeginSkillShortcutDrag(Skill, data.position);
        }

        public void OnDrag(PointerEventData data)
        {
            if (dragging && data.button == PointerEventData.InputButton.Left)
                Hud.MoveSkillShortcutDrag(data.position);
        }

        public void OnEndDrag(PointerEventData data)
        {
            if (!dragging || data.button != PointerEventData.InputButton.Left)
                return;
            dragging = false;
            Hud.EndSkillShortcutDrag(Skill, data.position);
        }

        private void OnDisable()
        {
            if (dragging)
                Hud.CancelSkillShortcutDrag();
            dragging = false;
        }
    }
}
