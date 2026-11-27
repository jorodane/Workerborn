using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Workerborn.EditModeTests")]
[assembly: InternalsVisibleTo("Workerborn.PlayModeTests")]

namespace Workerborn
{
    public enum SessionKind { SteamFriends = 1, Dedicated = 2 }
    internal enum WorkerMotion { Idle, Run, Work, Walk, GroundSit, SeatSit }
    internal enum WorkerGesture { None, Jump, GatherSuccess, GatherFailure, TimberToss, Eat }

    public enum CampaignAction
    {
        AcceptFoundation, DeliverTimber, SelectFarmSite, DonateGold, DonateItems,
        Rebirth, VoteForEra, AnnounceEra, ParticipateInEra, CancelAnnouncement,
        RecordLegend, ProveLegend, SetCharacterLimit, BuildFacility, Incarnate
    }

    [Serializable]
    public sealed class SessionAvatar
    {
        public string id;
        public string name;
        public string mountId, parkedMountId;
        public string foodItem;
        public CharacterLook look;
        public CharacterGaze gaze;
        public float x, y, z, yaw, jumpOffset, gestureProgress;
        public float timberProgress;
        public float overeating;
        public bool hasSeat;
        public float seatX, seatY, seatZ, seatLift;
        public int motion, gesture;
        public int timberSwing;
    }

    [Serializable]
    public sealed class CampaignProfile
    {
        public string identity;
        public string id;
        public string name;
        public string portableId;
        public long createdUtc, lastConnectedUtc, lastDisconnectedUtc;
        public bool banned;
        public bool canManage;
        public VillageSave character;
        public int enteredEra;
        public bool optedIn;
        internal long NextHarvestPickupAt { get; set; }
    }

    [Serializable]
    public sealed class CampaignRecord
    {
        public string worldId;
        public VillageWorldRecord world;
        public CampaignProfile[] profiles = Array.Empty<CampaignProfile>();
        public GroundDropSave[] groundDrops = Array.Empty<GroundDropSave>();
        public WoodlandSave woodland;
        public MountSave[] mounts;
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

    internal sealed partial class VillageCampaign
    {
        private readonly VillageRules rules;
        private readonly IReadOnlyDictionary<string, string> sites;
        private readonly Dictionary<string, CampaignProfile> profiles = new Dictionary<string, CampaignProfile>();
        private readonly HashSet<string> connected = new HashSet<string>();
        private readonly HashSet<string> votes = new HashSet<string>();
        private readonly Dictionary<string, SessionAvatar> avatars = new Dictionary<string, SessionAvatar>();
        private double deadline;
        private readonly float spawnX, spawnZ;
        private Func<float, float, bool> isCloudPosition;
        private SessionAvatar cloudSpawn;
        public string WorldId { get; }
        public VillageWorldRecord World { get; private set; }
        public int CharacterLimit => World.characterLimit == 0 ? WorldCapacity.MaxCharacters : World.characterLimit;
        public int CreatedCharacters => profiles.Count;
        public bool HasFreeCharacterSlot => CreatedCharacters < CharacterLimit;
        public IEnumerable<CampaignProfile> Profiles => profiles.Values;

        internal void ConfigureCloudArea(Func<float, float, bool> contains, SessionAvatar spawn)
        {
            isCloudPosition = contains;
            cloudSpawn = spawn;
        }

        public VillageCampaign(VillageRules rules, IReadOnlyDictionary<string, string> sites,
            float spawnX, float spawnZ, CampaignRecord saved = null, int initialCharacterLimit = 0)
        {
            if (initialCharacterLimit != 0 && !WorldCapacity.IsValid(initialCharacterLimit))
                throw new ArgumentOutOfRangeException(nameof(initialCharacterLimit));
            this.rules = rules;
            this.sites = sites;
            this.spawnX = spawnX;
            this.spawnZ = spawnZ;
            WorldId = Guid.TryParseExact(saved?.worldId, "N", out var id) ? id.ToString("N") : Guid.NewGuid().ToString("N");
            World = saved?.world ?? new VillageState(rules).ExportWorld();
            deadline = saved?.deadline ?? 0;
            storedWoodland = saved?.woodland ?? saved?.profiles?.FirstOrDefault(profile => profile?.character?.woodland != null)?.character.woodland;
            savedMounts = saved?.mounts ?? saved?.profiles?.FirstOrDefault(profile => profile?.character?.mounts is { Length: > 0 })?.character.mounts;
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
                    var state = VillageState.RestorePlayer(profile.character, rules, out bool learnedSoulGrowth);
                    if (learnedSoulGrowth)
                        SaveCharacter(profile, state, teleport: false);
                    profiles.Add(profile.identity, profile);
                }
                RestoreGroundDrops(saved.groundDrops);
                foreach (var profile in profiles.Values)
                    LoadSoloGroundDrops(profile.character);
            }
        }

