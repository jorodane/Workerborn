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
        [SerializeField] private RawImage selectionOutline;
        [SerializeField] private GameObject dialogueBackdrop;
        [SerializeField] private RectTransform chatLayer;
        [SerializeField] private TMP_Text dateText;
        [SerializeField] private TMP_Text weatherText;
        private readonly List<VillageWindowView> windowFocus = new List<VillageWindowView>();
        private int clockDay, clockMinute = -1;
        private Vector2 lastCanvasSize;


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
        private RectTransform minimapImage;
        public bool HasModal => dialogueTarget != null;
        public bool IsShopOpen => views.TryGetValue("shop", out var shop) && shop.gameObject.activeSelf;
        public bool WorldActionsLocked => HasModal || IsShopOpen || HasQuantityPrompt;
        public bool AllowsGroundItems => game != null && !game.AwaitingWorldCommand && !IsChatTyping;
        public bool HasQuantityPrompt => views.TryGetValue("quantity", out var quantity) && quantity.gameObject.activeSelf;
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
            InitializeChat();
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
            if (toastText == null || string.IsNullOrEmpty(message))
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
            if (WorkerbornInput.Current == null || game == null)
                return;
            if (!game.AwaitingWorldCommand &&
                (WorkerbornInput.Pressed("Gameplay/Interact") || WorkerbornInput.Pressed("Camera/OrbitRight")))
            {
                var hit = game.Targeting?.UiAt(WorkerbornInput.Point);
                if (hit != null)
                    FocusWindow(hit.GetComponentInParent<VillageWindowView>());
            }
            if (HandleChatInput() || game.AwaitingWorldCommand)
                return;
            if (WorkerbornInput.Pressed("Gameplay/Cancel"))
            {
                if (!CloseFocusedWindow())
                {
                    if (IsHoldingItem)
                        CancelItemDrag();
                    else
                        OpenWindow("system")?.Refresh();
                }
                return;
            }

            if (WorkerbornInput.Pressed("Gameplay/Inventory"))
                ToggleWindow("inventory");
            if (WorkerbornInput.Pressed("Gameplay/Quests"))
                ToggleWindow("quests");
            if (WorkerbornInput.Pressed("Gameplay/Skills"))
                ToggleWindow("skills");
            if (WorkerbornInput.Pressed("Gameplay/Character"))
                ToggleWindow("character");
            if (WorkerbornInput.Pressed("Gameplay/Map"))
                ToggleWindow("map");
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
            if (!game.AwaitingWorldCommand)
                ReleaseDraggedItem();
            var state = game.State;
            int minute = Mathf.FloorToInt(state.Minute);
            if (minute != clockMinute)
            {
                clockMinute = minute;
                clockText.text = $"{minute / 60:00}:{minute % 60:00}";
            }
            if (state.Day != clockDay)
            {
                clockDay = state.Day;
                if (dateText != null)
                    dateText.text = $"{state.Day}일 · {Weekdays[(state.Day - 1) % Weekdays.Length]}";
                if (weatherText != null)
                    weatherText.text = game.WeatherName;
            }
            if (lastCanvasSize != CanvasRect.rect.size)
            {
                lastCanvasSize = CanvasRect.rect.size;
                foreach (var view in views.Values)
                    if (view.IsFloating && view.gameObject.activeSelf)
                        WindowDragHandle.KeepOnScreen(view.Rect);
            }
            if (localOverhead != null)
                PositionPlayerOverhead(localOverhead, game.PlayerActor.NamePosition);
            int stamina = state.StaminaLimit;
            energyText.text = $"HP {state.Health}/{state.HealthLimit}   SP {state.Energy}/{stamina}";
            energyText.color = state.Energy < 20 ? new Color(1f, 0.65f, 0.5f) : Color.white;
            energyFill.fillAmount = (float)state.Energy / stamina;
            growthText.text = $"Lv.{state.Level}";
            experienceFill.fillAmount = state.Level == VillageState.LevelLimit ? 1f : (float)state.Experience / state.NextLevelExperience;
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

        internal void SetCommandPending(bool pending)
        {
            if (!TryGetComponent<CanvasGroup>(out var controls))
            {
                if (!pending)
                    return;
                controls = gameObject.AddComponent<CanvasGroup>();
            }
            controls.interactable = !pending;
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
                skillBalloon.SetIcon(game.Settings.FindSkill(active.Definition.Skill)?.Icon ?? active.Definition.SkillIcon);
                skillBalloon.SetProgress(active.Progress);
                PlaceWorld(skillBubble, worker.transform.position + Vector3.up * skillWorldHeight);
            }

            var target = game.Targeting != null ? game.Targeting.CurrentTarget : null;
            bool show = target != null && target.CanInteract && !IsHoldingItem;
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
            if (view.IsFloating)
                WindowDragHandle.KeepOnScreen(view.Rect);
            FocusWindow(view);
            if (id == "tooltip")
                view.transform.SetAsLastSibling();
            return view;
        }

        private void FocusWindow(VillageWindowView view)
        {
            if (view == null || view.Id == "tooltip" || !view.gameObject.activeSelf)
                return;
            windowFocus.Remove(view);
            windowFocus.Add(view);
            if (view.Id != "conversation" || !IsShopOpen)
                view.transform.SetAsLastSibling();
        }

        private bool CloseFocusedWindow()
        {
            for (int index = windowFocus.Count - 1; index >= 0; index--)
            {
                var view = windowFocus[index];
                if (view == null || !view.gameObject.activeSelf)
                {
                    windowFocus.RemoveAt(index);
                    continue;
                }
                CloseWindow(view.Id);
                return true;
            }
            return false;
        }

        public void CloseWindow(string id)
        {
            if (id == "conversation")
            {
                CloseDialogue();
                return;
            }
            if (!views.TryGetValue(id, out var view) || !view.gameObject.activeSelf)
                return;
            view.gameObject.SetActive(false);
            windowFocus.Remove(view);
            HideItemTooltip();
            if (id == "inventory")
            {
                CancelItemDrag();
                CloseItemContext();
            }
            if (id == "shop")
            {
                if (views.TryGetValue("quantity", out var quantity) && quantity is QuantityWindowView prompt && prompt.IsTrade)
                    CloseQuantity();
                SetTradeDialogue(false);
            }
        }

        public void ToggleWindow(string id)
        {
            if (views.TryGetValue(id, out var view) && view.gameObject.activeSelf)
                CloseWindow(id);
            else
                OpenWindow(id)?.Refresh();
        }

        internal void SetSelectionOutline(RenderTexture texture)
        {
            if (selectionOutline == null)
                return;
            selectionOutline.texture = texture;
            selectionOutline.uvRect = SystemInfo.graphicsUVStartsAtTop ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
            selectionOutline.gameObject.SetActive(texture != null);
        }

        public void OpenQuestDetail(bool expansion) => OpenQuestDetail(expansion ? VillageQuestPage.Expansion : VillageQuestPage.FirstWork);

        public void OpenWorldContext(WorldInteractable subject, Vector2 screen)
        {
            if (subject == null || !subject.CanInteract || WorldActionsLocked && !(subject is GroundPickup) || IsHoldingItem)
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
                CloseWindow(view.Id);
        }

        public void OpenQuestDetail(VillageQuestPage page)
        {
            var view = OpenWindow("questdetail") as QuestDetailWindowView;
            view?.Select(page);
        }

        public void OpenShop(VillageInteractable npc)
        {
            if (dialogueTarget != npc)
                OpenDialogue(npc);
            CloseQuantity();
            CancelItemDrag();
            if (InventoryOpen)
                FocusWindow(views["inventory"]);
            else
                OpenWindow("inventory")?.Refresh();
            bool wasOpen = IsShopOpen;
            if (!(OpenWindow("shop") is ShopWindowView shop))
                return;
            shop.Open(npc);
            if (!wasOpen && !shop.HasSavedPosition && windows.TryGetValue("inventory", out var inventory))
            {
                const float gap = 8f;
                var bag = WindowDragHandle.BoundsInParent(inventory);
                Rect canvas = CanvasRect.rect;
                var origin = new Vector2(bag.xMax, bag.yMax + gap);
                if (origin.y + shop.Rect.rect.height > canvas.yMax && bag.xMin - gap - shop.Rect.rect.width >= canvas.xMin)
                    origin = new Vector2(bag.xMin - gap, bag.yMin);
                shop.Rect.anchoredPosition = origin - new Vector2(canvas.xMax, canvas.yMin);
                WindowDragHandle.KeepOnScreen(shop.Rect);
            }
            SetTradeDialogue(true);
        }

        internal void OpenPurchaseQuantity(string id)
        {
            HideItemTooltip();
            OpenQuantity(id, QuantityAction.Buy);
        }

        private void OpenStackQuantity(InventoryEntry item, QuantityAction action)
        {
            HideItemTooltip();
            OpenQuantity(item.ItemId, action, item.Id);
        }

        private void OpenQuantity(string id, QuantityAction action, int stackId = 0)
        {
            bool wasOpen = HasQuantityPrompt;
            if (!(OpenWindow("quantity") is QuantityWindowView prompt))
                return;
            prompt.Open(id, action, stackId);
            if (wasOpen || prompt.HasSavedPosition)
                return;

            const float gap = 10f;
            var canvas = CanvasRect.rect;
            float right = canvas.xMax - gap;
            if (windows.TryGetValue(action == QuantityAction.Buy ? "shop" : "inventory", out var panel) && panel.gameObject.activeSelf)
                right = WindowDragHandle.BoundsInParent(panel).xMin - gap;
            var rect = prompt.Rect;
            rect.anchoredPosition = new Vector2(right - canvas.xMax, Mathf.Max(28f, (canvas.height - rect.rect.height) * .5f));
            WindowDragHandle.KeepOnScreen(rect);
        }

        private void CloseQuantity()
        {
            CloseWindow("quantity");
        }

        public void SellHeldItem()
        {
            var item = game.State.Inventory.Find(draggedEntry);
            if (item == null)
                return;
            CancelItemDrag();
            if (game.State.MaximumSellQuantity(item.Id) == 0)
            {
                ShowToast("이 물건은 판매할 수 없습니다.");
                return;
            }
            OpenStackQuantity(item, QuantityAction.Sell);
        }

        private void ShowAt(VillageWindowView view, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, screen, null, out var local);
            var bounds = CanvasRect.rect;
            local.x = Mathf.Clamp(local.x, bounds.xMin, bounds.xMax - view.Rect.rect.width);
            local.y = Mathf.Clamp(local.y, bounds.yMin + view.Rect.rect.height, bounds.yMax);
            view.Rect.anchoredPosition = local;
        }

        private void OnDestroy()
        {
            if (game != null && game.Worker != null)
                game.Worker.Message -= ShowToast;
        }
    }
}
