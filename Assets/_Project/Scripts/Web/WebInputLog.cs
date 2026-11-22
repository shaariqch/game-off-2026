using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

// Development WebGL builds only: each frame's mouse input, look and camera turn, for the local test log. The page
// sends the lines to Tools/webdev_server.py, which writes them beside the build (Builds/WebGL-Dev-log.txt), next to
// what the browser itself saw. Frames are logged while the mouse or camera moves or the input state changes, and
// once a second otherwise. The game's own log messages go there too.
[DefaultExecutionOrder(10000)]
public class WebInputLog : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void WebPage_Log(string text);
#else
    static void WebPage_Log(string text) { }
#endif

    string lastState;
    Vector3 lastAngles;
    float nextBeat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (!Debug.isDebugBuild || Application.platform != RuntimePlatform.WebGLPlayer) return;
        var go = new GameObject("Web Input Log");
        DontDestroyOnLoad(go);
        go.AddComponent<WebInputLog>();
    }

    void OnEnable() => Application.logMessageReceived += OnLog;
    void OnDisable() => Application.logMessageReceived -= OnLog;

    void OnLog(string message, string stackTrace, LogType type)
    {
        WebPage_Log($"log {type}: {message}" + (type == LogType.Exception || type == LogType.Error ? " | " + stackTrace.Replace('\n', ' ') : ""));
    }

    void Start()
    {
        WebPage_Log($"start {Screen.width}x{Screen.height} quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
    }

    void LateUpdate()
    {
        var mouse = Mouse.current;
        Vector2 raw = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
        Vector2 look = GameInput.RawLook;
        var cam = Camera.main;
        Vector3 angles = cam != null ? cam.transform.eulerAngles : Vector3.zero;
        float turnX = Mathf.DeltaAngle(lastAngles.y, angles.y), turnY = Mathf.DeltaAngle(lastAngles.x, angles.x);
        lastAngles = angles;

        string state = $"lock {(GameInput.CursorLocked ? "on" : "off")}/{Cursor.lockState} act {(GameInput.CanAct ? 1 : 0)} blocks {GameInput.Describe()} ts {Time.timeScale:0.##}";
        bool moving = raw != Vector2.zero || look != Vector2.zero || Mathf.Abs(turnX) > 0.001f || Mathf.Abs(turnY) > 0.001f;
        bool changed = state != lastState;
        if (!moving && !changed && Time.unscaledTime < nextBeat) return;
        nextBeat = Time.unscaledTime + 1f;
        lastState = state;

        WebPage_Log($"f{Time.frameCount} dt {Time.unscaledDeltaTime * 1000f:0.0} mouse {raw.x:0.#},{raw.y:0.#} look {look.x:0.###},{look.y:0.###} "
            + $"turn {turnX:0.##},{turnY:0.##} yaw {angles.y:0.#} pitch {Mathf.DeltaAngle(0f, angles.x):0.#} {state}");
    }
}
