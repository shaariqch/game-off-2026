using System.Diagnostics;
using System.Threading;
using UnityEngine;

// The 6000.7.0b7 Windows player hung in its own shutdown, after the input system stopped: the menu's Exit
// and closing the window both left the process running (seen 2026-11-06 in the previous game, still alive
// 40 s later). Not yet checked on b8. Once quitting starts, a background thread ends the process if it's
// still there after a short grace period. The game's own quit handlers have run by the time the hang happens.
public static class QuitWatchdog
{
    const int GraceMilliseconds = 3000;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Install()
    {
#if !UNITY_EDITOR && !UNITY_WEBGL
        Application.quitting -= Arm;
        Application.quitting += Arm;
#endif
    }

    static void Arm()
    {
        PlayerPrefs.Save();
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(GraceMilliseconds);
            Process.GetCurrentProcess().Kill();
        })
        { IsBackground = true, Name = "Quit watchdog" };
        watchdog.Start();
    }
}
