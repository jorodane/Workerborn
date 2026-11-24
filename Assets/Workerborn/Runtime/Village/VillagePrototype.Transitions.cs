using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        private const float CoverDuration = .22f;
        private const float RevealDuration = .30f;
        private readonly Queue<(Func<bool> NeedsFade, Action Apply)> pendingWorldUpdates =
            new Queue<(Func<bool>, Action)>();

        public bool IsWorldTransitioning { get; private set; }

        private bool TryWorldTransition(Action commit)
        {
            if (IsWorldTransitioning)
                return false;
            IsWorldTransitioning = true;
            worker.Cancel();
            hud.SetWorldFade(commit == null ? 1f : 0f, true);
            StartCoroutine(PlayWorldTransition(commit));
            return true;
        }

        private void PresentWorldUpdate(Func<bool> needsFade, Action apply)
        {
            if (IsWorldTransitioning)
                pendingWorldUpdates.Enqueue((needsFade, apply));
            else if (needsFade())
                TryWorldTransition(apply);
            else
                apply();
        }

        private IEnumerator PlayWorldTransition(Action commit)
        {
            try
            {
                if (commit != null)
                {
                    yield return FadeWorld(0f, 1f, CoverDuration);
                    commit();
                }

                // A successful reload replaces this scene. Its HUD starts the reveal.
                while (!initialized)
                    yield return null;
                while (pendingWorldUpdates.Count > 0)
                    pendingWorldUpdates.Dequeue().Apply();
                lighting?.SetDate(State.Day, State.Minute);
                yield return null;
                yield return FadeWorld(1f, 0f, RevealDuration);
            }
            finally
            {
                if (hud != null)
                    hud.SetWorldFade(0f, false);
                IsWorldTransitioning = false;
            }

            while (!IsWorldTransitioning && pendingWorldUpdates.Count > 0)
            {
                var update = pendingWorldUpdates.Dequeue();
                PresentWorldUpdate(update.NeedsFade, update.Apply);
            }
        }

        private IEnumerator FadeWorld(float from, float to, float duration)
        {
            float started = Time.unscaledTime;
            for (float progress = 0f; progress < 1f; progress = (Time.unscaledTime - started) / duration)
            {
                hud.SetWorldFade(Mathf.SmoothStep(from, to, progress), true);
                yield return null;
            }
            hud.SetWorldFade(to, true);
        }
    }
}
