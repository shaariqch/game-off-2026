using System.Collections.Generic;
using UnityEngine;

// WebGL only, made when a scene loads. URP on WebGL draws at most 32 extra lights; past that it logs a warning every
// frame (22,000 in one test of the previous game) and drops lights itself. This keeps only the point lights nearest
// the camera on, which also saves the browser some work. Lights that are off when the scene starts stay under their
// own scripts' control.
public class WebLightLimit : MonoBehaviour
{
    // leaves room under URP's 32 for spot lights and anything else that isn't a point light
    [SerializeField] int maxPointLights = 28;
    [SerializeField] float interval = 0.25f;

    readonly List<Light> lights = new List<Light>();
    readonly List<Light> sorted = new List<Light>();
    float next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer) return;
        var go = new GameObject("Web Light Limit");
        // kept apart from the map, which BattleLink hides during a battle
        DontDestroyOnLoad(go);
        go.AddComponent<WebLightLimit>();
    }

    void Start()
    {
        Collect();
        // scenes loaded later (the battle scene) bring their own lights
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Collect();

    void Collect()
    {
        foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            if (light.type == LightType.Point && light.enabled && !lights.Contains(light)) lights.Add(light);
        lights.RemoveAll(light => light == null);
    }

    void LateUpdate()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + interval;
        var cam = Camera.main;
        if (cam == null || lights.Count <= maxPointLights) return;

        Vector3 from = cam.transform.position;
        sorted.Clear();
        foreach (var light in lights) if (light != null) sorted.Add(light);
        sorted.Sort((a, b) => (a.transform.position - from).sqrMagnitude.CompareTo((b.transform.position - from).sqrMagnitude));
        for (int i = 0; i < sorted.Count; i++)
        {
            bool on = i < maxPointLights;
            if (sorted[i].enabled != on) sorted[i].enabled = on;
        }
    }
}
