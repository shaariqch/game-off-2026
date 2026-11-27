using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif

/// <summary>
/// The game is played on the keyboard alone: no cursor, and on Windows none of the accessibility shortcuts that
/// pressing or holding Shift sets off.
/// </summary>
/// <remarks>
/// Pressing Shift five times opens the Sticky Keys prompt and holding right Shift for eight seconds opens the Filter
/// Keys one, and this game is all Shift presses and holds. Following Microsoft's advice for games, the shortcuts are
/// turned off only when the feature itself isn't on (a player who uses Sticky Keys keeps it), only while the game
/// has focus, and never saved: the settings are put back when focus goes and when the game quits. A browser can't do
/// this, so the WebGL build still shows the prompts.
/// One is made automatically after the first scene loads.
/// </remarks>
public class KeyboardOnly : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (FindAnyObjectByType<KeyboardOnly>() != null)
            return;
        var go = new GameObject("Keyboard Only");
        DontDestroyOnLoad(go);
        go.AddComponent<KeyboardOnly>();
    }

    void Update()
    {
        // the cursor comes back when the window regains focus, so it's hidden every frame
        if (Cursor.visible)
            Cursor.visible = false;
    }

    void OnEnable() => SetShortcuts(false);
    void OnApplicationFocus(bool focused) => SetShortcuts(!focused);
    void OnApplicationQuit() => SetShortcuts(true);
    void OnDisable()
    {
        SetShortcuts(true);
        Cursor.visible = true;
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [StructLayout(LayoutKind.Sequential)]
    struct StickyKeys { public uint size; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    struct FilterKeys { public uint size, flags, waitMs, delayMs, repeatMs, bounceMs; }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    static extern bool SystemParametersInfo(uint action, uint param, ref StickyKeys value, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    static extern bool SystemParametersInfo(uint action, uint param, ref FilterKeys value, uint winIni);

    const uint GetStickyKeys = 0x003A, SetStickyKeys = 0x003B, GetFilterKeys = 0x0032, SetFilterKeys = 0x0033;
    // the same bits for both: the feature is on, its shortcut is on, the shortcut asks first
    const uint FeatureOn = 0x1, HotkeyActive = 0x4, ConfirmHotkey = 0x8;

    StickyKeys savedSticky;
    FilterKeys savedFilter;
    bool saved, shortcutsOff;

    void SetShortcuts(bool allowed)
    {
        if (!saved)
        {
            savedSticky = new StickyKeys { size = (uint)Marshal.SizeOf<StickyKeys>() };
            savedFilter = new FilterKeys { size = (uint)Marshal.SizeOf<FilterKeys>() };
            if (!SystemParametersInfo(GetStickyKeys, savedSticky.size, ref savedSticky, 0)
                || !SystemParametersInfo(GetFilterKeys, savedFilter.size, ref savedFilter, 0))
                return;
            saved = true;
        }
        if (allowed == !shortcutsOff)
            return;
        if (allowed)
        {
            var sticky = savedSticky;
            var filter = savedFilter;
            SystemParametersInfo(SetStickyKeys, sticky.size, ref sticky, 0);
            SystemParametersInfo(SetFilterKeys, filter.size, ref filter, 0);
        }
        else
        {
            if ((savedSticky.flags & FeatureOn) == 0)
            {
                var sticky = savedSticky;
                sticky.flags &= ~(HotkeyActive | ConfirmHotkey);
                SystemParametersInfo(SetStickyKeys, sticky.size, ref sticky, 0);
            }
            if ((savedFilter.flags & FeatureOn) == 0)
            {
                var filter = savedFilter;
                filter.flags &= ~(HotkeyActive | ConfirmHotkey);
                SystemParametersInfo(SetFilterKeys, filter.size, ref filter, 0);
            }
        }
        shortcutsOff = !allowed;
    }
#else
    void SetShortcuts(bool allowed) { }
#endif
}
