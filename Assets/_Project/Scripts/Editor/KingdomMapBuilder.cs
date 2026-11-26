using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

/// <summary>
/// Builds the kingdom map's land from the Azgaar map (Assets/_Project/Data/Map/Kingdom Map.json, written by
/// Tools/export_kingdom_map.py): Unity Terrain tiles stacked north to south, with heights from Azgaar's cells, lakes and
/// rivers cut in and covered by water meshes, ground layers painted by biome, slope and height, and trees, rocks and
/// reeds scattered as terrain trees. North is +Z, so the top of the drawn map is the far end of the strip.
///
/// Tools > Map > Build Kingdom Terrain rebuilds everything under the "Kingdom Map" object in the open scene from
/// scratch, so hand sculpting or painting on these terrains is lost on the next build. Tune here, then sculpt by hand
/// once the layout is settled.
/// </summary>
[CreateAssetMenu(menuName = "Game/Kingdom Map Builder", fileName = "Kingdom Map")]
public class KingdomMapBuilder : ScriptableObject
{
    public enum Cover { Broadleaf, Conifer, Wetland, Open, Rocky, Waterside, Orchard }

    [Serializable]
    public class Scatter
    {
        public string name;
        public List<GameObject> prefabs = new List<GameObject>();
        [Tooltip("Which ground this grows on. Its density is scaled by how much of that ground is at each spot.")]
        public Cover cover;
        [Tooltip("Plants per 100 square metres where the cover is full.")]
        [Min(0f)] public float density = 1f;
        [Tooltip("Smallest gap between two of these, in metres.")]
        [Min(0.3f)] public float spacing = 4f;
        [Tooltip("Random scale, min and max.")]
        public Vector2 scale = new Vector2(0.8f, 1.2f);
        [Tooltip("Steepest ground it grows on, in degrees.")]
        [Range(0f, 90f)] public float maxSlope = 30f;
        [Tooltip("Lowest and highest ground it grows on, in metres above sea level.")]
        public Vector2 heightRange = new Vector2(0.4f, 200f);
        [Tooltip("On: also grows in shallow water (reeds, lily pads).")]
        public bool inShallows;
        [Tooltip("On: also above the snow line.")]
        public bool onSnow;
    }

    [Header("Source")]
    [Tooltip("Kingdom Map.json, written by Tools/export_kingdom_map.py from Azgaar's full JSON export.")]
    public TextAsset mapData;
    [Tooltip("Where the terrain data and water meshes are saved.")]
    public string outputFolder = "Assets/_Project/Terrain/Kingdom";

    [Header("Size")]
    [Tooltip("West to east, in metres. North to south follows the map's proportions (640 by 2048 pixels).")]
    [Min(32f)] public float width = 256f;
    [Tooltip("Terrain tiles from south to north.")]
    [Range(1, 8)] public int tiles = 4;
    [Tooltip("Heightmap samples along each tile's side.")]
    public int heightmapResolution = 513;
    [Tooltip("Ground paint samples along each tile's side.")]
    public int alphamapResolution = 512;
    [Tooltip("The lowest point of the land, under the sea, in metres.")]
    public float seaFloor = -14f;
    [Tooltip("From the sea floor to the highest possible peak, in metres.")]
    public float terrainHeight = 115f;

    [Header("Height")]
    [Tooltip("Azgaar height (0 to 100, sea level 20) to metres above sea level.")]
    public AnimationCurve height = new AnimationCurve(
        new Keyframe(0, -14), new Keyframe(15, -6), new Keyframe(19, -1.2f), new Keyframe(20, 0.8f),
        new Keyframe(22, 1.6f), new Keyframe(25, 2.6f), new Keyframe(35, 5f), new Keyframe(45, 9f),
        new Keyframe(55, 16f), new Keyframe(65, 27f), new Keyframe(80, 50f), new Keyframe(90, 66f), new Keyframe(100, 84f));
    [Tooltip("Blurs Azgaar's cells into slopes, in metres.")]
    [Min(0f)] public float smoothing = 5f;
    [Tooltip("Small bumps on all land, in metres.")]
    [Min(0f)] public float detail = 0.6f;
    [Tooltip("Extra roughness on high ground, in metres, reached at 30 m up.")]
    [Min(0f)] public float mountainDetail = 9f;
    [Tooltip("Size of the bumps, in metres.")]
    [Min(1f)] public float detailScale = 22f;
    [Tooltip("Size of the ridges on high ground, in metres.")]
    [Min(1f)] public float ridgeScale = 40f;
    [Tooltip("Where the mountains begin, as a share of the strip from its south end. South of this, high ground is squashed.")]
    [Range(0f, 1f)] public float mountainLine = 0.74f;
    [Tooltip("South of the mountain line, ground above this many metres rises only by Lowland Squash.")]
    public float lowlandCap = 9f;
    [Range(0f, 1f)] public float lowlandSquash = 0.35f;
    public int seed = 2026;

    [Header("Water")]
    public Material water;
    [Tooltip("A dark plane under the sea, so deep water reads as deep instead of showing the sky through it.")]
    public Material seaFloorMaterial;
    [Tooltip("How far lake beds sit below the lake's surface, in metres.")]
    [Min(0.2f)] public float lakeDepth = 2.5f;
    [Tooltip("Land this close to a lake is kept just above its water, in metres.")]
    [Min(0f)] public float lakeBank = 2f;
    [Tooltip("Rounds off lake outlines, in metres.")]
    [Min(0.5f)] public float lakeSmoothing = 5f;
    [Tooltip("Rivers with fewer Azgaar cells than this are left out.")]
    [Min(2)] public int riverMinCells = 10;
    [Tooltip("River width in metres: at the source (at least) and the most it gets.")]
    public Vector2 riverWidth = new Vector2(2.5f, 10f);
    [Tooltip("Metres of width per unit of Azgaar's river width (about 0 at a source, 1.4 for the Greywater's mouth).")]
    [Min(0f)] public float riverWidthScale = 5f;
    [Tooltip("River bed depth below the water, in metres.")]
    [Min(0.2f)] public float riverDepth = 1.4f;
    [Tooltip("How far the banks slope out from the water's edge, in metres.")]
    [Min(0.5f)] public float riverBank = 4f;

