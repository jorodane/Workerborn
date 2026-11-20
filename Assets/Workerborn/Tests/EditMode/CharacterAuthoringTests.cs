using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Workerborn.Tests
{
    public sealed class CharacterAuthoringTests
    {
        [TestCase(0, "Fem", "Female")]
        [TestCase(1, "Male", "Male")]
        public void P09OutfitPiecesKeepTheirNativeBones(int body, string armor, string face)
        {
            var original = AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/Workerborn/Data/Characters/Player_Default.asset");
            Assert.That(original != null && original.UsesP09, Is.True);
            var model = CharacterAssembler.Build(original, null, new CharacterLook { body = body, hair = 7, outfit = 4, face = 2 });
            try
            {
                var rig = model.transform.GetChild(0);
                var meshes = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
                Assert.That(meshes.Any(mesh => mesh.name == $"{face}_Face_02" && mesh.enabled), Is.True);
                Assert.That(meshes.Any(mesh => mesh.name == "Hair_07" && mesh.enabled), Is.True);
                foreach (var piece in new[] { "Chest", "Arm", "Leg" })
                {
                    var skin = meshes.Single(mesh => mesh.name == $"{armor}_Armor_004_{piece}");
                    Assert.That(skin.enabled, Is.True);
                    Assert.That(skin.bones.Length, Is.GreaterThan(0));
                    Assert.That(skin.bones.All(b => b != null && b.IsChildOf(rig)), Is.True);
                }
                Assert.That(original.DefaultLook.outfit, Is.EqualTo(3));
            }
            finally { Object.DestroyImmediate(model); }
        }

        [Test]
        public void P09TrialUsesTheImportedHumanoidAndItsToolSocket()
        {
            var trial = AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/Workerborn/Data/Characters/Player_P09_Trial.asset");
            var axe = AssetDatabase.LoadAssetAtPath<CharacterPartDefinition>("Assets/Workerborn/Data/Characters/Parts/WoodcuttingAxe.asset");
            Assert.That(trial, Is.Not.Null);
            Assert.That(axe, Is.Not.Null);
            var model = CharacterAssembler.Build(trial, null);
            try
            {
                var rig = model.transform.GetChild(0);
                var animator = rig.GetComponentInChildren<Animator>();
                Assert.That(animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman, Is.True);
                Assert.That(new[] { trial.Idle, trial.Walk, trial.Run, trial.Work }.All(clip => clip != null && clip.humanMotion), Is.True);
                Assert.That(rig.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Where(renderer => renderer.enabled)
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Any(material => material != null && material.shader != null && material.shader.name.Contains("lilToon")), Is.True);

                var vendorWeapons = rig.GetComponentsInChildren<Transform>(true).Single(transform => transform.name == "Weapon");
                Assert.That(vendorWeapons.gameObject.activeSelf, Is.False);
                var handSocket = rig.GetComponentsInChildren<Transform>().Single(transform => transform.name == "Weapon_Target_Hand_R");
                var tool = CharacterAssembler.AttachPart(axe, rig, appearance: trial);
                Assert.That(tool != null && tool.transform.parent == handSocket, Is.True);
                Assert.That(tool.transform.localPosition, Is.EqualTo(axe.GripPosition(trial.UsesP09)));
                Assert.That(Quaternion.Angle(tool.transform.localRotation, Quaternion.Euler(axe.GripRotation(trial.UsesP09))), Is.LessThan(.01f));
            }
            finally { Object.DestroyImmediate(model); }
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
