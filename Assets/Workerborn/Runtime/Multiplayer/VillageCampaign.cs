using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Workerborn.EditModeTests")]

namespace Workerborn
{
    public enum SessionKind { SteamFriends = 1, Dedicated = 2 }

    public enum CampaignAction
    {
        AcceptFoundation, DeliverTimber, SelectFarmSite, DonateGold, DonateItems,
        Rebirth, VoteForEra, AnnounceEra, ParticipateInEra, CancelAnnouncement,
        RecordLegend, ProveLegend, SetCharacterLimit
    }

    [Serializable]
    public sealed class SessionAvatar
    {
        public string id;
        public string name;
        public CharacterLook look;
        public float x, y, z, yaw;
        public int motion;
    }

    [Serializable]
    public sealed class CampaignProfile
    {
        public string identity;
        public string id;
        public string name;
        public VillageSave character;
        public int enteredEra;
        public bool optedIn;
    }

    [Serializable]
    public sealed class CampaignRecord
    {
        public VillageWorldRecord world;
        public CampaignProfile[] profiles = Array.Empty<CampaignProfile>();
        public double deadline;
    }

    [Serializable]
    public sealed class CampaignStatus
    {
        public int connected;
        public int votes;
        public bool voted;
        public bool optedIn;
        public bool canAnnounce;
        public double deadline;
        public int characterLimit;
        public int createdCharacters;
        public int requirementCharacters;
    }

    internal sealed class VillageCampaign
    {
        private readonly VillageRules rules;
        private readonly IReadOnlyDictionary<string, string> sites;
        private readonly Dictionary<string, CampaignProfile> profiles = new Dictionary<string, CampaignProfile>();
        private readonly HashSet<string> connected = new HashSet<string>();
        private readonly HashSet<string> votes = new HashSet<string>();
        private readonly Dictionary<string, SessionAvatar> avatars = new Dictionary<string, SessionAvatar>();
        private double deadline;
        private readonly float spawnX, spawnZ;
        public VillageWorldRecord World { get; private set; }
        public int CharacterLimit => World.characterLimit == 0 ? WorldCapacity.MaxCharacters : World.characterLimit;
        public int CreatedCharacters => profiles.Count;
        public bool HasFreeCharacterSlot => CreatedCharacters < CharacterLimit;

        public VillageCampaign(VillageRules rules, IReadOnlyDictionary<string, string> sites,
            float spawnX, float spawnZ, CampaignRecord saved = null, int initialCharacterLimit = 0)
        {
            if (initialCharacterLimit != 0 && !WorldCapacity.IsValid(initialCharacterLimit))
                throw new ArgumentOutOfRangeException(nameof(initialCharacterLimit));
            this.rules = rules;
            this.sites = sites;
            this.spawnX = spawnX;
            this.spawnZ = spawnZ;
            World = saved?.world ?? new VillageState(rules).ExportWorld();
            deadline = saved?.deadline ?? 0;
            if (saved == null)
            {
                World.characterLimit = initialCharacterLimit;
                World.requirementCharacters = Math.Max(1, initialCharacterLimit);
            }
            else
            {
                if (saved.world == null || saved.profiles == null || saved.profiles.Length > CharacterLimit ||
                    double.IsNaN(deadline) || double.IsInfinity(deadline) || deadline < 0)
                    throw new ArgumentException("Invalid campaign save.");
                var sample = new VillageState(rules).Export();
                sample.lifeStartedAt = World.Clock;
                ApplyWorld(sample);
                World = VillageState.Restore(sample, rules).ExportWorld();
                foreach (var profile in saved.profiles)
                {
                    if (profile == null || string.IsNullOrEmpty(profile.identity) || string.IsNullOrEmpty(profile.id) ||
                        profile.character == null || profiles.ContainsKey(profile.identity))
                        throw new ArgumentException("Invalid campaign profile.");
                    ApplyWorld(profile.character);
                    VillageState.Restore(profile.character, rules);
                    profiles.Add(profile.identity, profile);
                }
            }
        }

        public bool Connect(string identity, string name, out CampaignProfile profile, CharacterLook look = default)
        {
            profile = null;
            if (connected.Count >= WorldCapacity.MaxConnections || connected.Contains(identity))
                return false;
            if (!profiles.TryGetValue(identity, out profile))
            {
                if (!HasFreeCharacterSlot)
                    return false;
                var state = new VillageState(rules);
                state.BeginInWorld(World);
                profile = new CampaignProfile
                {
                    identity = identity,
                    id = Guid.NewGuid().ToString("N"),
                    name = name,
                    character = state.Export(),
                    enteredEra = World.era
                };
                SpawnAtStart(profile);
                profiles.Add(identity, profile);
            }
            if (profile.enteredEra != World.era)
                SpawnAtStart(profile);
            profile.name = name;
            ApplyWorld(profile.character);
            connected.Add(identity);
            avatars.Add(identity, new SessionAvatar { id = profile.id, name = name, look = look.Validated(), x = profile.character.playerX, z = profile.character.playerZ });
            return true;
        }

