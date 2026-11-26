using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The cavalry ride Omabuarts' Quirky Series zebra, recoloured as a horse. The zebra has no painted texture: its
/// faces sample an 8 by 8 colour palette, and its stripes are faces mapped to the palette's grey cells. So a copy of
/// the palette with its blocks recoloured (stripes the same as the body) turns it into a plain horse. Build with
/// Tools > Battle > Build Horse Coat; the texture and material go to the git-ignored ThirdParty folder, since they're
/// made from the pack.
/// </summary>
[CreateAssetMenu(menuName = "Game/Horse Coat", fileName = "Horse Coat")]
public class HorseCoat : ScriptableObject
{
    [Serializable]
    public struct Block
    {
        public string what;
        [Tooltip("Palette cells, from the bottom left (the zebra's palette is 8 by 8).")]
        public RectInt cells;
        public Color colour;
    }

    [Tooltip("The zebra's material; its palette is the one copied, and the new material copies its settings.")]
    public Material source;
    [Tooltip("Where the coat's texture and material are written (git-ignored).")]
    public string folder = "Assets/_Project/ThirdParty/Quirky Series/Horse";
    public string coatName = "Bay";
    public List<Block> blocks = new List<Block>();

    const string SettingsPath = "Assets/_Project/Settings/Horse Coat.asset";

    [MenuItem("Tools/Battle/Build Horse Coat")]
    static void BuildFromMenu()
    {
        var coat = AssetDatabase.LoadAssetAtPath<HorseCoat>(SettingsPath);
        if (coat == null)
        {
            Debug.LogError("No horse coat settings at " + SettingsPath);
            return;
        }
        Debug.Log(coat.Build());
    }

    /// <summary>Writes the recoloured palette and its material. Returns a summary.</summary>
    public string Build()
    {
        var palette = source != null ? source.mainTexture as Texture2D : null;
        if (palette == null)
            return "The horse coat's source material has no texture.";
        string sourcePath = AssetDatabase.GetAssetPath(palette);
        // read the file itself: the imported texture isn't readable
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(File.ReadAllBytes(sourcePath));
        var pixels = texture.GetPixels32();
        foreach (var block in blocks)
            for (int y = block.cells.yMin; y < block.cells.yMax; y++)
                for (int x = block.cells.xMin; x < block.cells.xMax; x++)
                    if (x >= 0 && y >= 0 && x < texture.width && y < texture.height)
                        pixels[y * texture.width + x] = block.colour;
        texture.SetPixels32(pixels);

        Directory.CreateDirectory(folder);
        string texturePath = folder + "/T_Horse_" + coatName + ".png";
        File.WriteAllBytes(texturePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(texturePath);
        // same import as the palette: point filtering, no compression, so the colours stay exact
        var from = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
        var to = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        var settings = new TextureImporterSettings();
        from.ReadTextureSettings(settings);
        to.SetTextureSettings(settings);
        to.textureCompression = from.textureCompression;
        to.SaveAndReimport();

        string materialPath = folder + "/M_Horse_" + coatName + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(source);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else
            material.CopyPropertiesFromMaterial(source);
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return "Horse coat " + coatName + ": " + blocks.Count + " blocks recoloured into " + texturePath + " and " + materialPath;
    }
}
