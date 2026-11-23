using System;
using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    public sealed class VillageMapRegion : MonoBehaviour
    {
        [Serializable]
        private struct LayerColor
        {
            public TerrainLayer layer;
            public Color color;
        }

        [SerializeField, Min(1f), Tooltip("Width in world units along X.")]
        private float width = 80f;
        [SerializeField, Min(1f), Tooltip("Depth in world units along Z.")]
        private float depth = 72f;
        [SerializeField] private LayerColor[] layerColors = Array.Empty<LayerColor>();

        private const int Resolution = 384;
        private static readonly Color Paper = new Color(.79f, .73f, .60f);
        private static readonly Color Unpainted = new Color(.68f, .71f, .53f);
        private Texture2D image;
        private TerrainData source;
        private Vector3 sourceOrigin, sourceSize;
        private Rect drawnArea;
        private bool dirty = true;

        public Rect WorldBounds
        {
            get
            {
                Vector3 center = transform.position;
                Vector2 size = new Vector2(Mathf.Max(1f, width), Mathf.Max(1f, depth));
                return new Rect(new Vector2(center.x, center.z) - size * .5f, size);
            }
        }

        internal Vector2 WorldToMap(Vector3 position)
        {
            Rect bounds = WorldBounds;
            return new Vector2((position.x - bounds.xMin) / bounds.width, (position.z - bounds.yMin) / bounds.height);
        }

        internal Vector2 MapToWorld(Vector2 point)
        {
            Rect bounds = WorldBounds;
            return bounds.min + Vector2.Scale(point, bounds.size);
        }

        internal Texture2D TerrainImage(Terrain terrain)
        {
            if (!isActiveAndEnabled || terrain == null || terrain.terrainData == null)
                return null;
            var data = terrain.terrainData;
            Vector3 origin = terrain.GetPosition();
            Vector3 size = data.size;
            Rect area = WorldBounds;
            if (dirty || source != data || sourceOrigin != origin || sourceSize != size || drawnArea != area)
            {
                Redraw(data, new Rect(origin.x, origin.z, size.x, size.z), area);
                source = data;
                sourceOrigin = origin;
                sourceSize = size;
                drawnArea = area;
                dirty = false;
            }
            return image;
        }

        private void Redraw(TerrainData data, Rect terrain, Rect area)
        {
            float longestSide = Mathf.Max(area.width, area.height);
            int columns = Mathf.Max(1, Mathf.RoundToInt(Resolution * area.width / longestSide));
            int rows = Mathf.Max(1, Mathf.RoundToInt(Resolution * area.height / longestSide));
            if (image == null || image.width != columns || image.height != rows)
            {
                if (image != null)
                    Destroy(image);
                image = new Texture2D(columns, rows, TextureFormat.RGBA32, false)
                {
                    name = "Terrain map", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
            }

            var pixels = new Color32[columns * rows];
            Array.Fill(pixels, (Color32)Paper);
            float stepX = area.width / columns;
            float stepZ = area.height / rows;
            int firstX = Mathf.Clamp(Mathf.CeilToInt((terrain.xMin - area.xMin) / stepX - .5f), 0, columns);
            int firstZ = Mathf.Clamp(Mathf.CeilToInt((terrain.yMin - area.yMin) / stepZ - .5f), 0, rows);
            int lastX = Mathf.Clamp(Mathf.FloorToInt((terrain.xMax - area.xMin) / stepX - .5f) + 1, 0, columns);
            int lastZ = Mathf.Clamp(Mathf.FloorToInt((terrain.yMax - area.yMin) / stepZ - .5f) + 1, 0, rows);
            if (lastX > firstX && lastZ > firstZ && terrain.width > 0f && terrain.height > 0f)
            {
                Vector2 start = new Vector2((area.xMin + (firstX + .5f) * stepX - terrain.xMin) / terrain.width,
                    (area.yMin + (firstZ + .5f) * stepZ - terrain.yMin) / terrain.height);
                Vector2 step = new Vector2(stepX / terrain.width, stepZ / terrain.height);
                var covered = new RectInt(firstX, firstZ, lastX - firstX, lastZ - firstZ);
                PaintTerrain(data, covered, start, step, new Vector2(stepX, stepZ), pixels, columns);
            }
            image.SetPixels32(pixels);
            image.Apply(false);
        }

        private void PaintTerrain(TerrainData data, RectInt covered, Vector2 start, Vector2 step, Vector2 worldStep,
            Color32[] pixels, int stride)
        {
            var heights = data.GetInterpolatedHeights(start.x, start.y, covered.width, covered.height, step.x, step.y);
            var alphaScale = new Vector2(data.alphamapWidth - 1, data.alphamapHeight - 1);
            Vector2 paintStart = Vector2.Scale(start, alphaScale);
            Vector2 paintStep = Vector2.Scale(step, alphaScale);
            Vector2 paintEnd = paintStart + Vector2.Scale(paintStep, covered.size - Vector2Int.one);
            int paintX = Mathf.Clamp(Mathf.FloorToInt(paintStart.x), 0, data.alphamapWidth - 1);
            int paintZ = Mathf.Clamp(Mathf.FloorToInt(paintStart.y), 0, data.alphamapHeight - 1);
            int paintWidth = Mathf.Clamp(Mathf.CeilToInt(paintEnd.x), paintX, data.alphamapWidth - 1) - paintX + 1;
            int paintDepth = Mathf.Clamp(Mathf.CeilToInt(paintEnd.y), paintZ, data.alphamapHeight - 1) - paintZ + 1;
            var layers = data.terrainLayers ?? Array.Empty<TerrainLayer>();
            var paint = layers.Length > 0 ? data.GetAlphamaps(paintX, paintZ, paintWidth, paintDepth) : null;
            var colors = Array.ConvertAll(layers, ColorFor);
            for (int z = 0; z < covered.height; z++)
            {
                int south = Mathf.Max(0, z - 1);
                int north = Mathf.Min(covered.height - 1, z + 1);
                float alphaZ = Mathf.Clamp(paintStart.y + z * paintStep.y - paintZ, 0, paintDepth - 1);
                for (int x = 0; x < covered.width; x++)
                {
                    int west = Mathf.Max(0, x - 1);
                    int east = Mathf.Min(covered.width - 1, x + 1);
                    float alphaX = Mathf.Clamp(paintStart.x + x * paintStep.x - paintX, 0, paintWidth - 1);
                    Color color = paint == null ? Unpainted : BlendLayers(paint, colors, alphaX, alphaZ);
                    float slopeX = (heights[z, east] - heights[z, west]) / worldStep.x;
                    float slopeZ = (heights[north, x] - heights[south, x]) / worldStep.y;
                    color *= 1f + Mathf.Clamp((slopeZ - slopeX) * .12f, -.19f, .12f);
                    color.a = 1f;
                    pixels[(covered.y + z) * stride + covered.x + x] = color;
                }
            }
        }

        private Color ColorFor(TerrainLayer layer)
        {
            foreach (var entry in layerColors)
                if (entry.layer == layer)
                    return entry.color;
            return Unpainted;
        }

        private static Color BlendLayers(float[,,] paint, Color[] colors, float x, float z)
        {
            int x0 = (int)x;
            int z0 = (int)z;
            int x1 = Mathf.Min(x0 + 1, paint.GetLength(1) - 1);
            int z1 = Mathf.Min(z0 + 1, paint.GetLength(0) - 1);
            Color blend = Color.clear;
            float total = 0f;
            for (int layer = 0; layer < colors.Length; layer++)
            {
                float south = Mathf.Lerp(paint[z0, x0, layer], paint[z0, x1, layer], x - x0);
                float north = Mathf.Lerp(paint[z1, x0, layer], paint[z1, x1, layer], x - x0);
                float weight = Mathf.Lerp(south, north, z - z0);
                blend += colors[layer] * weight;
                total += weight;
            }
            return total > 0f ? blend / total : Unpainted;
        }

        internal void Invalidate() => dirty = true;

        private void OnEnable()
        {
            Invalidate();
            TerrainCallbacks.heightmapChanged += HeightChanged;
            TerrainCallbacks.textureChanged += PaintChanged;
        }

        private void OnDisable()
        {
            TerrainCallbacks.heightmapChanged -= HeightChanged;
            TerrainCallbacks.textureChanged -= PaintChanged;
        }

        private void HeightChanged(Terrain terrain, RectInt region, bool synced)
        {
            if (terrain != null && terrain.terrainData == source)
                Invalidate();
        }

        private void PaintChanged(Terrain terrain, string texture, RectInt region, bool synced) =>
            HeightChanged(terrain, region, synced);

        private void OnValidate() => Invalidate();

        private void OnDestroy()
        {
            if (image != null)
                Destroy(image);
        }

        private void OnDrawGizmos()
        {
            Rect area = WorldBounds;
            Color previous = Gizmos.color;
            Gizmos.color = new Color(1f, .71f, .2f);
            Gizmos.DrawWireCube(new Vector3(area.center.x, transform.position.y, area.center.y),
                new Vector3(area.width, 0f, area.height));
            Gizmos.color = previous;
        }
    }
}
