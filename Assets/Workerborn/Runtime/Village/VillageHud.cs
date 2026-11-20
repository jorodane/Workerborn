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
        private readonly List<FloatingText> floating = new List<FloatingText>();
        private PrefabPool<TMP_Text> floatingTexts;
        private int seenVersion = -1;
        private int seenInventoryVersion = -1;
        private int seenNotice = -1;
        private bool suppressRightContext;
        private static readonly string[] Weekdays = { "월요일", "화요일", "수요일", "목요일", "금요일", "토요일", "일요일" };
        [SerializeField]
        private RectTransform minimapPanel;
        [SerializeField]
        private RectTransform minimapImage;
        public bool HasModal => dialogueTarget != null;
        public bool IsHoldingItem => draggedEntry != 0;
        public bool InventoryOpen => windows.TryGetValue("inventory", out var panel) && panel.gameObject.activeSelf;

        private struct FloatingText
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
            floatingTexts = new PrefabPool<TMP_Text>(library.FloatingText, transform);
            floatingTexts.Prewarm(12);
            pickupNames?.Initialize(game, game.PrewarmGroundPickups);
            game.Worker.Message += ShowToast;
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
            var text = floatingTexts.Rent();
            text.text = content;
            text.color = color;
            PlaceWorld(text.rectTransform, position);
            floating.Add(new FloatingText { Text = text, Position = position, Started = Time.unscaledTime });
        }

        internal void ClearFloatingText()
        {
            foreach (var entry in floating)
                floatingTexts.Return(entry.Text);
            floating.Clear();
        }

        private void Update()
        {
            if (WorkerbornInput.Current == null)
                return;
            if (WorkerbornInput.Pressed("Gameplay/Cancel"))
            {
                bool hadWindow = IsHoldingItem;
                if (HasModal)
                {
                    CloseDialogue();
                    hadWindow = true;
                }
                else
                    foreach (var window in windows.Values)
                        if (window.gameObject.activeSelf)
                        {
                            hadWindow = true;
                            window.gameObject.SetActive(false);
                        }
                CancelItemDrag();
                if (!hadWindow)
                    OpenWindow("system")?.Refresh();
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
            ReleaseDraggedItem();
            var state = game.State;
            int minute = Mathf.FloorToInt(state.Minute);
            string weekday = Weekdays[(state.Day - 1) % Weekdays.Length];
            clockText.text = $"{weekday}\n{minute / 60:00}:{minute % 60:00}";
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

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && IsHoldingItem)
                CancelItemDrag();
        }

        private void UpdateWorldFeedback()
        {
            var worker = game.Worker;
            var active = worker.ActiveCrop;
            skillBubble.gameObject.SetActive(active != null && !HasModal);
            if (active != null && !HasModal)
            {
                skillBalloon.SetIcon(active.Definition.SkillIcon);
                skillBalloon.SetProgress(active.Progress);
                PlaceWorld(skillBubble, worker.transform.position + Vector3.up * skillWorldHeight);
            }

            var target = game.Targeting != null ? game.Targeting.CurrentTarget : null;
            bool show = !game.ControlsBlocked && target != null && target.CanInteract;
            hoverTip.gameObject.SetActive(show);
            if (show)
            {
                hoverText.text = target.InteractionVerbFor(worker);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, WorkerbornInput.Point, null, out var cursor);
                var offset = hoverTip.GetComponent<WorldTooltipView>().CursorOffset;
                Vector2 at = cursor + offset;
                at.x = Mathf.Clamp(at.x, CanvasRect.rect.xMin, CanvasRect.rect.xMax - hoverTip.rect.width);
                at.y = Mathf.Clamp(at.y, CanvasRect.rect.yMin + hoverTip.rect.height, CanvasRect.rect.yMax);
                hoverTip.anchoredPosition = at - CanvasRect.rect.min;
            }

            for (int i = floating.Count - 1; i >= 0; i--)
            {
                var entry = floating[i];
                float age = Time.unscaledTime - entry.Started;
                if (age >= 1.5f)
                {
                    floatingTexts.Return(entry.Text);
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

        public void OpenQuestDetail(bool expansion) => OpenQuestDetail(expansion ? VillageQuestPage.Expansion : VillageQuestPage.FirstWork);

        public void OpenWorldContext(WorldInteractable subject, Vector2 screen)
        {
            if (subject == null || !subject.CanInteract || HasModal || IsHoldingItem)
                return;
            HideItemTooltip();
            CloseItemContext();
            var view = OpenWindow("worldcontext") as WorldContextView;
            if (view == null)
                return;
            view.Show(subject);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, screen, null, out var point);
            var bounds = CanvasRect.rect;
            point.x = Mathf.Clamp(point.x, bounds.xMin, bounds.xMax - view.Rect.rect.width);
            point.y = Mathf.Clamp(point.y, bounds.yMin + view.Rect.rect.height, bounds.yMax);
            view.Rect.anchoredPosition = point;
        }

        public void CloseWorldContext()
        {
            if (views.TryGetValue("worldcontext", out var view))
                view.gameObject.SetActive(false);
        }

        public void OpenQuestDetail(VillageQuestPage page)
        {
            var view = OpenWindow("questdetail") as QuestDetailWindowView;
            view?.Select(page);
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
            if (game.State.TryGetDocumentPage(id, out _))
            {
                ShowToast("계약서는 판매할 수 없습니다.");
                return;
            }
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
