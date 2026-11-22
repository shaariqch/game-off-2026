using System;
using System.Collections.Generic;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using Unity.Scripting.LifecycleManagement; // lifecycle-attr
using UnityEngine;
using UnityEngine.InputSystem;

// The one place that decides whether the player can look around and act, and who owns the cursor.
//
// Look is read fresh from the Look action every frame, so nothing is held over between frames: in the previous game a
// latched value kept the last mouse movement until a "stopped" message arrived, and on WebGL a late one left the
// camera turning on its own. Menus and scripted moments call Block while they're up and Unblock when they're done;
// look stops and CanAct is false while anything is blocking, and the cursor is free while any block asks for it. Key
// presses themselves are still read by each script, which checks CanAct first. Only this sets Cursor.lockState.
// Its statics are reset by ResetStatics below, so Unity's automatic statics cleanup is off for it.
[NoAutoStaticsCleanup] // lifecycle-attr
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PlayerInput))]
public class GameInput : MonoBehaviour
{
    static GameInput instance;
    // each owner and whether it wants the cursor free
    static readonly Dictionary<object, bool> blocks = new Dictionary<object, bool>();

#pragma warning disable CS0414 // only used on WebGL
    // WebGL only: the browser let go of the pointer lock after granting it, usually its own Esc, which the game never
    // sees as a key. Open the pause menu here; its Continue click then asks for the lock again.
    public static event Action LockLost;
    bool hadLock;
    float nextLockRequest;
#pragma warning restore CS0414

    PlayerInput playerInput;
    InputAction lookAction;
    Vector2 look;
    bool lookIsMouse = true;
    bool? appliedFree;

#if UNITY_WEBGL && !UNITY_EDITOR
    // Plugins/WebGL/WebPage.jslib: Unity's Cursor.lockState there reports what was asked for, not what the browser did
    [DllImport("__Internal")] static extern int WebPage_IsPointerLocked();
    [DllImport("__Internal")] static extern void WebPage_RequestPointerLock();
    public static bool CursorLocked => WebPage_IsPointerLocked() != 0;
#else
    public static bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;
#endif

    // this frame's look, already scaled by the action's processors; zero while anything is blocking
    public static Vector2 Look => instance != null && blocks.Count == 0 ? instance.look : Vector2.zero;
    // this frame's look even while blocked, for logs
    public static Vector2 RawLook => instance != null ? instance.look : Vector2.zero;
    // mouse movement is a distance moved this frame, a stick is a rate to be scaled by the frame time
    public static bool LookIsMouse => instance == null || instance.lookIsMouse;
    // nothing is blocking and the cursor is actually locked into the game
    public static bool CanAct => blocks.Count == 0 && CursorLocked;
    public static bool Blocked => blocks.Count > 0;
    public static bool WantsFreeCursor
    {
        get
        {
            foreach (var free in blocks.Values) if (free) return true;
            return false;
        }
    }

    // who is blocking, for logs
    public static string Describe()
    {
        var names = new List<string>();
        foreach (var pair in blocks) names.Add(pair.Key is UnityEngine.Object o && o != null ? o.GetType().Name : pair.Key?.ToString() ?? "?");
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    public static void Block(object owner, bool freeCursor)
    {
        if (owner != null) blocks[owner] = freeCursor;
    }

    public static void Unblock(object owner)
    {
        if (owner != null) blocks.Remove(owner);
    }

    // Play mode can start without a domain reload, which would keep the last run's blocks
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        blocks.Clear();
        instance = null;
        LockLost = null;
    }

    void Awake()
    {
        instance = this;
        playerInput = GetComponent<PlayerInput>();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (lookAction == null && playerInput != null && playerInput.actions != null)
            lookAction = playerInput.actions.FindAction("Look");

        // a disabled action reads zero
        look = lookAction != null && lookAction.enabled ? lookAction.ReadValue<Vector2>() : Vector2.zero;
        var control = lookAction?.activeControl;
        if (control != null) lookIsMouse = control.device is Pointer;
    }

    void LateUpdate()
    {
        bool free = WantsFreeCursor;
        bool locked = CursorLocked;

#if UNITY_WEBGL && !UNITY_EDITOR
        // The browser only grants the lock shortly after a click or key press, and its Esc lets go of the lock without
        // the game seeing the key. So the lock is asked for when it becomes wanted (right after a Start or Continue
        // click) and again on any click while it's missing, and losing it after it was granted raises LockLost.
        bool changed = appliedFree != free;
        if (changed) Apply(free);
        if (!free)
        {
            if (locked) hadLock = true;
            else if (hadLock)
            {
                hadLock = false;
                LockLost?.Invoke();
            }
            else
            {
                var mouse = Mouse.current;
                bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
                if (changed || clicked || Time.unscaledTime >= nextLockRequest)
                {
                    nextLockRequest = Time.unscaledTime + 1f;
                    WebPage_RequestPointerLock();
                }
            }
        }
        else hadLock = false;
#else
        // anything else, like the window regaining focus, that changed the cursor is put back
        if (appliedFree != free || locked == free) Apply(free);
#endif
    }

    void Apply(bool free)
    {
        appliedFree = free;
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }
}
