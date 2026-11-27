using System.Collections.Generic;
using DistantLands.Cozy;
using UnityEngine;

/// <summary>
/// Makes COZY's weather show on a map seen from far above. COZY centres its weather on the camera and sizes rain and
/// snow for someone standing in it (a few metres across, drops 5 cm wide); here it's centred on the ground in the
/// middle of the view instead, and each particle effect COZY makes is spread wider, starts higher, and has bigger,
/// more numerous particles. Only the runtime copies COZY makes are changed, never its assets.
/// </summary>
[DefaultExecutionOrder(-150)] // the focus moves before COZY's LateUpdate centres the weather on it
public class MapWeather : MonoBehaviour
{
    [Tooltip("The camera the map is seen through. Main camera if empty.")]
    [SerializeField] Camera mapCamera;
    [Tooltip("Particle effects are spread over this many times their own width.")]
    [SerializeField, Min(1f)] float areaScale = 9f;
    [Tooltip("Particles start this high above the ground in view, in metres.")]
    [SerializeField, Min(0f)] float startHeight = 40f;
    [Tooltip("Particles are drawn this many times their own size.")]
    [SerializeField, Min(0.1f)] float sizeScale = 6f;
    [Tooltip("This many times as many particles (more ground to cover).")]
    [SerializeField, Min(0.1f)] float rateScale = 4f;
    [Tooltip("Mist sprites (already large) are drawn only this many times their size.")]
    [SerializeField, Min(0.1f)] float mistScale = 1.5f;
    [Tooltip("Rain streaks (stretched particles) are widened this much more, so they show from far above.")]
    [SerializeField, Min(1f)] float streakScale = 2f;
    [Tooltip("Only COZY effects whose name holds one of these are rescaled; its ambience (birds, wind) is left as it is.")]
    [SerializeField] string[] weatherEffects = { "Rain", "Snow", "Thunder", "Hail", "Dust Storm" };

    Transform focus;
    readonly HashSet<ParticleSystem> scaled = new HashSet<ParticleSystem>();
    readonly HashSet<CozyParticles> boosted = new HashSet<CozyParticles>();

    void OnEnable()
    {
        if (focus == null)
        {
            focus = new GameObject("Weather Focus").transform;
            focus.SetParent(transform, false);
        }
    }

    void LateUpdate()
    {
        var cozy = CozyWeather.instance;
        var cam = mapCamera != null ? mapCamera : Camera.main;
        if (cozy == null || cam == null)
            return;

        // the ground where the middle of the view looks
        var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float t = ray.direction.y < -0.01f ? -ray.origin.y / ray.direction.y : 100f;
        focus.position = ray.origin + ray.direction * t;
        cozy.centerAroundCustomObject = true;
        cozy.customPivot = focus;

        if (cozy.particleFXParent == null)
            return;
        foreach (var particles in cozy.particleFXParent.GetComponentsInChildren<CozyParticles>(true))
        {
            if (!boosted.Add(particles) || !IsWeather(particles.name))
                continue;
            // COZY sets each system's emission from the amount it read when it set up, so that's what's raised
            if (particles.m_ParticleTypes != null)
                foreach (var type in particles.m_ParticleTypes)
                    type.emissionAmount *= rateScale;
            foreach (var system in particles.GetComponentsInChildren<ParticleSystem>(true))
                Spread(system);
        }
    }

    bool IsWeather(string effect)
    {
        foreach (var name in weatherEffects)
            if (effect.Contains(name))
                return true;
        return false;
    }

    void Spread(ParticleSystem system)
    {
        if (!scaled.Add(system))
            return;
        var main = system.main;
        main.maxParticles = Mathf.RoundToInt(main.maxParticles * rateScale);
        // ripples and splashes are made where drops land, so they stay where they are, only bigger
        bool ground = system.name.Contains("Impact") || system.name.Contains("Ripple") || system.name.Contains("Splash");
        if (ground)
        {
            main.startSizeMultiplier *= sizeScale;
            return;
        }
        var shape = system.shape;
        shape.radius *= areaScale;
        shape.scale = new Vector3(shape.scale.x * areaScale, shape.scale.y, shape.scale.z * areaScale);
        shape.position = new Vector3(shape.position.x, startHeight, shape.position.z);
        bool streak = system.TryGetComponent<ParticleSystemRenderer>(out var renderer) && renderer.renderMode == ParticleSystemRenderMode.Stretch;
        // mist sprites are already tens of metres across; scaled like the drops they buried half the map
        bool mist = system.name.Contains("Mist");
        main.startSizeMultiplier *= mist ? mistScale : sizeScale * (streak ? streakScale : 1f);
        // falling further from higher up
        main.startLifetimeMultiplier *= Mathf.Max(1f, startHeight / 15f);
    }
}
