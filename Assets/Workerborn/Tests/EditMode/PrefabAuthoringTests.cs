using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
    }
}
