using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class PartTimeScheduleRowView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text time;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Color idle = new Color(.10f, .17f, .22f, 1f);
        [SerializeField] private Color active = new Color(.17f, .29f, .36f, 1f);
        [SerializeField] private Color hovered = new Color(.24f, .37f, .44f, 1f);
        private VillageMapView map;
        private Transform workplace;
        private bool isCurrent, pointerOver;

        internal void Bind(VillageMapView view) => map = view;

        internal void Show(string name, string hours, string state, bool current, Transform location)
        {
            title.text = name;
            time.text = hours;
            status.text = state;
            isCurrent = current;
            if (workplace != location)
            {
                workplace = location;
                if (pointerOver && map != null)
                    map.ShowJobLocation(this, workplace);
            }
            UpdateColor();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            pointerOver = true;
            if (map != null)
                map.ShowJobLocation(this, workplace);
            UpdateColor();
        }

        public void OnPointerExit(PointerEventData eventData) => ClearHover();
        private void OnDisable() => ClearHover();

        private void ClearHover()
        {
            if (!pointerOver)
                return;
            pointerOver = false;
            if (map != null)
                map.ClearJobLocation(this);
            UpdateColor();
        }

        private void UpdateColor() => background.color = pointerOver ? hovered : isCurrent ? active : idle;
    }
}
