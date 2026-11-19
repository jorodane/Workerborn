using UnityEngine;

namespace Workerborn
{
    public sealed class VillageEffects : MonoBehaviour
    {
        [SerializeField] private GameObject click;
        [SerializeField] private GameObject itemLanding;
        [SerializeField] private GameObject itemPickup;
        [SerializeField] private GameObject levelUp;
        [SerializeField, Min(.1f)] private float lifetime = 3f;

        public void PlayClick(Vector3 position) => Play(click, position);
        public void PlayLanding(Vector3 position) => Play(itemLanding, position);
        public void PlayPickup(Vector3 position) => Play(itemPickup, position);
        public void PlayLevelUp(Vector3 position) => Play(levelUp, position);

        private void Play(GameObject prefab, Vector3 position)
        {
            if (prefab != null)
                Destroy(Instantiate(prefab, position, Quaternion.identity, transform), lifetime);
        }
    }
}
