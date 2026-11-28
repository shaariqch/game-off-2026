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
/// on the map's battlefield: at each of <see cref="skinnedSizes"/> with SkinnedMeshRenderers, then at each of
/// <see cref="sizes"/> with figures skinned on the GPU from their bakes. Each logs the frame rate at the height of the
/// battle and the view's own per-frame costs as "[Stress]" lines (the local test log on WebGL, Player.log on Windows).
/// Both sides get the same mix of every unit type, and neither holds, so the lines meet quickly. It switches to the PC
/// quality level where the platform has it (WebGL doesn't); <c>?stress&amp;q=0</c> keeps the default.
/// </summary>
// requested is read once per load, so Unity's automatic statics cleanup is off for it
[NoAutoStaticsCleanup] // lifecycle-attr
public class BattleStress : MonoBehaviour
{
    [Tooltip("Soldiers per side in each battle with baked figures, in turn.")]
    [SerializeField] int[] sizes = { 20, 40, 60, 100, 150, 200 };
    [Tooltip("Soldiers per side in each battle with skinned figures, which are slow past about 60.")]
    [SerializeField] int[] skinnedSizes = { 20, 40, 60 };
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

        // stress3 showed CPU skinning was most of the figures' cost; this compares skinned figures with figures
        // skinned on the GPU from their bakes (BattleView.BakedFigures), at each size
        bool wasBaked = view.BakedFigures;
        foreach (bool baked in new[] { false, true })
            foreach (int size in baked ? sizes : skinnedSizes)
            {
                view.BakedFigures = baked;
                var army = Army(types, size, out int figures, out int riders);
                view.Begin(army, Army(types, size, out _, out _), false);
                yield return null;
                int skinned = 0, drawn = 0;
                foreach (var root in view.StressFigureRoots())
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                    {
                        if (renderer is SkinnedMeshRenderer) skinned++;
                        drawn++;
                    }
                yield return new WaitForSeconds(settle);
                view.TakeTimings();
                yield return Measure($"{size} a side, {(baked ? "baked" : "skinned")}", figures * 2, riders * 2);
                Log($"  {skinned} skinned meshes, {drawn} renderers on figures | view: {view.TakeTimings()}");
                view.Clear();
                yield return new WaitForSeconds(2f);
            }
        view.BakedFigures = wasBaked;
        Log("done");
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
