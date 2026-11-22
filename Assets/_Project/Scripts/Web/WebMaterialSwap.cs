using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement; // lifecycle-attr
using UnityEngine;

// On WebGL, Synty's PolygonShader can fail to link: it samples all 13 of its textures in every variant, and WebGL
// allows 16 per shader including URP's own. In the previous game its triplanar materials (rocks, cave walls) didn't
// draw at all. When a scene loads there, every PolygonShader material with triplanar texturing is swapped for one on
// Synty's own Triplanar_Basic shader (3 textures): the side texture and normals as its albedo and normal map, and a
// different top texture (grass on rocks) as its overlay. Windows keeps PolygonShader. Apply(true/false) previews it
// in the Editor.
//
// Needs a template material at Resources/Web Triplanar.mat using Synty's Triplanar_Basic shader (PolygonGeneric
// Shaders/Triplanar_Basic.shadergraph), which keeps that shader in the build. It's only loaded once a triplanar
// PolygonShader material turns up, so this does nothing until Synty content is in the scene.
// OnLoad clears its statics on every load, so Unity's automatic statics cleanup is off for it
[NoAutoStaticsCleanup] // lifecycle-attr
public static class WebMaterialSwap
{
    const string SyntyShader = "Synty/PolygonShader";

    static readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
    static readonly List<(Renderer renderer, Material[] original)> swapped = new List<(Renderer, Material[])>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnLoad()
    {
        copies.Clear();
        swapped.Clear();
        if (Application.platform == RuntimePlatform.WebGLPlayer) Apply(true);
    }

    // PolygonShader's triplanar tiling times this gives Triplanar_Basic's
    public static float TilingScale = 1f;

    static Material MakeCopy(Material template, Material source)
    {
        var copy = new Material(template) { name = source.name + " (Web)" };
        var side = source.GetTexture("_Triplanar_Texture_Side");
        var top = source.GetTexture("_Triplanar_Texture_Top");
        copy.SetTexture("_Albedo_Map", side);
        copy.SetFloat("_Albedo_Tiling", source.GetVector("_Triplanar_Tiling_Side").x * TilingScale);
        copy.SetTexture("_Normal_Map", source.GetTexture("_Triplanar_Normal_Texture_Side"));
        copy.SetFloat("_Normal_Tiling", source.GetVector("_Triplanar_Normal_Tiling_Side").x * TilingScale);
        bool grassTop = top != null && top != side;
        copy.SetTexture("_Overlay_Map", grassTop ? top : null);
        copy.SetFloat("_Overlay_Tiling", source.GetVector("_Triplanar_Tiling_Top").x * TilingScale);
        copy.SetFloat("_Overlay_Intensity", grassTop ? 1f : 0f);
        if (source.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
        return copy;
    }

    static bool IsTriplanarPolygon(Material material)
    {
        if (material == null || material.shader == null || material.shader.name != SyntyShader) return false;
        return !material.HasProperty("_Enable_Triplanar_Texture") || material.GetFloat("_Enable_Triplanar_Texture") >= 0.5f;
    }

    // returns how many renderers changed
    public static int Apply(bool on)
    {
        // put the originals back first, so a second Apply rebuilds the copies with the current TilingScale
        foreach (var (renderer, original) in swapped)
            if (renderer != null) renderer.sharedMaterials = original;
        int restored = swapped.Count;
        swapped.Clear();
        copies.Clear();
        if (!on) return restored;

        Material template = null;
        int changed = 0;
        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            var materials = renderer.sharedMaterials;
            bool any = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!IsTriplanarPolygon(source)) continue;
                if (template == null)
                {
                    template = Resources.Load<Material>("Web Triplanar");
                    if (template == null)
                    {
                        Debug.LogWarning("WebMaterialSwap: triplanar PolygonShader materials are in the scene, but Resources/Web Triplanar.mat is missing");
                        return changed;
                    }
                }
                if (!copies.TryGetValue(source, out var copy))
                {
                    copy = MakeCopy(template, source);
                    copies[source] = copy;
                }
                if (!any) swapped.Add((renderer, renderer.sharedMaterials));
                materials[i] = copy;
                any = true;
            }
            if (any)
            {
                renderer.sharedMaterials = materials;
                changed++;
            }
        }
        return changed;
    }
}
