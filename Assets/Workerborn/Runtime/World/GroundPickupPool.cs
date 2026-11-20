using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Workerborn
{
    internal sealed class GroundPickupPool
    {
        private readonly PrefabPool<GroundPickup> instances;
        private readonly CameraVisibility visibility;
        private readonly CapsuleCollider defaultCapsule;
        private readonly Action<GroundPickup> onReturned;
        private readonly Action<GroundPickup> decorate;
        private readonly Dictionary<GroundPickup, MeshPart[]> models = new Dictionary<GroundPickup, MeshPart[]>();
        private int largestModel;

        public GroundPickupPool(GroundPickup template, Transform parent, CameraVisibility visibility, Action<GroundPickup> onReturned,
            Action<GroundPickup> decorate = null)
        {
            this.visibility = visibility;
            this.onReturned = onReturned;
            this.decorate = decorate;
            defaultCapsule = template.GetComponent<CapsuleCollider>();
            instances = new PrefabPool<GroundPickup>(template, parent, PrepareInstance);
        }

        public bool CanSpawn(GroundPickup source)
        {
            if (source == null)
                return false;
            if (!models.TryGetValue(source, out var parts))
            {
                var meshes = new List<MeshPart>();
                var palette = source.GetComponent<ToonSurface>()?.Palette;
                foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && filter.TryGetComponent<MeshRenderer>(out var renderer) &&
                        renderer.enabled && VisibleInPrefab(filter.transform, source.transform))
                        meshes.Add(new MeshPart(source.transform, filter, renderer, palette));
                parts = meshes.ToArray();
                models.Add(source, parts);
                largestModel = Mathf.Max(largestModel, parts.Length);
            }
            return parts.Length > 0;
        }

        private static bool VisibleInPrefab(Transform mesh, Transform root)
        {
            for (var node = mesh; node != root; node = node.parent)
                if (!node.gameObject.activeSelf)
                    return false;
            return true;
        }

        public void Prewarm(int count) => instances.Prewarm(count);

        public GroundPickup Spawn(GroundPickup source, CropDefinition crop, int quantity, Vector3 start, Vector3 end, bool settled)
        {
            var pickup = instances.Rent();
            var pivot = pickup.Visual;
            if (pickup.AppearanceSource != source)
            {
                ApplyAppearance(pickup, source);
                pickup.AppearanceSource = source;
            }

            pickup.ApplyDefinition(source);
            pickup.SetQuantity(quantity);
            pickup.name = pickup.DisplayName;
            pivot.localRotation = Quaternion.identity;
            pivot.localPosition = Vector3.zero;
            pickup.Initialize(crop, start, end, settled);
            pickup.gameObject.SetActive(true);
            return pickup;
        }

        private void ApplyAppearance(GroundPickup pickup, GroundPickup source)
        {
            var pivot = pickup.Visual;
            var model = models[source];
            EnsureMeshSlots(pivot, model.Length);
            for (int i = 0; i < pivot.childCount; i++)
            {
                var slot = pivot.GetChild(i);
                if (i < model.Length)
                    model[i].Apply(slot);
                slot.gameObject.SetActive(i < model.Length);
            }

            var capsule = pickup.GetComponent<CapsuleCollider>();
            var sourceCapsule = source.GetComponent<CapsuleCollider>();
            if (sourceCapsule == null)
                sourceCapsule = defaultCapsule;
            capsule.center = sourceCapsule.center;
            capsule.direction = sourceCapsule.direction;
            capsule.radius = sourceCapsule.radius;
            capsule.height = sourceCapsule.height;
            capsule.enabled = sourceCapsule.enabled;
        }

        private void PrepareInstance(GroundPickup pickup)
        {
            pickup.BindPool(Return);
            EnsureMeshSlots(pickup.Visual, largestModel);
            decorate?.Invoke(pickup);
        }

        private static void EnsureMeshSlots(Transform pivot, int count)
        {
            while (pivot.childCount < count)
            {
                var slot = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
                slot.transform.SetParent(pivot, false);
            }
        }

        private void Return(GroundPickup pickup)
        {
            if (visibility != null)
            {
                var pivot = pickup.Visual;
                for (int i = 0; i < pivot.childCount; i++)
                    visibility.ForgetFade(pivot.GetChild(i).GetComponent<MeshRenderer>());
            }
            instances.Return(pickup);
            onReturned(pickup);
        }

        private readonly struct MeshPart
        {
            private readonly Mesh mesh;
            private readonly Material[] materials;
            private readonly Vector3 position, scale;
            private readonly Quaternion rotation;
            private readonly int layer;
            private readonly uint renderingLayerMask;
            private readonly ShadowCastingMode shadows;
            private readonly bool receiveShadows;

            public MeshPart(Transform source, MeshFilter filter, MeshRenderer renderer, ToonMaterialPalette palette)
            {
                var local = source.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                mesh = filter.sharedMesh;
                materials = renderer.sharedMaterials;
                palette?.RemapMaterials(materials);
                position = local.GetColumn(3);
                rotation = Quaternion.Inverse(source.rotation) * filter.transform.rotation;
                scale = local.lossyScale;
                layer = renderer.gameObject.layer;
                renderingLayerMask = renderer.renderingLayerMask;
                shadows = renderer.shadowCastingMode;
                receiveShadows = renderer.receiveShadows;
            }

            public void Apply(Transform slot)
            {
                slot.SetLocalPositionAndRotation(position, rotation);
                slot.localScale = scale;
                slot.gameObject.layer = layer;
                slot.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = slot.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.renderingLayerMask = renderingLayerMask;
                renderer.shadowCastingMode = shadows;
                renderer.receiveShadows = receiveShadows;
            }
        }
    }
}
