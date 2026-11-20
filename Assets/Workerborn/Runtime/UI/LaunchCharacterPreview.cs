using UnityEngine;

namespace Workerborn
{
    public sealed class LaunchCharacterPreview : MonoBehaviour
    {
        [SerializeField] private Camera previewCamera;
        [SerializeField] private CharacterDefinition appearance;
        [SerializeField] private WorkerAnimation animationDriver;
        private GameObject model;

        public RenderTexture Texture => previewCamera.targetTexture;
        public CharacterLook DefaultLook => appearance.DefaultLook;

        public void Show(CharacterLook look)
        {
            if (model == null)
            {
                model = CharacterAssembler.Build(appearance, transform, look);
                foreach (var child in model.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = 10;
                animationDriver.Initialize(model.GetComponentInChildren<Animator>(), appearance.Idle, appearance.Idle, appearance.Idle);
            }
            else
                P09Wardrobe.Apply(model.transform, look);
            previewCamera.enabled = true;
        }

        public void Hide() => previewCamera.enabled = false;
    }
}
