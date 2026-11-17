using UnityEngine;

namespace Workerborn
{
    public sealed class CharacterPartInstance : MonoBehaviour
    {
        [SerializeField] private CharacterPartDefinition definition;
        public CharacterPartDefinition Definition => definition;
        public void Initialize(CharacterPartDefinition part) => definition = part;
    }
}
