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

    public enum EdgeStyle { Cut, Diorama, Haze }

    [Header("Edges")]
    [Tooltip("Cut: the land just stops. Diorama: the strip stands as a block with layered earth sides. " +
             "Haze: the land carries on past the sides and the north end, sinking into a sea mist. " +
             "Diorama and Haze are trials the user didn't take to (2026-11-27); Cut is in use.")]
    public EdgeStyle edges = EdgeStyle.Cut;
    [Tooltip("Haze: how far the land carries on past each side, in metres.")]
    [Min(16f)] public float sideMargin = 128f;
    [Tooltip("Haze: over how many metres past a side the land sinks into the sea.")]
    [Min(5f)] public float sideFade = 75f;
    [Tooltip("Haze: the height the land sinks to past the sides, in metres.")]
    public float sideLevel = -3f;
    [Tooltip("Haze: the mist starts this far past an edge and is solid by the second distance, in metres.")]
    public Vector2 hazeDistance = new Vector2(8f, 95f);
    [Tooltip("Haze: the mist's height above sea level, in metres.")]
    public float hazeHeight = 2.5f;
    public Material hazeMaterial;
    [Tooltip("Diorama: the bottom of the block, in metres.")]
    public float dioramaBase = -18f;
    public Material dioramaSideMaterial;
    public Material dioramaTableMaterial;

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
        if (edges == EdgeStyle.Haze)
            MakeHazeEdges(root, terrains);
        else if (edges == EdgeStyle.Diorama)
            MakeDioramaEdges(root);
        EditorSceneManager.MarkSceneDirty(root.scene);
        return $"Kingdom map built in {timer.ElapsedMilliseconds / 1000f:F1} s: {tiles} tiles, {width:F0} by {length:F0} m, " +
               $"{rivers.Count} rivers, {waterParts} water meshes, {planted} plants and rocks, edges: {edges}.";
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
        var layers = Layers();
        float tileLength = length / tiles;
        var land = new GameObject("Land");
        land.transform.SetParent(root.transform, false);
        var terrains = new Terrain[tiles];
        int res = heightmapResolution, ares = alphamapResolution;
        for (int t = 0; t < tiles; t++)
        {
            int offset = t * (res - 1);
            terrains[t] = CreateTile(land.transform, $"Land {t + 1}", $"Kingdom Land {t + 1}", 0f, t * tileLength, width, tileLength,
                res, ares, (x, z) => heights[(offset + z) * nx + x], layers, 4f);
        }
        for (int t = 0; t < tiles; t++)
            terrains[t].SetNeighbors(null, t + 1 < tiles ? terrains[t + 1] : null, null, t > 0 ? terrains[t - 1] : null);
        AssetDatabase.SaveAssets();
        return terrains;
    }

    List<TerrainLayer> Layers() => new List<TerrainLayer> { grass, grassDry, grassFlowers, forestFloor, pineFloor, swampGrass, mud, shore, rock, snow };

    /// <summary>A terrain tile over a rectangle of the world. Heights come from height(column, row) in metres.</summary>
    Terrain CreateTile(Transform parent, string name, string asset, float x0, float z0, float w, float l, int res, int ares,
                       Func<int, int, float> height, List<TerrainLayer> layers, float pixelError)
    {
        string path = $"{outputFolder}/{asset}.asset";
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, path);
        }
        data.heightmapResolution = res;
        data.alphamapResolution = ares;
        data.baseMapResolution = 512;
        data.size = new Vector3(w, terrainHeight, l);
        var h = new float[res, res];
        for (int z = 0; z < res; z++)
        for (int x = 0; x < res; x++)
            h[z, x] = Mathf.Clamp01((height(x, z) - seaFloor) / terrainHeight);
        data.SetHeights(0, 0, h);
        // each tile keeps only the layers it uses (at most 8, two passes), most used first
        var full = Splat(x0, z0, w, l, ares, layers.Count);
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
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x0, seaFloor, z0);
        var terrain = go.GetComponent<Terrain>();
        var pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline != null && pipeline.defaultTerrainMaterial != null)
            terrain.materialTemplate = pipeline.defaultTerrainMaterial;
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = pixelError;
        terrain.basemapDistance = 400f;
        terrain.treeDistance = 900f;
        terrain.treeBillboardDistance = 900f;
        terrain.treeMaximumFullLODCount = 2000;
        terrain.groupingID = 7;
        terrain.allowAutoConnect = true;
        return terrain;
    }

    // Where a point outside the strip takes its land from: mirrored back in across the nearest edge.
    Vector2 Mirror(float x, float z)
    {
        if (x < 0f) x = -x;
        else if (x > width) x = 2f * width - x;
        if (z > length) z = 2f * length - z;
        return new Vector2(Mathf.Clamp(x, 0f, width), Mathf.Clamp(z, 0f, length));
    }

    float SideOutside(float x) => Mathf.Max(0f, Mathf.Max(-x, x - width));

    float SideFadeAt(float x, float z) => sideFade * (0.6f + 0.8f * Mathf.PerlinNoise(z / 90f + 3.1f, x / 90f + 8.4f));

    /// <summary>Height anywhere: the strip itself, or past its sides and north end the land mirrored back in and,
    /// past the sides, sinking into the sea.</summary>
    float WorldHeight(float x, float z)
    {
        var m = Mirror(x, z);
        float h = Height(m.x, m.y);
        float side = SideOutside(x);
        if (side > 0f)
            h = Mathf.Lerp(h, sideLevel, Mathf.SmoothStep(0f, 1f, side / SideFadeAt(x, z)));
        return h;
    }

    float[,,] Splat(float x0, float z0, float tw, float tl, int ares, int count)
    {
        var maps = new float[ares, ares, count];
        var w = new float[count];
        for (int az = 0; az < ares; az++)
        for (int ax = 0; ax < ares; ax++)
        {
            float wx = x0 + (ax + 0.5f) / ares * tw, wz = z0 + (az + 0.5f) / ares * tl;
            bool beyond = wx < 0f || wx > width || wz > length;
            var mirrored = Mirror(wx, wz);
            float x = mirrored.x, z = mirrored.y;
            float fx = Mathf.Clamp(x / sx, 0, nx - 1), fz = Mathf.Clamp(z / sz, 0, nz - 1);
            int s = Mathf.RoundToInt(fz) * nx + Mathf.RoundToInt(fx);
            float h = beyond ? WorldHeight(wx, wz) : Bilinear(heights, x, z);
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
                          * (beyond ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.35f, Bilinear(coast, x, z))));
            float riverEdge = riverDistance[s] < float.MaxValue
                ? Mathf.InverseLerp(riverHalf[s] + 0.9f, riverHalf[s] + 0.1f, riverDistance[s]) : 0f;
            float lakeEdge = lakeOf[s] >= 0 ? 1f : 0f;
            Overlay(w, 7, Mathf.Max(beach, Mathf.Max(riverEdge, lakeEdge)));
            float rocky = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rockSlope.x, rockSlope.y, Bilinear(slope, x, z)));
            Overlay(w, 8, rocky);
            float snowy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SnowLine(Mathf.Min(wz, length)), SnowLine(Mathf.Min(wz, length)) + snowBlend, h)) * (1f - rocky * 0.55f);
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

    struct Placed { public Vector3 position; public int prototype; public float scale, rotation; }
    readonly List<Placed> placed = new List<Placed>();
    TreePrototype[] treePrototypes;

    int Plant(Terrain[] terrains)
    {
        placed.Clear();
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
                placed.Add(new Placed { position = new Vector3(px, h, pz), prototype = protoIndex[prefabs[pick]], scale = scale, rotation = turn });
                total++;
            }
        }
        for (int t = 0; t < tiles; t++)
        {
            var data = terrains[t].terrainData;
            data.treePrototypes = treePrototypes = prototypes.ToArray();
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
        float pad = edges == EdgeStyle.Diorama ? 0f : 800f, cell = 32f;
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
        if (seaFloorMaterial != null && edges != EdgeStyle.Diorama)
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

    // ---------------------------------------------------------------- edges: haze

    /// <summary>Land past the sides and the north end, mirrored from the strip, the sides sinking into the sea; the
    /// strip's plants mirrored onto it, thinning out; and a mist over everything outside the strip.</summary>
    void MakeHazeEdges(GameObject root, Terrain[] main)
    {
        var beyond = new GameObject("Beyond");
        beyond.transform.SetParent(root.transform, false);
        var layers = Layers();
        int res = heightmapResolution, ares = Mathf.Max(64, alphamapResolution / 2);
        float tileLength = length / tiles, m = sideMargin;
        var grid = new Dictionary<Vector2Int, Terrain>();
        for (int t = 0; t < tiles; t++)
            grid[new Vector2Int(0, t)] = main[t];
        void Tile(int col, int row, string name)
        {
            float x0 = col < 0 ? -m : col == 0 ? 0f : width, w = col == 0 ? width : m;
            float z0 = row * tileLength;
            grid[new Vector2Int(col, row)] = CreateTile(beyond.transform, name, "Kingdom Beyond " + name, x0, z0, w, tileLength, res, ares,
                (x, z) => WorldHeight(x0 + x / (float)(res - 1) * w, z0 + z / (float)(res - 1) * tileLength), layers, 8f);
        }
        for (int t = 0; t < tiles; t++)
        {
            Tile(-1, t, $"West {t + 1}");
            Tile(1, t, $"East {t + 1}");
        }
        Tile(-1, tiles, "North West");
        Tile(0, tiles, "North");
        Tile(1, tiles, "North East");
        Terrain At(int c, int r) => grid.TryGetValue(new Vector2Int(c, r), out var found) ? found : null;
        foreach (var kv in grid)
            kv.Value.SetNeighbors(At(kv.Key.x - 1, kv.Key.y), At(kv.Key.x, kv.Key.y + 1), At(kv.Key.x + 1, kv.Key.y), At(kv.Key.x, kv.Key.y - 1));

        // the strip's plants, mirrored across the nearest edges, thinning out as the land sinks
        var perTile = new Dictionary<Terrain, List<TreeInstance>>();
        var rng = new Random(seed + 7);
        foreach (var plant in placed)
        {
            float px = plant.position.x, pz = plant.position.z;
            float mx = px < m ? -px : px > width - m ? 2f * width - px : float.NaN;
            float mz = pz > length - tileLength ? 2f * length - pz : float.NaN;
            var copies = new List<Vector2>();
            if (!float.IsNaN(mx)) copies.Add(new Vector2(mx, pz));
            if (!float.IsNaN(mz)) copies.Add(new Vector2(px, mz));
            if (!float.IsNaN(mx) && !float.IsNaN(mz)) copies.Add(new Vector2(mx, mz));
            foreach (var c in copies)
            {
                if (WorldHeight(c.x, c.y) < 0.5f)
                    continue;
                float side = SideOutside(c.x);
                if (side > 0f && rng.NextDouble() < Mathf.SmoothStep(0f, 1f, side / SideFadeAt(c.x, c.y)))
                    continue;
                var tile = At(c.x < 0f ? -1 : c.x > width ? 1 : 0, Mathf.Min((int)(c.y / tileLength), tiles));
                if (tile == null || Array.IndexOf(main, tile) >= 0)
                    continue;
                if (!perTile.TryGetValue(tile, out var list))
                    perTile[tile] = list = new List<TreeInstance>();
                var o = tile.transform.localPosition;
                var size = tile.terrainData.size;
                list.Add(new TreeInstance
                {
                    position = new Vector3((c.x - o.x) / size.x, 0f, (c.y - o.z) / size.z),
                    prototypeIndex = plant.prototype,
                    widthScale = plant.scale,
                    heightScale = plant.scale,
                    rotation = -plant.rotation,
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
        }
        foreach (var kv in perTile)
        {
            var data = kv.Key.terrainData;
            data.treePrototypes = treePrototypes;
            data.RefreshPrototypes();
            data.SetTreeInstances(kv.Value.ToArray(), true);
            EditorUtility.SetDirty(data);
        }

        // the mist: a flat bank in the fog's colour, clear over the strip and thickening past its edges
        if (hazeMaterial != null)
        {
            var xs = Axis(-1400f, width + 1400f, 0f, width);
            var zs = Axis(-1400f, length + 1400f, 0f, length);
            var verts = new List<Vector3>();
            var colours = new List<Color>();
            var tris = new List<int>();
            var colour = RenderSettings.fogColor.linear;
            foreach (float z in zs)
            foreach (float x in xs)
            {
                float outside = Mathf.Max(0f, Mathf.Max(Mathf.Max(-x, x - width), Mathf.Max(z - length, -z)));
                verts.Add(new Vector3(x, 0f, z));
                colour.a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hazeDistance.x, hazeDistance.y, outside));
                colours.Add(colour);
            }
            int row = xs.Count;
            for (int j = 0; j + 1 < zs.Count; j++)
            for (int i = 0; i + 1 < row; i++)
            {
                int a = j * row + i;
                if (colours[a].a + colours[a + 1].a + colours[a + row].a + colours[a + row + 1].a <= 0f)
                    continue;
                tris.AddRange(new[] { a, a + row, a + 1, a + 1, a + row, a + row + 1 });
            }
            var mesh = new Mesh { name = "Haze", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            string path = $"{outputFolder}/Kingdom Edges.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject("Haze");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, hazeHeight, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = hazeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>Grid lines from lo to hi: fine near the strip (from start to end, with a margin), coarse further out.</summary>
    static List<float> Axis(float lo, float hi, float start, float end)
    {
        var values = new List<float>();
        float nearLo = start - 160f, nearHi = end + 160f;
        for (float v = lo; v < nearLo; v += 60f) values.Add(v);
        for (float v = nearLo; v < nearHi; v += 6f) values.Add(v);
        for (float v = nearHi; v < hi; v += 60f) values.Add(v);
        values.Add(hi);
        return values;
    }

    // ---------------------------------------------------------------- edges: diorama

    /// <summary>The strip as a block: layered earth sides down to a base, a cut face where sea, river or lake water
    /// meets the edge, and a table under it all.</summary>
    void MakeDioramaEdges(GameObject root)
    {
        // the outline, anticlockwise seen from above, a point every metre
        var outline = new List<Vector2>();
        for (float x = 0; x < width; x += 1f) outline.Add(new Vector2(x, 0f));
        for (float z = 0; z < length; z += 1f) outline.Add(new Vector2(width, z));
        for (float x = width; x > 0f; x -= 1f) outline.Add(new Vector2(x, length));
        for (float z = length; z > 0f; z -= 1f) outline.Add(new Vector2(0f, z));
        outline.Add(outline[0]);

        var verts = new List<Vector3>();
        var colours = new List<Color>();
        var tris = new List<int>();
        void Quad(Vector2 a, Vector2 b, float aTop, float bTop, float aBottom, float bBottom, Color aTopColour, Color bTopColour, Color aBottomColour, Color bBottomColour)
        {
            if (aTop - aBottom < 0.01f && bTop - bBottom < 0.01f)
                return;
            int k = verts.Count;
            verts.Add(new Vector3(a.x, aTop, a.y)); verts.Add(new Vector3(b.x, bTop, b.y));
            verts.Add(new Vector3(a.x, aBottom, a.y)); verts.Add(new Vector3(b.x, bBottom, b.y));
            colours.Add(aTopColour); colours.Add(bTopColour); colours.Add(aBottomColour); colours.Add(bBottomColour);
            tris.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); // clockwise seen from outside
        }
        // band colours: grass lip, topsoil, clay, stone, bedrock; sand instead of grass under water
        var grassLip = new Color(0.33f, 0.42f, 0.17f);
        var sand = new Color(0.62f, 0.55f, 0.40f);
        var bandColours = new[] { new Color(0.30f, 0.21f, 0.14f), new Color(0.47f, 0.34f, 0.22f), new Color(0.50f, 0.46f, 0.40f), new Color(0.34f, 0.33f, 0.32f) };
        float along = 0f;
        float[] Levels(Vector2 p, float ground, float at)
        {
            float lip = ground - 0.6f;
            float soil = Mathf.Min(lip, -1.2f + Mathf.PerlinNoise(at / 23f, 1.3f) * 1.6f);
            float clay = Mathf.Min(soil, -5f + Mathf.PerlinNoise(at / 31f, 7.7f) * 2.2f);
            float stone = Mathf.Min(clay, -9.5f + Mathf.PerlinNoise(at / 19f, 4.2f) * 2.5f);
            return new[] { ground, lip, soil, clay, stone, dioramaBase };
        }
        float Shade(float at, int band) => 0.88f + 0.24f * Mathf.PerlinNoise(at / 6f, band * 3.7f);
        Color Times(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, 1f);
        float WaterAt(Vector2 p, float ground)
        {
            float level = ground < 0f ? 0f : float.MinValue;
            int s = Mathf.RoundToInt(Mathf.Clamp(p.y / sz, 0, nz - 1)) * nx + Mathf.RoundToInt(Mathf.Clamp(p.x / sx, 0, nx - 1));
            if (riverDistance[s] < riverHalf[s])
                level = Mathf.Max(level, riverSurface[s]);
            if (lakeOf[s] >= 0)
                level = Mathf.Max(level, LakeSurface(lakeOf[s]));
            return level;
        }
        var waterTop = new Color(0.22f, 0.46f, 0.52f);
        var waterDeep = new Color(0.05f, 0.16f, 0.24f);
        for (int i = 0; i + 1 < outline.Count; i++)
        {
            Vector2 a = outline[i], b = outline[i + 1];
            float atA = along, atB = along + Vector2.Distance(a, b);
            along = atB;
            float ga = Bilinear(heights, a.x, a.y), gb = Bilinear(heights, b.x, b.y);
            var la = Levels(a, ga, atA);
            var lb = Levels(b, gb, atB);
            // the top band: grass on land, sand under water
            var ca = Times(ga < 0.2f ? sand : grassLip, Shade(atA, 0));
            var cb = Times(gb < 0.2f ? sand : grassLip, Shade(atB, 0));
            Quad(a, b, la[0], lb[0], la[1], lb[1], ca, cb, ca, cb);
            for (int band = 1; band < 5; band++)
            {
                var c1 = Times(bandColours[band - 1], Shade(atA, band));
                var c2 = Times(bandColours[band - 1], Shade(atB, band));
                Quad(a, b, la[band], lb[band], la[band + 1], lb[band + 1], c1, c2, c1, c2);
            }
            // water standing above the ground at the edge: its cut face, light at the top, dark below
            float wa = WaterAt(a, ga), wb = WaterAt(b, gb);
            if (wa > ga || wb > gb)
            {
                float ta = Mathf.Max(wa, ga), tb = Mathf.Max(wb, gb);
                Color Depth(float top, float y) => Color.Lerp(waterTop, waterDeep, Mathf.InverseLerp(0f, 8f, top - y));
                Quad(a, b, ta, tb, ga, gb, waterTop, waterTop, Depth(ta, ga), Depth(tb, gb));
            }
        }
        var mesh = new Mesh { name = "Diorama Sides", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetColors(colours);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        string path = $"{outputFolder}/Kingdom Edges.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        var sides = new GameObject("Diorama Sides");
        sides.transform.SetParent(root.transform, false);
        sides.AddComponent<MeshFilter>().sharedMesh = mesh;
        sides.AddComponent<MeshRenderer>().sharedMaterial = dioramaSideMaterial;

        // the table the block stands on
        var table = new Mesh { name = "Table" };
        float pad = 3000f, y = dioramaBase - 0.02f;
        table.vertices = new[] { new Vector3(-pad, y, -pad), new Vector3(width + pad, y, -pad), new Vector3(-pad, y, length + pad), new Vector3(width + pad, y, length + pad) };
        table.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        table.RecalculateNormals();
        table.RecalculateBounds();
        AssetDatabase.AddObjectToAsset(table, mesh);
        var tableObject = new GameObject("Table");
        tableObject.transform.SetParent(root.transform, false);
        tableObject.AddComponent<MeshFilter>().sharedMesh = table;
        tableObject.AddComponent<MeshRenderer>().sharedMaterial = dioramaTableMaterial;
        AssetDatabase.SaveAssets();
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
