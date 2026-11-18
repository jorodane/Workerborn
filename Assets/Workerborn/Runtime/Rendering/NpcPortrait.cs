using UnityEngine;

namespace Workerborn
{
    public sealed class NpcPortrait : MonoBehaviour
    {
        [SerializeField]
        private Camera portraitCamera;
        [SerializeField]
        private float distance = 3f;
        [SerializeField]
        private float height = .9f;
        private CharacterActor actor;
        public void Show(CharacterActor target)
        {
            if (actor != null)
                actor.SetPortraitLayer(false);
            actor = target;
            if (actor != null)
                actor.SetPortraitLayer(true);
            portraitCamera.enabled = actor != null;
        }

        private void LateUpdate()
        {
            if (actor == null)
                return;
            Vector3 focus = actor.transform.position + Vector3.up * height;
            portraitCamera.transform.SetPositionAndRotation(focus + actor.transform.forward * distance, Quaternion.LookRotation(-actor.transform.forward, Vector3.up));
        }

        private void OnDisable()
        {
            if (actor != null)
                actor.SetPortraitLayer(false);
        }
    }
}