    [Header("Ground layers")]
    public TerrainLayer grass;
    [Tooltip("Drier grass, in broad patches across open land.")]
    public TerrainLayer grassDry;
    [Tooltip("Grass with flowers, in smaller patches across open land.")]
    public TerrainLayer grassFlowers;
    [Tooltip("Size of the dry and flowery patches, in metres.")]
    [Min(5f)] public float grassPatchScale = 60f;
    public TerrainLayer forestFloor;
    public TerrainLayer pineFloor;
    public TerrainLayer swampGrass;
    public TerrainLayer mud;
    public TerrainLayer shore;
    public TerrainLayer rock;
    public TerrainLayer snow;
    [Tooltip("Slopes from the first angle start to show rock; full rock by the second.")]
    public Vector2 rockSlope = new Vector2(26f, 40f);
    [Tooltip("Snow line at the north edge, in metres.")]
    public float snowLineNorth = 20f;
    [Tooltip("How much higher the snow line is at the south edge, in metres.")]
    public float snowLineRise = 140f;
    [Tooltip("Metres over which snow fades in above the snow line.")]
    [Min(0.5f)] public float snowBlend = 6f;
    [Tooltip("Sand and gravel up to this height near the sea, in metres.")]
    public float beachHeight = 0.9f;
    [Tooltip("How far inland a beach can reach from the water's edge, in metres.")]
    [Min(0.5f)] public float beachWidth = 6f;

    [Header("Plants and rocks")]
    public List<Scatter> scatter = new List<Scatter>();
    [Tooltip("Nothing grows within this many metres of these towns, to leave room to build them.")]
    public List<string> clearTowns = new List<string>
        { "Stonewatch", "Millbrook", "Kingsford", "Yewdale", "Highfield", "Reedmere", "Kingsmere", "Harrowby", "Saltmouth" };
    [Min(0f)] public float townClearing = 18f;
    [Tooltip("Towns with orchards round them (the Orchard cover).")]
    public List<string> orchardTowns = new List<string> { "Harrowby", "Millbrook" };
    [Min(1f)] public float orchardRadius = 45f;

    const string SettingsPath = "Assets/_Project/Settings/Kingdom Map.asset";

    [MenuItem("Tools/Map/Build Kingdom Terrain")]
    static void BuildFromMenu()
    {
        var settings = AssetDatabase.LoadAssetAtPath<KingdomMapBuilder>(SettingsPath);
        if (settings == null)
        {
            Debug.LogError("No kingdom map settings at " + SettingsPath);
            return;
        }
        Debug.Log(settings.Build());
    }

    // ---------------------------------------------------------------- data

    [Serializable] class CellData { public float[] x, y; public float[] h; public int[] biome, water, lake; }
    [Serializable] class LakeData { public string name; public float level; }
    [Serializable] class RiverData { public string name, type; public float width, sourceWidth, discharge, length; public float[] points; }
    [Serializable] class TownData { public string name; public float x, y; public bool capital, port; }
    [Serializable] class MapData { public int width, height; public float seaLevel; public CellData cells; public LakeData[] lakes; public RiverData[] rivers; public TownData[] towns; }

    MapData map;
    int nx, nz;            // heightmap samples across the whole strip
    float sx, sz, step;    // metres between samples: west to east, south to north, and their mean
    float length;          // north to south, metres
    float[] heights;       // metres above sea level
    byte[] biome, waterKind;
    short[] lakeOf;
    float[] riverDistance, riverSurface, riverHalf;
    float[] slope;         // degrees
    float[][] cover;       // per Cover, 0..1, blurred
    float[] coast;         // nearness of the sea, 0..1

    /// <summary>Rebuilds the terrain tiles, water and plants. Returns a summary.</summary>
    public string Build()
    {
        if (mapData == null)
            return "The kingdom map settings have no map data.";
        map = JsonUtility.FromJson<MapData>(mapData.text);
        length = width * map.height / map.width;
        nx = heightmapResolution;
        nz = tiles * (heightmapResolution - 1) + 1;
        sx = width / (nx - 1); sz = length / (nz - 1); step = (sx + sz) * 0.5f;
        if (Mathf.Abs(sz - sx) > sx * 0.3f)
            Debug.LogWarning($"Kingdom map: samples are {sx:F2} m across but {sz:F2} m north to south. " +
                             "Change the tile count to keep them closer to square.");

        var timer = System.Diagnostics.Stopwatch.StartNew();
        SampleCells();
        ShapeHeights();
        var rivers = CarveRivers();
        ComputeSlope();
        ComputeCover();
        var root = MakeRoot();
        var terrains = MakeTerrains(root);
        int planted = Plant(terrains);
        int waterParts = MakeWater(root, rivers);
        EditorSceneManager.MarkSceneDirty(root.scene);
        return $"Kingdom map built in {timer.ElapsedMilliseconds / 1000f:F1} s: {tiles} tiles, {width:F0} by {length:F0} m, " +
               $"{rivers.Count} rivers, {waterParts} water meshes, {planted} plants and rocks.";
    }

