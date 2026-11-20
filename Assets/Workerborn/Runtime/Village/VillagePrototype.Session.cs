using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        private readonly HashSet<string> currentPeers = new HashSet<string>();
        private readonly List<string> departedPeers = new List<string>();
        private readonly Dictionary<string, RemoteVillagePlayer> peers = new Dictionary<string, RemoteVillagePlayer>();
        private VillageSession Session => VillageSession.Instance;
        private bool wasOnline;
        public bool IsOnline => wasOnline || Session?.Online == true;
        public bool AwaitingWorldCommand => IsOnline && Session?.Submitting == true;
        public bool CanInviteFriends => Session?.SteamHost == true;
        public string SessionName => Session?.SessionLabel ?? "싱글플레이";
        public CampaignStatus EraStatus => Session?.Status;
        public string LegendPlayerId => Session?.LocalPlayerId ?? "single-player";
        public string LegendPlayerName => Session?.LocalPlayerName ?? "일꾼";

        private void StartHeadless()
        {
            if (settings == null || mapRoot == null || spawnPoint == null)
            {
                Debug.LogError("The dedicated server needs VillageSettings, Map Root and a Spawn Point.");
                Application.Quit(1);
                return;
            }
            farmSites = mapRoot.GetComponentsInChildren<VillageFarmSite>(true);
            npcs.AddRange(mapRoot.GetComponentsInChildren<VillageInteractable>(true));
            Session.InitializeAuthority(settings.CreateRules(FindInteraction(VillageRole.Farmer)?.Definition), AuthoredSites(), spawnPoint.position);
            foreach (var root in gameObject.scene.GetRootGameObjects())
                root.SetActive(false);
        }

        private Dictionary<string, string> AuthoredSites()
        {
            var catalog = new Dictionary<string, string>();
            foreach (var site in farmSites)
                if (!string.IsNullOrWhiteSpace(site.SiteId))
                    catalog[site.SiteId] = site.DisplayName;
            return catalog;
        }

        public bool PerformWorld(CampaignAction action, string argument = null, int value = 1)
        {
            if (IsOnline)
            {
                bool submitted = Session.Submit(action, argument, value);
                if (submitted && Session.Submitting)
                    worker.Cancel();
                return submitted;
            }
            if (action == CampaignAction.Rebirth)
            {
                if (!State.TryRebirth())
                {
                    hud.ShowToast(State.LastMessage);
                    return false;
                }
                FinishTransition(reborn: true, eraChanged: false);
                hud.ShowToast(State.LastMessage);
                SaveGame(false);
                return true;
            }
            switch (action)
            {
                case CampaignAction.AcceptFoundation: return Perform(State.TryAcceptFoundation);
                case CampaignAction.DeliverTimber: return Perform(State.TryDeliverTimber);
                case CampaignAction.SelectFarmSite:
                    var site = FindFarmSite(argument);
                    return site != null && Perform(() => State.TryChooseFarmSite(site.SiteId, site.DisplayName));
                case CampaignAction.DonateGold: return Perform(State.TryDonateGold);
                case CampaignAction.DonateItems: return Perform(State.TryDonatePotatoes);
                case CampaignAction.RecordLegend: return Perform(() => State.TryRecordLegend(LegendPlayerId, LegendPlayerName));
                case CampaignAction.ProveLegend: return Perform(() => State.TryProveLegend(LegendPlayerId));
                default: return false;
            }
        }

        internal SessionAvatar CaptureAvatar()
        {
            var pose = worker.transform;
            return new SessionAvatar { x = pose.position.x, y = pose.position.y, z = pose.position.z,
                yaw = pose.eulerAngles.y, motion = player.GetComponent<WorkerAnimation>().CurrentAction };
        }

        internal void ApplySharedWorld(VillageWorldRecord world, SessionAvatar[] avatars)
        {
            if (!initialized || world?.legend == null || world.era < State.Era)
                return;
            int era = State.Era;
            int day = State.Day;
            State.ApplyWorld(world);
            if (era != State.Era)
                FinishTransition(reborn: false, eraChanged: true);
            else if (day != State.Day)
                ResetDailyResources();
            if (avatars != null)
                SetPeers(avatars);
        }

        internal void ApplySessionProfile(VillageSave save, VillageWorldRecord world, string message, bool transition)
        {
            if (!initialized || world?.legend == null)
                return;
            int era = State.Era;
            int life = State.Life;
            int level = State.Level;
            int day = State.Day;
            if (save != null && (!transition || save.life != life))
            {
                State = VillageState.Restore(save, State.Rules);
                equipmentVersion = -1;
            }
            State.ApplyWorld(world);
            bool reborn = life != State.Life;
            bool eraChanged = era != State.Era;
            if (reborn || eraChanged)
            {
                FinishTransition(reborn, eraChanged);
                SaveGame(false);
                if (string.IsNullOrEmpty(message) && eraChanged)
                    message = $"{State.Era}시대가 시작됐어요.";
            }
            else
            {
                if (day != State.Day)
                    ResetDailyResources();
                ShowLevelUp(level);
                hud.RefreshWindows();
            }
            hud.ShowToast(message);
        }

        private void SetPeers(IEnumerable<SessionAvatar> avatars)
        {
            var prefab = settings.RemotePlayerPrefab;
            if (prefab == null)
                return;
            currentPeers.Clear();
            foreach (var avatar in avatars)
            {
                if (avatar == null || string.IsNullOrEmpty(avatar.id) || avatar.id == Session.LocalPlayerId)
                    continue;
                currentPeers.Add(avatar.id);
                if (!peers.TryGetValue(avatar.id, out var peer))
                {
                    peer = Instantiate(prefab, SurfacePosition(avatar.x, avatar.z), Quaternion.identity);
                    peer.Bind(settings.Player, hud, avatar.name, avatar.look);
                    peers.Add(avatar.id, peer);
                }
                peer.SetPose(avatar, SurfacePosition(avatar.x, avatar.z).y);
            }
            departedPeers.Clear();
            foreach (var pair in peers)
                if (!currentPeers.Contains(pair.Key))
                    departedPeers.Add(pair.Key);
            foreach (string id in departedPeers)
            {
                Destroy(peers[id].gameObject);
                peers.Remove(id);
            }
        }

        public void InviteFriends() => Session?.InviteFriends();
        public void ReturnToMenu() => Session?.ReturnToMenu();

        // Session shutdown saves before releasing ownership. The old scene may still update in that frame.
        internal void MarkOnline() => wasOnline = true;
    }
}
