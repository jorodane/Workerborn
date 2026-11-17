using UnityEngine;

namespace Workerborn
{
    public sealed class VillageWorldStage : MonoBehaviour
    {
        [SerializeField, Min(1)] private int minimumStage = 2;
        [SerializeField, Min(0), Tooltip("0 means no upper limit.")] private int maximumStage;
        public void Apply(int stage) => gameObject.SetActive(stage >= minimumStage && (maximumStage == 0 || stage <= maximumStage));
    }
}
