using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    // The source coordinates are world-aligned and relative to the standing tree's foot.
    internal sealed class TimberGeometry : IDisposable
    {
        private const float Epsilon = .00001f;
        private readonly Vertex[] vertices;
        private readonly int[][] triangles;
        private readonly Dictionary<(float Start, float End), Mesh> sections = new Dictionary<(float, float), Mesh>();
        internal Material[] Materials { get; }

        internal TimberGeometry(MeshFilter source, Vector3 foot, Material cutSurface)
        {
            Mesh mesh = source.sharedMesh;
            var meshToTree = Matrix4x4.Translate(-foot) * source.transform.localToWorldMatrix;
            var normalToTree = meshToTree.inverse.transpose;
            bool mirrored = meshToTree.determinant < 0f;
            var points = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var uv = mesh.uv;
            var colors = mesh.colors;
            vertices = new Vertex[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 normal = i < normals.Length ? normalToTree.MultiplyVector(normals[i]).normalized : Vector3.up;
                Vector4 sourceTangent = i < tangents.Length ? tangents[i] : new Vector4(1f, 0f, 0f, 1f);
                Vector3 tangent = meshToTree.MultiplyVector(sourceTangent).normalized;
                vertices[i] = new Vertex(meshToTree.MultiplyPoint3x4(points[i]), normal,
                    new Vector4(tangent.x, tangent.y, tangent.z, mirrored ? -sourceTangent.w : sourceTangent.w),
                    i < uv.Length ? uv[i] : Vector2.zero, i < colors.Length ? colors[i] : Color.white);
            }

            triangles = new int[mesh.subMeshCount][];
            var sourceMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
            Materials = new Material[mesh.subMeshCount + 1];
            Array.Copy(sourceMaterials, Materials, Math.Min(sourceMaterials.Length, triangles.Length));
            Materials[triangles.Length] = cutSurface != null ? cutSurface : Materials[0];
            for (int submesh = 0; submesh < triangles.Length; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);
                if (mirrored)
                    for (int i = 0; i < indices.Length; i += 3)
                        (indices[i], indices[i + 1]) = (indices[i + 1], indices[i]);
                triangles[submesh] = indices;
            }
        }

        internal Mesh Slice(TimberSection section)
        {
            var key = (section.Start, section.End);
            if (sections.TryGetValue(key, out var cached))
                return cached;

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var faces = new List<int>[Materials.Length];
            var bottom = new List<Vector3>();
            var top = new List<Vector3>();
            var input = new Vertex[6];
            var clipped = new Vertex[6];

            for (int submesh = 0; submesh < triangles.Length; submesh++)
            {
                var output = faces[submesh] = new List<int>();
                int[] indices = triangles[submesh];
                for (int i = 0; i < indices.Length; i += 3)
                {
                    input[0] = vertices[indices[i]];
                    input[1] = vertices[indices[i + 1]];
                    input[2] = vertices[indices[i + 2]];
                    // The pine's first material is the solid trunk. Foliage cards keep their open edges.
                    int count = Clip(input, 3, clipped, section.Start, keepAbove: true, submesh == 0 ? bottom : null);
                    count = Clip(clipped, count, input, section.End, keepAbove: false, submesh == 0 ? top : null);
                    for (int n = 2; n < count; n++)
                        AddTriangle(output, input[0], input[n - 1], input[n]);
                }
            }

            var capFaces = faces[triangles.Length] = new List<int>();
            AddCap(bottom, Vector3.down);
            AddCap(top, Vector3.up);
            var mesh = new Mesh { name = "Pine cut section" };
            if (positions.Count > ushort.MaxValue)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.subMeshCount = faces.Length;
            for (int i = 0; i < faces.Length; i++)
                mesh.SetTriangles(faces[i], i, false);
            mesh.RecalculateBounds();
            sections.Add(key, mesh);
            return mesh;

            void AddTriangle(List<int> indices, Vertex a, Vertex b, Vertex c)
            {
                if (Vector3.Cross(b.Point - a.Point, c.Point - a.Point).sqrMagnitude < Epsilon * Epsilon)
                    return;
                AddVertex(a);
                AddVertex(b);
                AddVertex(c);

                void AddVertex(Vertex vertex)
                {
                    indices.Add(positions.Count);
                    positions.Add(vertex.Point - Vector3.up * section.Center);
                    normals.Add(vertex.Normal.normalized);
                    tangents.Add(vertex.Tangent);
                    uvs.Add(vertex.Uv);
                    colors.Add(vertex.Color);
                }
            }

            void AddCap(List<Vector3> intersections, Vector3 normal)
            {
                // The bark is a convex ring at each cut. The hull removes duplicate and collinear edge points.
                var ring = Hull(intersections);
                for (int i = 2; i < ring.Count; i++)
                    AddTriangle(capFaces, CapVertex(ring[0]), CapVertex(ring[normal.y > 0f ? i : i - 1]),
                        CapVertex(ring[normal.y > 0f ? i - 1 : i]));

                Vertex CapVertex(Vector3 point) => new Vertex(point, normal, new Vector4(1f, 0f, 0f, -normal.y),
                    new Vector2(point.x, point.z), Color.white);
            }
        }

        private static int Clip(Vertex[] input, int count, Vertex[] output, float height, bool keepAbove, List<Vector3> ring)
        {
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                Vertex a = input[i], b = input[(i + 1) % count];
                bool keepA = keepAbove ? a.Point.y >= height : a.Point.y <= height;
                bool keepB = keepAbove ? b.Point.y >= height : b.Point.y <= height;
                if (keepA)
                    output[written++] = a;
                if (keepA == keepB)
                    continue;
                var cut = Vertex.Interpolate(a, b, (height - a.Point.y) / (b.Point.y - a.Point.y));
                cut.Point.y = height;
                output[written++] = cut;
                ring?.Add(cut.Point);
            }
            return written;
        }

        private static List<Vector3> Hull(List<Vector3> intersections)
        {
            intersections.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
            var ring = new List<Vector3>();
            for (int i = 0; i < intersections.Count; i++)
                Append(intersections[i], 0);
            int lower = ring.Count;
            for (int i = intersections.Count - 2; i >= 0; i--)
                Append(intersections[i], lower - 1);
            if (ring.Count > 1)
                ring.RemoveAt(ring.Count - 1);
            return ring;

            void Append(Vector3 point, int boundary)
            {
                while (ring.Count > boundary + 1)
                {
                    Vector3 a = ring[ring.Count - 1] - ring[ring.Count - 2];
                    Vector3 b = point - ring[ring.Count - 1];
                    if (a.x * b.z - a.z * b.x > Epsilon * Epsilon)
                        break;
                    ring.RemoveAt(ring.Count - 1);
                }
                ring.Add(point);
            }
        }

        public void Dispose()
        {
            foreach (var mesh in sections.Values)
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(mesh);
                else
                    UnityEngine.Object.DestroyImmediate(mesh);
            sections.Clear();
        }

        private struct Vertex
        {
            internal Vector3 Point;
            internal readonly Vector3 Normal;
            internal readonly Vector4 Tangent;
            internal readonly Vector2 Uv;
            internal readonly Color Color;

            internal Vertex(Vector3 point, Vector3 normal, Vector4 tangent, Vector2 uv, Color color)
            {
                Point = point;
                Normal = normal;
                Tangent = tangent;
                Uv = uv;
                Color = color;
            }

            internal static Vertex Interpolate(Vertex a, Vertex b, float t) => new Vertex(Vector3.Lerp(a.Point, b.Point, t),
                Vector3.Lerp(a.Normal, b.Normal, t), Vector4.Lerp(a.Tangent, b.Tangent, t), Vector2.Lerp(a.Uv, b.Uv, t),
                Color.Lerp(a.Color, b.Color, t));
        }
    }
}
