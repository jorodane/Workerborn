using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

namespace Workerborn.Tests
{
    public sealed class PrefabAuthoringTests
    {
        [Test]
        public void EveryWindowHasAnEditableLayoutBeforePlay()
        {
            var library = AssetDatabase.LoadAssetAtPath<VillageUiLibrary>("Assets/Workerborn/Data/VillageUI.asset");
            foreach (string id in new[]
            {
                "inventory",
                "skills",
                "quests",
                "questdetail",
                "character",
                "map",
                "shop",
                "quantity",
                "conversation",
                "system",
                "tooltip",
                "itemcontext"
            }

            )
            {
                var window = library.Find(id);
                Assert.That(window, Is.Not.Null, id);
                Assert.That(window.GetComponentsInChildren<RectTransform>(true).Length, Is.GreaterThan(5), id);
                Assert.That(window.GetComponentsInChildren<Component>(true).All(c => c != null), Is.True, "Missing script in " + id);
            }

            var bag = library.Find("inventory");
            Assert.That(bag.GetComponentsInChildren<EquipmentSlotPointer>(true).Length, Is.EqualTo(7));
        }

        [Test]
        public void InteractivePrefabsContainTheirVisualsCollidersAndSharedInteractionContract()
        {
            foreach (string file in new[]
            {
                "Characters/Mira",
                "Characters/Noah",
                "Characters/Lena",
                "Interaction/Board",
                "Interaction/Shrine",
                "Interaction/Monster",
                "Crops/Potato",
                "Items/potato",
                "Items/bread",
                "Items/hoe",
                "Items/bag",
                "Items/gold"
            }

            )
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Workerborn/Prefabs/" + file + ".prefab");
                Assert.That(prefab, Is.Not.Null, file);
                Assert.That(prefab.GetComponent<WorldInteractable>(), Is.Not.Null, file);
                Assert.That(prefab.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0), file);
                Assert.That(prefab.GetComponent<Collider>(), Is.Not.Null, file);
            }
        }

        [Test]
        public void AuthoredUiUsesTmpWithTheKoreanFontAndOutlinedNameMaterial()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Workerborn/Fonts/WorkerbornKorean-SDF.asset");
            Assert.That(font, Is.Not.Null);
            Assert.That(font.sourceFontFile, Is.Not.Null);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Workerborn/Prefabs/UI" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                    Assert.That(component == null || component.GetType().FullName != "UnityEngine.UI.Text", Is.True, prefab.name);
                foreach (var text in prefab.GetComponentsInChildren<TMP_Text>(true))
                {
                    Assert.That(text.font, Is.SameAs(font), prefab.name);
                    Assert.That(text.fontSharedMaterial, Is.Not.Null, prefab.name);
                }
            }

            var library = AssetDatabase.LoadAssetAtPath<VillageUiLibrary>("Assets/Workerborn/Data/VillageUI.asset");
            foreach (var name in new WorldNameplateView[]
            {
                library.PickupName,
                library.NpcName
            }

            )
                Assert.That(name.GetComponentInChildren<TMP_Text>().fontSharedMaterial.GetFloat("_OutlineWidth"), Is.GreaterThan(0));
        }

        [Test]
        public void InputAssetContainsCameraGameplayAndUiBindings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Workerborn/Input/Workerborn.inputactions");
            foreach (string name in new[]
            {
                "Gameplay/Interact",
                "Gameplay/Target",
                "Gameplay/Names",
                "Camera/OrbitMiddle",
                "Camera/OrbitRight",
                "Camera/Zoom",
                "UI/Click",
                "UI/Point"
            }

            )
                Assert.That(asset.FindAction(name, true).bindings.Count, Is.GreaterThan(0), name);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void CameraContainmentUsesTheCapsuleAxisRotationAndScale(int direction)
        {
            var root = new GameObject("Capsule containment test");
            try
            {
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.direction = direction;
                capsule.radius = .5f;
                capsule.height = 4;
                capsule.center = new Vector3(1, 2, 3);
                root.transform.SetPositionAndRotation(new Vector3(4, 2, 1), Quaternion.Euler(20, 35, 10));
                root.transform.localScale = new Vector3(2, 3, 4);
                Vector3 center = root.transform.TransformPoint(capsule.center);
                Assert.That(CameraVisibility.Contains(capsule, center), Is.True);
                Vector3 axis = root.transform.rotation * (direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward);
                Vector3 scale = root.transform.lossyScale;
                float radius = capsule.radius * Mathf.Max(scale[(direction + 1) % 3], scale[(direction + 2) % 3]);
                float end = Mathf.Max(capsule.height * scale[direction] * .5f, radius);
                Assert.That(CameraVisibility.Contains(capsule, center + axis * (end - .01f)), Is.True);
                Assert.That(CameraVisibility.Contains(capsule, center + axis * (end + .01f)), Is.False);
                Assert.That(CameraVisibility.Contains(capsule, center + Vector3.one * 100), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