    // Map pixels (y down) for a world position (z north).
    Vector2 ToMap(float x, float z) => new Vector2(x / width * map.width, (1f - z / length) * map.height);
    Vector2 ToWorld(float px, float py) => new Vector2(px / map.width * width, (1f - py / map.height) * length);

    // ---------------------------------------------------------------- cells to samples

    void SampleCells()
    {
        var c = map.cells;
        int count = c.x.Length;
        const float bucket = 8f;
        int bw = Mathf.CeilToInt(map.width / bucket) + 1, bh = Mathf.CeilToInt(map.height / bucket) + 1;
        var buckets = new List<int>[bw * bh];
        for (int i = 0; i < count; i++)
        {
            int bx = Mathf.Clamp((int)(c.x[i] / bucket), 0, bw - 1), by = Mathf.Clamp((int)(c.y[i] / bucket), 0, bh - 1);
            (buckets[by * bw + bx] ??= new List<int>()).Add(i);
        }
        int n = nx * nz;
        var azgaar = new float[n];
        biome = new byte[n];
        waterKind = new byte[n];
        lakeOf = new short[n];
        for (int iz = 0; iz < nz; iz++)
        for (int ix = 0; ix < nx; ix++)
        {
            var p = ToMap(ix * sx, iz * sz);
            int bx = Mathf.Clamp((int)(p.x / bucket), 0, bw - 1), by = Mathf.Clamp((int)(p.y / bucket), 0, bh - 1);
            int best = -1;
            float bestD = float.MaxValue;
            for (int ring = 1; best < 0 || ring <= 3; ring++)
            {
                for (int y = by - ring; y <= by + ring; y++)
                for (int x = bx - ring; x <= bx + ring; x++)
                {
                    if (x < 0 || y < 0 || x >= bw || y >= bh || buckets[y * bw + x] == null)
                        continue;
                    foreach (int i in buckets[y * bw + x])
                    {
                        float dx = c.x[i] - p.x, dy = c.y[i] - p.y, d = dx * dx + dy * dy;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                }
                if (ring > 12)
                    break;
            }
            int s = iz * nx + ix;
            if (best < 0 || bestD > 40f * 40f)
            {
                // Azgaar leaves open sea out of its cell list: deep water
                azgaar[s] = 5f;
                waterKind[s] = 1;
                lakeOf[s] = -1;
                continue;
            }
            azgaar[s] = c.h[best];
            biome[s] = (byte)c.biome[best];
            waterKind[s] = (byte)c.water[best];
            lakeOf[s] = (short)c.lake[best];
        }
        SmoothLakes();
        heights = new float[n];
        for (int s = 0; s < n; s++)
        {
            float m = height.Evaluate(azgaar[s]);
            if (lakeOf[s] >= 0)
                m = Mathf.Min(m, LakeSurface(lakeOf[s]) - lakeDepth);
            heights[s] = m;
        }
    }

    float LakeSurface(int lake) => height.Evaluate(map.lakes[lake].level);

    /// <summary>Rounds off the lakes' outlines, which otherwise follow Azgaar's cell edges in straight steps.</summary>
    void SmoothLakes()
    {
        int n = nx * nz, r = Mathf.Max(1, Mathf.RoundToInt(lakeSmoothing / step));
        var soft = new float[n];
        for (int s = 0; s < n; s++)
            soft[s] = lakeOf[s] >= 0 ? 1f : 0f;
        Blur(soft, nx, nz, r, 2);
        var smoothed = new short[n];
        for (int s = 0; s < n; s++)
        {
            smoothed[s] = -1;
            if (soft[s] < 0.5f)
                continue;
            if (lakeOf[s] >= 0) { smoothed[s] = lakeOf[s]; continue; }
            // newly inside a lake: take the nearest lake's id
            int ix = s % nx, iz = s / nx, best = -1;
            float bestD = float.MaxValue;
            for (int dz = -r * 2; dz <= r * 2; dz++)
            for (int dx = -r * 2; dx <= r * 2; dx++)
            {
                int x = ix + dx, z = iz + dz;
                if (x < 0 || z < 0 || x >= nx || z >= nz || lakeOf[z * nx + x] < 0)
                    continue;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = lakeOf[z * nx + x]; }
            }
            smoothed[s] = (short)best;
        }
        lakeOf = smoothed;
        for (int s = 0; s < n; s++)
            if (lakeOf[s] >= 0)
                waterKind[s] = 2;
            else if (waterKind[s] == 2)
                waterKind[s] = 0;
    }

    // ---------------------------------------------------------------- heights

