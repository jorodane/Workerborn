using System;
using UnityEngine;

namespace Workerborn
{
    internal enum InteractionCursor
    {
        Default = 0,
        Orbit = 1,
        Inspect = 2,
        Talk = 3,
        PickUp = 4,
        Harvest = 5,
        Chop = 6,
        Fish = 7,
        Attack = 8,
        Enter = 9,
        Rest = 10,
        Manage = 11,
        Pray = 12
    }

    [CreateAssetMenu(menuName = "Workerborn/UI/Cursors", fileName = "VillageCursors")]
    public sealed class VillageCursorSet : ScriptableObject
    {
        [Serializable]
        private struct CursorStyle
        {
            public InteractionCursor kind;
            public Texture2D texture;
            public Vector2 hotspot;
        }

        [SerializeField] private CursorStyle[] styles = Array.Empty<CursorStyle>();

        internal Texture2D Find(InteractionCursor kind, out Vector2 hotspot)
        {
            foreach (var style in styles)
                if (style.kind == kind && style.texture != null)
                {
                    hotspot = style.hotspot;
                    return style.texture;
                }
            hotspot = Vector2.zero;
            return null;
        }
    }
}
