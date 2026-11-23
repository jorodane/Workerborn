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
        [SerializeField] private Sprite npcIcon;
        [SerializeField] private Sprite plotIcon;
        [SerializeField] private bool showPlaceNames = true;
        private VillagePrototype game;
        private string villageName;
        private readonly Dictionary<VillageInteractable, Marker> npcs = new Dictionary<VillageInteractable, Marker>();
        private readonly Dictionary<VillagePlot, Marker> plots = new Dictionary<VillagePlot, Marker>();
        private Marker farm;
        private Vector2 imageSize, imagePadding;
        private Rect area;
        private PartTimeScheduleRowView jobSource;
        private Transform jobLocation;

        private readonly struct Marker
        {
            internal readonly Image Icon;
            internal readonly TMP_Text Label;

            internal Marker(Image icon, TMP_Text label)
            {
                Icon = icon;
                Label = label;
            }
        }

        public void Bind(VillagePrototype owner)
        {
            if (game != null)
                return;
            game = owner;
            var mapClick = GetComponent<VillageMapClick>();
            mapClick.Game = game;
            mapClick.Map = this;
            villageName = areaName.text;
            var image = map.rectTransform;
            image.anchorMin = image.anchorMax = image.pivot = Vector2.one * .5f;
            image.anchoredPosition = Vector2.zero;
            foreach (var npc in game.Interactables)
                if (npc != null)
                    npcs.Add(npc, AddMarker(npc.DisplayName, npcIcon));
            var guide = game.RebirthArea?.Guide;
            if (guide != null)
                npcs.Add(guide, AddMarker(guide.DisplayName, npcIcon));
            foreach (var plot in game.BuildPlots)
                plots.Add(plot, AddMarker(plot.DisplayName, plotIcon));
            if (showPlaceNames && game.FarmMapAnchor != null)
                farm = AddMarker("농장", null);
            if (game.MapRegion != null)
                FitImage();
            jobMarker.SetAsLastSibling();
        }

        private Marker AddMarker(string title, Sprite sprite)
        {
            var node = new GameObject("Map marker", typeof(RectTransform), typeof(Image));
            node.SetActive(false);
            var icon = node.GetComponent<Image>();
            icon.rectTransform.SetParent(transform, false);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = Vector2.zero;
            icon.rectTransform.sizeDelta = Vector2.one * Mathf.Clamp(npcMarker.fontSize, 7f, 12f);
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            if (!showPlaceNames)
                return new Marker(icon, null);
            var label = Instantiate(npcMarker, icon.transform);
            label.text = title;
            label.color = new Color(.20f, .16f, .10f);
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            label.gameObject.SetActive(true);
            return new Marker(icon, label);
        }

        private void FitImage()
        {
            Vector2 available = ((RectTransform)transform).rect.size;
            area = game.MapBounds;
            Vector2 size = area.size;
            if (size.x <= 0f || size.y <= 0f)
                return;
            imageSize = size * Mathf.Min(available.x / size.x, available.y / size.y);
            imagePadding = (available - imageSize) * .5f;
            if (map.rectTransform.sizeDelta != imageSize)
                map.rectTransform.sizeDelta = imageSize;
        }

        internal bool TryGetMapPoint(Vector2 screenPoint, Camera eventCamera, out Vector2 point)
        {
            point = default;
            var image = map.rectTransform;
            if (game == null || area.width <= 0f || area.height <= 0f ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(image, screenPoint, eventCamera, out var local) ||
                !image.rect.Contains(local))
                return false;
            point = Rect.PointToNormalized(image.rect, local);
            return true;
        }

        private void LateUpdate()
        {
            if (game == null)
                return;
            FitImage();
            if (area.width <= 0f || area.height <= 0f)
                return;
            areaName.text = game.InRebirthZone ? game.RebirthArea.MapName : villageName;
            map.texture = game.InRebirthZone ? null : game.MapRegion.TerrainImage(game.GroundTerrain, game);
            map.enabled = map.texture != null;
            Place(player, game.Worker.transform.position);
            foreach (var pair in npcs)
                if (pair.Key != null && pair.Key.isActiveAndEnabled && game.SharesMap(pair.Key.transform.position))
                    Place(pair.Value.Icon.rectTransform, pair.Key.transform.position);
                else
                    pair.Value.Icon.gameObject.SetActive(false);
            foreach (var pair in plots)
            {
                var marker = pair.Value;
                var plot = pair.Key;
                if (plot == null || !plot.isActiveAndEnabled)
                {
                    marker.Icon.gameObject.SetActive(false);
                    continue;
                }
                var facility = plot.InstalledFacility;
                marker.Icon.enabled = facility == null;
                if (marker.Label != null)
                    marker.Label.text = facility != null ? facility.Title : plot.DisplayName;
                Place(marker.Icon.rectTransform, plot.transform.position);
            }
            if (farm.Icon != null)
            {
                var anchor = game.FarmMapAnchor;
                if (anchor != null && anchor.gameObject.activeInHierarchy)
                    Place(farm.Icon.rectTransform, anchor.position);
                else
                    farm.Icon.gameObject.SetActive(false);
            }
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
            bool visible = jobLocation != null && game != null && !game.InRebirthZone &&
                game.MapRegion != null && game.SharesMap(jobLocation.position);
            jobMarker.gameObject.SetActive(visible);
            if (!visible)
                return;

            Vector2 point = VillageMapRegion.WorldToMap(area, jobLocation.position);
            Vector2 center = imageSize * .5f;
            Vector2 offset = Vector2.Scale(point, imageSize) - center;
            Vector2 edge = Vector2.Max(Vector2.one, center - jobMarker.sizeDelta * .5f - Vector2.one * 3f);
            float distance = Mathf.Max(Mathf.Abs(offset.x) / edge.x, Mathf.Abs(offset.y) / edge.y);
            bool outside = distance > 1f;
            jobMarker.anchoredPosition = imagePadding + center + (outside ? offset / distance : offset);
            jobPin.SetActive(!outside);
            jobArrow.gameObject.SetActive(outside);
            if (outside)
                jobArrow.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg - 90f);
        }

        private void Place(RectTransform marker, Vector3 position)
        {
            Vector2 point = VillageMapRegion.WorldToMap(area, position);
            bool inside = point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1;
            marker.gameObject.SetActive(inside);
            if (inside)
                marker.anchoredPosition = imagePadding + Vector2.Scale(point, imageSize);
        }
    }
}
