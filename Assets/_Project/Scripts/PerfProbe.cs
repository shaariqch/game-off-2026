using System.Text;
using Unity.Profiling;
using Unity.Scripting.LifecycleManagement; // lifecycle-attr
using UnityEngine;

// Averages the profiler's frame counters over the last couple of seconds, for PerfTour and for
// reading from Editor scripts while testing performance. Only runs in the Editor and development builds.
// instance is set again by Create on every load, so Unity's automatic statics cleanup is off for it
[NoAutoStaticsCleanup] // lifecycle-attr
public class PerfProbe : MonoBehaviour
{
    const int Window = 120;

    static PerfProbe instance;

    ProfilerRecorder mainThread, renderThread, drawCalls, setPassCalls, triangles, shadowCasters;
    readonly FrameTiming[] timing = new FrameTiming[1];
    readonly float[] frame = new float[Window], cpu = new float[Window], gpu = new float[Window], render = new float[Window];
    readonly float[] draws = new float[Window], setPasses = new float[Window], tris = new float[Window], casters = new float[Window];
    int index, count;

    public static string Summary => instance != null ? instance.Describe() : "PerfProbe isn't running";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (!Debug.isDebugBuild) return;
        // Play mode here starts without a domain reload, and a hidden object can outlive its session
        if (instance != null) Destroy(instance.gameObject);
        var go = new GameObject("Perf Probe") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PerfProbe>();
    }

    void OnEnable()
    {
        mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 15);
        drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        setPassCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
    }

    void OnDisable()
    {
        mainThread.Dispose();
        renderThread.Dispose();
        drawCalls.Dispose();
        setPassCalls.Dispose();
        triangles.Dispose();
        shadowCasters.Dispose();
    }

    void Update()
    {
        FrameTimingManager.CaptureFrameTimings();
        bool haveTiming = FrameTimingManager.GetLatestTimings(1, timing) > 0;

        frame[index] = Time.unscaledDeltaTime * 1000f;
        cpu[index] = mainThread.Valid ? mainThread.LastValue * 1e-6f : 0f;
        render[index] = renderThread.Valid ? renderThread.LastValue * 1e-6f : 0f;
        gpu[index] = haveTiming ? (float)timing[0].gpuFrameTime : 0f;
        draws[index] = drawCalls.Valid ? drawCalls.LastValue : 0f;
        setPasses[index] = setPassCalls.Valid ? setPassCalls.LastValue : 0f;
        tris[index] = triangles.Valid ? triangles.LastValue : 0f;
        casters[index] = shadowCasters.Valid ? shadowCasters.LastValue : 0f;

        index = (index + 1) % Window;
        if (count < Window) count++;
    }

    float Average(float[] values)
    {
        if (count == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < count; i++) sum += values[i];
        return sum / count;
    }

    string Describe()
    {
        float ms = Average(frame);
        var text = new StringBuilder();
        text.Append($"{(ms > 0f ? 1000f / ms : 0f):0.0} fps ({ms:0.0} ms)   main thread {Average(cpu):0.0} ms   render thread {Average(render):0.0} ms   GPU {Average(gpu):0.0} ms\n");
        text.Append($"draw calls {Average(draws):0}   set pass {Average(setPasses):0}   triangles {Average(tris) / 1e6f:0.00}M   shadow casters {Average(casters):0}");
        return text.ToString();
    }
}
