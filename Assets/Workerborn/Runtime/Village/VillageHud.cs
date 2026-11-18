using TMPro;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed partial class VillageHud : MonoBehaviour
    {
        private VillagePrototype game;
        [SerializeField]
        private VillageUiLibrary library;
        [SerializeField]
        private PickupNames pickupNames;
        [SerializeField]
        private TargetLinkView targetLink;
        private readonly Dictionary<string, VillageWindowView> views = new Dictionary<string, VillageWindowView>();
        public VillageUiLibrary Library => library;
        public TargetLinkView TargetLink => targetLink;
        public RectTransform CanvasRect => (RectTransform)transform;

        [SerializeField]
        private TMP_Text clockText;
        [SerializeField]
        private TMP_Text energyText;
        [SerializeField]
        private TMP_Text growthText;
        [SerializeField]
        private TMP_Text toastText;
        [SerializeField]
        private Image energyFill;
        [SerializeField]
        private Image experienceFill;
        [SerializeField]
        private RectTransform toast;
        private float toastUntil;
        private RectTransform skillBubble;
        private SkillBalloonView skillBalloon;
        private RectTransform hoverTip;
        private TMP_Text hoverText;
        private readonly Queue<string> log = new Queue<string>();
        private string lastLoggedMessage;
        private readonly Dictionary<string, RectTransform> windows = new Dictionary<string, RectTransform>();
        private readonly Dictionary<VillageInteractable, WorldNameplateView> npcLabels = new Dictionary<VillageInteractable, WorldNameplateView>();
        private readonly List<FloatingText> floating = new List<FloatingText>();
        private int seenVersion = -1;
        private int seenInventoryVersion = -1;
        private int seenNotice = -1;
        private bool suppressRightContext;
        [SerializeField]
        private RectTransform minimapPanel;
        [SerializeField]
        private RectTransform minimapImage;
        public bool HasModal => dialogueTarget != null;
        public bool IsHoldingItem => draggedEntry != 0;
        public bool InventoryOpen => windows.TryGetValue("inventory", out var panel) && panel.gameObject.activeSelf;

        private sealed class FloatingText
        {
            public TMP_Text Text;
            public Vector3 Position;
            public float Started;
        }

        public void Initialize(VillagePrototype prototype, TMP_FontAsset interfaceFont)
        {
            game = prototype;
            if (GetComponent<Canvas>() == null || GetComponent<GraphicRaycaster>() == null)
            {
                Debug.LogError("Assign the complete HUD prefab with its Canvas and raycaster.", this);
                return;
            }

            if (EventSystem.current == null)
            {
                var events = new GameObject("Village input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                WorkerbornInput.Current?.BindUi(events.GetComponent<InputSystemUIInputModule>());
            }
            else
                WorkerbornInput.Current?.BindUi(EventSystem.current.GetComponent<InputSystemUIInputModule>());
            if (clockText == null || library == null)
            {
                Debug.LogError("Assign the authored HUD and UI prefab library.", this);
                return;
            }

            if (minimapImage != null)
                DrawMap(minimapImage, false);
            BuildWorldFeedback();
            pickupNames?.Initialize(game);
            game.Worker.Message += ShowToast;
            foreach (var target in game.Interactables)
            {
                var text = Instantiate(library.NpcName, transform);
                text.Bind(target, game.Targeting);
                text.SetBox(WorkerbornInput.Held("Gameplay/Names"));
                npcLabels.Add(target, text);
            }
        }

        private float skillWorldHeight;
        private void BuildWorldFeedback()
        {
            var balloon = Instantiate(library.SkillBalloon, transform);
            skillBubble = balloon.Rect;
            skillBalloon = balloon;
            skillWorldHeight = balloon.WorldHeight;
            skillBubble.gameObject.SetActive(false);
            var tooltip = Instantiate(library.WorldTooltip, transform);
            hoverTip = tooltip.Rect;
            hoverText = tooltip.Label;
            hoverTip.gameObject.SetActive(false);
        }

        public void ShowToast(string message)
        {
            if (toastText == null)
                return;
            toastText.text = message;
            toastUntil = Time.unscaledTime + 5f;
            string shortMessage = message.Length > 42 ? message.Substring(0, 41) + "…" : message;
            if (lastLoggedMessage != message)
            {
                log.Enqueue(shortMessage);
                while (log.Count > 3)
                    log.Dequeue();
                lastLoggedMessage = message;
            }
        }

        public void ShowFloatingText(Vector3 position, string content, Color color)
        {
            var text = Instantiate(library.FloatingText, transform);
            text.text = content;
            text.color = color;
            floating.Add(new FloatingText { Text = text, Position = position, Started = Time.unscaledTime });
        }

        private void Update()
        {
            if (WorkerbornInput.Current == null)
                return;
            if (WorkerbornInput.Pressed("Gameplay/Cancel"))
            {
                if (HasModal)
                    CloseDialogue();
                else
                    foreach (var window in windows.Values)
                        window.gameObject.SetActive(false);
                CancelItemDrag();
            }

            if (WorkerbornInput.Pressed("Gameplay/Inventory"))
                ToggleWindow("inventory");
            if (WorkerbornInput.Pressed("Gameplay/Quests"))
                ToggleWindow("quests");
            if (WorkerbornInput.Pressed("Gameplay/Skills"))
                ToggleWindow("skills");
            if (WorkerbornInput.Pressed("Gameplay/Character"))
                ToggleWindow("character");
            if (WorkerbornInput.Pressed("Gameplay/Map") && minimapPanel != null)
                minimapPanel.gameObject.SetActive(!minimapPanel.gameObject.activeSelf);
            if (WorkerbornInput.Current != null)
            {
                if (IsHoldingItem)
                    MoveItemDrag(WorkerbornInput.Point);
                if (WorkerbornInput.Pressed("Camera/OrbitRight"))
                {
                    suppressRightContext = IsHoldingItem;
                    if (IsHoldingItem)
                        CancelItemDrag();
                }
            }
        }

        private void LateUpdate()
        {
            if (game == null || game.Worker == null)
                return;
            var state = game.State;
            int minute = Mathf.FloorToInt(state.Minute);
            clockText.text = $"{minute / 60:00} : {minute % 60:00}";
            energyText.text = $"{state.Energy} / 100";
            energyText.color = state.Energy < 20 ? new Color(1f, 0.65f, 0.5f) : Color.white;
            energyFill.fillAmount = state.Energy / 100f;
            growthText.text = $"Lv.{state.Level}";
            experienceFill.fillAmount = (float)state.Experience / state.NextLevelExperience;
            toast.gameObject.SetActive(Time.unscaledTime < toastUntil);
            if (state.NoticeSerial != seenNotice)
            {
                if (state.NoticeSerial > 0)
                    ShowToast(state.LastMessage);
                seenNotice = state.NoticeSerial;
            }

            if (state.Version != seenVersion || state.Inventory.Version != seenInventoryVersion)
            {
                seenVersion = state.Version;
                seenInventoryVersion = state.Inventory.Version;
                RefreshWindows();
            }

            UpdateWorldFeedback();
        }

        private void UpdateWorldFeedback()
        {
            var worker = game.Worker;
            var active = worker.ActiveCrop;
            skillBubble.gameObject.SetActive(active != null && !HasModal);
            if (active != null && !HasModal)
            {
                skillBalloon.SetProgress(active.Progress);
                PlaceWorld(skillBubble, worker.transform.position + Vector3.up * skillWorldHeight);
            }

            var target = game.Targeting != null ? game.Targeting.CurrentTarget : null;
            bool show = !game.ControlsBlocked && target != null && target.CanInteract;
            hoverTip.gameObject.SetActive(show);
            if (show)
            {
                hoverText.text = target.InteractionVerb;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, WorkerbornInput.Point, null, out var cursor);
                var offset = hoverTip.GetComponent<WorldTooltipView>().CursorOffset;
                Vector2 at = cursor + offset;
                at.x = Mathf.Clamp(at.x, CanvasRect.rect.xMin, CanvasRect.rect.xMax - hoverTip.rect.width);
                at.y = Mathf.Clamp(at.y, CanvasRect.rect.yMin + hoverTip.rect.height, CanvasRect.rect.yMax);
                hoverTip.anchoredPosition = at - CanvasRect.rect.min;
            }

            foreach (var pair in npcLabels)
            {
                pair.Value.SetText(pair.Key.DisplayName);
                pair.Value.SetBox(WorkerbornInput.Held("Gameplay/Names"));
                PlaceWorld(pair.Value.Rect, pair.Key.LabelPosition);
            }

            for (int i = floating.Count - 1; i >= 0; i--)
            {
                var entry = floating[i];
                float age = Time.unscaledTime - entry.Started;
                if (age >= 1.5f)
                {
                    Destroy(entry.Text.gameObject);
                    floating.RemoveAt(i);
                    continue;
                }

                Color color = entry.Text.color;
                color.a = 1f - age / 1.5f;
                entry.Text.color = color;
                PlaceWorld(entry.Text.rectTransform, entry.Position + Vector3.up * age * 0.6f);
            }
        }

        private void PlaceWorld(RectTransform rect, Vector3 position)
        {
            Vector3 screen = game.WorldCamera.WorldToScreenPoint(position);
            bool visible = screen.z > 0f && screen.x > -30f && screen.x < Screen.width + 30f && screen.y > 0f && screen.y < Screen.height;
            rect.gameObject.SetActive(visible);
            if (!visible)
                return;
            var root = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local);
            rect.anchoredPosition = local - root.rect.min;
        }

        public VillageWindowView OpenWindow(string id)
        {
            if (!views.TryGetValue(id, out var view))
            {
                var prefab = library != null ? library.Find(id) : null;
                if (prefab == null)
                {
                    Debug.LogError("Assign the UI prefab for " + id, this);
                    return null;
                }

                view = Instantiate(prefab, transform);
                views.Add(id, view);
                windows.Add(id, view.Rect);
                view.Bind(game, this);
            }

            view.gameObject.SetActive(true);
            view.transform.SetAsLastSibling();
            return view;
        }

        public void ToggleWindow(string id)
        {
            if (id == "conversation")
            {
                CloseDialogue();
                return;
            }

            if (views.TryGetValue(id, out var existing) && existing.gameObject.activeSelf)
            {
                existing.gameObject.SetActive(false);
                if (id == "inventory")
                    CancelItemDrag();
                return;
            }

            OpenWindow(id)?.Refresh();
        }

        public void OpenQuestDetail(bool expansion)
        {
            var view = OpenWindow("questdetail") as QuestDetailWindowView;
            view?.Select(expansion);
        }

        public void OpenShop(VillageInteractable npc)
        {
            CloseDialogue();
            (OpenWindow("shop") as ShopWindowView)?.Open(npc);
            if (!InventoryOpen)
                ToggleWindow("inventory");
        }

        public void OpenQuantity(string id, bool selling)
        {
            (OpenWindow("quantity") as QuantityWindowView)?.Open(id, selling);
        }

        public void SellHeldItem()
        {
            var item = game.State.Inventory.Find(draggedEntry);
            if (item == null)
                return;
            string id = item.ItemId;
            CancelItemDrag();
            OpenQuantity(id, true);
        }

        private void ShowAt(VillageWindowView view, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, screen, null, out var local);
            view.Rect.anchoredPosition = local;
        }

        private static string RankName(int rank) => new[]
        {
            "F",
            "E",
            "D",
            "C",
            "B",
            "A",
            "9",
            "8",
            "7"
        }[Mathf.Clamp(rank, 0, 8)];
        private void OnDestroy()
        {
            if (game != null && game.Worker != null)
                game.Worker.Message -= ShowToast;
        }
    }
}
