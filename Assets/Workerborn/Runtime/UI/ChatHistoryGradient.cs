using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class ChatHistoryGradient : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            Color32 bottom = color;
            Color32 top = bottom;
            top.a = 0;

            mesh.AddVert(new Vector3(rect.xMin, rect.yMin), bottom, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMin, rect.yMax), top, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMax), top, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMin), bottom, Vector2.zero);
            mesh.AddTriangle(0, 1, 2);
            mesh.AddTriangle(2, 3, 0);
        }
    }
}