        public bool Connect(string identity, string name, out CampaignProfile profile, CharacterLook look = default, string portableId = null)
        {
            profile = null;
            if (connected.Count >= WorldCapacity.MaxConnections || connected.Contains(identity) || IsBanned(identity, portableId))
                return false;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!profiles.TryGetValue(identity, out profile))
            {
                if (!HasFreeCharacterSlot)
                    return false;
                var state = VillageState.BeforeFirstLife(rules);
                state.BeginInWorld(World);
                profile = new CampaignProfile
                {
                    identity = identity,
                    id = Guid.NewGuid().ToString("N"),
                    name = name,
                    createdUtc = now,
                    character = state.Export(),
                    enteredEra = World.era
                };
                SpawnAtStart(profile);
                profiles.Add(identity, profile);
            }
            if (profile.enteredEra != World.era)
                SpawnAtStart(profile);
            profile.name = name;
            profile.lastConnectedUtc = now;
            if (string.IsNullOrEmpty(profile.portableId))
                profile.portableId = portableId;
            ApplyWorld(profile.character);
            connected.Add(identity);
            var avatar = new SessionAvatar { id = profile.id, name = name, look = look.Validated() };
            SetAvatarStart(profile.character, avatar);
            avatars.Add(identity, avatar);
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
            storedWoodland = save.woodland ?? storedWoodland;
            savedMounts = save.mounts ?? savedMounts;
            var profile = profiles[identity];
            profile.enteredEra = world.era;
            if (!save.awaitingIncarnation)
            {
                foreach (var drop in SharedGroundDrops.Where(drop => drop.IsVisibleTo(profile.id)).ToArray())
                    groundDrops.Remove(drop.id);
                LoadSoloGroundDrops(save);
            }
            profile.character = save;
        }

        public void Disconnect(string identity)
        {
            if (connected.Remove(identity) && profiles.TryGetValue(identity, out var profile))
            {
                DropCarriedTimber(profile);
                ReleaseMount(profile.id);
                profile.lastDisconnectedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            votes.Remove(identity);
            avatars.Remove(identity);
        }

        public bool HasIdentity(string identity) => profiles.ContainsKey(identity);
        public bool MatchesPortableCharacter(string identity, string portableId) =>
            profiles.TryGetValue(identity, out var profile) && (string.IsNullOrEmpty(profile.portableId) ||
                string.Equals(profile.portableId, portableId, StringComparison.OrdinalIgnoreCase));
        public bool HasPortableCharacter(string portableId) =>
            !string.IsNullOrEmpty(portableId) && profiles.Values.Any(profile =>
                string.Equals(profile.portableId, portableId, StringComparison.OrdinalIgnoreCase));
        public bool IsConnected(string identity) => connected.Contains(identity);
        public bool IsOperator(string identity) => identity != null && connected.Contains(identity) &&
            profiles.TryGetValue(identity, out var profile) && profile.canManage && !profile.banned;
        public int ConnectedCount => connected.Count;
        public bool IsBanned(string identity, string portableId) =>
            profiles.TryGetValue(identity, out var profile) && profile.banned ||
            !string.IsNullOrEmpty(portableId) && profiles.Values.Any(other =>
                other.banned && string.Equals(other.portableId, portableId, StringComparison.OrdinalIgnoreCase));
        public CampaignProfile Profile(string identity) => profiles[identity];
        public SessionAvatar[] Avatars() => avatars.Values.ToArray();
        public bool CanAdvance => World.foundationStage == (int)FarmFoundationStage.ReadyToRebirth || World.expansionReady;

        public bool TrySetBan(string id, bool banned, Func<bool> save, out string identity)
        {
            identity = null;
            var profile = FindCharacter(id);
            if (profile == null)
                return false;
            bool wasBanned = profile.banned;
            long previousDisconnect = profile.lastDisconnectedUtc;
            profile.banned = banned;
            if (banned && IsConnected(profile.identity))
                profile.lastDisconnectedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!save())
            {
                profile.banned = wasBanned;
                profile.lastDisconnectedUtc = previousDisconnect;
                return false;
            }
            identity = profile.identity;
            if (banned)
                Disconnect(identity);
            return true;
        }

        public bool TryDeleteCharacter(string id, Func<bool> save, out string identity)
        {
            identity = null;
            var profile = FindCharacter(id);
            if (profile == null)
                return false;
            profiles.Remove(profile.identity);
            if (!save())
            {
                profiles.Add(profile.identity, profile);
                return false;
            }
            identity = profile.identity;
            Disconnect(identity);
            return true;
        }

        public bool TrySetOperator(CampaignProfile profile, bool enabled, Func<bool> save)
        {
            bool previous = profile.canManage;
            if (previous == enabled)
                return true;
            profile.canManage = enabled;
            if (save())
                return true;
            profile.canManage = previous;
            return false;
        }

        public CampaignProfile FindCharacter(string id) =>
            profiles.Values.FirstOrDefault(profile => string.Equals(profile.id, id, StringComparison.OrdinalIgnoreCase));

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

        public bool Checkpoint(string identity, VillageSave save) => TryCheckpoint(identity, save, out _);

        private bool TryCheckpoint(string identity, VillageSave save, out VillageState state)
        {
            state = null;
            var profile = profiles[identity];
            if (save == null || save.schema != VillageState.CurrentSaveSchema ||
                save.era != World.era || save.life != profile.character.life ||
                save.awaitingIncarnation != profile.character.awaitingIncarnation ||
                double.IsNaN(save.lifeStartedAt) || Math.Abs(save.lifeStartedAt - profile.character.lifeStartedAt) > .001 ||
                !FiniteCoordinate(save.playerX) || !FiniteCoordinate(save.playerZ) || isCloudPosition?.Invoke(save.playerX, save.playerZ) == true)
                return false;
            save.lifeStartedAt = profile.character.lifeStartedAt;
            save.hasLifeLook = profile.character.hasLifeLook;
            save.lifeLook = profile.character.lifeLook;
            ApplyWorld(save);
            try
            {
                state = VillageState.RestorePlayer(save, rules, out bool learnedSoulGrowth);
                profile.character = save;
                if (learnedSoulGrowth)
                    SaveCharacter(profile, state, teleport: false);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        public void Move(string identity, SessionAvatar pose)
        {
            if (pose == null || !FiniteCoordinate(pose.x) || !FiniteCoordinate(pose.y) ||
                !FiniteCoordinate(pose.z) || !FiniteCoordinate(pose.yaw) || pose.motion < 0 || pose.motion > (int)WorkerMotion.SeatSit)
                return;
            var character = profiles[identity].character;
            if (isCloudPosition != null && isCloudPosition(pose.x, pose.z) != character.awaitingIncarnation)
                return;
            var target = avatars[identity];
            target.x = pose.x;
            target.y = pose.y;
            target.z = pose.z;
            target.yaw = pose.yaw;
            target.motion = pose.motion;
            target.gaze = character.health > 0 ? pose.gaze.Validated() : default;
            float dx = pose.seatX - pose.x, dy = pose.seatY - pose.y, dz = pose.seatZ - pose.z;
            bool holdsSeat = pose.motion == (int)WorkerMotion.SeatSit || pose.motion == (int)WorkerMotion.Idle && target.hasSeat;
            target.hasSeat = holdsSeat && pose.hasSeat &&
                FiniteCoordinate(pose.seatX) && FiniteCoordinate(pose.seatY) && FiniteCoordinate(pose.seatZ) &&
                dx * dx + dz * dz <= 1f && Math.Abs(dy) <= 1.25f;
            target.seatX = target.hasSeat ? pose.seatX : pose.x;
            target.seatY = target.hasSeat ? pose.seatY : pose.y;
            target.seatZ = target.hasSeat ? pose.seatZ : pose.z;
            target.seatLift = target.hasSeat && float.IsFinite(pose.seatLift) ? Math.Clamp(pose.seatLift, -.12f, .65f) : 0f;
            target.jumpOffset = float.IsNaN(pose.jumpOffset) ? 0f : Math.Max(0f, Math.Min(.6f, pose.jumpOffset));
            target.gesture = pose.gesture >= (int)WorkerGesture.None && pose.gesture <= (int)WorkerGesture.Eat
                ? pose.gesture : (int)WorkerGesture.None;
            target.foodItem = null;
            if (target.gesture == (int)WorkerGesture.Eat)
            {
                var motion = (WorkerMotion)pose.motion;
                if (!character.awaitingIncarnation && character.health > 0 &&
                    motion is WorkerMotion.Idle or WorkerMotion.GroundSit or WorkerMotion.SeatSit &&
                    ItemCatalog.Contains(pose.foodItem) && ItemCatalog.Get(pose.foodItem).IsFood)
                    target.foodItem = pose.foodItem;
                else
                    target.gesture = (int)WorkerGesture.None;
            }
            target.gestureProgress = target.gesture == (int)WorkerGesture.None || !float.IsFinite(pose.gestureProgress)
                ? 0f : Math.Clamp(pose.gestureProgress, 0f, 1f);
            target.timberSwing = pose.motion == (int)WorkerMotion.Work && pose.timberSwing is >= 0 and <= (int)TimberSwing.Strike
                ? pose.timberSwing : 0;
            target.timberProgress = float.IsFinite(pose.timberProgress) ? Math.Clamp(pose.timberProgress, 0f, 1f) : 0f;
            target.overeating = !character.awaitingIncarnation && float.IsFinite(pose.overeating)
                ? Math.Clamp(pose.overeating, 0f, 1f) : 0f;
            MoveMount(target.id, pose);
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
            if (state.AwaitingIncarnation && action != CampaignAction.Incarnate)
            {
                message = "운명의 인도자에게서 환생을 마쳐 주세요.";
                return false;
            }
            bool success = action switch
            {
                CampaignAction.AcceptFoundation => state.TryAcceptFoundation(),
                CampaignAction.DeliverTimber => state.TryDeliverTimber(),
                CampaignAction.SelectFarmSite => argument != null && sites.TryGetValue(argument, out string site) && state.TryChooseFarmSite(argument, site),
                CampaignAction.DonateGold => state.TryDonateGold(),
                CampaignAction.DonateItems => state.TryDonatePotatoes(),
                CampaignAction.Rebirth => state.TryRebirth(),
                CampaignAction.Incarnate => state.TryIncarnate(value),
                CampaignAction.RecordLegend => state.TryRecordLegend(profile.id, profile.name),
                CampaignAction.ProveLegend => state.TryProveLegend(profile.id),
                CampaignAction.BuildFacility => state.TryInstallFacility(argument, value),
                _ => false
            };
            message = state.LastMessage;
            if (!success)
                return false;
            if (action == CampaignAction.Rebirth)
                DropCarriedTimber(profile);
            World = state.ExportWorld();
            SaveCharacter(profile, state, action is CampaignAction.Rebirth or CampaignAction.Incarnate);
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
            int day = World.day;
            World.AddMinutes(seconds * rules.MinutesPerSecond);
            if (day != World.day)
                timber?.StartDay(World.day);
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
                    DropCarriedTimber(profile);
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
            profile.character.CopyPoseAndAction(previous, teleport);
            profile.character.characters = previous.characters;
            profile.character.woodland = previous.woodland;
            profile.character.mounts = previous.mounts;
            profile.character.cropRemaining = teleport ? Array.Empty<int>() : previous.cropRemaining;
            profile.character.cropStates = teleport ? Array.Empty<CropStateSave>() : previous.cropStates;
            profile.character.animalStates = teleport ? Array.Empty<AnimalStateSave>() : previous.animalStates;
            profile.character.collectedPickupIds = teleport && previous.life != state.Life ? Array.Empty<string>() : previous.collectedPickupIds;
            profile.character.drops = teleport && previous.life != state.Life ? Array.Empty<GroundDropSave>() : previous.drops;
            if (teleport)
            {
                ReleaseMount(profile.id);
                SpawnAtStart(profile);
                if (avatars.TryGetValue(profile.identity, out var avatar))
                    SetAvatarStart(profile.character, avatar);
            }
        }

        private void SetAvatarStart(VillageSave character, SessionAvatar avatar)
        {
            var origin = character.awaitingIncarnation ? cloudSpawn : null;
            avatar.x = origin?.x ?? character.playerX;
            avatar.y = origin?.y ?? 0f;
            avatar.z = origin?.z ?? character.playerZ;
            avatar.yaw = origin?.yaw ?? 0f;
            avatar.motion = (int)WorkerMotion.Idle;
            avatar.hasSeat = false;
            avatar.seatLift = 0f;
            avatar.jumpOffset = 0f;
            avatar.gesture = (int)WorkerGesture.None;
            avatar.gestureProgress = 0f;
            avatar.foodItem = null;
            avatar.timberSwing = 0;
            avatar.timberProgress = 0f;
            avatar.overeating = 0f;
            avatar.gaze = default;
            if (character.hasLifeLook)
                avatar.look = character.lifeLook.Validated();
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
            save.lifeDay = save.awaitingIncarnation ? 1 : Math.Max(1, World.day - (int)(save.lifeStartedAt / 1440d));
            save.farmLevel = World.farmLevel;
            save.foundationStage = World.foundationStage;
            save.timberDelivered = World.timberDelivered;
            save.farmSiteId = World.farmSiteId;
            save.plotPlacements = World.plotPlacements;
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
            return new CampaignRecord
            {
                worldId = WorldId, world = World, deadline = deadline, profiles = profiles.Values.ToArray(),
                groundDrops = SharedGroundDrops.ToArray(), woodland = CaptureWoodlandForSave(), mounts = CaptureParkedMounts()
            };
        }
    }
}
