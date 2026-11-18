using UnityEngine;

namespace Workerborn
{
    [ExecuteAlways]
    public sealed class ToonSurface : MonoBehaviour
    {
        [SerializeField]
        private ToonMaterialPalette palette;
        private void OnEnable() => Apply();
        public void Apply()
        {
            if (palette != null)
                palette.Apply(transform);
        }
    }
}
