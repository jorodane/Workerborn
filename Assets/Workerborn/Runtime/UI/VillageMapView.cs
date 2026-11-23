using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class VillageMapView : MonoBehaviour
    {
        [SerializeField]
        private RawImage map;
        [SerializeField]
        private RectTransform player;
        [SerializeField]
        private TMP_Text npcMarker;
        [SerializeField] private RectTransform jobMarker;
        [SerializeField] private GameObject jobPin;
        [SerializeField] private RectTransform jobArrow;
        [SerializeField] private TMP_Text areaName;
        private VillagePrototype game;
        private string villageName;
        private readonly Dictionary<WorldInteractable, TMP_Text> markers = new Dictionary<WorldInteractable, TMP_Text>();
        private PartTimeScheduleRowView jobSource;
        private Transform jobLocation;
        public void Bind(VillagePrototype owner)
        {
            if (game != null)
                return;
            game = owner;
            map.texture = game.MinimapTexture;
            GetComponent<VillageMapClick>().Game = game;
            if (areaName != null)
                villageName = areaName.text;
            foreach (var npc in game.Interactables)
                AddMarker(npc);
            if (game.RebirthArea != null)
            {
                AddMarker(game.RebirthArea.Guide);
                AddMarker(game.RebirthArea.Exit);
            }
            jobMarker.SetAsLastSibling();
        }

        private void AddMarker(WorldInteractable target)
        {
            if (target == null || markers.ContainsKey(target))
                return;
            var label = Instantiate(npcMarker, transform);
            label.text = target.DisplayName;
            markers.Add(target, label);
        }

        private void LateUpdate()
        {
            if (game == null || game.MinimapCamera == null)
                return;
            if (areaName != null)
                areaName.text = game.InRebirthZone ? game.RebirthArea.MapName : villageName;
            Place(player, game.Worker.transform.position);
            foreach (var pair in markers)
                if (pair.Key != null && pair.Key.isActiveAndEnabled && game.SharesMap(pair.Key.transform.position))
                    Place(pair.Value.rectTransform, pair.Key.transform.position);
                else
                    pair.Value.gameObject.SetActive(false);
            PlaceJobMarker();
        }

        internal void ShowJobLocation(PartTimeScheduleRowView source, Transform location)
        {
            jobSource = source;
            jobLocation = location;
            PlaceJobMarker();
        }

        internal void ClearJobLocation(PartTimeScheduleRowView source)
        {
            if (jobSource != source)
                return;
            jobSource = null;
            jobLocation = null;
            jobMarker.gameObject.SetActive(false);
        }

        private void PlaceJobMarker()
        {
            bool visible = jobLocation != null && game != null && game.MinimapCamera != null && game.SharesMap(jobLocation.position);
            jobMarker.gameObject.SetActive(visible);
            if (!visible)
                return;

            var point = game.MinimapCamera.WorldToViewportPoint(jobLocation.position);
            Vector2 size = ((RectTransform)transform).rect.size;
            Vector2 center = size * .5f;
            Vector2 offset = Vector2.Scale(new Vector2(point.x, point.y), size) - center;
            Vector2 edge = Vector2.Max(Vector2.one, center - jobMarker.sizeDelta * .5f - Vector2.one * 3f);
            float distance = Mathf.Max(Mathf.Abs(offset.x) / edge.x, Mathf.Abs(offset.y) / edge.y);
            bool outside = distance > 1f;
            jobMarker.anchoredPosition = center + (outside ? offset / distance : offset);
            jobPin.SetActive(!outside);
            jobArrow.gameObject.SetActive(outside);
            if (outside)
                jobArrow.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg - 90f);
        }

        private void Place(RectTransform marker, Vector3 position)
        {
            var point = game.MinimapCamera.WorldToViewportPoint(position);
            bool inside = point.z > 0 && point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1;
            marker.gameObject.SetActive(inside);
            if (!inside)
                return;
            var rect = ((RectTransform)transform).rect;
            marker.anchoredPosition = new Vector2(point.x * rect.width, point.y * rect.height);
        }
    }
}
