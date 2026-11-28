using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Cinemachine;
using Unity.Scripting.LifecycleManagement; // lifecycle-attr
using UnityEngine;

/// <summary>
/// A performance check for army sizes, in development builds only: opened with <c>?stress</c> in a WebGL build's
/// address (or launched with <c>-stress</c> on Windows), it stops the trading run, mutes the game and fights battles
/// on the map's battlefield at each size in <see cref="sizes"/>: once with everything on, then with one cost off at a
/// time (the figures' shadows, their skinning, the figures themselves). Each logs the frame rate at the height of the
/// battle and the view's own per-frame costs as "[Stress]" lines (the local test log on WebGL, Player.log on Windows).
/// Both sides get the same mix of every unit type, and neither holds, so the lines meet quickly. It switches to the PC
/// quality level where the platform has it (WebGL doesn't); <c>?stress&amp;q=0</c> keeps the default.
/// </summary>
// requested is read once per load, so Unity's automatic statics cleanup is off for it
[NoAutoStaticsCleanup] // lifecycle-attr
public class BattleStress : MonoBehaviour
{
    [Tooltip("Soldiers per side in each battle, in turn.")]
    [SerializeField] int[] sizes = { 20, 40, 60 };
    [Tooltip("Shares of each side by unit type name: militia, spearmen, archers, cavalry.")]
    [SerializeField] float[] mix = { 0.4f, 0.25f, 0.2f, 0.15f };
    [SerializeField] string[] typeNames = { "Militia", "Spearmen", "Archers", "Cavalry" };
    [Tooltip("Seconds after the start before measuring: the lines meet after about 5.")]
    [SerializeField] float settle = 5f;
    [SerializeField] float measure = 6f;
    [SerializeField] float startDelay = 4f;

    static bool Requested()
    {
        if (!Debug.isDebugBuild || Application.isEditor) return false;
        if (Application.platform == RuntimePlatform.WebGLPlayer) return Application.absoluteURL.Contains("stress");
        return Array.IndexOf(Environment.GetCommandLineArgs(), "-stress") >= 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (!Requested()) return;
        new GameObject("Battle Stress").AddComponent<BattleStress>();
    }

    static void Log(string message) => Debug.Log("[Stress] " + message);

