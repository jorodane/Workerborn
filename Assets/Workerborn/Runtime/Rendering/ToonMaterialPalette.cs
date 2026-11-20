using UnityEngine;

namespace Workerborn
{
    [System.Serializable]
    public sealed class ToonMaterialPair
    {
        public Material source;
        public Material styled;
    }

    [CreateAssetMenu(menuName = "Workerborn/Rendering/Toon Material Palette")]
    public sealed class ToonMaterialPalette : ScriptableObject
    {
        [SerializeField]
        private ToonMaterialPair[] materials;
        public void Apply(Transform root)
        {
            if (root == null || materials == null)
                return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var assigned = renderer.sharedMaterials;
                if (RemapMaterials(assigned))
                    renderer.sharedMaterials = assigned;
            }
        }

        internal bool RemapMaterials(Material[] assigned)
        {
            if (materials == null)
                return false;
            bool changed = false;
            for (int i = 0; i < assigned.Length; i++)
                foreach (var pair in materials)
                    if (assigned[i] == pair.source && pair.styled != null)
                    {
                        assigned[i] = pair.styled;
                        changed = true;
                        break;
                    }
            return changed;
        }
    }
}
