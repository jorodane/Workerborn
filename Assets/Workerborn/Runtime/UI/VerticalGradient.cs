using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class VerticalGradient : MaskableGraphic
    {
        [SerializeField] private bool darkAtTop;
        [SerializeField, Min(0f)] private float solidHeight;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            Color32 dark = color;
            Color32 clear = dark;
            clear.a = 0;
            float solid = Mathf.Min(solidHeight, rect.height);
            if (darkAtTop)
            {
                AddQuad(mesh, rect, rect.yMin, rect.yMax - solid, clear, dark);
                if (solid > 0)
                    AddQuad(mesh, rect, rect.yMax - solid, rect.yMax, dark, dark);
            }
            else
            {
                if (solid > 0)
                    AddQuad(mesh, rect, rect.yMin, rect.yMin + solid, dark, dark);
                AddQuad(mesh, rect, rect.yMin + solid, rect.yMax, dark, clear);
            }
        }

        private static void AddQuad(VertexHelper mesh, Rect rect, float bottom, float top, Color32 bottomColor, Color32 topColor)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(rect.xMin, bottom), bottomColor, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMin, top), topColor, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, top), topColor, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, bottom), bottomColor, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
