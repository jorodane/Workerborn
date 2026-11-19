using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Workerborn.Tests
{
    public sealed class CharacterAuthoringTests
    {
        [Test]
        public void CombinedPartsUseTheBaseRigAndKeepTheSourceAssetsUntouched()
        {
            var original = AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/Workerborn/Data/Characters/Player_Default.asset");
            Assert.That(original, Is.Not.Null);
            var definition = Object.Instantiate(original);
            GameObject model = null;
            try
            {
                var serialized = new SerializedObject(definition);
                foreach (var pair in new[] { ("hair", "DarkCrop"), ("outfit", "VillageTravelwearM"), ("eyes", "SageEyes") })
                    serialized.FindProperty(pair.Item1).objectReferenceValue = AssetDatabase.LoadAssetAtPath<CharacterPartDefinition>("Assets/Workerborn/Data/Characters/Parts/ModularAnime2/" + pair.Item2 + ".asset");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                model = CharacterAssembler.Build(definition, null);
                var rig = model.transform.GetChild(0);
                var parts = model.GetComponentsInChildren<CharacterPartInstance>(true);
                Assert.That(parts.Length, Is.EqualTo(2));
                var head = rig.GetComponentsInChildren<Transform>(true).First(t => t.name == "CC_Base_Head");
                Assert.That(parts.Single(p => p.Definition.Slot == CharacterPartSlot.Hair).transform.parent, Is.EqualTo(head));
                var face = rig.GetComponentsInChildren<SkinnedMeshRenderer>().Single(renderer => renderer.name == "Head");
                Assert.That(face.sharedMaterials[0].name, Is.EqualTo("SageEyes"));
                var outfit = parts.First(p => p.Definition.Slot == CharacterPartSlot.Outfit);
                var skins = outfit.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.That(skins.Length, Is.GreaterThan(0));
                foreach (var skin in skins)
                {
                    Assert.That(skin.enabled, Is.True);
                    Assert.That(skin.bones.Length, Is.GreaterThan(0));
                    Assert.That(skin.bones.All(b => b != null && b.IsChildOf(rig) && !b.IsChildOf(outfit.transform)), Is.True);
                }
                Assert.That(original.Parts.Count(), Is.Zero, "Preview assembly must not modify the saved source definition.");
            }
            finally
            {
                if (model != null) Object.DestroyImmediate(model);
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void VillageContainsEditableObjectsBeforePlay()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Workerborn/Scenes/VillagePrototype.unity");
            try
            {
                var roots = scene.GetRootGameObjects();
                var actors = roots.SelectMany(r => r.GetComponentsInChildren<CharacterActor>(true)).ToArray();
                var crops = roots.SelectMany(r => r.GetComponentsInChildren<HarvestableCrop>(true)).ToArray();
                Assert.That(actors.Length, Is.EqualTo(5));
                Assert.That(actors.All(a => a.VisualRoot != null && a.Appearance != null && a.Appearance.Run != null
                    && a.Appearance.Run != a.Appearance.Walk && a.VisualRoot.GetComponentInChildren<Animator>(true)?.avatar?.isHuman == true), Is.True);
                Assert.That(crops.Count(c => c.Definition.ItemId == ItemIds.Potato), Is.EqualTo(24));
                Assert.That(crops.Count(c => c.Definition.ItemId == ItemIds.Egg), Is.EqualTo(2));
                Assert.That(crops.Count(c => c.Definition.ItemId == ItemIds.Timber), Is.EqualTo(3));
                Assert.That(crops.Select(c => c.PersistentId).Distinct().Count(), Is.EqualTo(crops.Length));
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<VillageFarmSite>(true)).Select(site => site.SiteId), Is.EquivalentTo(new[] { "meadow", "south" }));
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<VillageInteractable>(true)).All(n => n.Definition != null), Is.True);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
