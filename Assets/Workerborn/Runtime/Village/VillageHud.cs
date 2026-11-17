using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed partial class VillageHud : MonoBehaviour
    {
        [SerializeField] private Color paper = new Color(0.92f, 0.92f, 0.9f, 0.97f);
        [SerializeField] private Color dark = new Color(0.22f, 0.29f, 0.39f, 0.96f);
        [SerializeField] private Color ink = new Color(0.19f, 0.21f, 0.19f);
        [SerializeField] private Color gold = new Color(0.87f, 0.72f, 0.43f);
        [SerializeField] private Color pale = new Color(0.82f, 0.83f, 0.83f);
        private VillagePrototype game;
        private Font font;
        private Canvas canvas;
        private Texture2D uiTexture;
        private Sprite uiSprite;
        [SerializeField] private Text clockText;
        [SerializeField] private Text energyText;
        [SerializeField] private Text growthText;
        [SerializeField] private Text toastText;
        [SerializeField] private Image energyFill;
        [SerializeField] private Image experienceFill;
        [SerializeField] private RectTransform toast;
        private float toastUntil;
        private RectTransform skillBubble;
        private Text skillTitle;
        private Text skillTime;
        private Image skillFill;
        private RectTransform hoverTip;
        private Text hoverText;
        private readonly Queue<string> log = new Queue<string>();
        private string lastLoggedMessage;
        private readonly Dictionary<string, RectTransform> windows = new Dictionary<string, RectTransform>();
        private readonly Dictionary<VillageInteractable, Text> npcLabels = new Dictionary<VillageInteractable, Text>();
        private readonly List<MapView> maps = new List<MapView>();
        private readonly List<FloatingText> floating = new List<FloatingText>();
        private int seenVersion = -1;
        private int seenInventoryVersion = -1;
        private int seenNotice = -1;
        private bool suppressRightContext;
        [SerializeField] private RectTransform minimapPanel;
        [SerializeField] private RectTransform minimapImage;
        public bool HasModal => dialogueTarget != null;
        public bool IsHoldingItem => draggedEntry != 0;
        public bool InventoryOpen => windows.TryGetValue("inventory", out var panel) && panel.gameObject.activeSelf;

        private sealed class FloatingText { public Text Text; public Vector3 Position; public float Started; }
        private sealed class MapView { public RectTransform Root; public RectTransform Player; public Dictionary<VillageInteractable, RectTransform> Markers; }

        public void Initialize(VillagePrototype prototype, Font interfaceFont)
        {
            game = prototype; font = interfaceFont;
            CreateSprite();
            canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
            {
                var events = new GameObject("Village input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            else
            {
                var input = EventSystem.current.GetComponent<InputSystemUIInputModule>();
                if (input != null && input.actionsAsset == null) input.AssignDefaultActions();
            }
            if (clockText == null) BuildMainHud();
            else if (minimapImage != null) DrawMap(minimapImage, false);
            BuildWorldFeedback();
            game.Worker.Message += ShowToast;
            foreach (var target in game.Interactables)
            {
                var text = Label(target.DisplayName, transform, Vector2.zero, new Vector2(160, 28), 13, Color.white, false);
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = Vector2.zero;
                text.rectTransform.pivot = new Vector2(0.5f, 0f);
                text.alignment = TextAnchor.MiddleCenter;
                var shadow = text.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0f, 0f, 0f, 0.9f); shadow.effectDistance = new Vector2(1, -1);
                npcLabels.Add(target, text);
            }
        }

        private void BuildMainHud()
        {
            var menu = Panel("System bar", transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, paper, true);
            menu.anchorMax = new Vector2(1, 0); menu.offsetMin = Vector2.zero; menu.offsetMax = new Vector2(0, 28);
            string[] titles = { "메뉴", "캐릭터", "스킬", "소지품", "퀘스트", "지도" };
            string[] ids = { "system", "character", "skills", "inventory", "quests", "map" };
            for (int i = 0; i < titles.Length; i++)
            {
                string id = ids[i];
                Button(titles[i], menu, new Vector2(3 + i * 72, -2), new Vector2(68, 24), () => ToggleWindow(id));
            }
            var status = Panel("Character status", transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(360, 52), new Vector2(0, 32), new Color(0.1f, 0.13f, 0.17f, 0.55f));
            growthText = Label("Lv.1", status, new Vector2(8, -3), new Vector2(65, 25), 12, Color.white);
            energyText = Label("100/100", status, new Vector2(80, -3), new Vector2(270, 25), 12, Color.white);
            energyText.alignment = TextAnchor.MiddleRight;
            energyFill = Bar(status, new Vector2(80, -29), new Vector2(270, 6), new Color(0.55f, 0.72f, 0.4f));
            experienceFill = Bar(status, new Vector2(8, -44), new Vector2(342, 3), new Color(0.85f, 0.75f, 0.45f));
            clockText = Label("08:00", transform, new Vector2(-16, -12), new Vector2(180, 30), 16, Color.white);
            clockText.rectTransform.anchorMin = clockText.rectTransform.anchorMax = Vector2.one;
            clockText.rectTransform.pivot = Vector2.one; clockText.alignment = TextAnchor.MiddleRight;
            clockText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -1);
            minimapPanel = Panel("Mini map", transform, Vector2.one, Vector2.one, new Vector2(190, 170), new Vector2(-12, -49), paper, true);
            Label("솔바람 마을", minimapPanel, new Vector2(8, -1), new Vector2(174, 25), 12, ink);
            minimapImage = Panel("Map image", minimapPanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(174, 132), new Vector2(8, -30), Color.white, true);
            DrawMap(minimapImage, false);
            toast = Panel("System message", transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(720, 34), new Vector2(0, 104), Color.clear);
            toastText = Label("", toast, new Vector2(0, 0), new Vector2(720, 34), 14, Color.white);
            toastText.alignment = TextAnchor.MiddleCenter;
            toastText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -1);
        }

        private void BuildWorldFeedback()
        {
            skillBubble = Panel("Skill balloon", transform, Vector2.zero, new Vector2(0.5f, 0), new Vector2(240, 110), Vector2.zero, paper);
            skillTitle = Label("감자 수확", skillBubble, new Vector2(16, -10), new Vector2(208, 28), 19, ink, true);
            skillTime = Label("", skillBubble, new Vector2(16, -45), new Vector2(208, 24), 13, ink);
            skillFill = Bar(skillBubble, new Vector2(16, -81), new Vector2(208, 10), new Color(0.32f, 0.49f, 0.36f));
            var tail = Panel("Balloon tail", skillBubble, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(15, 15), new Vector2(0, -3), paper);
            tail.localRotation = Quaternion.Euler(0, 0, 45);
            skillBubble.gameObject.SetActive(false);
            hoverTip = Panel("Object tooltip", transform, Vector2.zero, new Vector2(0.5f, 0), new Vector2(260, 44), Vector2.zero, dark);
            hoverText = Label("", hoverTip, new Vector2(10, -6), new Vector2(240, 32), 14, paper);
            hoverText.alignment = TextAnchor.MiddleCenter;
            hoverTip.gameObject.SetActive(false);
        }

        public void ShowToast(string message)
        {
            if (toastText == null) return;
            toastText.text = message;
            toastUntil = Time.unscaledTime + 5f;
            string shortMessage = message.Length > 42 ? message.Substring(0, 41) + "…" : message;
            if (lastLoggedMessage != message)
            {
                log.Enqueue(shortMessage);
                while (log.Count > 3) log.Dequeue();
                lastLoggedMessage = message;
            }

        }

        public void ShowFloatingText(Vector3 position, string content, Color color)
        {
            var text = Label(content, transform, Vector2.zero, new Vector2(260, 42), 21, color, true);
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = Vector2.zero;
            text.rectTransform.pivot = new Vector2(0.5f, 0);
            text.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -1);
            floating.Add(new FloatingText { Text = text, Position = position, Started = Time.unscaledTime });
        }

        private void Update()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame)
            {
                if (HasModal) CloseDialogue();
                else foreach (var window in windows.Values) window.gameObject.SetActive(false);
                CancelItemDrag();
            }
            if (keys.iKey.wasPressedThisFrame) ToggleWindow("inventory");
            if (keys.qKey.wasPressedThisFrame) ToggleWindow("quests");
            if (keys.zKey.wasPressedThisFrame || keys.kKey.wasPressedThisFrame) ToggleWindow("skills");
            if (keys.cKey.wasPressedThisFrame) ToggleWindow("character");
            if (keys.mKey.wasPressedThisFrame && minimapPanel != null) minimapPanel.gameObject.SetActive(!minimapPanel.gameObject.activeSelf);
            if (Mouse.current != null)
            {
                if (IsHoldingItem) MoveItemDrag(Mouse.current.position.ReadValue());
                if (Mouse.current.rightButton.wasPressedThisFrame)
                {
                    suppressRightContext = IsHoldingItem;
                    if (IsHoldingItem) CancelItemDrag();
                }
            }
        }

        private void LateUpdate()
        {
            if (game == null || game.Worker == null) return;
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
                if (state.NoticeSerial > 0) ShowToast(state.LastMessage);
                seenNotice = state.NoticeSerial;
            }
            if (state.Version != seenVersion || state.Inventory.Version != seenInventoryVersion)
            {
                seenVersion = state.Version; seenInventoryVersion = state.Inventory.Version;
                RefreshWindows();
            }
            UpdateWorldFeedback();
            UpdateMaps();
        }

        private void UpdateWorldFeedback()
        {
            var worker = game.Worker;
            var active = worker.ActiveCrop;
            skillBubble.gameObject.SetActive(active != null && !HasModal);
            if (active != null && !HasModal)
            {
                skillTitle.text = "감자 수확  ·  " + RankName(game.State.SkillRank);
                float seconds = active.Definition.HarvestSeconds * (1f - active.Progress) / game.State.WorkRate;
                skillTime.text = $"{seconds:0.0}초 남음  ·  남은 수확 {active.Remaining}/{active.Capacity}";
                skillFill.fillAmount = active.Progress;
                PlaceWorld(skillBubble, worker.transform.position + Vector3.up * 2.25f);
            }
            bool show = !HasModal && (worker.HoveredPickup != null || worker.HoveredInteraction != null || worker.HoveredCrop != null && active != worker.HoveredCrop);
            hoverTip.gameObject.SetActive(show);
            if (show)
            {
                if (worker.HoveredPickup != null)
                {
                    hoverText.text = "감자 1개  ·  클릭해서 줍기";
                    PlaceWorld(hoverTip, worker.HoveredPickup.transform.position + Vector3.up * 0.7f);
                }
                else if (worker.HoveredInteraction != null)
                {
                    hoverText.text = worker.HoveredInteraction.DisplayName + "  ·  클릭해서 대화";
                    PlaceWorld(hoverTip, worker.HoveredInteraction.LabelPosition);
                }
                else
                {
                    hoverText.text = $"감자  ·  남은 수확 {worker.HoveredCrop.Remaining}/{worker.HoveredCrop.Capacity}회";
                    PlaceWorld(hoverTip, worker.HoveredCrop.transform.position + Vector3.up * 1.1f);
                }
            }
            foreach (var pair in npcLabels)
            {
                pair.Value.text = pair.Key.DisplayName;
                PlaceWorld(pair.Value.rectTransform, pair.Key.LabelPosition);
            }
            for (int i = floating.Count - 1; i >= 0; i--)
            {
                var entry = floating[i]; float age = Time.unscaledTime - entry.Started;
                if (age >= 1.5f) { Destroy(entry.Text.gameObject); floating.RemoveAt(i); continue; }
                Color color = entry.Text.color; color.a = 1f - age / 1.5f; entry.Text.color = color;
                PlaceWorld(entry.Text.rectTransform, entry.Position + Vector3.up * age * 0.6f);
            }
        }

        private void PlaceWorld(RectTransform rect, Vector3 position)
        {
            Vector3 screen = game.WorldCamera.WorldToScreenPoint(position);
            bool visible = screen.z > 0f && screen.x > -30f && screen.x < Screen.width + 30f && screen.y > 0f && screen.y < Screen.height;
            rect.gameObject.SetActive(visible);
            if (!visible) return;
            var root = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local);
            rect.anchoredPosition = local - root.rect.min;
        }

        public void ToggleWindow(string id)
        {
            if (!windows.TryGetValue(id, out var window))
            {
                switch (id)
                {
                    case "inventory": BuildInventoryWindow(); break;
                    case "quests": BuildQuestWindow(); break;
                    case "skills": BuildSkillsWindow(); break;
                    case "character": BuildCharacterWindow(); break;
                    case "map": BuildMapWindow(); break;
                    case "system": BuildSystemWindow(); break;
                }
                return;
            }
            window.gameObject.SetActive(!window.gameObject.activeSelf);
            if (window.gameObject.activeSelf) { window.SetAsLastSibling(); RefreshWindows(); }
            else if (id == "inventory") CancelItemDrag();
        }

        private RectTransform Window(string id, string title, Vector2 size, Vector2 position)
        {
            var panel = Panel(title, transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, position, paper, true);
            var outline = panel.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(0.19f, 0.24f, 0.23f, 0.9f); outline.effectDistance = new Vector2(2, -2);
            var header = Panel("Window header", panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(size.x, 32), Vector2.zero, dark, true);
            Label(title, header, new Vector2(10, -2), new Vector2(size.x - 50, 28), 14, paper, true);
            header.gameObject.AddComponent<WindowDragHandle>().Window = panel;
            Button("×", header, new Vector2(size.x - 28, -3), new Vector2(24, 24), () =>
            {
                panel.gameObject.SetActive(false);
                if (id == "inventory") CancelItemDrag();
            });
            windows.Add(id, panel);
            return panel;
        }

        private RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position, Color color, bool block = false)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)root.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.sizeDelta = size; rect.anchoredPosition = position;
            var image = root.GetComponent<Image>(); image.sprite = uiSprite; image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = block;
            return rect;
        }

        private Text Label(string content, Transform parent, Vector2 position, Vector2 size, int fontSize, Color color, bool bold = false)
        {
            var root = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)root.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = size;
            var text = root.GetComponent<Text>(); text.font = font; text.fontSize = fontSize; text.text = content; text.color = color;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; text.raycastTarget = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Button Button(string content, Transform parent, Vector2 position, Vector2 size, Action action, bool highlighted = false)
        {
            var rect = Panel(content, parent, new Vector2(0, 1), new Vector2(0, 1), size, position, highlighted ? gold : pale, true);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors; colors.highlightedColor = new Color(1f, 0.97f, 0.86f); colors.pressedColor = new Color(0.73f, 0.76f, 0.66f); button.colors = colors;
            button.onClick.AddListener(() => action());
            var text = Label(content, rect, Vector2.zero, size, 12, ink, highlighted);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private Image Bar(Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var track = Panel("Progress track", parent, new Vector2(0, 1), new Vector2(0, 1), size, position, new Color(0.38f, 0.42f, 0.37f));
            var fill = Panel("Progress fill", track, Vector2.zero, Vector2.zero, size, Vector2.zero, color);
            fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
            var image = fill.GetComponent<Image>(); image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal; image.fillOrigin = 0;
            return image;
        }

        private void BuildSystemWindow()
        {
            var root = Window("system", "메뉴", new Vector2(240, 170), Vector2.zero);
            Button("저장", root, new Vector2(20, -46), new Vector2(200, 30), () => game.SaveGame(true));
            Button("캐릭터", root, new Vector2(20, -84), new Vector2(200, 30), () => ToggleWindow("character"));
            Button("닫기", root, new Vector2(20, -122), new Vector2(200, 30), () => ToggleWindow("system"));
        }

        private static string RankName(int rank) => new[] { "F", "E", "D", "C", "B", "A", "9", "8", "7" }[Mathf.Clamp(rank, 0, 8)];

        private void CreateSprite()
        {
            const int size = 24;
            uiTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Village UI corners" };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0, Mathf.Max(5f - x, x - 18f)); float dy = Mathf.Max(0, Mathf.Max(5f - y, y - 18f));
                pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(5.5f - new Vector2(dx, dy).magnitude));
            }
            uiTexture.SetPixels(pixels); uiTexture.Apply();
            uiSprite = Sprite.Create(uiTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(6, 6, 6, 6));
        }

        private void OnDestroy()
        {
            if (game != null && game.Worker != null) game.Worker.Message -= ShowToast;
            if (uiSprite != null) Destroy(uiSprite);
            if (uiTexture != null) Destroy(uiTexture);
        }
    }
}
