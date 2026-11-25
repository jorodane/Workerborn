using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        private const string LocalCharacterId = "player";
        private readonly Dictionary<string, HarvestWorker> villageCharacters = new Dictionary<string, HarvestWorker>();
        internal bool CanRunNpcSchedules => initialized;

        internal HarvestWorker FindCharacter(string id) => id == worker.CharacterId ? worker :
            villageCharacters.TryGetValue(id, out var character) ? character : null;

        private void BindWorker(CharacterActor actor, HarvestWorker character, VillageState state, string id,
            Camera inputCamera = null, string playerId = null)
        {
            BindCharacter(actor);
            character.Initialize(inputCamera, actor.GetComponent<WorkerAnimation>());
            character.BindVillage(this, state, id, playerId);
        }

        internal void BindNpcCharacter(CharacterActor actor, string instanceId = null, bool inClouds = false)
        {
            if (!actor.TryGetComponent<VillageInteractable>(out var npc) || npc.Definition == null)
                return;
            string id = instanceId ?? (string.IsNullOrEmpty(npc.PersistentId) ? npc.Definition.Id : npc.PersistentId);
            if (string.IsNullOrWhiteSpace(id) || villageCharacters.TryGetValue(id, out var previous) && previous != null && previous.isActiveAndEnabled)
                return;

            var character = actor.GetComponent<HarvestWorker>();
            if (character == null)
                character = actor.gameObject.AddComponent<HarvestWorker>();
            character.UseActionPresentation(worker);
            actor.GetComponent<WorkerAnimation>()?.UseDefaultGestures(player.GetComponent<WorkerAnimation>());
            var state = inClouds ? VillageState.BeforeFirstLife(State.Rules) : new VillageState(State.Rules);
            state.LearnSkill(SkillId.HarvestAcquisition, SkillProgression.MaxRank);
            BindWorker(actor, character, state, id);
            character.EnsureNavigation();
            if (initialized)
                state.BeginInWorld(State.ExportWorld());
            villageCharacters[id] = character;

            if (npc.Definition.DailySchedule.Length == 0)
                return;
            var routine = actor.gameObject.AddComponent<NpcRoutine>();
            routine.Bind(this, npc, character);
            npc.Routine = routine;
        }

        private void RestoreVillageCharacters()
        {
            var records = new Dictionary<string, CharacterSave>();
            if (restoredWorld?.characters != null)
                foreach (var saved in restoredWorld.characters)
                    if (saved != null && !string.IsNullOrEmpty(saved.characterId))
                        records[saved.characterId] = saved;

            var world = State.ExportWorld();
            foreach (var entry in villageCharacters)
            {
                var character = entry.Value;
                if (records.TryGetValue(entry.Key, out var saved) && saved.awaitingIncarnation == character.CharacterState.AwaitingIncarnation)
                {
                    try
                    {
                        var state = VillageState.Restore(saved, State.Rules);
                        state.LearnSkill(SkillId.HarvestAcquisition, SkillProgression.MaxRank);
                        character.SetCharacterState(state);
                        RestoreCharacterPosition(character, saved);
                        if (saved.day == State.Day && saved.era == State.Era)
                            RestoreCharacterAction(character, saved);
                    }
                    catch (System.ArgumentException error)
                    {
                        Debug.LogWarning($"Could not restore character {entry.Key}: {error.Message}", character);
                        character.CharacterState.BeginInWorld(world);
                    }
                }
                else
                    character.CharacterState.BeginInWorld(world);
                character.CharacterState.ApplyWorld(world);
            }
        }

        private void UpdateVillageCharacters(long now)
        {
            if (villageCharacters.Count == 0)
                return;
            var world = State.ExportWorld();
            foreach (var character in villageCharacters.Values)
            {
                if (character == null || !character.isActiveAndEnabled)
                    continue;
                var state = character.CharacterState;
                state.CollectReadyHarvests(now);
                if (state.Day < world.day && character.Action == WorkerAction.Sitting)
                    state.TrySleep(paidRoom: false, sharedClock: true);
                state.ApplyWorld(world);
            }
        }

        private CharacterSave[] CaptureVillageCharacters()
        {
            var saves = new List<CharacterSave>();
            foreach (var character in villageCharacters.Values)
                if (character != null && character.isActiveAndEnabled)
                    saves.Add(CaptureCharacter<CharacterSave>(character));
            return saves.ToArray();
        }

        private T CaptureCharacter<T>(HarvestWorker character) where T : CharacterSave, new()
        {
            var save = character.CharacterState.Export<T>();
            Vector3 position = character.StandingPosition;
            if (save.awaitingIncarnation)
                position = spawnPoint.position;
            else if (InteriorAt(position.x, position.z) is { } room)
            {
                save.interior = room.CapturePosition(position);
                position = ExteriorPosition(room);
            }
            save.characterId = character.CharacterId;
            save.playerX = position.x;
            save.playerZ = position.z;
            save.yaw = character.transform.eulerAngles.y;
            save.sitting = character.Action == WorkerAction.Sitting;
            if (character.PlannedCrop != null)
            {
                save.harvestTargetId = character.PlannedCrop.PersistentId;
                save.harvestProgress = character.HarvestProgress;
                if (character.FishingPoint is Vector3 cast)
                {
                    save.casting = true;
                    save.castX = cast.x;
                    save.castY = cast.y;
                    save.castZ = cast.z;
                }
            }
            return save;
        }

        private void RestoreCharacterPosition(HarvestWorker character, CharacterSave save)
        {
            if (save.awaitingIncarnation)
                return;
            bool restored = TryRestoreInterior(save.interior, out var room, out var point) && TryWarpCharacter(character, point, room);
            if (!restored && float.IsFinite(save.playerX) && float.IsFinite(save.playerZ))
                restored = TryWarpCharacter(character, new Vector3(save.playerX, 0f, save.playerZ));
            if (!restored)
                return;
            if (!string.IsNullOrEmpty(save.characterId) && float.IsFinite(save.yaw))
                character.transform.rotation = Quaternion.Euler(0f, save.yaw, 0f);
            if (save.sitting)
                character.ToggleSit();
        }

        private void RestoreCharacterAction(HarvestWorker character, CharacterSave save)
        {
            if (save.awaitingIncarnation || string.IsNullOrEmpty(save.harvestTargetId) || !float.IsFinite(save.harvestProgress))
                return;
            foreach (var crop in allCrops)
                if (crop != null && crop.PersistentId == save.harvestTargetId && crop.CanInteract)
                {
                    Vector3? cast = save.casting && float.IsFinite(save.castX) && float.IsFinite(save.castY) && float.IsFinite(save.castZ)
                        ? new Vector3(save.castX, save.castY, save.castZ) : null;
                    character.RestoreHarvest(crop, Mathf.Clamp01(save.harvestProgress), cast);
                    return;
                }
        }
    }
}
