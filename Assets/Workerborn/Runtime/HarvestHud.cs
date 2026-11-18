using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class HarvestHud : MonoBehaviour
    {
        private readonly Color ink = new Color(0.12f, 0.22f, 0.19f);
        private readonly Color paper = new Color(0.97f, 0.95f, 0.87f);
        private readonly Color soft = new Color(0.70f, 0.79f, 0.70f);
        private readonly Color accent = new Color(0.91f, 0.77f, 0.48f);
        private HarvestPrototype prototype;
        private Camera worldCamera;
        private Canvas canvas;
        private TMP_FontAsset font;
        private Sprite panelSprite;
        private Texture2D panelTexture;
        private TMP_Text bagCount;
        private TMP_Text fieldCount;
        private TMP_Text groundCount;
        private TMP_Text message;
        private RectTransform skillBubble;
        private SkillBalloonView skillView;
        private SkillBalloonView skillPrefab;
        private RectTransform tooltip;
        private TMP_Text tooltipTitle;
        private string latestMessage;
        private float messageUntil;
        public void Initialize(HarvestPrototype farm, Camera camera, TMP_FontAsset interfaceFont, SkillBalloonView balloonPrefab)
        {
            prototype = farm;
            worldCamera = camera;
            font = interfaceFont;
            skillPrefab = balloonPrefab;
            CreatePanelSprite();
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
            {
                var events = new GameObject("Input events", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                WorkerbornInput.Current?.BindUi(events.GetComponent<InputSystemUIInputModule>());
            }

            BuildHeader();
            BuildFooter();
            BuildSkillBubble();
            BuildTooltip();
            prototype.Worker.Message += ShowMessage;
            ShowMessage("감자밭에 오신 것을 환영해요. 작물을 클릭해 첫 수확을 시작해 보세요.");
        }

        private void BuildHeader()
        {
            var title = Panel("Farm title", transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(318f, 100f), new Vector2(28f, -26f), ink);
            Label("WORKERBORN", title, new Vector2(20f, -14f), new Vector2(280f, 38f), 27, paper, true);
            Label("작은 감자밭  /  첫 번째 일거리", title, new Vector2(22f, -62f), new Vector2(280f, 24f), 15, soft);
            var bag = Panel("Bag", transform, Vector2.one, Vector2.one, new Vector2(226f, 100f), new Vector2(-28f, -26f), paper);
            Label("가방  /  주운 감자", bag, new Vector2(19f, -13f), new Vector2(190f, 25f), 15, ink);
            bagCount = Label("0 개", bag, new Vector2(19f, -42f), new Vector2(190f, 48f), 34, ink, true);
            var field = Panel("Field status", transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(240f, 89f), new Vector2(28f, -138f), new Color(0.12f, 0.22f, 0.19f, 0.9f));
            fieldCount = Label("", field, new Vector2(17f, -12f), new Vector2(210f, 28f), 17, paper, true);
            groundCount = Label("", field, new Vector2(17f, -47f), new Vector2(210f, 24f), 14, soft);
        }

        private void BuildFooter()
        {
            var footer = Panel("Instructions", transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, ink);
            footer.anchorMin = Vector2.zero;
            footer.anchorMax = new Vector2(1f, 0f);
            footer.offsetMin = new Vector2(28f, 26f);
            footer.offsetMax = new Vector2(-244f, 148f);
            message = Label("", footer, new Vector2(20f, -15f), new Vector2(0f, 44f), 18, paper, true);
            var textRect = message.rectTransform;
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20f, -59f);
            textRect.offsetMax = new Vector2(-20f, -15f);
            var instructions = Label("작물 클릭: 이동·수확    감자 클릭: 줍기    땅 클릭: 이동\n우클릭·Esc: 작업 취소    R: 밭 다시 준비", footer, new Vector2(20f, -68f), new Vector2(0f, 47f), 14, soft);
            var instructionsRect = instructions.rectTransform;
            instructionsRect.anchorMax = Vector2.one;
            instructionsRect.offsetMin = new Vector2(20f, -115f);
            instructionsRect.offsetMax = new Vector2(-20f, -68f);
            var reset = Panel("Reset farm", transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(200f, 122f), new Vector2(-28f, 26f), accent);
            reset.GetComponent<Image>().raycastTarget = true;
            var button = reset.gameObject.AddComponent<Button>();
            button.targetGraphic = reset.GetComponent<Image>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.95f, 0.8f);
            colors.pressedColor = new Color(0.82f, 0.72f, 0.5f);
            button.colors = colors;
            button.onClick.AddListener(prototype.ResetFarm);
            var resetText = Label("밭 다시 준비\n[R]", reset, Vector2.zero, Vector2.zero, 19, ink, true);
            resetText.alignment = TextAlignmentOptions.Center;
            Stretch(resetText.rectTransform);
        }

        private void BuildSkillBubble()
        {
            skillView = Instantiate(skillPrefab, transform);
            skillBubble = skillView.Rect;
            skillBubble.gameObject.SetActive(false);
        }

        private void BuildTooltip()
        {
            tooltipTitle = Label("", transform, Vector2.zero, new Vector2(140f, 28f), 14, Color.white);
            tooltip = tooltipTitle.rectTransform;
            tooltip.pivot = new Vector2(0, 1);
            tooltipTitle.outlineColor = Color.black;
            tooltipTitle.outlineWidth = .22f;
            tooltip.gameObject.SetActive(false);
        }

        public void ShowMessage(string value)
        {
            latestMessage = value;
            messageUntil = Time.unscaledTime + 5f;
        }

        private void LateUpdate()
        {
            if (prototype == null || prototype.Worker == null)
                return;
            var worker = prototype.Worker;
            bagCount.text = $"{worker.TotalCollected} 개";
            fieldCount.text = $"남은 수확  {prototype.RemainingHarvests} 회";
            groundCount.text = $"바닥에 떨어진 감자  {prototype.GroundItemCount} 개";
            if (prototype.RemainingHarvests == 0 && prototype.GroundItemCount == 0)
                message.text = "오늘의 수확을 모두 마쳤어요! 밭을 다시 준비해서 한 번 더 해보세요.";
            else if (Time.unscaledTime < messageUntil)
                message.text = latestMessage;
            else if (worker.Action == WorkerAction.Harvesting)
                message.text = "수확 중이에요. 다른 곳을 클릭하면 작업을 중단하고 이동해요.";
            else if (prototype.GroundItemCount > 0)
                message.text = "바닥에 감자가 있어요. 클릭하면 즉시 가방에 담아요.";
            else
                message.text = "수확할 작물을 선택해 주세요. 작은 금색 점은 남은 수확 횟수예요.";
            var active = worker.ActiveCrop;
            skillBubble.gameObject.SetActive(active != null);
            if (active != null)
            {
                skillView.SetProgress(active.Progress);
                PlaceAtWorld(skillBubble, worker.transform.position + Vector3.up * 2.25f);
            }

            var hovered = worker.HoveredCrop;
            var item = worker.HoveredPickup;
            bool show = item != null || (hovered != null && hovered != active);
            tooltip.gameObject.SetActive(show);
            if (show)
            {
                tooltipTitle.text = item != null ? "줍기" : "수확하기";
                var canvasRect = (RectTransform)canvas.transform;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, WorkerbornInput.Point, null, out var cursor);
                tooltip.anchoredPosition = cursor + new Vector2(18, -22) - canvasRect.rect.min;
            }
        }

        private void PlaceAtWorld(RectTransform rect, Vector3 position)
        {
            Vector3 screen = worldCamera.WorldToScreenPoint(position);
            rect.gameObject.SetActive(screen.z > 0f);
            var canvasRect = (RectTransform)canvas.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            float halfWidth = rect.rect.width * 0.5f + 10f;
            local.x = Mathf.Clamp(local.x, canvasRect.rect.xMin + halfWidth, canvasRect.rect.xMax - halfWidth);
            local.y = Mathf.Clamp(local.y, canvasRect.rect.yMin + 155f, canvasRect.rect.yMax - rect.rect.height - 8f);
            // WorldToScreen produces coordinates relative to the canvas centre; these widgets anchor at bottom left.
            rect.anchoredPosition = local - canvasRect.rect.min;
        }

        private RectTransform Panel(string label, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position, Color color)
        {
            var root = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = root.GetComponent<Image>();
            image.sprite = panelSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private TMP_Text Label(string content, Transform parent, Vector2 position, Vector2 size, int fontSize, Color color, bool bold = false)
        {
            var root = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = root.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.color = color;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.richText = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void CreatePanelSprite()
        {
            const int size = 32;
            const float radius = 8f;
            panelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Farm UI corners"
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - (x + 0.5f), x + 0.5f - (size - radius));
                    float dy = Mathf.Max(radius - (y + 0.5f), y + 0.5f - (size - radius));
                    float distance = new Vector2(Mathf.Max(0f, dx), Mathf.Max(0f, dy)).magnitude;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius + 0.5f - distance));
                }

            panelTexture.SetPixels(pixels);
            panelTexture.Apply();
            panelSprite = Sprite.Create(panelTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(9f, 9f, 9f, 9f));
        }

        private void OnDestroy()
        {
            if (prototype != null && prototype.Worker != null)
                prototype.Worker.Message -= ShowMessage;
            if (panelSprite != null)
                Destroy(panelSprite);
            if (panelTexture != null)
                Destroy(panelTexture);
        }
    }
}
