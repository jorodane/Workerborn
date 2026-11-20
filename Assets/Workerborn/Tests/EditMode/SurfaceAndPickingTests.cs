using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Workerborn.Tests
{
    public sealed class SurfaceAndPickingTests
    {
        [Test]
        public void CameraFadePreservesAssignedMaterialsAndRestoresExistingPropertyBlocks()
        {
            var cameraRoot = new GameObject("Fade camera test");
            var player = new GameObject("Fade player test");
            var prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var camera = cameraRoot.AddComponent<Camera>();
                var visibility = cameraRoot.AddComponent<CameraVisibility>();
                Invoke(visibility, "Awake");
                player.transform.position = Vector3.forward * 10;
                prop.transform.position = Vector3.forward * 5;
                prop.transform.localScale = Vector3.one * 2;
                visibility.Initialize(player.transform);
                var renderer = prop.GetComponent<Renderer>();
                var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Workerborn/Materials/Toon/M_Poato_01_21a5b0b4.mat");
                renderer.sharedMaterial = original;
                var before = new MaterialPropertyBlock();
                before.SetColor("_BaseColor", Color.red);
                before.SetFloat("_CameraOpacity", .73f);
                renderer.SetPropertyBlock(before, 0);
                visibility.RefreshVisibility();
                visibility.AdvanceFade(.04f);
                Assert.That(visibility.Opacity(renderer), Is.InRange(.001f, .999f));
                // Exercise the SRP callbacks without depending on GPU readback or Game view focus.
                Invoke(visibility, "BeginCamera", default(ScriptableRenderContext), camera);
                Assert.That(renderer.sharedMaterial, Is.SameAs(original));
                var during = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(during, 0);
                Assert.That(during.GetFloat("_CameraOpacity"), Is.EqualTo(visibility.Opacity(renderer)).Within(.0001f));
                Assert.That(during.GetColor("_BaseColor"), Is.EqualTo(Color.red));
                Invoke(visibility, "EndCamera", default(ScriptableRenderContext), camera);
                renderer.GetPropertyBlock(during, 0);
                Assert.That(renderer.sharedMaterial, Is.SameAs(original));
                Assert.That(during.GetFloat("_CameraOpacity"), Is.EqualTo(.73f).Within(.0001f));
                Assert.That(during.GetColor("_BaseColor"), Is.EqualTo(Color.red));
            }
            finally
            {
                Object.DestroyImmediate(cameraRoot);
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(prop);
            }
        }

        [Test]
        public void FlatKitSelectionChangesOnlyTheMainCameraOutlineProperties()
        {
            var cameraRoot = new GameObject("Outline camera test");
            var prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var camera = cameraRoot.AddComponent<Camera>();
                var visibility = cameraRoot.AddComponent<CameraVisibility>();
                Invoke(visibility, "Awake");
                var target = prop.AddComponent<GroundPickup>();
                var renderer = prop.GetComponent<Renderer>();
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Workerborn/Materials/Toon/ForestRabbit.mat");
                renderer.sharedMaterial = material;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", Color.red);
                renderer.SetPropertyBlock(block);
                Invoke(visibility, "Select", target, Color.yellow);
                Invoke(visibility, "BeginCamera", default(ScriptableRenderContext), camera);
                renderer.GetPropertyBlock(block, 0);
                Assert.That(renderer.sharedMaterial, Is.SameAs(material));
                Assert.That(block.GetColor("_OutlineColor"), Is.EqualTo(Color.yellow));
                Assert.That(block.GetFloat("_OutlineWidth"), Is.GreaterThan(material.GetFloat("_OutlineWidth")));
                Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(Color.red));
                Invoke(visibility, "EndCamera", default(ScriptableRenderContext), camera);
                renderer.GetPropertyBlock(block, 0);
                Assert.That(block.isEmpty, Is.True);
                renderer.GetPropertyBlock(block);
                Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(Color.red));
            }
            finally
            {
                Object.DestroyImmediate(cameraRoot);
                Object.DestroyImmediate(prop);
            }
        }

        private static void Invoke(CameraVisibility target, string method, params object[] args) => typeof(CameraVisibility).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        [TestCase("CameraFadeSurface.shader")]
        [TestCase("CameraFadeTrees.shadergraph")]
        [TestCase("CameraFadeMoss.shadergraph")]
        [TestCase("CameraFadeLOD.shadergraph")]
        public void AssignedFadeShadersImportWithoutCompilerErrors(string filename)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Workerborn/Shaders/" + filename);
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, filename);
        }

        [TestCase("Cottage")]
        [TestCase("Generalstore")]
        [TestCase("ForestPine")]
        [TestCase("ForestBirch")]
        [TestCase("ForestBoulder")]
        public void AuthoredPickingMeshesMatchTheVisibleMeshAndTransform(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Workerborn/Prefabs/World/" + name + ".prefab");
            var geometry = prefab.transform.Find("Pointer geometry");
            var visual = prefab.transform.Find("Visual");
            var filters = visual.GetComponentsInChildren<MeshFilter>(true);
            var colliders = geometry.GetComponentsInChildren<MeshCollider>(true);
            Assert.That(colliders.Length, Is.GreaterThan(0), name);
            foreach (var collider in colliders)
            {
                Assert.That(collider.convex, Is.False);
                Assert.That(collider.gameObject.layer, Is.EqualTo(VillageMapProp.PointerGeometryLayer));
                Assert.That(filters.Any(f => f.sharedMesh == collider.sharedMesh && SameMatrix(f.transform.localToWorldMatrix, collider.transform.localToWorldMatrix)), Is.True, "Picking geometry drifted away from the rendered mesh: " + name + "/" + collider.name);
            }
        }

        private static bool SameMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(a[i] - b[i]) > .0001f)
                    return false;
            return true;
        }

        [Test]
        public void FoliageSelectionDoesNotReadANonexistentCutoffProperty()
        {
            var path = "Assets/Synty/PolygonNature/Materials/";
            var guids = AssetDatabase.FindAssets("t:Material", new[] { path.TrimEnd('/') });
            int checkedCount = 0;
            foreach (var guid in guids)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material.shader.name != "Synty/SyntyStudios_Trees")
                    continue;
                Assert.That(PointerTargeting.SelectionCutoff(material), Is.InRange(0f, 1f));
                checkedCount++;
            }

            Assert.That(checkedCount, Is.GreaterThan(0));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase("M_PotatoLeaf_01_98386f85")]
        [TestCase("M_Poato_01_21a5b0b4")]
        public void PotatoMaterialsKeepTheirSuppliedAlbedoAndNormalTextures(string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Workerborn/Materials/Toon/" + name + ".mat");
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), Does.EndWith("T_Potato_01_AlbedoTransparency.png"));
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BumpMap")), Does.EndWith("T_Potato_01_Normal.png"));
            Assert.That(material.HasProperty("_CameraOpacity"), Is.True);
        }
    }
}