    void ShapeHeights()
    {
        int radius = Mathf.Max(1, Mathf.RoundToInt(smoothing / step));
        Blur(heights, nx, nz, radius, 3);
        // hills south of the mountains stay hills: Azgaar's lone peaks would otherwise stand as tall as the range
        for (int iz = 0; iz < nz; iz++)
        {
            float lowland = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mountainLine - 0.06f, mountainLine + 0.02f, (float)iz / (nz - 1)));
            if (lowland <= 0f)
                continue;
            for (int ix = 0; ix < nx; ix++)
            {
                int s = iz * nx + ix;
                float h = heights[s];
                if (h > lowlandCap)
                    heights[s] = Mathf.Lerp(h, lowlandCap + (h - lowlandCap) * lowlandSquash, lowland);
            }
        }
        var rng = new Random(seed);
        float ox = (float)rng.NextDouble() * 1000f, oz = (float)rng.NextDouble() * 1000f;
        for (int iz = 0; iz < nz; iz++)
        for (int ix = 0; ix < nx; ix++)
        {
            int s = iz * nx + ix;
            float h = heights[s];
            if (lakeOf[s] >= 0)
            {
                heights[s] = Mathf.Min(h, LakeSurface(lakeOf[s]) - 0.5f);
                continue;
            }
            float x = ix * sx / detailScale + ox, z = iz * sz / detailScale + oz;
            float noise = 0f, amp = 1f, freq = 1f;
            for (int o = 0; o < 4; o++)
            {
                noise += (Mathf.PerlinNoise(x * freq, z * freq) - 0.5f) * amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            // ridged noise on high ground: sharp crests and gullies instead of round lumps
            float ridges = 0f;
            amp = 1f; freq = 1f / ridgeScale * detailScale;
            for (int o = 0; o < 4; o++)
            {
                float r = 1f - Mathf.Abs(Mathf.PerlinNoise(x * freq + 37.1f, z * freq - 12.7f) * 2f - 1f);
                ridges += (r * r - 0.45f) * amp;
                amp *= 0.5f;
                freq *= 2.1f;
            }
            float high = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 30f, h));
            float strength = detail * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 3f, h));
            heights[s] = h + noise * 2f * strength + ridges * mountainDetail * high;
        }
        // a low bank round every lake, so its water stays inside it instead of spilling over flat ground beside it
        int ring = Mathf.Max(1, Mathf.RoundToInt(lakeBank / step));
        var raised = (float[])heights.Clone();
        for (int iz = 0; iz < nz; iz++)
        for (int ix = 0; ix < nx; ix++)
        {
            int lake = lakeOf[iz * nx + ix];
            if (lake < 0)
                continue;
            float top = LakeSurface(lake) + 0.25f;
            for (int dz = -ring; dz <= ring; dz++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                int x = ix + dx, z = iz + dz;
                if (x < 0 || z < 0 || x >= nx || z >= nz || lakeOf[z * nx + x] >= 0)
                    continue;
                float fall = Mathf.Sqrt(dx * dx + dz * dz) / (ring + 1);
                int t = z * nx + x;
                raised[t] = Mathf.Max(raised[t], top - fall * 0.2f);
            }
        }
        heights = raised;
    }

    /// <summary>Box blur, repeated: close to a Gaussian.</summary>
    static void Blur(float[] values, int w, int h, int radius, int passes)
    {
        var temp = new float[values.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int y = 0; y < h; y++)
                BlurLine(values, temp, y * w, 1, w, radius);
            for (int x = 0; x < w; x++)
                BlurLine(temp, values, x, w, h, radius);
        }
    }

    static void BlurLine(float[] src, float[] dst, int start, int stride, int count, int radius)
    {
        float sum = 0f;
        for (int i = -radius; i <= radius; i++)
            sum += src[start + Mathf.Clamp(i, 0, count - 1) * stride];
        float inv = 1f / (radius * 2 + 1);
        for (int i = 0; i < count; i++)
        {
            dst[start + i * stride] = sum * inv;
            sum += src[start + Mathf.Min(i + radius + 1, count - 1) * stride] - src[start + Mathf.Max(i - radius, 0) * stride];
        }
    }

    float Height(float x, float z) => Bilinear(heights, x, z);

    float Bilinear(float[] grid, float x, float z)
    {
        float fx = Mathf.Clamp(x / sx, 0, nx - 1.001f), fz = Mathf.Clamp(z / sz, 0, nz - 1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;
        int s = iz * nx + ix;
        return Mathf.Lerp(Mathf.Lerp(grid[s], grid[s + 1], tx), Mathf.Lerp(grid[s + nx], grid[s + nx + 1], tx), tz);
    }

    // ---------------------------------------------------------------- rivers

    class River
    {
        public string name;
        public List<Vector2> points = new List<Vector2>();
        public List<float> half = new List<float>(), surface = new List<float>();
    }

    List<River> CarveRivers()
    {
        var rivers = new List<River>();
        foreach (var data in map.rivers)
        {
            if (data.points.Length / 2 < riverMinCells)
                continue;
            var raw = new List<Vector2>();
            for (int i = 0; i + 1 < data.points.Length; i += 2)
                raw.Add(ToWorld(data.points[i], data.points[i + 1]));
            for (int k = 0; k < 3; k++)
                raw = Chaikin(raw);
            var river = new River { name = data.name };
            float total = 0f;
            for (int i = 1; i < raw.Count; i++)
                total += Vector2.Distance(raw[i - 1], raw[i]);
            // resample at about a metre and a half
            float along = 0f, next = 0f;
            for (int i = 0; i < raw.Count; i++)
            {
                if (i > 0)
                    along += Vector2.Distance(raw[i - 1], raw[i]);
                if (along + 1e-3f < next && i != raw.Count - 1)
                    continue;
                next = along + 1.5f;
                float t = total > 0 ? along / total : 0f;
                float w = Mathf.Lerp(data.sourceWidth, data.width, Mathf.Sqrt(t)) * riverWidthScale;
                river.points.Add(raw[i]);
                river.half.Add(Mathf.Clamp(w, riverWidth.x, riverWidth.y) * 0.5f);
            }
            if (river.points.Count < 3)
                continue;
            // the water only ever runs downhill
            float level = float.MaxValue;
            foreach (var p in river.points)
            {
                level = Mathf.Min(level, Height(p.x, p.y) - 0.35f);
                river.surface.Add(Mathf.Max(level, 0.05f));
            }
            rivers.Add(river);
        }

        int n = nx * nz;
        riverDistance = new float[n];
        riverSurface = new float[n];
        riverHalf = new float[n];
        for (int i = 0; i < n; i++)
            riverDistance[i] = float.MaxValue;
        foreach (var river in rivers)
            for (int i = 1; i < river.points.Count; i++)
                StampSegment(river, i - 1, i);

        for (int s = 0; s < n; s++)
        {
            float d = riverDistance[s];
            if (d == float.MaxValue)
                continue;
            float half = riverHalf[s], bed = riverSurface[s] - riverDepth;
            if (d > half + riverBank)
                continue;
            // flat bed in the middle, then up the bank to the land
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(half * 0.6f, half + riverBank, d));
            float bank = Mathf.Lerp(bed, riverSurface[s] + 0.6f, Mathf.InverseLerp(half * 0.6f, half + riverBank, d));
            float target = Mathf.Lerp(bed, Mathf.Max(bank, heights[s]), t);
            heights[s] = Mathf.Min(heights[s], target);
        }
        return rivers;
    }

    void StampSegment(River river, int a, int b)
    {
        Vector2 pa = river.points[a], pb = river.points[b];
        float reach = Mathf.Max(river.half[a], river.half[b]) + riverBank;
        int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(pa.x, pb.x) - reach) / sx));
        int x1 = Mathf.Min(nx - 1, Mathf.CeilToInt((Mathf.Max(pa.x, pb.x) + reach) / sx));
        int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(pa.y, pb.y) - reach) / sz));
        int z1 = Mathf.Min(nz - 1, Mathf.CeilToInt((Mathf.Max(pa.y, pb.y) + reach) / sz));
        var ab = pb - pa;
        float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
        for (int iz = z0; iz <= z1; iz++)
        for (int ix = x0; ix <= x1; ix++)
        {
            var p = new Vector2(ix * sx, iz * sz);
            float t = Mathf.Clamp01(Vector2.Dot(p - pa, ab) / len2);
            float d = Vector2.Distance(p, pa + ab * t);
            int s = iz * nx + ix;
            if (d >= riverDistance[s])
                continue;
            riverDistance[s] = d;
            riverSurface[s] = Mathf.Lerp(river.surface[a], river.surface[b], t);
            riverHalf[s] = Mathf.Lerp(river.half[a], river.half[b], t);
        }
    }

    static List<Vector2> Chaikin(List<Vector2> points)
    {
        if (points.Count < 3)
            return points;
        var result = new List<Vector2> { points[0] };
        for (int i = 0; i < points.Count - 1; i++)
        {
            result.Add(Vector2.Lerp(points[i], points[i + 1], 0.25f));
            result.Add(Vector2.Lerp(points[i], points[i + 1], 0.75f));
        }
        result.Add(points[points.Count - 1]);
        return result;
    }

    // ---------------------------------------------------------------- slope and cover

    void ComputeSlope()
    {
        slope = new float[nx * nz];
        for (int iz = 0; iz < nz; iz++)
        for (int ix = 0; ix < nx; ix++)
        {
            int l = Mathf.Max(ix - 1, 0), r = Mathf.Min(ix + 1, nx - 1), d = Mathf.Max(iz - 1, 0), u = Mathf.Min(iz + 1, nz - 1);
            float gx = (heights[iz * nx + r] - heights[iz * nx + l]) / ((r - l) * sx);
            float gz = (heights[u * nx + ix] - heights[d * nx + ix]) / ((u - d) * sz);
            slope[iz * nx + ix] = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }
    }

    static Cover? CoverOfBiome(int b)
    {
        switch (b)
        {
            case 5: case 6: case 7: case 8: return Cover.Broadleaf;
            case 9: return Cover.Conifer;
            case 12: return Cover.Wetland;
            case 1: case 2: case 3: case 4: case 10: return Cover.Open;
            default: return null; // sea and glacier
        }
    }

    void ComputeCover()
    {
        int n = nx * nz, kinds = Enum.GetValues(typeof(Cover)).Length;
        cover = new float[kinds][];
        for (int k = 0; k < kinds; k++)
            cover[k] = new float[n];
        for (int s = 0; s < n; s++)
        {
            var c = CoverOfBiome(biome[s]);
            if (c.HasValue)
                cover[(int)c.Value][s] = 1f;
            if (biome[s] == 11)
                cover[(int)Cover.Conifer][s] = 0.35f; // the glacier biome's lower slopes: thin pine woods
            float h = heights[s];
            if (h > 0.2f && (riverDistance[s] < riverHalf[s] + 2.5f || NearLake(s)))
                cover[(int)Cover.Waterside][s] = 1f;
            cover[(int)Cover.Rocky][s] = Mathf.InverseLerp(rockSlope.x - 8f, rockSlope.y, slope[s]);
        }
        int radius = Mathf.Max(1, Mathf.RoundToInt(smoothing * 1.4f / step));
        foreach (Cover k in new[] { Cover.Broadleaf, Cover.Conifer, Cover.Wetland, Cover.Open })
            Blur(cover[(int)k], nx, nz, radius, 2);
        Blur(cover[(int)Cover.Waterside], nx, nz, Mathf.Max(1, Mathf.RoundToInt(1.5f / step)), 1);
        // how close the sea is: beaches only form along it, not on every low field
        coast = new float[n];
        for (int s = 0; s < n; s++)
            coast[s] = waterKind[s] == 1 || heights[s] < 0f ? 1f : 0f;
        Blur(coast, nx, nz, Mathf.Max(1, Mathf.RoundToInt(beachWidth / step)), 2);
        // orchards: the land around the farm towns
        foreach (var town in map.towns)
            if (orchardTowns.Contains(town.name))
                Paint(cover[(int)Cover.Orchard], ToWorld(town.x, town.y), orchardRadius, 1f);
    }

    bool NearLake(int s)
    {
        int ix = s % nx, iz = s / nx, r = Mathf.CeilToInt(2f / step);
        for (int z = Mathf.Max(0, iz - r); z <= Mathf.Min(nz - 1, iz + r); z++)
        for (int x = Mathf.Max(0, ix - r); x <= Mathf.Min(nx - 1, ix + r); x++)
            if (lakeOf[z * nx + x] >= 0)
                return true;
        return false;
    }

    void Paint(float[] grid, Vector2 centre, float radius, float value)
    {
        int x0 = Mathf.Max(0, (int)((centre.x - radius) / sx)), x1 = Mathf.Min(nx - 1, (int)((centre.x + radius) / sx));
        int z0 = Mathf.Max(0, (int)((centre.y - radius) / sz)), z1 = Mathf.Min(nz - 1, (int)((centre.y + radius) / sz));
        for (int iz = z0; iz <= z1; iz++)
        for (int ix = x0; ix <= x1; ix++)
        {
            float d = Vector2.Distance(centre, new Vector2(ix * sx, iz * sz)) / radius;
            if (d < 1f)
                grid[iz * nx + ix] = Mathf.Max(grid[iz * nx + ix], value * Mathf.SmoothStep(1f, 0f, d));
        }
    }

    float SnowLine(float z) => snowLineNorth + snowLineRise * (1f - z / length);

    // ---------------------------------------------------------------- terrains

    GameObject MakeRoot()
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var existing in scene.GetRootGameObjects())
            if (existing.name == "Kingdom Map")
                DestroyImmediate(existing);
        var root = new GameObject("Kingdom Map");
        return root;
    }

    Terrain[] MakeTerrains(GameObject root)
    {
        if (!AssetDatabase.IsValidFolder(outputFolder))
            Directory.CreateDirectory(outputFolder);
        var layers = new List<TerrainLayer> { grass, grassDry, grassFlowers, forestFloor, pineFloor, swampGrass, mud, shore, rock, snow };
        float tileLength = length / tiles;
        var land = new GameObject("Land");
        land.transform.SetParent(root.transform, false);
        var terrains = new Terrain[tiles];
        int res = heightmapResolution, ares = alphamapResolution;
        for (int t = 0; t < tiles; t++)
        {
            string path = $"{outputFolder}/Kingdom Land {t + 1}.asset";
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, path);
            }
            data.heightmapResolution = res;
            data.alphamapResolution = ares;
            data.baseMapResolution = 512;
            data.size = new Vector3(width, terrainHeight, tileLength);
            var h = new float[res, res];
            int offset = t * (res - 1);
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                h[z, x] = Mathf.Clamp01((heights[(offset + z) * nx + x] - seaFloor) / terrainHeight);
            data.SetHeights(0, 0, h);
            // each tile keeps only the layers it uses (at most 8, two passes), most used first
            var full = Splat(t, ares, layers.Count);
            var use = new List<int>();
            var totals = new float[layers.Count];
            for (int z = 0; z < ares; z++)
            for (int x = 0; x < ares; x++)
            for (int k = 0; k < layers.Count; k++)
                totals[k] += full[z, x, k];
            for (int k = 0; k < layers.Count; k++)
                if (layers[k] != null && totals[k] > ares * ares * 0.0005f)
                    use.Add(k);
            use.Sort((a, b) => totals[b].CompareTo(totals[a]));
            if (use.Count > 8)
                use.RemoveRange(8, use.Count - 8);
            var maps = new float[ares, ares, use.Count];
            for (int z = 0; z < ares; z++)
            for (int x = 0; x < ares; x++)
            {
                float sum = 0f;
                for (int i = 0; i < use.Count; i++)
                    sum += maps[z, x, i] = full[z, x, use[i]];
                for (int i = 0; i < use.Count; i++)
                    maps[z, x, i] = sum > 0f ? maps[z, x, i] / sum : (i == 0 ? 1f : 0f);
            }
            data.terrainLayers = use.ConvertAll(k => layers[k]).ToArray();
            data.SetAlphamaps(0, 0, maps);
            data.treeInstances = Array.Empty<TreeInstance>();
            EditorUtility.SetDirty(data);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = $"Land {t + 1}";
            go.transform.SetParent(land.transform, false);
            go.transform.localPosition = new Vector3(0f, seaFloor, t * tileLength);
            var terrain = go.GetComponent<Terrain>();
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultTerrainMaterial != null)
                terrain.materialTemplate = pipeline.defaultTerrainMaterial;
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 400f;
            terrain.treeDistance = 900f;
            terrain.treeBillboardDistance = 900f;
            terrain.treeMaximumFullLODCount = 2000;
            terrain.groupingID = 7;
            terrain.allowAutoConnect = true;
            terrains[t] = terrain;
        }
        for (int t = 0; t < tiles; t++)
            terrains[t].SetNeighbors(null, t + 1 < tiles ? terrains[t + 1] : null, null, t > 0 ? terrains[t - 1] : null);
        AssetDatabase.SaveAssets();
        return terrains;
    }

    float[,,] Splat(int tile, int ares, int count)
    {
        var maps = new float[ares, ares, count];
        float tileLength = length / tiles;
        var w = new float[count];
        for (int az = 0; az < ares; az++)
        for (int ax = 0; ax < ares; ax++)
        {
            float x = (ax + 0.5f) / ares * width, z = tile * tileLength + (az + 0.5f) / ares * tileLength;
            float fx = Mathf.Clamp(x / sx, 0, nx - 1), fz = Mathf.Clamp(z / sz, 0, nz - 1);
            int s = Mathf.RoundToInt(fz) * nx + Mathf.RoundToInt(fx);
            float h = Bilinear(heights, x, z);
            float open = Bilinear(cover[(int)Cover.Open], x, z), broad = Bilinear(cover[(int)Cover.Broadleaf], x, z);
            float pine = Bilinear(cover[(int)Cover.Conifer], x, z), wet = Bilinear(cover[(int)Cover.Wetland], x, z);
            Array.Clear(w, 0, count);
            float openLand = open + 0.05f;
            float dry = Mathf.SmoothStep(0f, 0.85f, Mathf.InverseLerp(0.5f, 0.72f, Mathf.PerlinNoise(x / grassPatchScale + 51.3f, z / grassPatchScale + 7.7f)));
            float flowers = Mathf.SmoothStep(0f, 0.7f, Mathf.InverseLerp(0.58f, 0.78f, Mathf.PerlinNoise(x / (grassPatchScale * 0.45f) - 13.1f, z / (grassPatchScale * 0.45f) + 91.4f)));
            flowers *= 1f - dry;
            w[0] = openLand * (1f - dry - flowers);
            w[1] = openLand * dry;
            w[2] = openLand * flowers;
            w[3] = broad;
            w[4] = pine + (biome[s] == 11 ? 0.6f : 0f);
            w[5] = wet * 0.65f;
            w[6] = wet * 0.35f;
            Normalise(w);
            // shores: the beach by the sea, the banks of rivers and lakes
            float beach = Mathf.InverseLerp(beachHeight + 0.6f, beachHeight - 0.3f, h)
                          * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.35f, Bilinear(coast, x, z)));
            float riverEdge = riverDistance[s] < float.MaxValue
                ? Mathf.InverseLerp(riverHalf[s] + 0.9f, riverHalf[s] + 0.1f, riverDistance[s]) : 0f;
            float lakeEdge = lakeOf[s] >= 0 ? 1f : 0f;
            Overlay(w, 7, Mathf.Max(beach, Mathf.Max(riverEdge, lakeEdge)));
            float rocky = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rockSlope.x, rockSlope.y, Bilinear(slope, x, z)));
            Overlay(w, 8, rocky);
            float snowy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SnowLine(z), SnowLine(z) + snowBlend, h)) * (1f - rocky * 0.55f);
            Overlay(w, 9, snowy);
            for (int k = 0; k < count; k++)
                maps[az, ax, k] = w[k];
        }
        return maps;
    }

    static void Normalise(float[] w)
    {
        float sum = 0f;
        foreach (float v in w) sum += v;
        if (sum <= 0f) { w[0] = 1f; return; }
        for (int i = 0; i < w.Length; i++) w[i] /= sum;
    }

    static void Overlay(float[] w, int layer, float amount)
    {
        amount = Mathf.Clamp01(amount);
        for (int i = 0; i < w.Length; i++) w[i] *= 1f - amount;
        w[layer] += amount;
    }

    // ---------------------------------------------------------------- plants

    int Plant(Terrain[] terrains)
    {
        var prototypes = new List<TreePrototype>();
        var protoIndex = new Dictionary<GameObject, int>();
        foreach (var set in scatter)
            foreach (var prefab in set.prefabs)
                if (prefab != null && !protoIndex.ContainsKey(prefab))
                {
                    protoIndex[prefab] = prototypes.Count;
                    prototypes.Add(new TreePrototype { prefab = prefab });
                }
        var perTile = new List<TreeInstance>[tiles];
        for (int t = 0; t < tiles; t++)
            perTile[t] = new List<TreeInstance>();

        var clearings = new List<Vector2>();
        foreach (var town in map.towns)
            if (clearTowns.Contains(town.name))
                clearings.Add(ToWorld(town.x, town.y));

        float tileLength = length / tiles;
        var rng = new Random(seed + 1);
        int total = 0;
        foreach (var set in scatter)
        {
            var prefabs = set.prefabs.FindAll(p => p != null);
            if (prefabs.Count == 0 || set.density <= 0f)
                continue;
            float cell = set.spacing;
            float chance = set.density * cell * cell / 100f;
            for (float z = 0; z < length; z += cell)
            for (float x = 0; x < width; x += cell)
            {
                float px = x + (float)rng.NextDouble() * cell, pz = z + (float)rng.NextDouble() * cell;
                double roll = rng.NextDouble();
                int pick = rng.Next(prefabs.Count);
                float scale = Mathf.Lerp(set.scale.x, set.scale.y, (float)rng.NextDouble());
                float turn = (float)(rng.NextDouble() * Math.PI * 2);
                if (px >= width || pz >= length)
                    continue;
                float amount = Mathf.Clamp01(Bilinear(cover[(int)set.cover], px, pz));
                if (roll >= chance * amount)
                    continue;
                float h = Height(px, pz);
                int s = Mathf.RoundToInt(pz / sz) * nx + Mathf.RoundToInt(px / sx);
                bool wet = lakeOf[s] >= 0 || riverDistance[s] < riverHalf[s] + 0.4f || h < 0.2f;
                if (wet && !set.inShallows)
                    continue;
                if (set.inShallows && wet && (riverDistance[s] < riverHalf[s] * 0.7f || h < riverSurface[s] - 0.9f))
                    continue;
                if (!wet && (h < set.heightRange.x || h > set.heightRange.y))
                    continue;
                if (Bilinear(slope, px, pz) > set.maxSlope)
                    continue;
                if (!set.onSnow && h > SnowLine(pz) + snowBlend * 0.5f)
                    continue;
                bool cleared = false;
                foreach (var c in clearings)
                    if ((c - new Vector2(px, pz)).sqrMagnitude < townClearing * townClearing)
                        cleared = true;
                if (cleared)
                    continue;
                int t = Mathf.Clamp((int)(pz / tileLength), 0, tiles - 1);
                perTile[t].Add(new TreeInstance
                {
                    position = new Vector3(px / width, 0f, (pz - t * tileLength) / tileLength),
                    prototypeIndex = protoIndex[prefabs[pick]],
                    widthScale = scale,
                    heightScale = scale,
                    rotation = turn,
                    color = Color.white,
                    lightmapColor = Color.white,
                });
                total++;
            }
        }
        for (int t = 0; t < tiles; t++)
        {
            var data = terrains[t].terrainData;
            data.treePrototypes = prototypes.ToArray();
            data.RefreshPrototypes();
            data.SetTreeInstances(perTile[t].ToArray(), true);
            EditorUtility.SetDirty(data);
        }
        AssetDatabase.SaveAssets();
        return total;
    }

    // ---------------------------------------------------------------- water

    int MakeWater(GameObject root, List<River> rivers)
    {
        var waterRoot = new GameObject("Water");
        waterRoot.transform.SetParent(root.transform, false);
        string path = $"{outputFolder}/Kingdom Water.asset";
        AssetDatabase.DeleteAsset(path);
        Mesh holder = null;
        int count = 0;

        // the sea: one plane at sea level, well past the edges so the land fades out over water
        // a grid rather than one huge quad: Flat Kit's depth and foam went wrong across triangles kilometres wide
        var sea = new Mesh { name = "Sea", indexFormat = IndexFormat.UInt32 };
        float pad = 800f, cell = 32f;
        int cx = Mathf.CeilToInt((width + pad * 2) / cell), cz = Mathf.CeilToInt((length + pad * 2) / cell);
        var seaVerts = new List<Vector3>();
        var seaTris = new List<int>();
        for (int z = 0; z <= cz; z++)
        for (int x = 0; x <= cx; x++)
            seaVerts.Add(new Vector3(-pad + x * cell, 0f, -pad + z * cell));
        for (int z = 0; z < cz; z++)
        for (int x = 0; x < cx; x++)
        {
            int a = z * (cx + 1) + x;
            seaTris.AddRange(new[] { a, a + cx + 1, a + 1, a + 1, a + cx + 1, a + cx + 2 });
        }
        sea.SetVertices(seaVerts);
        sea.SetTriangles(seaTris, 0);
        sea.RecalculateNormals();
        sea.RecalculateBounds();
        AssetDatabase.CreateAsset(sea, path);
        holder = sea;
        AddWater(waterRoot, null, sea, "Sea");
        count++;
        if (seaFloorMaterial != null)
        {
            var floor = new Mesh { name = "Sea Floor" };
            float y = seaFloor + 0.3f;
            floor.vertices = new[] { new Vector3(-pad, y, -pad), new Vector3(width + pad, y, -pad), new Vector3(-pad, y, length + pad), new Vector3(width + pad, y, length + pad) };
            floor.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            floor.RecalculateNormals();
            floor.RecalculateBounds();
            AddWater(waterRoot, holder, floor, "Sea Floor");
            waterRoot.transform.Find("Sea Floor").GetComponent<MeshRenderer>().sharedMaterial = seaFloorMaterial;
        }

        // lakes: a grid over each lake's samples, at its surface
        var lakeVerts = new List<Vector3>();
        var lakeTris = new List<int>();
        int stride = 2;
        for (int iz = 0; iz + stride < nz; iz += stride)
        for (int ix = 0; ix + stride < nx; ix += stride)
        {
            int lake = -1;
            for (int dz = -1; dz <= stride + 1 && lake < 0; dz++)
            for (int dx = -1; dx <= stride + 1 && lake < 0; dx++)
            {
                int x = Mathf.Clamp(ix + dx, 0, nx - 1), z = Mathf.Clamp(iz + dz, 0, nz - 1);
                lake = lakeOf[z * nx + x];
            }
            if (lake < 0)
                continue;
            float y = LakeSurface(lake);
            if (y < 0.05f)
                y = 0.05f; // a lake at sea level would flicker against the sea
            int b = lakeVerts.Count;
            float x0 = ix * sx, z0 = iz * sz, x1 = (ix + stride) * sx, z1 = (iz + stride) * sz;
            lakeVerts.Add(new Vector3(x0, y, z0)); lakeVerts.Add(new Vector3(x1, y, z0));
            lakeVerts.Add(new Vector3(x0, y, z1)); lakeVerts.Add(new Vector3(x1, y, z1));
            lakeTris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
        }
        if (lakeVerts.Count > 0)
        {
            var mesh = new Mesh { name = "Lakes", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(lakeVerts);
            mesh.SetTriangles(lakeTris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AddWater(waterRoot, holder, mesh, "Lakes");
            count++;
        }

        // rivers: a ribbon along each, a little wider than the bed so its edges sink into the banks
        foreach (var river in rivers)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < river.points.Count; i++)
            {
                var a = river.points[Mathf.Max(i - 1, 0)];
                var b = river.points[Mathf.Min(i + 1, river.points.Count - 1)];
                var dir = (b - a).normalized;
                var side = new Vector2(-dir.y, dir.x) * (river.half[i] * 1.25f + 0.4f);
                var p = river.points[i];
                float y = river.surface[i] - 0.03f;
                verts.Add(new Vector3(p.x - side.x, y, p.y - side.y));
                verts.Add(new Vector3(p.x + side.x, y, p.y + side.y));
                if (i > 0)
                {
                    int v = i * 2;
                    tris.AddRange(new[] { v - 2, v - 1, v, v - 1, v + 1, v }); // clockwise from above
                }
            }
            var mesh = new Mesh { name = "River " + river.name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            // ribbons wound either way: make every normal point up
            var normals = mesh.normals;
            for (int i = 0; i < normals.Length; i++)
                normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            AddWater(waterRoot, holder, mesh, "River " + river.name);
            count++;
        }
        AssetDatabase.SaveAssets();
        return count;
    }

    void AddWater(GameObject parent, Mesh holder, Mesh mesh, string name)
    {
        if (holder != null)
            AssetDatabase.AddObjectToAsset(mesh, holder);
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = water;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }
}
