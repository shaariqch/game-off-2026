using System;
using System.Collections.Generic;
using DistantLands.Cozy;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Darkens the map in bad weather, and flashes it when COZY strikes lightning. COZY's own storm light barely shows
/// from the map camera, so this weighs COZY's current weather mix by how dark each kind should make the map and
/// fades in a global grade (less exposure, less colour, a cooler tint) by that much. COZY strikes lightning 700 m
/// and more away, out of view, so each new strike also lifts the exposure for a moment.
/// </summary>
public class MapStorm : MonoBehaviour
{
    [Serializable]
    public struct Darkness
    {
        [Tooltip("A COZY weather profile's name, or part of it.")]
        public string weather;
        [Range(0f, 1f)] public float amount;
    }

    [Tooltip("How dark each kind of weather makes the map (1 is the full storm grade). The first entry a weather's " +
             "name contains is used; anything not listed doesn't darken.")]
    [SerializeField] List<Darkness> darkness = new List<Darkness>
    {
        new Darkness { weather = "Thunder Storm", amount = 1f },
        new Darkness { weather = "Imminent Storm", amount = 0.85f },
        new Darkness { weather = "Hail Storm", amount = 0.8f },
        new Darkness { weather = "Heavy Precipitation", amount = 0.6f },
        new Darkness { weather = "Dust Storm", amount = 0.5f },
        new Darkness { weather = "Storm Eye", amount = 0.45f },
        new Darkness { weather = "Approaching Storm", amount = 0.45f },
        new Darkness { weather = "Receeding Storm", amount = 0.45f },
        new Darkness { weather = "Overcast", amount = 0.35f },
        new Darkness { weather = "Dense Fog", amount = 0.35f },
        new Darkness { weather = "Distant Storm", amount = 0.3f },
        new Darkness { weather = "Dense Clouds", amount = 0.3f },
        new Darkness { weather = "Light Precipitation", amount = 0.3f },
        new Darkness { weather = "Mostly Cloudy", amount = 0.15f },
    };

    [Header("Storm grade at full darkness")]
    [SerializeField] float exposure = -1.1f;
    [SerializeField] float saturation = -35f;
    [SerializeField] float contrast = 8f;
    [SerializeField] Color tint = new Color(0.78f, 0.84f, 0.98f);
    [Tooltip("How fast the grade follows the weather, per second.")]
    [SerializeField, Min(0.01f)] float fadeSpeed = 0.25f;

    [Header("Lightning")]
    [Tooltip("Exposure added at the moment of a strike.")]
    [SerializeField] float flash = 1.6f;
    [Tooltip("Seconds the flash takes to die away.")]
    [SerializeField, Min(0.01f)] float flashTime = 0.35f;

    Volume volume;
    ColorAdjustments grade;
    float amount, flashLeft;
    readonly HashSet<CozyThunder> strikes = new HashSet<CozyThunder>();

    /// <summary>0 in fair weather, 1 in the worst storm.</summary>
    public float Amount => amount;

    void OnEnable()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Storm (runtime)";
        grade = profile.Add<ColorAdjustments>(true);
        volume = new GameObject("Storm Grade").AddComponent<Volume>();
        volume.transform.SetParent(transform, false);
        volume.isGlobal = true;
        volume.priority = 50f;
        volume.weight = 0f;
        volume.sharedProfile = profile;
    }

    void OnDisable()
    {
        if (volume != null)
        {
            Destroy(volume.sharedProfile);
            Destroy(volume.gameObject);
        }
    }

    void Update()
    {
        var cozy = CozyWeather.instance;
        float target = 0f;
        if (cozy != null && cozy.weatherModule != null)
        {
            foreach (var relation in cozy.weatherModule.currentWeatherProfiles)
                if (relation?.profile != null)
                    target += relation.weight * DarknessOf(relation.profile.name);
            WatchLightning(cozy);
            EndUnfitAmbience(cozy);
        }
        amount = Mathf.MoveTowards(amount, Mathf.Clamp01(target), fadeSpeed * Time.deltaTime);
        flashLeft = Mathf.Max(0f, flashLeft - Time.deltaTime);
        float strike = flash * (flashLeft / flashTime);

        // a higher-priority volume replaces the map grade's values rather than adding to them, so start from those
        ColorAdjustments map = null;
        foreach (var other in FindObjectsByType<Volume>())
            if (other != volume && other.isGlobal && other.sharedProfile != null && other.sharedProfile.TryGet(out map))
                break;
        float baseExposure = map != null && map.postExposure.overrideState ? map.postExposure.value : 0f;
        float baseSaturation = map != null && map.saturation.overrideState ? map.saturation.value : 0f;
        float baseContrast = map != null && map.contrast.overrideState ? map.contrast.value : 0f;
        var baseFilter = map != null && map.colorFilter.overrideState ? map.colorFilter.value : Color.white;

        volume.weight = 1f;
        grade.postExposure.Override(baseExposure + exposure * amount + strike);
        grade.saturation.Override(baseSaturation + saturation * amount);
        grade.contrast.Override(baseContrast + contrast * amount);
        grade.colorFilter.Override(baseFilter * Color.Lerp(Color.white, tint, amount));
    }

    float DarknessOf(string weather)
    {
        foreach (var entry in darkness)
            if (!string.IsNullOrEmpty(entry.weather) && weather.Contains(entry.weather))
                return entry.amount;
        return 0f;
    }

    /// <summary>
    /// COZY counts down to its next ambience on its own clock speed, which MapDayCycle's clock doesn't move, so the
    /// first ambience would play forever (birdsong through a thunderstorm). When the current one shouldn't play in
    /// this weather, its timer is ended so COZY picks another.
    /// </summary>
    static void EndUnfitAmbience(CozyWeather cozy)
    {
        var ambience = cozy.GetModule<CozyAmbienceModule>();
        var weather = cozy.weatherModule.ecosystem?.currentWeather;
        var current = ambience != null ? ambience.currentAmbienceProfile : null;
        if (current == null || weather == null || current.dontPlayDuring == null)
            return;
        if (Array.IndexOf(current.dontPlayDuring, weather) >= 0)
            ambience.ambienceTimer = 0f;
    }

    void WatchLightning(CozyWeather cozy)
    {
        if (cozy.thunderFXParent == null)
            return;
        strikes.RemoveWhere(s => s == null);
        foreach (var thunder in cozy.thunderFXParent.GetComponentsInChildren<CozyThunder>())
            if (strikes.Add(thunder))
                flashLeft = flashTime;
    }
}
