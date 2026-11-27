using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Gives the kingdom map Flat Kit's cel-shaded look: Stylized Surface copies of the Synty materials on the towns
/// and the terrain trees. Tools > Map > Apply Flat Kit Look makes (or remakes) the copies from these settings and
/// swaps them in; Remove Flat Kit Look swaps the Synty originals back.
/// </summary>
/// <remarks>
/// The copies and the tree prefab copies go in the git-ignored ThirdParty folder, since they're made from Synty's
/// materials and models. A terrain rebuild resets the tree prototypes to the originals, so apply it again after one.
/// Foliage loses Synty's wind (Stylized Surface has none), which at the map camera's distance doesn't show.
/// </remarks>
[CreateAssetMenu(menuName = "Game/Map Flat Look", fileName = "Map Flat Look")]
public class MapFlatLook : ScriptableObject
{
    [System.Serializable]
    public struct Cel
    {
        [Tooltip("The shaded side is the lit colour multiplied by this (Flat Kit's Color Shaded is set per material from it).")]
        public Color shaded;
        [Tooltip("The lit colour is the Synty colour times this. Stylized Surface isn't brightened by the sun the way " +
                 "Synty's lit shaders are, so it needs a little more.")]
        [Range(0.5f, 2.5f)] public float brightness;
        [Tooltip("How far round the object the shaded side reaches (Flat Kit's Self Shading Size).")]
        [Range(0f, 1f)] public float shadingSize;
        [Tooltip("Softness of the line between lit and shaded.")]
        [Range(0f, 0.5f)] public float edgeSize;
        [Tooltip("Flat Kit's Localized Shading: 1 shades each face on its own, 0 shades the whole object as one.")]
        [Range(0f, 1f)] public float flatness;
        [Tooltip("How dark cast shadows make it (Unity's shadows multiplied in).")]
        [Range(0f, 1f)] public float shadowPower;
        [Tooltip("A light edge on the side away from the camera.")]
        public bool rim;
        public Color rimColour;
        [Range(0f, 1f)] public float rimSize;
        [Tooltip("How much the sun's colour tints it.")]
        [Range(0f, 1f)] public float lightContribution;
    }

    public Cel buildings = new Cel
    {
        shaded = new Color(0.62f, 0.6f, 0.74f), brightness = 1.1f, shadingSize = 0.45f, edgeSize = 0.03f, flatness = 1f,
        shadowPower = 0.35f, rim = false, rimColour = new Color(1f, 0.95f, 0.85f), rimSize = 0.3f, lightContribution = 0.4f,
    };
    public Cel foliage = new Cel
    {
        shaded = new Color(0.55f, 0.6f, 0.72f), brightness = 1.35f, shadingSize = 0.5f, edgeSize = 0.08f, flatness = 0.6f,
        shadowPower = 0.3f, rim = true, rimColour = new Color(1f, 0.97f, 0.8f), rimSize = 0.25f, lightContribution = 0.4f,
    };
    [Tooltip("Leaves are cut out where their texture's alpha is below this.")]
    [Range(0f, 1f)] public float leafCutoff = 0.25f;

    [Header("Foliage style")]
    [Tooltip("How much leaf textures show through the flat colour (1 is Synty's texture as it is).")]
    [Range(0f, 1f)] public float leafTexture = 0.4f;
    [Tooltip("Leaves darken towards the bottom of each plant (Flat Kit's height gradient, in the plant's own space).")]
    public bool leafGradient = true;
    [Tooltip("The colour at the bottom: the lit colour times this.")]
    public Color leafGradientColour = new Color(0.45f, 0.55f, 0.6f);
    [Tooltip("Metres over which the gradient fades, and the height its middle sits at.")]
    public float leafGradientSize = 7f;
    public float leafGradientCentre = 2.5f;

    [Header("Shadows")]
    [Tooltip("Cast shadows take this colour instead of just darkening (alpha is how strongly).")]
    public Color shadowColour = new Color(0.27f, 0.26f, 0.45f, 0.55f);

