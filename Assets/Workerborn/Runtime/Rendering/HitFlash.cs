using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    internal sealed class HitFlash : MonoBehaviour
    {
        private const float Duration = .65f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int TintColor = Shader.PropertyToID("_Color");

        private readonly List<(Renderer Renderer, int Index, int Property, Color OriginalColor, MaterialPropertyBlock Original,
            MaterialPropertyBlock Flash)> slots = new();
        private float startedAt;

        internal static void PlayOn(Transform visual)
        {
            if (visual == null)
                return;
            if (!visual.TryGetComponent<HitFlash>(out var flash))
                flash = visual.gameObject.AddComponent<HitFlash>();
            if (flash.slots.Count == 0)
                flash.CaptureColors();
            if (flash.slots.Count == 0)
                return;
            flash.startedAt = Time.unscaledTime;
            flash.enabled = true;
            flash.Apply(1f);
        }

        private void Awake() => enabled = false;

        private void CaptureColors()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer is not (MeshRenderer or SkinnedMeshRenderer) || !renderer.enabled)
                    continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null)
                        continue;
                    int property = material.HasProperty(BaseColor) ? BaseColor : TintColor;
                    if (!material.HasProperty(property))
                        continue;
                    var original = new MaterialPropertyBlock();
                    var working = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(original, i);
                    if (original.isEmpty)
                        renderer.GetPropertyBlock(working);
                    else
                        renderer.GetPropertyBlock(working, i);
                    Color rest = working.HasColor(property) ? working.GetColor(property) : material.GetColor(property);
                    slots.Add((renderer, i, property, rest, original, working));
                }
            }
        }

        private void LateUpdate()
        {
            float elapsed = (Time.unscaledTime - startedAt) / Duration;
            if (elapsed >= 1f)
                enabled = false;
            else
                Apply(1f - Mathf.SmoothStep(0f, 1f, elapsed));
        }

        private void Apply(float strength)
        {
            foreach (var slot in slots)
            {
                if (slot.Renderer == null)
                    continue;
                var red = new Color(1f, .13f, .12f, slot.OriginalColor.a);
                slot.Flash.SetColor(slot.Property, Color.Lerp(slot.OriginalColor, red, strength * .86f));
                slot.Renderer.SetPropertyBlock(slot.Flash, slot.Index);
            }
        }

        private void OnDisable()
        {
            foreach (var slot in slots)
                if (slot.Renderer != null)
                    slot.Renderer.SetPropertyBlock(slot.Original.isEmpty ? null : slot.Original, slot.Index);
            slots.Clear();
        }
    }
}
