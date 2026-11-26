using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Each side's soldiers wear their side's colour. The figures share one Synty material that samples a colour-swatch
/// atlas, so this builds a copy of that atlas per side with the clothing swatches recoloured, and a material for each.
/// A pick names a unit type and an atlas cell (64 px squares of the 4096 px atlas): the swatches that unit's faces
/// sample in that cell are recoloured, keeping the texture's light and dark. Build with Tools > Battle > Build Team
/// Colours; the results go to the git-ignored ThirdParty folder, since they're made from Synty's texture.
/// </summary>
[CreateAssetMenu(menuName = "Game/Team Colours", fileName = "Team Colours")]
public class TeamColours : ScriptableObject
{
    [Serializable]
    public struct Pick
    {
        [Tooltip("The unit type whose figure's faces choose the swatches.")]
        public UnitType unit;
        [Tooltip("Atlas cell, in 64 px squares from the bottom left.")]
        public Vector2Int cell;
        [Tooltip("How far the swatch moves to the side's colour: 1 replaces its hue, less tints it.")]
        [Range(0f, 1f)] public float strength;
        [Tooltip("Use the side's dark colour (trousers, under-layers) instead of its main one.")]
        public bool dark;
    }

    [Tooltip("The material the figures use. Its atlas is the one copied.")]
    public Material source;
    public Color colourA = new Color(0.2f, 0.36f, 0.72f);
    public Color colourB = new Color(0.72f, 0.15f, 0.12f);
    [Tooltip("The dark colour is the main one times this.")]
    [Range(0f, 1f)] public float darkShade = 0.45f;
    [Tooltip("A swatch spreads from where faces sample it to every pixel within this colour distance of that point.")]
    [Range(0f, 0.5f)] public float swatchTolerance = 0.09f;
    [Tooltip("Size the team atlases are imported at. The characters only need a few swatches, so 2048 saves memory in the browser.")]
    public int importSize = 2048;
    public List<Pick> picks = new List<Pick>();

    const string OutputFolder = "Assets/_Project/ThirdParty/Synty/PolygonFantasyKingdom/Teams";
    const string SettingsPath = "Assets/_Project/Settings/Team Colours.asset";
    const int Cell = 64;
    const int SwatchRadius = 48;

    [MenuItem("Tools/Battle/Build Team Colours")]
    static void BuildFromMenu()
    {
        var settings = AssetDatabase.LoadAssetAtPath<TeamColours>(SettingsPath);
        if (settings == null)
        {
            Debug.LogError("No team colour settings at " + SettingsPath);
            return;
        }
        Debug.Log(settings.Build());
    }