    [Header("Ground")]
    [Tooltip("How much of each ground layer's texture is kept: 0 paints the layer in its average colour alone.")]
    [Range(0f, 1f)] public float groundDetail = 0.3f;
    [Tooltip("Saturation of each ground layer's colour (1 keeps it).")]
    [Range(0f, 2f)] public float groundSaturation = 1.25f;
    [HideInInspector] public List<TerrainLayer> layerOriginals = new List<TerrainLayer>();
    [HideInInspector] public List<TerrainLayer> layerCopies = new List<TerrainLayer>();

    [Header("Post-processing and sky")]
    [Tooltip("The map volume's profile with the look (no ACES, Flat Kit style grading) and without it.")]
    public UnityEngine.Rendering.VolumeProfile flatProfile;
    public UnityEngine.Rendering.VolumeProfile plainProfile;
    [Tooltip("Flat Kit's gradient sky, seen past the edges of the strip, and the sky it replaces.")]
    public Material flatSkybox;
    public Material plainSkybox;

    [Header("Land and camera")]
    [Tooltip("Flat Kit's terrain material for the map tiles (its own settings are on the material).")]
    public Material terrainMaterial;
    [Tooltip("What the tiles go back to without the look.")]
    public Material plainTerrainMaterial;
    [Tooltip("The map camera's renderer with the look (Map Renderer: Flat Kit fog and outline) and without it.")]
    public int flatRenderer = 1;
    public int plainRenderer = 0;

    [HideInInspector] public List<Material> originals = new List<Material>();
    [HideInInspector] public List<Material> copies = new List<Material>();
    [HideInInspector] public List<GameObject> treeOriginals = new List<GameObject>();
    [HideInInspector] public List<GameObject> treeCopies = new List<GameObject>();

    const string SettingsPath = "Assets/_Project/Settings/Map Flat Look.asset";
    const string Output = "Assets/_Project/ThirdParty/Map Flat Look";
    const string ShaderName = "FlatKit/Stylized Surface";

    static MapFlatLook Settings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<MapFlatLook>(SettingsPath);
        if (settings == null)
        {
            settings = CreateInstance<MapFlatLook>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        return settings;
    }

    [MenuItem("Tools/Map/Apply Flat Kit Look")]
    public static void Apply() => Settings().Swap(true);

    [MenuItem("Tools/Map/Remove Flat Kit Look")]
    public static void Remove() => Settings().Swap(false);

    void Swap(bool flat)
    {
        var towns = GameObject.Find("Towns");
        var terrains = Terrain.activeTerrains;
        if (flat)
        {
            Directory.CreateDirectory(Output + "/Materials");
            Directory.CreateDirectory(Output + "/Trees");
            // start from the originals, so applying twice remakes rather than copies copies
            Swap(false);
        }

        int swapped = 0;
        if (towns != null)
        {
            foreach (var renderer in towns.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var next = flat ? Flat(materials[i]) : Original(materials[i]);
                    if (next != materials[i])
                    {
                        materials[i] = next;
                        changed = true;
                    }
                }
                if (changed)
                {
                    Undo.RecordObject(renderer, flat ? "Apply Flat Kit Look" : "Remove Flat Kit Look");
                    renderer.sharedMaterials = materials;
                    swapped++;
                }
            }
        }

        foreach (var terrain in terrains)
        {
            var land = flat ? terrainMaterial : plainTerrainMaterial;
            if (land != null && terrain.materialTemplate != land)
            {
                Undo.RecordObject(terrain, flat ? "Apply Flat Kit Look" : "Remove Flat Kit Look");
                terrain.materialTemplate = land;
            }
        }
        int layers = 0;
        foreach (var terrain in terrains)
        {
            var data = terrain.terrainData;
            var current = data.terrainLayers;
            bool changed = false;
            for (int i = 0; i < current.Length; i++)
            {
                var next = flat ? FlatLayer(current[i]) : OriginalLayer(current[i]);
                if (next != current[i])
                {
                    current[i] = next;
                    changed = true;
                    layers++;
                }
            }
            if (changed)
            {
                data.terrainLayers = current;
                EditorUtility.SetDirty(data);
            }
        }

