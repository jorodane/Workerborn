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
        [SerializeField] private GameObject normalHud;
        [SerializeField] private ItemShortcutBarView itemShortcuts;
        [SerializeField] private JobWaypointView jobWaypoint;
        [SerializeField] private RectTransform chatLayer;
        [SerializeField] private VillageClockView clock;
        [SerializeField] private VillageMapView minimap;
        [SerializeField] private PartTimeScheduleView mapSchedule;
        [SerializeField] private CanvasGroup worldFade;
        private readonly List<VillageWindowView> windowFocus = new List<VillageWindowView>();
        private Vector2 lastCanvasSize;
        internal VillageJobSchedule Jobs { get; private set; }


        [SerializeField]
        private TMP_Text energyText;
        [SerializeField]
        private TMP_Text growthText;
        [SerializeField]
        private TMP_Text toastText;
        [SerializeField]
        private HealthBarView healthBar;
        [SerializeField]
        private Image energyFill;
        [SerializeField]
        private Image experienceFill;
        [SerializeField]
        private CanvasGroup toast;
        private const float ToastFadeIn = .22f;
        private const float ToastHold = 2.5f;
        private const float ToastFadeOut = .48f;
        private float toastStartedAt;
        private float toastInitialAlpha;
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
        public bool HasModal => dialogueTarget != null;
        public bool IsShopOpen => views.TryGetValue("shop", out var shop) && shop.IsOpen;
        public bool WorldActionsLocked => HasModal || dialogueClosing || IsShopOpen || HasQuantityPrompt;
        public bool AllowsGroundItems => game != null && !game.IsWorldTransitioning && !game.AwaitingWorldCommand && !IsChatTyping;
        public bool HasQuantityPrompt => views.TryGetValue("quantity", out var quantity) && quantity.IsOpen;
        internal bool TradeQuantityOpen => views.TryGetValue("quantity", out var quantity) &&
            quantity is QuantityWindowView { IsOpen: true, IsTrade: true };
        public bool IsHoldingItem => draggedEntry != 0 || game?.State.PendingPurchase != null;
        public bool InventoryOpen => views.TryGetValue("inventory", out var inventory) && inventory.IsOpen;

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
            if (clock == null || library == null)
            {
                Debug.LogError("Assign the authored HUD and UI prefab library.", this);
                return;
            }

            Jobs = new VillageJobSchedule(game);
            clock.Bind(game);
            minimap?.Bind(game);
            mapSchedule?.Bind(game);
            if (dialogueBackdrop != null)
                backdropTransitions = dialogueBackdrop.GetComponentsInChildren<WindowTransition>(true);
            BuildWorldFeedback();
            itemShortcuts?.Bind(this);
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
            if (toast == null || toastText == null || string.IsNullOrEmpty(message))
                return;
            toastInitialAlpha = toast.gameObject.activeSelf && toastText.text == message ? toast.alpha : 0f;
            toastStartedAt = Time.unscaledTime;
            toastText.text = message;
            toast.alpha = toastInitialAlpha;
            toast.gameObject.SetActive(true);
            string shortMessage = message.Length > 42 ? message.Substring(0, 41) + "…" : message;
            if (lastLoggedMessage != message)
            {
                log.Enqueue(shortMessage);
                while (log.Count > 3)
                    log.Dequeue();
                lastLoggedMessage = message;
            }
        }

        private void UpdateToast()
        {
            if (toast == null || !toast.gameObject.activeSelf)
                return;
            float elapsed = Time.unscaledTime - toastStartedAt;
            float remaining = ToastFadeIn + ToastHold + ToastFadeOut - elapsed;
            if (remaining <= 0f)
            {
                toast.gameObject.SetActive(false);
                return;
            }
            float fadeIn = Mathf.SmoothStep(toastInitialAlpha, 1f, elapsed / ToastFadeIn);
            float fadeOut = Mathf.SmoothStep(0f, 1f, remaining / ToastFadeOut);
            toast.alpha = fadeIn * fadeOut;
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
            if (WorkerbornInput.Current == null || game == null || game.IsWorldTransitioning)
                return;
            bool hasPointer = WorkerbornInput.TryGetPointerPosition(out var cursor);
            if (hasPointer && !game.AwaitingWorldCommand &&
                (WorkerbornInput.Pressed("Gameplay/Interact") || WorkerbornInput.Pressed("Camera/OrbitRight")))
            {
                var hit = game.Targeting?.UiAt(cursor);
                if (hit != null)
                    FocusWindow(hit.GetComponentInParent<VillageWindowView>());
            }
            if ((!game.AwaitingWorldCommand && HandleDialoguePageInput()) ||
                HandleChatInput() || game.AwaitingWorldCommand || dialogueClosing)
                return;
            if (WorkerbornInput.Pressed("Gameplay/Cancel"))
            {
                if (IsShopOpen)
                    TryLeaveDialogue();
                else if (!CloseFocusedWindow())
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
            ReadItemShortcuts();
            if (hasPointer)
            {
                if (IsHoldingItem)
                    MoveItemDrag(cursor);
                if (WorkerbornInput.Pressed("Camera/OrbitRight"))
                {
                    suppressRightContext = IsHoldingItem;
                    if (IsHoldingItem && !HasQuantityPrompt)
                        CancelItemDrag();
                }
            }
        }

        private void LateUpdate()
        {
            if (game == null || game.Worker == null)
                return;
            UpdateDialogueClose();
            if (IsHoldingItem && dragGhost == null)
                ShowHeldItem(WorkerbornInput.Point);
            else if (!IsHoldingItem && dragGhost != null)
                ClearHeldVisual();
            if (!game.IsWorldTransitioning && !game.AwaitingWorldCommand)
                ReleaseDraggedItem();
            var state = game.State;
            if (lastCanvasSize != CanvasRect.rect.size)
            {
                lastCanvasSize = CanvasRect.rect.size;
                foreach (var view in views.Values)
                    if (view.IsFloating && view.IsOpen)
                        WindowDragHandle.KeepOnScreen(view.Rect);
            }
            UpdatePlayerOverhead();
            int stamina = state.StaminaLimit;
            energyText.text = $"HP {state.Health}/{state.HealthLimit}   SP {state.Energy}/{stamina}";
            energyText.color = state.Energy < 20 ? new Color(1f, 0.65f, 0.5f) : Color.white;
            healthBar.SetValue(state.Health, state.HealthLimit);
            energyFill.fillAmount = (float)state.Energy / stamina;
            growthText.text = $"Lv.{state.Level}";
            experienceFill.fillAmount = state.Level == VillageState.LevelLimit ? 1f : (float)state.Experience / state.NextLevelExperience;
            if (state.NoticeSerial != seenNotice)
            {
                if (state.NoticeSerial > 0)
                    ShowToast(state.LastMessage);
                seenNotice = state.NoticeSerial;
            }
            Jobs.Update();
            UpdateJobWaypoint();
            UpdateToast();

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

        internal void SetWorldFade(float opacity, bool active)
        {
            if (worldFade == null)
                return;
            worldFade.alpha = opacity;
            if (worldFade.gameObject.activeSelf != active)
            {
                if (active)
                    releaseHeldOnMouseUp = false;
                worldFade.gameObject.SetActive(active);
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && draggedEntry != 0)
                CancelItemDrag();
        }

        private void UpdateJobWaypoint()
        {
            if (jobWaypoint == null)
                return;
            WorldInteractable target = Jobs.PinnedProvider;
            if (HasModal || game.IsWorldTransitioning || game.InRebirthZone)
                target = null;
            else if (target != null && !game.SharesMap(target.transform.position))
                target = game.CurrentInterior?.Exit;
            jobWaypoint.Display(game.WorldCamera, CanvasRect, target);
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
            bool visible = screen.z > 0f && screen.z <= game.WorldCamera.farClipPlane &&
                screen.x > -30f && screen.x < Screen.width + 30f && screen.y > 0f && screen.y < Screen.height;
            rect.gameObject.SetActive(visible);
            if (!visible)
                return;
            var root = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local);
            rect.anchoredPosition = local - root.rect.min;
        }

        public VillageWindowView OpenWindow(string id) => OpenWindow(id, temporaryInDialogue: false);

        private VillageWindowView OpenWindow(string id, bool temporaryInDialogue)
        {
            if (!CanShowInCurrentLife(id))
                return null;
            bool opening;
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
                opening = true;
            }
            else
                opening = !view.IsOpen;

            if (HasModal)
            {
                if (temporaryInDialogue)
                    dialogueWindowStates.TryAdd(id, !opening);
                else
                    dialogueWindowStates.Remove(id);
            }
            view.gameObject.SetActive(true);
            if (view.IsFloating)
                WindowDragHandle.KeepOnScreen(view.Rect);
            if (opening)
                view.PlayOpening();
            FocusWindow(view);
            if (id == "tooltip")
                view.transform.SetAsLastSibling();
            return view;
        }

        private bool CanShowInCurrentLife(string id) => game == null || !game.State.AwaitingIncarnation ||
            id is "system" or "settings" or "character" or "inventory" or "skills" or "quests" or "questdetail" or
                "map" or "conversation" or "worldcontext" or "serveradmin" ||
            (id == "era" && game.IsOnline);

        internal void SetIncarnationPresentation()
        {
            foreach (var view in views.Values)
                if (!CanShowInCurrentLife(view.Id))
                    HideWindow(view.Id, animate: false);
            SetDialoguePresentation(HasModal);
        }

        private void FocusWindow(VillageWindowView view)
        {
            if (view == null || view.Id == "tooltip" || !view.IsOpen)
                return;
            windowFocus.Remove(view);
            windowFocus.Add(view);
            if (view.Id != "conversation" || !IsShopOpen)
                view.transform.SetAsLastSibling();
            if ((view.Id == "inventory" || view.Id == "shop") && TradeQuantityOpen)
                views["quantity"].transform.SetAsLastSibling();
            if (dragGhost != null)
                dragGhost.SetAsLastSibling();
        }

        private bool CloseFocusedWindow()
        {
            for (int index = windowFocus.Count - 1; index >= 0; index--)
            {
                var view = windowFocus[index];
                if (view == null || !view.IsOpen)
                {
                    if (view == null || !dialogueWindowStates.ContainsKey(view.Id))
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
            if (id == "conversation" || (id == "shop" && HasModal))
            {
                TryLeaveDialogue();
                return;
            }
            dialogueWindowStates.Remove(id);
            HideWindow(id);
        }

        private void HideWindow(string id, bool animate = true)
        {
            if (!views.TryGetValue(id, out var view) || !view.gameObject.activeSelf || (animate && view.IsClosing))
                return;
            view.Hide(animate);
            windowFocus.Remove(view);
            HideItemTooltip();
            if (id == "quantity")
                SetTradeSlotInput(true);
            if (id == "inventory")
            {
                CancelItemDrag();
                CloseItemContext();
            }
        }

        public void ToggleWindow(string id)
        {
            if (views.TryGetValue(id, out var view) && view.IsOpen)
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
            if (subject == null || !subject.CanInteract || WorldActionsLocked && !(subject is GroundPickup) || IsHoldingItem ||
                !WorkerbornInput.IsInView(game.WorldCamera, screen))
                return;
            HideItemTooltip();
            CloseItemContext();
            var view = OpenWindow("worldcontext") as WorldContextView;
            if (view == null)
                return;
            if (!view.Show(subject, game.WorldCamera.ScreenPointToRay(screen)))
            {
                CloseWorldContext();
                return;
            }
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

        internal void OpenPlot(VillagePlot plot)
        {
            HideItemTooltip();
            (OpenWindow("plot") as PlotWindowView)?.Show(plot);
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
                OpenWindow("inventory", temporaryInDialogue: true)?.Refresh();
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
            if (views.TryGetValue("conversation", out var dialogue) && dialogue is ConversationWindowView conversation)
                conversation.SetTrading();
        }

        internal void ClickShopItem(string id, Vector2 screen)
        {
            if (!IsShopOpen || HasQuantityPrompt)
                return;
            if (IsHoldingItem)
                SellHeldItem(screen);
            else if (WorkerbornInput.Held("Gameplay/Target"))
                game.Perform(() => game.State.TryBuy(id));
            else
                OpenQuantity(id, QuantityAction.Buy, screen: screen);
        }

        private void OpenQuantity(string id, QuantityAction action, int stackId = 0, Vector2? screen = null,
            bool confirmQuickSale = false)
        {
            if (TradeQuantityOpen)
                return;
            HideItemTooltip();
            CloseItemContext();
            bool wasOpen = HasQuantityPrompt;
            if (!(OpenWindow("quantity") is QuantityWindowView prompt))
                return;
            prompt.Open(id, action, stackId, confirmQuickSale);
            if (prompt.IsTrade)
            {
                PlaceTradeQuantity(prompt.Rect, screen ?? WorkerbornInput.Point);
                SetTradeSlotInput(false);
                return;
            }
            if (wasOpen || prompt.HasSavedPosition)
                return;

            const float gap = 10f;
            var canvas = CanvasRect.rect;
            float right = canvas.xMax - gap;
            if (views.TryGetValue("inventory", out var panel) && panel.IsOpen)
                right = WindowDragHandle.BoundsInParent(panel.Rect).xMin - gap;
            var rect = prompt.Rect;
            rect.anchoredPosition = new Vector2(right - canvas.xMax, Mathf.Max(28f, (canvas.height - rect.rect.height) * .5f));
            WindowDragHandle.KeepOnScreen(rect);
        }

        private void PlaceTradeQuantity(RectTransform rect, Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, screen, null, out var point))
                return;

            const float gap = 10f;
            Rect canvas = CanvasRect.rect;
            Rect popup = WindowDragHandle.BoundsInParent(rect);
            float x = point.x + gap;
            float y = point.y - gap - popup.height;
            if (x + popup.width > canvas.xMax)
                x = point.x - gap - popup.width;
            if (y < canvas.yMin)
                y = point.y + gap;
            rect.anchoredPosition += new Vector2(x, y) - popup.min;
            WindowDragHandle.KeepOnScreen(rect);
        }

        private void SetTradeSlotInput(bool enabled)
        {
            if (views.TryGetValue("inventory", out var inventory))
                inventory.SetBodyInputEnabled(enabled);
            if (views.TryGetValue("shop", out var shop))
                shop.SetBodyInputEnabled(enabled);
        }

        private void CloseQuantity()
        {
            CloseWindow("quantity");
        }

        public void SellHeldItem(Vector2 screen)
        {
            if (IsHoldingItem)
                OpenSaleQuantity(draggedEntry, screen);
        }

        private void OpenSaleQuantity(int entryId, Vector2 screen, bool quick = false)
        {
            if (!IsShopOpen || TradeQuantityOpen)
                return;
            var item = game.State.CarriedItem(entryId);
            if (item == null)
                return;
            if (entryId != 0)
                CancelItemDrag();
            if (game.State.MaximumSellQuantity(item.Id) == 0)
            {
                ShowToast("이 물건은 판매할 수 없습니다.");
                return;
            }
            bool warn = quick && game.Settings.FindItem(item.ItemId)?.ConfirmQuickSale == true;
            if (quick && !warn)
                game.Perform(() => game.State.TrySellStack(item.Id, item.Quantity));
            else
                OpenQuantity(item.ItemId, QuantityAction.Sell, item.Id, screen, warn);
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