        public bool AdoptLegacyIdentity(string previous, string identity)
        {
            if (connected.Contains(previous) || profiles.ContainsKey(identity) ||
                !profiles.TryGetValue(previous, out var profile))
                return false;
            profiles.Remove(previous);
            profile.identity = identity;
            profiles.Add(identity, profile);
            return true;
        }

        public void StoreSolo(string identity, VillageWorldRecord world, VillageSave save)
        {
            World = world;
            var profile = profiles[identity];
            profile.enteredEra = world.era;
            profile.character = save;
        }

        public void Disconnect(string identity)
        {
            connected.Remove(identity);
            votes.Remove(identity);
            avatars.Remove(identity);
        }

        public bool HasIdentity(string identity) => profiles.ContainsKey(identity);
        public CampaignProfile Profile(string identity) => profiles[identity];
        public SessionAvatar[] Avatars() => avatars.Values.ToArray();
        public bool CanAdvance => World.foundationStage == (int)FarmFoundationStage.ReadyToRebirth || World.expansionReady;

        public bool TrySetCharacterLimit(int limit, out string message)
        {
            if (!WorldCapacity.IsValid(limit))
            {
                message = $"캐릭터 정원은 1명부터 {WorldCapacity.MaxCharacters}명까지 설정할 수 있어요.";
                return false;
            }
            if (limit < CreatedCharacters)
            {
                message = $"이미 생성된 캐릭터 {CreatedCharacters}명보다 정원을 줄일 수 없어요.";
                return false;
            }

            // Slots open immediately. Raising the requirements waits for the next era.
            World.characterLimit = limit;
            World.requirementCharacters = Math.Min(World.requirementCharacters, limit);
            message = $"월드 캐릭터 정원을 {limit}명으로 설정했어요.";
            return true;
        }

        public CampaignStatus Status(string identity, bool canAnnounce) => new CampaignStatus
        {
            connected = connected.Count,
            votes = votes.Count,
            voted = votes.Contains(identity),
            optedIn = profiles[identity].optedIn,
            canAnnounce = canAnnounce,
            deadline = deadline,
            characterLimit = CharacterLimit,
            createdCharacters = CreatedCharacters,
            requirementCharacters = World.requirementCharacters
        };

