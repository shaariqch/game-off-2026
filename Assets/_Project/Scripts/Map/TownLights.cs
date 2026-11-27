using System.Collections.Generic;
using DistantLands.Cozy;
using UnityEngine;

/// <summary>
/// Lights the towns as the sun goes down: a warm point light on every street lamp, and windows (the buildings'
/// glass meshes) that glow. Both fade with how dark COZY's sunlight is, so they come on through dusk.
/// </summary>
/// <remarks>
/// Window glow needs the glass material's emission keyword on, which the Flat Kit look's glass copies have. URP on
/// WebGL draws at most 32 extra lights (WebLightLimit keeps the nearest 28), so keep lamps per town modest.
/// </remarks>
public class TownLights : MonoBehaviour
{
    [Tooltip("The root holding the towns (the scene's Towns object).")]
    [SerializeField] Transform towns;
    [Tooltip("Street lamps are props whose name holds this.")]
    [SerializeField] string lampName = "Street_Lamp";
    [SerializeField] Color lampColour = new Color(1f, 0.68f, 0.38f);
    [SerializeField, Min(0f)] float lampIntensity = 8f;
    [SerializeField, Min(0.1f)] float lampRange = 14f;
    [Tooltip("Window glass is any renderer whose name holds this.")]
    [SerializeField] string windowName = "Glass";
    [Tooltip("The windows' glow at full night (HDR: above 1 also blooms).")]
    [SerializeField, ColorUsage(false, true)] Color windowGlow = new Color(2.4f, 1.4f, 0.6f);
    [Tooltip("Sunlight brightness (its strongest colour channel times intensity) at and above which it's full day.")]
    [SerializeField, Min(0.01f)] float daylight = 0.8f;
    [Tooltip("How fast the lights follow the light level, per second.")]
    [SerializeField, Min(0.1f)] float fadeSpeed = 1.5f;

    readonly List<Light> lamps = new List<Light>();
    readonly List<Renderer> windows = new List<Renderer>();
    MaterialPropertyBlock block;
    float night = -1f;
    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    /// <summary>0 in full daylight, 1 at night.</summary>
    public float Night => Mathf.Max(0f, night);

    void Start()
    {
        if (towns == null)
        {
            var found = GameObject.Find("Towns");
            if (found != null) towns = found.transform;
        }
        if (towns == null)
            return;
        block = new MaterialPropertyBlock();
        foreach (var renderer in towns.GetComponentsInChildren<Renderer>())
        {
            if (renderer.name.Contains(lampName))
            {
                var light = new GameObject("Lamp Light").AddComponent<Light>();
                light.type = LightType.Point;
                light.color = lampColour;
                light.range = lampRange;
                light.shadows = LightShadows.None;
                light.intensity = 0f;
                light.transform.SetParent(renderer.transform, false);
                // just under the top of the lamp
                light.transform.position = new Vector3(renderer.bounds.center.x, renderer.bounds.max.y - 0.4f, renderer.bounds.center.z);
                lamps.Add(light);
            }
            else if (renderer.name.Contains(windowName))
                windows.Add(renderer);
        }
    }

    void Update()
    {
        var cozy = CozyWeather.instance;
        float target = 1f;
        if (cozy != null && cozy.sunLight != null && cozy.sunLight.enabled)
        {
            var c = cozy.sunLight.color;
            target = 1f - Mathf.Clamp01(Mathf.Max(c.r, Mathf.Max(c.g, c.b)) * cozy.sunLight.intensity / daylight);
        }
        night = night < 0f ? target : Mathf.MoveTowards(night, target, fadeSpeed * Time.deltaTime);

        foreach (var lamp in lamps)
        {
            lamp.intensity = lampIntensity * night;
            lamp.enabled = night > 0.01f;
        }
        if (block == null)
            return;
        var glow = windowGlow * night;
        foreach (var window in windows)
        {
            window.GetPropertyBlock(block);
            block.SetColor(EmissionColor, glow);
            window.SetPropertyBlock(block);
        }
    }
}
