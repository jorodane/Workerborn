using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class TargetLinkView : MonoBehaviour
    {
        [SerializeField]
        private Image line;
        [SerializeField, Min(.5f)]
        private float thickness = 1.5f;
        private Material runtimeMaterial;
        private void Awake()
        {
            if (line.material == null)
                return;
            runtimeMaterial = new Material(line.material);
            line.material = runtimeMaterial;
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        public void Show(RectTransform canvas, Vector2 from, Vector2 to, Color tint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, from, null, out var a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, to, null, out var b);
            var rect = line.rectTransform;
            rect.anchoredPosition = a;
            rect.sizeDelta = new Vector2(Vector2.Distance(a, b), thickness);
            rect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            line.color = tint;
            line.raycastTarget = false;
            gameObject.SetActive(true);
        }

        public void SetSilhouetteMask(RenderTexture mask, bool flipY)
        {
            if (line.material == null)
                return;
            line.material.SetTexture("_TargetMask", mask);
            line.material.SetFloat("_MaskFlipY", flipY ? 1f : 0f);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