    /// <summary>Writes both team atlases and materials. Returns a summary.</summary>
    public string Build()
    {
        var albedo = source != null ? source.GetTexture("_Albedo_Map") as Texture2D : null;
        if (albedo == null)
            return "Team colours: the source material has no _Albedo_Map.";
        // the asset isn't readable, so read its file
        var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        atlas.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(albedo)));
        var pixels = atlas.GetPixels32();
        int size = atlas.width;

        // every pixel a pick covers, with how far to move it and to which shade
        var cover = new Dictionary<int, (float strength, bool dark, float seedLight)>();
        foreach (var pick in picks)
            foreach (var seed in Seeds(pick, size))
                Spread(pixels, size, seed, pick, cover);

        Directory.CreateDirectory(OutputFolder);
        string result = "Team colours: " + cover.Count + " pixels recoloured from " + picks.Count + " picks.";
        for (int side = 0; side < 2; side++)
        {
            var colour = side == 0 ? colourA : colourB;
            var recoloured = (Color32[])pixels.Clone();
            foreach (var entry in cover)
            {
                Color original = pixels[entry.Key];
                var (strength, dark, seedLight) = entry.Value;
                var target = dark ? colour * darkShade : colour;
                // keep the swatch's shading: brighter or darker than its seed by the same ratio
                float light = Mathf.Clamp(Light(original) / Mathf.Max(0.04f, seedLight), 0.6f, 1.4f);
                var shaded = new Color(target.r * light, target.g * light, target.b * light, original.a);
                recoloured[entry.Key] = Color.Lerp(original, shaded, strength);
            }
            var output = new Texture2D(size, size, TextureFormat.RGBA32, false);
            output.SetPixels32(recoloured);
            string name = side == 0 ? "Team A" : "Team B";
            string texturePath = OutputFolder + "/" + name + " Atlas.png";
            File.WriteAllBytes(texturePath, output.EncodeToPNG());
            AssetDatabase.ImportAsset(texturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.maxTextureSize = importSize;
            importer.SaveAndReimport();

            string materialPath = OutputFolder + "/" + name + " Outfit.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
                material.CopyPropertiesFromMaterial(source);
            material.SetTexture("_Albedo_Map", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            EditorUtility.SetDirty(material);
            result += " " + materialPath + ".";
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    /// <summary>The atlas pixels a unit's figure samples inside the pick's cell.</summary>
    IEnumerable<Vector2Int> Seeds(Pick pick, int size)
    {
        var seeds = new HashSet<Vector2Int>();
        if (pick.unit == null || pick.unit.visualPrefab == null)
            return seeds;
        var figure = Instantiate(pick.unit.visualPrefab);
        try
        {
            var meshes = new List<Mesh>();
            foreach (var skinned in figure.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                meshes.Add(skinned.sharedMesh);
            foreach (var filter in figure.GetComponentsInChildren<MeshFilter>(false))
                meshes.Add(filter.sharedMesh);
            foreach (var mesh in meshes)
            {
                if (mesh == null)
                    continue;
                var uv = mesh.uv;
                var triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector2 a = uv[triangles[i]], b = uv[triangles[i + 1]], c = uv[triangles[i + 2]];
                    foreach (var point in new[] { (a + b + c) / 3f, a, b, c })
                    {
                        var pixel = new Vector2Int(Mathf.Clamp((int)(point.x * size), 0, size - 1), Mathf.Clamp((int)(point.y * size), 0, size - 1));
                        if (pixel.x / Cell == pick.cell.x && pixel.y / Cell == pick.cell.y)
                            seeds.Add(pixel);
                    }
                }
            }
        }
        finally
        {
            DestroyImmediate(figure);
        }
        return seeds;
    }

    /// <summary>Marks the swatch around a seed: nearby pixels close to the seed's colour.</summary>
    void Spread(Color32[] pixels, int size, Vector2Int seed, Pick pick, Dictionary<int, (float, bool, float)> cover)
    {
        int start = seed.y * size + seed.x;
        if (cover.ContainsKey(start))
            return;
        Color seedColour = pixels[start];
        float seedLight = Light(seedColour);
        var open = new Stack<Vector2Int>();
        var seen = new HashSet<int> { start };
        open.Push(seed);
        while (open.Count > 0)
        {
            var at = open.Pop();
            int index = at.y * size + at.x;
            Color colour = pixels[index];
            if (Mathf.Abs(colour.r - seedColour.r) + Mathf.Abs(colour.g - seedColour.g) + Mathf.Abs(colour.b - seedColour.b) > swatchTolerance * 3f)
                continue;
            if (!cover.ContainsKey(index))
                cover[index] = (pick.strength, pick.dark, seedLight);
            foreach (var step in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
            {
                var next = at + step;
                if (next.x < 0 || next.y < 0 || next.x >= size || next.y >= size)
                    continue;
                if (Mathf.Abs(next.x - seed.x) > SwatchRadius || Mathf.Abs(next.y - seed.y) > SwatchRadius)
                    continue;
                if (seen.Add(next.y * size + next.x))
                    open.Push(next);
            }
        }
    }

    static float Light(Color colour) => colour.r * 0.299f + colour.g * 0.587f + colour.b * 0.114f;
}
