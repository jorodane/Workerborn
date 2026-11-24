using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class HealthBarView : MonoBehaviour
    {
        [SerializeField] private Image fill;
        [SerializeField] private Image changeFill;
        [SerializeField] private Color damageColor = new Color(1f, .72f, .32f);
        [SerializeField] private Color healingColor = new Color(.56f, .95f, .67f);
        [SerializeField, Min(0f)] private float holdSeconds = .09f;
        [SerializeField, Min(.01f)] private float changeSeconds = .22f;

        private int capacity;
        private float from, target, changedAt;
        private bool animating, takingDamage;

        internal void SetValue(int health, int maximum)
        {
            if (maximum <= 0)
            {
                ClearValue();
                return;
            }

            float next = Mathf.Clamp01((float)health / maximum);
            if (capacity != maximum)
            {
                capacity = maximum;
                target = next;
                animating = false;
                Render(next);
                return;
            }
            if (target == next)
                return;

            float now = Time.unscaledTime;
            float displayed = Sample(now);
            takingDamage = next < target;
            from = takingDamage ? Mathf.Max(displayed, target) : Mathf.Min(displayed, target);
            target = next;
            changedAt = now;
            animating = true;
            changeFill.color = takingDamage ? damageColor : healingColor;
            Render(from);
        }

        internal void ClearValue()
        {
            capacity = 0;
            target = 0f;
            animating = false;
            Render(0f);
        }

        private float Sample(float now)
        {
            if (!animating)
                return target;

            float progress = Mathf.Clamp01((now - changedAt - holdSeconds) / Mathf.Max(.01f, changeSeconds));
            animating = progress < 1f;
            return Mathf.Lerp(from, target, progress * (2f - progress));
        }

        private void Render(float displayed)
        {
            // The solid fill covers the common part, leaving only the amount gained or lost in color.
            fill.fillAmount = animating && !takingDamage ? displayed : target;
            changeFill.fillAmount = animating ? (takingDamage ? displayed : target) : 0f;
        }

        private void OnEnable() => Render(Sample(Time.unscaledTime));

        private void Update()
        {
            if (animating)
                Render(Sample(Time.unscaledTime));
        }
    }
}