    IEnumerator Start()
    {
        // a test run, not a play session: nobody needs to hear it
        AudioListener.volume = 0f;
        bool web = Application.platform == RuntimePlatform.WebGLPlayer;
        if (!(web && Application.absoluteURL.Contains("q=0")))
        {
            int pc = Array.IndexOf(QualitySettings.names, "PC");
            if (pc >= 0) QualitySettings.SetQualityLevel(pc, true);
        }
        Log($"start {Screen.width}x{Screen.height}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, " +
            $"{SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceVersion}");

        var run = FindAnyObjectByType<TradeRun>();
        var view = FindAnyObjectByType<BattleView>();
        if (run == null || view == null)
        {
            Log("no TradeRun or BattleView in the scene");
            yield break;
        }
        yield return new WaitForSeconds(startDelay);
        run.enabled = false;
        foreach (var link in FindObjectsByType<BattleLink>(FindObjectsSortMode.None))
            link.enabled = false;

        var types = new List<UnitType>();
        foreach (var name in typeNames)
        {
            var type = run.Market.listings.Find(l => l.type != null && l.type.name == name)?.type;
            if (type == null) Log("no unit type " + name);
            types.Add(type);
        }

        CinemachineCamera battleCamera = null;
        foreach (var cam in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (cam.name == "Battle Camera") battleCamera = cam;
        if (battleCamera != null) battleCamera.enabled = true;

        // the map with nothing fighting, as a baseline
        yield return new WaitForSeconds(3f);
        yield return Measure("no battle", 0);

        // A second run (stress2) showed the figures themselves are the cost: hiding them got every frame back, while
        // damage numbers, particles, frozen animation and the battle's own code changed little. This one splits that
        // between skinning (posing each mesh on the CPU) and drawing it (and its shadow).
        var variants = new (string name, bool noShadows, bool fixedMeshes, bool hide)[]
        {
            ("everything", false, false, false),
            ("figures cast no shadows", true, false, false),
            ("fixed meshes (drawn the same, no skinning)", false, true, false),
            ("fixed meshes, no shadows", true, true, false),
            ("figures hidden", false, false, true),
        };
        var baked = new List<Mesh>();
        foreach (int size in sizes)
            foreach (var variant in variants)
            {
                var army = Army(types, size, out int figures, out int riders);
                view.Begin(army, Army(types, size, out _, out _), false);
                // a frame for the figures to take their first pose before any are baked
                yield return null;
                yield return null;
                int skinned = 0, drawn = 0;
                foreach (var root in view.StressFigureRoots())
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                    {
                        if (variant.hide) { renderer.enabled = false; continue; }
                        if (variant.fixedMeshes && renderer is SkinnedMeshRenderer skin && skin.enabled)
                        {
                            Fix(skin, variant.noShadows, baked);
                            skinned++;
                        }
                        else if (renderer is SkinnedMeshRenderer) skinned++;
                        if (variant.noShadows) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        drawn++;
                    }
                yield return new WaitForSeconds(settle);
                view.TakeTimings();
                yield return Measure($"{size} a side, {variant.name}", figures * 2, riders * 2);
                Log($"  {skinned} skinned meshes, {drawn} renderers on figures | view: {view.TakeTimings()}");
                view.Clear();
                foreach (var mesh in baked) Destroy(mesh);
                baked.Clear();
                yield return new WaitForSeconds(2f);
            }
        Log("done");
    }

    /// <summary>
    /// Swaps a skinned mesh for a plain copy of its current pose that follows the figure: the same mesh and materials
    /// drawn the same way, without the CPU posing it every frame.
    /// </summary>
    static void Fix(SkinnedMeshRenderer skin, bool noShadows, List<Mesh> baked)
    {
        var mesh = new Mesh();
        // baked with the renderer's own scale, then drawn at world scale 1 (the horse's bones are scaled 100)
        skin.BakeMesh(mesh, true);
        baked.Add(mesh);
        var copy = new GameObject(skin.name + " (fixed)").transform;
        copy.SetParent(skin.transform, false);
        var scale = skin.transform.lossyScale;
        copy.localScale = new Vector3(1f / Mathf.Max(1e-5f, scale.x), 1f / Mathf.Max(1e-5f, scale.y), 1f / Mathf.Max(1e-5f, scale.z));
        copy.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = copy.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = skin.sharedMaterials;
        renderer.shadowCastingMode = noShadows ? UnityEngine.Rendering.ShadowCastingMode.Off : skin.shadowCastingMode;
        renderer.receiveShadows = skin.receiveShadows;
        skin.enabled = false;
    }

    List<ArmyEntry> Army(List<UnitType> types, int size, out int figures, out int riders)
    {
        var army = new List<ArmyEntry>();
        figures = 0;
        riders = 0;
        int left = size;
        for (int i = 0; i < types.Count; i++)
        {
            if (types[i] == null) continue;
            int count = i == types.Count - 1 ? left : Mathf.Min(left, Mathf.RoundToInt(size * mix[i]));
            left -= count;
            if (count <= 0) continue;
            army.Add(new ArmyEntry(types[i], count));
            figures += count;
            if (types[i].name == "Cavalry") riders += count;
        }
        return army;
    }

    IEnumerator Measure(string label, int figures, int riders = 0)
    {
        var frames = new List<float>();
        float end = Time.realtimeSinceStartup + measure;
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
            frames.Add(Time.unscaledDeltaTime * 1000f);
        }
        frames.Sort();
        float sum = 0f;
        foreach (var f in frames) sum += f;
        float mean = frames.Count > 0 ? sum / frames.Count : 0f;
        float median = frames.Count > 0 ? frames[frames.Count / 2] : 0f;
        float worst = frames.Count > 0 ? frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.95f))] : 0f;
        var line = new StringBuilder();
        line.Append($"{label}: {figures} figures ({riders} on horses), {1000f / Mathf.Max(0.01f, mean):0.0} fps, ");
        line.Append($"frame mean {mean:0.0} ms, median {median:0.0}, 95th {worst:0.0} | {PerfProbe.Summary}");
        Log(line.ToString());
    }
}
