using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// A repeatable performance check for development builds, because frame rates in the Editor are badly inflated.
// Add it to the scene, list the spots to stand at (any transforms: their position and rotation are used) and what to
// move there (the player; the main camera if empty). Launch a Windows dev build with -perftour, or open a WebGL dev
// build with ?perftour in the address, and it stands at each spot in turn and logs PerfProbe's averages as "[PerfTour]"
// lines (Player.log on Windows; the browser console and the local test log on WebGL). At the spots listed in
// variantSpots it also switches costs off one at a time. On Windows it saves a profiler capture at the first spot
// (perftour.raw beside Player.log) and quits at the end. Does nothing otherwise, or in release builds.
public class PerfTour : MonoBehaviour
{
    [Tooltip("What gets moved to each spot: the player, or the main camera if empty")]
    [SerializeField] Transform subject;
    [SerializeField] Transform[] spots = new Transform[0];
    [Tooltip("Indexes into spots where each cost is switched off in turn")]
    [SerializeField] int[] variantSpots = { 0 };
    [Tooltip("Renderer features, by name, to switch off one at a time")]
    [SerializeField] string[] rendererFeatures = { "ScreenSpaceAmbientOcclusion" };
    [Tooltip("Seconds to wait before starting, for a title screen or loading")]
    [SerializeField] float startDelay = 5f;
    [SerializeField] float settleSeconds = 3f;
    [Tooltip("PerfProbe averages the last 120 frames, so this should cover at least that many")]
    [SerializeField] float measureSeconds = 5f;

    static bool Requested()
    {
        if (!Debug.isDebugBuild || Application.isEditor) return false;
        if (Application.platform == RuntimePlatform.WebGLPlayer) return Application.absoluteURL.Contains("perftour");
        return Array.IndexOf(Environment.GetCommandLineArgs(), "-perftour") >= 0;
    }

    void Awake()
    {
        if (!Requested()) enabled = false;
    }

    IEnumerator Start()
    {
        Log($"start {Screen.width}x{Screen.height} {SystemInfo.graphicsDeviceName} / {SystemInfo.processorType}");
        yield return new WaitForSeconds(startDelay);

        if (subject == null && Camera.main != null) subject = Camera.main.transform;
        bool web = Application.platform == RuntimePlatform.WebGLPlayer;

        for (int i = 0; i < spots.Length; i++)
        {
            var spot = spots[i];
            if (spot == null) continue;
            MoveTo(spot);
            yield return Measure(spot.name);

            if (Array.IndexOf(variantSpots, i) >= 0)
                foreach (var variant in Variants())
                {
                    var undo = variant.apply();
                    yield return Measure($"{spot.name}, {variant.name}");
                    undo();
                }

            if (i == 0 && !web)
                yield return Capture(Path.Combine(Path.GetDirectoryName(Application.consoleLogPath) ?? Application.persistentDataPath, "perftour.raw"));
        }

        Log("done");
        if (!web) Application.Quit();
    }

    // Each switches one cost off and hands back how to put it back.
    List<(string name, Func<Action> apply)> Variants()
    {
        var list = new List<(string, Func<Action>)>
        {
            ("no shadows", () =>
            {
                var lights = FindObjectsByType<Light>();
                var was = new LightShadows[lights.Length];
                for (int i = 0; i < lights.Length; i++) { was[i] = lights[i].shadows; lights[i].shadows = LightShadows.None; }
                return () => { for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].shadows = was[i]; };
            }),
            ("no post-processing", () =>
            {
                var cam = Camera.main;
                if (cam == null) return () => { };
                var data = cam.GetUniversalAdditionalCameraData();
                bool was = data.renderPostProcessing;
                data.renderPostProcessing = false;
                return () => data.renderPostProcessing = was;
            }),
        };

        if (Terrain.activeTerrain != null)
        {
            list.Add(("no terrain trees or details", () => { var t = Terrain.activeTerrain; t.drawTreesAndFoliage = false; return () => t.drawTreesAndFoliage = true; }));
            list.Add(("no terrain details", () => { var t = Terrain.activeTerrain; float was = t.detailObjectDistance; t.detailObjectDistance = 0f; return () => t.detailObjectDistance = was; }));
        }

        foreach (var feature in rendererFeatures)
        {
            string name = feature;
            list.Add(($"no {name}", () => Feature(name)));
        }
        return list;
    }

    static Action Feature(string name)
    {
        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (asset == null || field == null) return () => { };
        foreach (var data in (ScriptableRendererData[])field.GetValue(asset))
        {
            if (data == null) continue;
            foreach (var feature in data.rendererFeatures)
                if (feature != null && feature.name == name && feature.isActive)
                {
                    feature.SetActive(false);
                    return () => feature.SetActive(true);
                }
        }
        Log($"no active renderer feature called {name}");
        return () => { };
    }

    void MoveTo(Transform spot)
    {
        if (subject == null) return;
        var body = subject.GetComponent<CharacterController>();
        if (body != null) body.enabled = false;
        subject.SetPositionAndRotation(spot.position, spot.rotation);
        if (body != null) body.enabled = true;
    }

    IEnumerator Measure(string place)
    {
        yield return new WaitForSeconds(settleSeconds);
        yield return new WaitForSeconds(measureSeconds);
        Log($"{place}: {PerfProbe.Summary.Replace("\n", "   ")}");
    }

    static IEnumerator Capture(string path)
    {
        Profiler.logFile = path;
        Profiler.enableBinaryLog = true;
        Profiler.enabled = true;
        for (int i = 0; i < 90; i++) yield return null;
        Profiler.enabled = false;
        Profiler.enableBinaryLog = false;
        Profiler.logFile = "";
        Log($"profiler capture: {path}");
    }

    static void Log(string text) => Debug.Log($"[PerfTour] {text}");
}
