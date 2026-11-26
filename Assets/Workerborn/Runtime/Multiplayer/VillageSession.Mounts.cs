using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    internal sealed partial class VillageSession
    {
        private MountSave[] initialMounts;
        internal MountSave[] StartingMounts => authority?.Mounts() ?? initialMounts;

        internal void ConfigureMounts(IEnumerable<(MountSave Pose, float Reach)> sites, Func<float, float, bool> ground) =>
            authority?.ConfigureMounts(sites, ground);

        internal bool SubmitMount(string id) => SubmitCommand(new SessionPacket { type = SessionMessage.Mount, argument = id });

        private void MountCommand(string identity, SessionPacket packet, Connection connection)
        {
            string message = "탑승 상태를 다시 확인해 주세요.";
            bool changed = authority.Checkpoint(identity, packet.character) && authority.TryChangeMount(identity, packet.argument, out message);
            SendProfile(identity, connection, message, packet.request, false, false, mounts: authority.Mounts());
            if (changed)
                nextSave = Mathf.Min(nextSave, Time.unscaledTime + 1f);
        }
    }
}
