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
        private VillagePrototype game;
        private readonly Dictionary<VillageInteractable, TMP_Text> markers = new Dictionary<VillageInteractable, TMP_Text>();
        public void Bind(VillagePrototype owner)
        {
            if (game != null)
                return;
            game = owner;
            map.texture = game.MinimapTexture;
            GetComponent<VillageMapClick>().Game = game;
            foreach (var npc in game.Interactables)
            {
                var label = Instantiate(npcMarker, transform);
                label.text = npc.DisplayName;
                label.gameObject.SetActive(true);
                markers.Add(npc, label);
            }
        }

        private void LateUpdate()
        {
            if (game == null || game.MinimapCamera == null)
                return;
            Place(player, game.Worker.transform.position);
            foreach (var pair in markers)
                if (pair.Key != null)
                    Place(pair.Value.rectTransform, pair.Key.transform.position);
        }

        private void Place(RectTransform marker, Vector3 position)
        {
            var point = game.MinimapCamera.WorldToViewportPoint(position);
            var rect = ((RectTransform)transform).rect;
            marker.anchoredPosition = new Vector2(point.x * rect.width, point.y * rect.height);
        }
    }
}
