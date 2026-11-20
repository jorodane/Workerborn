using UnityEngine;
using UnityEngine.Rendering;

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
            if (actor == target)
                return;
            actor = target;
            portraitCamera.enabled = actor != null;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == portraitCamera && actor != null)
                actor.SetPortraitLayer(true);
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == portraitCamera && actor != null)
                actor.SetPortraitLayer(false);
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
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            if (actor != null)
                actor.SetPortraitLayer(false);
        }
    }
}