        foreach (var volume in FindObjectsByType<UnityEngine.Rendering.Volume>())
        {
            var profile = flat ? flatProfile : plainProfile;
            var other = flat ? plainProfile : flatProfile;
            if (profile != null && volume.sharedProfile == other)
            {
                Undo.RecordObject(volume, flat ? "Apply Flat Kit Look" : "Remove Flat Kit Look");
                volume.sharedProfile = profile;
            }
        }
        var sky = flat ? flatSkybox : plainSkybox;
        if (sky != null)
            RenderSettings.skybox = sky;

        var cameraData = Camera.main != null ? Camera.main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() : null;
        if (cameraData != null)
        {
            Undo.RecordObject(cameraData, flat ? "Apply Flat Kit Look" : "Remove Flat Kit Look");
            cameraData.SetRenderer(flat ? flatRenderer : plainRenderer);
            EditorUtility.SetDirty(cameraData);
        }

        int trees = 0;
        foreach (var terrain in terrains)
        {
            var data = terrain.terrainData;
            var prototypes = data.treePrototypes;
            bool changed = false;
            foreach (var prototype in prototypes)
            {
                var next = flat ? FlatTree(prototype.prefab) : OriginalTree(prototype.prefab);
                if (next != null && next != prototype.prefab)
                {
                    prototype.prefab = next;
                    changed = true;
                    trees++;
                }
            }
            if (changed)
            {
                data.treePrototypes = prototypes;
                data.RefreshPrototypes();
                EditorUtility.SetDirty(data);
            }
        }

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
        if (towns != null)
            EditorSceneManager.MarkSceneDirty(towns.scene);
        Debug.Log($"[Map] {(flat ? "applied" : "removed")} the Flat Kit look: {swapped} town renderers, {trees} tree prototypes, {layers} ground layers");
    }

    Material Original(Material material)
    {
        int index = copies.IndexOf(material);
        return index >= 0 && originals[index] != null ? originals[index] : material;
    }

    TerrainLayer OriginalLayer(TerrainLayer layer)
    {
        int index = layerCopies.IndexOf(layer);
        return index >= 0 && layerOriginals[index] != null ? layerOriginals[index] : layer;
    }

    /// <summary>
    /// A copy of a ground layer painted mostly in its average colour (<see cref="groundDetail"/> of the texture kept),
    /// with no normal map, so the land reads as flat colour and Flat Kit's cel shading does the modelling.
    /// </summary>
    TerrainLayer FlatLayer(TerrainLayer source)
    {
        if (source == null || source.diffuseTexture == null)
            return source;
        int index = layerOriginals.IndexOf(source);
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)).Substring(0, 6);
        string texturePath = $"{Output}/Ground/{source.name} {guid} Flat.png";
        string layerPath = $"{Output}/Ground/{source.name} {guid} Flat.terrainlayer";
        Directory.CreateDirectory(Output + "/Ground");

        const int size = 128;
        var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source.diffuseTexture, target);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        var pixels = image.GetPixels();
        Color average = Color.clear;
        foreach (var pixel in pixels) average += pixel;
        average /= pixels.Length;
        Color.RGBToHSV(average, out float hue, out float saturation, out float value);
        var shift = Color.HSVToRGB(hue, Mathf.Clamp01(saturation * groundSaturation), value) - average;
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.Lerp(average, pixels[i], groundDetail) + shift;
        image.SetPixels(pixels);
        File.WriteAllBytes(texturePath, image.EncodeToPNG());
        DestroyImmediate(image);
        AssetDatabase.ImportAsset(texturePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();

        var copy = index >= 0 ? layerCopies[index] : null;
        if (copy == null)
        {
            copy = new TerrainLayer();
            AssetDatabase.CreateAsset(copy, layerPath);
            if (index >= 0) layerCopies[index] = copy;
            else
            {
                layerOriginals.Add(source);
                layerCopies.Add(copy);
            }
        }
        copy.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        copy.normalMapTexture = null;
        copy.tileSize = source.tileSize;
        copy.tileOffset = source.tileOffset;
        copy.diffuseRemapMin = source.diffuseRemapMin;
        copy.diffuseRemapMax = source.diffuseRemapMax;
        copy.smoothness = 0f;
        copy.metallic = 0f;
        EditorUtility.SetDirty(copy);
        return copy;
    }

    GameObject OriginalTree(GameObject prefab)
    {
        int index = treeCopies.IndexOf(prefab);
        return index >= 0 && treeOriginals[index] != null ? treeOriginals[index] : prefab;
    }

    /// <summary>The Stylized Surface copy of a Synty material, made or updated now; the material itself if it can't be converted.</summary>
    Material Flat(Material source)
    {
        if (source == null || source.shader == null)
            return source;
        bool foliage = source.shader.name == "Synty/Foliage";
        bool basic = source.shader.name == "Synty/Generic_Basic";
        if (!foliage && !basic)
            return source;
        // Synty's foliage shader picks its leaf or trunk texture per vertex, and a copy can only have one texture, so
        // canopies take the leaf texture: from the map camera the trunks hardly show under them
        Texture texture = foliage ? source.GetTexture("_Leaf_Texture") : source.GetTexture("_Albedo_Map");
        if (texture == null && foliage)
            texture = source.GetTexture("_Trunk_Texture");

        int index = originals.IndexOf(source);
        var copy = index >= 0 ? copies[index] : null;
        var shader = Shader.Find(ShaderName);
        if (copy == null)
        {
            copy = new Material(shader) { name = source.name + " Flat" };
            // several Synty materials share a name (three Branches_01s), so the file name carries the source's GUID
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            AssetDatabase.CreateAsset(copy, $"{Output}/Materials/{source.name} {guid.Substring(0, 6)} Flat.mat");
            if (index >= 0) copies[index] = copy;
            else
            {
                originals.Add(source);
                copies.Add(copy);
            }
        }
        copy.shader = shader;
        copy.shaderKeywords = new string[0];
        var cel = foliage ? this.foliage : buildings;
        // Synty's leaf colour: with Leaf Flat Color on, its base colour mixed with its colour noise and the texture
        // only giving the shape; otherwise the texture tinted by them
        bool flatLeaves = foliage && source.GetFloat("_Leaf_Flat_Color") > 0.5f;
        Color colour;
        if (basic)
            colour = source.GetColor("_BaseColor");
        else
        {
            colour = source.GetColor("_Leaf_Base_Color");
            if (source.GetFloat("_Use_Color_Noise") > 0.5f)
                colour = flatLeaves
                    ? (colour + source.GetColor("_Leaf_Noise_Color") + source.GetColor("_Leaf_Noise_Large_Color")) / 3f
                    : colour * (source.GetColor("_Leaf_Noise_Color") + source.GetColor("_Leaf_Noise_Large_Color")) * 0.5f;
        }
        // a textured leaf shows only some of its texture; the rest of its colour moves into the base colour, so the
        // average colour stays Synty's: base = average texture x tint / what the texture still multiplies by
        float impact = flatLeaves ? 0f : foliage ? leafTexture : 1f;
        if (foliage && !flatLeaves)
        {
            var average = Average(texture);
            var kept = Color.Lerp(Color.white, average, impact);
            colour = new Color(colour.r * average.r / Mathf.Max(0.05f, kept.r), colour.g * average.g / Mathf.Max(0.05f, kept.g),
                colour.b * average.b / Mathf.Max(0.05f, kept.b));
        }
        var lit = WithAlpha(colour * cel.brightness);
        copy.SetColor("_BaseColor", lit);
        copy.SetTexture("_BaseMap", texture);
        copy.SetFloat("_TextureImpact", impact);
        copy.SetFloat("_TextureBlendingMode", 0f);
        copy.EnableKeyword("_TEXTUREBLENDINGMODE_MULTIPLY");

        // Flat Kit's Color Shaded is the shaded side's colour, not a multiplier, so it's made per material
        copy.SetFloat("_CelPrimaryMode", 1f);
        copy.EnableKeyword("_CELPRIMARYMODE_SINGLE");
        copy.SetColor("_ColorDim", WithAlpha(lit * cel.shaded));
        copy.SetFloat("_SelfShadingSize", cel.shadingSize);
        copy.SetFloat("_ShadowEdgeSize", cel.edgeSize);
        copy.SetFloat("_Flatness", cel.flatness);
        copy.SetFloat("_LightContribution", cel.lightContribution);

        // cast shadows in a colour of their own rather than plain darkening
        copy.SetFloat("_UnityShadowMode", 2f);
        copy.EnableKeyword("_UNITYSHADOWMODE_COLOR");
        copy.SetColor("_UnityShadowColor", new Color(shadowColour.r, shadowColour.g, shadowColour.b, shadowColour.a * cel.shadowPower / 0.35f));
        copy.SetFloat("_UnityShadowSharpness", 3f);

        copy.SetFloat("_GradientEnabled", foliage && leafGradient ? 1f : 0f);
        if (foliage && leafGradient)
        {
            copy.EnableKeyword("DR_GRADIENT_ON");
            copy.SetFloat("_GradientSpace", 1f);
            copy.EnableKeyword("_GRADIENTSPACE_LOCAL");
            copy.SetColor("_ColorGradient", WithAlpha(lit * leafGradientColour));
            copy.SetFloat("_GradientCenterX", 0f);
            copy.SetFloat("_GradientCenterY", leafGradientCentre);
            copy.SetFloat("_GradientSize", leafGradientSize);
            copy.SetFloat("_GradientAngle", 0f);
        }

        copy.SetFloat("_RimEnabled", cel.rim ? 1f : 0f);
        if (cel.rim)
        {
            copy.EnableKeyword("DR_RIM_ON");
            copy.SetColor("_FlatRimColor", cel.rimColour);
            copy.SetFloat("_FlatRimSize", cel.rimSize);
            copy.SetFloat("_FlatRimEdgeSmoothness", 0.5f);
            copy.SetFloat("_FlatRimLightAlign", 0.6f);
        }

        bool clip = foliage || source.GetFloat("_AlphaClip") > 0.5f;
        copy.SetFloat("_AlphaClip", clip ? 1f : 0f);
        copy.SetFloat("_Cutoff", foliage ? leafCutoff : source.GetFloat("_Alpha_Clip_Threshold"));
        if (clip) copy.EnableKeyword("_ALPHATEST_ON");
        // leaves and cards are single planes seen from both sides
        copy.SetFloat("_Cull", foliage ? 0f : 2f);
        copy.renderQueue = clip ? 2450 : -1;
        copy.enableInstancing = true;
        EditorUtility.SetDirty(copy);
        return copy;
    }

    /// <summary>A copy of a tree prefab with every Synty material swapped for its Stylized Surface copy.</summary>
    GameObject FlatTree(GameObject prefab)
    {
        if (prefab == null)
            return null;
        int index = treeOriginals.IndexOf(prefab);
        string path = $"{Output}/Trees/{prefab.name} Flat.prefab";
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        bool any = false;
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var flat = Flat(materials[i]);
                any |= flat != materials[i];
                materials[i] = flat;
            }
            renderer.sharedMaterials = materials;
        }
        GameObject copy = null;
        if (any)
            copy = PrefabUtility.SaveAsPrefabAsset(instance, path);
        DestroyImmediate(instance);
        if (copy == null)
            return prefab;
        if (index >= 0) treeCopies[index] = copy;
        else
        {
            treeOriginals.Add(prefab);
            treeCopies.Add(copy);
        }
        return copy;
    }

    /// <summary>The average colour of a texture's visible pixels (alpha over a half).</summary>
    static Color Average(Texture texture)
    {
        if (texture == null)
            return Color.white;
        var target = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(texture, target);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        Color sum = Color.clear;
        int count = 0;
        foreach (var pixel in image.GetPixels())
            if (pixel.a > 0.5f)
            {
                sum += pixel;
                count++;
            }
        DestroyImmediate(image);
        return count > 0 ? sum / count : Color.white;
    }

    static Color WithAlpha(Color colour)
    {
        colour.a = 1f;
        return colour;
    }
}