        public bool Checkpoint(string identity, VillageSave save)
        {
            var profile = profiles[identity];
            if (save == null || save.schema != VillageState.CurrentSaveSchema ||
                save.era != World.era || save.life != profile.character.life ||
                double.IsNaN(save.lifeStartedAt) || Math.Abs(save.lifeStartedAt - profile.character.lifeStartedAt) > .001 ||
                !FiniteCoordinate(save.playerX) || !FiniteCoordinate(save.playerZ))
                return false;
            save.lifeStartedAt = profile.character.lifeStartedAt;
            ApplyWorld(save);
            try
            {
                VillageState.Restore(save, rules);
                profile.character = save;
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        public void Move(string identity, SessionAvatar pose)
        {
            if (pose == null || !FiniteCoordinate(pose.x) || !FiniteCoordinate(pose.y) ||
                !FiniteCoordinate(pose.z) || !FiniteCoordinate(pose.yaw) || pose.motion < 0 || pose.motion > 3)
                return;
            var target = avatars[identity];
            target.x = pose.x;
            target.y = pose.y;
            target.z = pose.z;
            target.yaw = pose.yaw;
            target.motion = pose.motion;
        }

        private static bool FiniteCoordinate(float value) => !float.IsNaN(value) && Math.Abs(value) <= 10000f;

        public bool Perform(string identity, CampaignAction action, string argument, int value, bool operatorRights, out string message)
        {
            message = null;
            var profile = profiles[identity];
            if (action == CampaignAction.VoteForEra || action == CampaignAction.AnnounceEra ||
                action == CampaignAction.ParticipateInEra || action == CampaignAction.CancelAnnouncement)
            {
                if (!CanAdvance)
                {
                    message = "시대 퀘스트를 완료해 주세요.";
                    return false;
                }
                if (action == CampaignAction.VoteForEra)
                {
                    if (value != 0) votes.Add(identity);
                    else votes.Remove(identity);
                }
                else if (action == CampaignAction.ParticipateInEra && deadline > 0)
                    profile.optedIn = value != 0;
                else if (operatorRights)
                    return Announce(action, value, out message);
                else
                {
                    message = action == CampaignAction.ParticipateInEra
                        ? "예고된 시대 전환이 없어요."
                        : "시대 전환 예고는 호스트가 관리해요.";
                    return false;
                }
                return true;
            }

            ApplyWorld(profile.character);
            var state = VillageState.Restore(profile.character, rules);
            bool success = action switch
            {
                CampaignAction.AcceptFoundation => state.TryAcceptFoundation(),
                CampaignAction.DeliverTimber => state.TryDeliverTimber(),
                CampaignAction.SelectFarmSite => argument != null && sites.TryGetValue(argument, out string site) && state.TryChooseFarmSite(argument, site),
                CampaignAction.DonateGold => state.TryDonateGold(),
                CampaignAction.DonateItems => state.TryDonatePotatoes(),
                CampaignAction.Rebirth => state.TryRebirth(),
                CampaignAction.RecordLegend => state.TryRecordLegend(profile.id, profile.name),
                CampaignAction.ProveLegend => state.TryProveLegend(profile.id),
                _ => false
            };
            message = state.LastMessage;
            if (!success)
                return false;
            World = state.ExportWorld();
            SaveCharacter(profile, state, action == CampaignAction.Rebirth);
            return true;
        }

        public bool Announce(CampaignAction action, int days, out string message)
        {
            message = null;
            if (action == CampaignAction.CancelAnnouncement)
            {
                deadline = 0;
                foreach (var p in profiles.Values)
                    p.optedIn = false;
                message = "시대 전환 예고를 철회했어요.";
                return true;
            }
            if (action != CampaignAction.AnnounceEra || !CanAdvance || days < 1 || days > 365)
            {
                message = "시대 퀘스트를 완료한 뒤 인게임 1일 이상 예고해 주세요.";
                return false;
            }
            deadline = World.Clock + days * 1440d;
            message = $"인게임 {days}일 뒤 시대 전환을 예고했어요.";
            return true;
        }

        public bool WillAdvance(float seconds) => CanAdvance &&
            (connected.Count > 0 && connected.SetEquals(votes) || deadline > 0 && World.Clock + seconds * rules.MinutesPerSecond >= deadline);

        public bool Tick(float seconds)
        {
            World.AddMinutes(seconds * rules.MinutesPerSecond);
            bool unanimous = connected.Count > 0 && connected.SetEquals(votes);
            if (!CanAdvance || !unanimous && (deadline <= 0 || World.Clock < deadline))
                return false;

            var sample = new VillageState(rules);
            sample.BeginInWorld(World);
            sample.TryAdvanceEra(reincarnate: false);
            World = sample.ExportWorld();
            foreach (var profile in profiles.Values)
            {
                ApplyWorld(profile.character);
                if (connected.Contains(profile.identity))
                {
                    var state = VillageState.Restore(profile.character, rules);
                    if (unanimous || profile.optedIn)
                        state.RebirthPersonalLife();
                    SaveCharacter(profile, state, teleport: true);
                    avatars[profile.identity].motion = 0;
                }
                profile.optedIn = false;
            }
            votes.Clear();
            deadline = 0;
            return true;
        }

        private void SaveCharacter(CampaignProfile profile, VillageState state, bool teleport)
        {
            var previous = profile.character;
            profile.character = state.Export();
            profile.character.playerX = previous.playerX;
            profile.character.playerZ = previous.playerZ;
            profile.character.cropStates = teleport ? Array.Empty<CropStateSave>() : previous.cropStates;
            profile.character.animalStates = teleport ? Array.Empty<AnimalStateSave>() : previous.animalStates;
            profile.character.collectedPickupIds = teleport && previous.life != state.Life ? Array.Empty<string>() : previous.collectedPickupIds;
            profile.character.drops = teleport && previous.life != state.Life ? Array.Empty<GroundDropSave>() : previous.drops;
            if (teleport)
            {
                SpawnAtStart(profile);
                if (avatars.TryGetValue(profile.identity, out var avatar))
                {
                    avatar.x = spawnX;
                    avatar.z = spawnZ;
                }
            }
        }

        private void SpawnAtStart(CampaignProfile profile)
        {
            profile.enteredEra = World.era;
            profile.character.playerX = spawnX;
            profile.character.playerZ = spawnZ;
        }

        private void ApplyWorld(VillageSave save)
        {
            save.era = World.era;
            save.day = World.day;
            save.minute = World.minute;
            save.lifeDay = Math.Max(1, World.day - (int)(save.lifeStartedAt / 1440d));
            save.farmLevel = World.farmLevel;
            save.foundationStage = World.foundationStage;
            save.timberDelivered = World.timberDelivered;
            save.farmSiteId = World.farmSiteId;
            save.donatedGold = World.donatedGold;
            save.donatedPotatoes = World.donatedItems;
            save.farmUpgradePending = World.expansionReady;
            save.characterLimit = World.characterLimit;
            save.requirementCharacters = World.requirementCharacters;
            save.legend = World.legend;
            if (save.jobStatus == (int)JobStatus.Active && (save.lastJobDay != World.day || World.minute >= rules.JobEnd))
                save.jobStatus = (int)JobStatus.Failed;
        }

        public CampaignRecord Export()
        {
            foreach (var profile in profiles.Values)
                ApplyWorld(profile.character);
            return new CampaignRecord { world = World, deadline = deadline, profiles = profiles.Values.ToArray() };
        }
    }
}
