using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

// One step for a release: builds Windows and WebGL from the enabled scenes in Build Settings, optionally pushes both
// to itch with butler, then switches the Editor back to the platform it was on. Unity can only build for the active
// platform, so the switch can't be avoided, but after the first time its imports come from the cache. The Editor is
// busy for the whole run. From the menu: Build > Release. From the command line: Tools/release.sh <version>.
public static class ReleaseBuilds
{
    // itch user/game, e.g. "shaariqch/my-game"; pushing is skipped while it's empty
    const string ItchGame = "";
    const bool IncludeWindows = true;
    const bool IncludeWebGL = true;
    const string WindowsFolder = "Builds/Windows";
    const string WebGLFolder = "Builds/WebGL";
    const string WindowsChannel = "windows";
    const string WebGLChannel = "html5";

    static string ButlerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "butler", "butler.exe");

    [MenuItem("Build/Release/Build")]
    static void BuildMenu() => RunFromMenu(false);

    [MenuItem("Build/Release/Build, then push to itch")]
    static void BuildAndPushMenu() => RunFromMenu(true);

    static void RunFromMenu(bool push)
    {
        string question = $"Builds version {PlayerSettings.bundleVersion} for {Platforms()}{(push ? ", then pushes to itch" : "")}. " +
                          "The Editor is busy until it's done. Change the version first in Player Settings if needed.";
        if (!EditorUtility.DisplayDialog("Release", question, "Build", "Cancel")) return;
        // the builds use the saved scenes
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string report = BuildAll(null, push, false);
        Debug.Log(report);
        EditorUtility.DisplayDialog("Release", report, "OK");
    }

    static string Platforms() => IncludeWindows && IncludeWebGL ? "Windows and WebGL" : IncludeWebGL ? "WebGL" : "Windows";

    // version null keeps PlayerSettings.bundleVersion. saveScenes saves open scenes without asking, for the command line.
    public static string BuildAll(string version, bool push, bool saveScenes = true)
    {
        if (saveScenes) EditorSceneManager.SaveOpenScenes();
        if (!string.IsNullOrEmpty(version))
        {
            PlayerSettings.bundleVersion = version;
            AssetDatabase.SaveAssets();
        }
        version = PlayerSettings.bundleVersion;

        var report = new StringBuilder($"Version {version}\n");
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) return report.Append("No enabled scenes in Build Settings.").ToString();
        report.AppendLine($"Scenes: {string.Join(", ", scenes)}");

        var startTarget = EditorUserBuildSettings.activeBuildTarget;
        var startGroup = BuildPipeline.GetBuildTargetGroup(startTarget);
        // switching platforms has turned this off before; keep it as it was
        bool runInBackground = PlayerSettings.runInBackground;
        bool windowsOk = !IncludeWindows, webOk = !IncludeWebGL;
        string exe = Path.Combine(WindowsFolder, PlayerSettings.productName + ".exe");

        try
        {
            if (IncludeWindows) windowsOk = Build("Windows", BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, exe, scenes, report);
            if (windowsOk && IncludeWebGL)
            {
                if (Directory.Exists(WebGLFolder)) Directory.Delete(WebGLFolder, true);
                webOk = Build("WebGL", BuildTarget.WebGL, BuildTargetGroup.WebGL, WebGLFolder, scenes, report) && CheckWebGLFiles(report);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (EditorUserBuildSettings.activeBuildTarget != startTarget)
            {
                EditorUtility.DisplayProgressBar("Release", $"Switching back to {startTarget}", 1f);
                EditorUserBuildSettings.SwitchActiveBuildTarget(startGroup, startTarget);
                EditorUtility.ClearProgressBar();
            }
            PlayerSettings.runInBackground = runInBackground;
            // builds leave a mid-build copy of the project settings on disk (Run In Background off, the Input
            // System's actions in Preloaded Assets); saving writes the settings as they really are again
            AssetDatabase.SaveAssets();
        }

        if (!push) return report.ToString();
        if (string.IsNullOrEmpty(ItchGame)) return report.Append("Not pushed: set ItchGame in ReleaseBuilds.cs first.").ToString();
        if (!windowsOk || !webOk) return report.Append("Not pushed, because a build failed.").ToString();

        if (IncludeWindows)
            Push(WindowsFolder, WindowsChannel, version, report,
                "--ignore", "*_BackUpThisFolder_ButDontShipItWithYourGame", "--ignore", "*_BurstDebugInformation_DoNotShip");
        if (IncludeWebGL) Push(WebGLFolder, WebGLChannel, version, report);
        return report.ToString();
    }

    static bool Build(string label, BuildTarget target, BuildTargetGroup group, string path, string[] scenes, StringBuilder report)
    {
        if (!BuildPipeline.IsBuildTargetSupported(group, target))
        {
            report.AppendLine($"{label}: its build module isn't installed for this Editor (Unity Hub), then restart the Editor");
            return false;
        }
        if (EditorUserBuildSettings.activeBuildTarget != target)
        {
            EditorUtility.DisplayProgressBar("Release", $"Switching to {label} (slow the first time)", 0f);
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
            {
                report.AppendLine($"{label}: couldn't switch to it");
                return false;
            }
        }

        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = target,
            targetGroup = group,
            options = BuildOptions.None,
        });
        var summary = result.summary;
        bool ok = summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
        report.AppendLine($"{label}: {summary.result} in {summary.totalTime:mm\\:ss}, {summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors");
        return ok;
    }

    // A release WebGL build's final IL2CPP and wasm step needs a lot of memory, and once died with the PC low on it,
    // leaving only the .data file while still looking like a finished build.
    static bool CheckWebGLFiles(StringBuilder report)
    {
        string folder = Path.Combine(WebGLFolder, "Build");
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder) : new string[0];
        bool ok = files.Length >= 4 && files.All(f => new FileInfo(f).Length > 0);
        if (!ok) report.AppendLine($"WebGL: expected 4 files in {folder} (loader, framework, wasm, data), found {files.Length}");
        return ok;
    }

    static void Push(string folder, string channel, string version, StringBuilder report, params string[] extra)
    {
        if (!File.Exists(ButlerPath))
        {
            report.AppendLine($"{channel}: not pushed, butler isn't at {ButlerPath}");
            return;
        }

        EditorUtility.DisplayProgressBar("Release", $"Pushing {channel} to itch", 0.5f);
        try
        {
            var args = new[] { "push", folder, $"{ItchGame}:{channel}" }.Concat(extra).Concat(new[] { "--userversion", version });
            var start = new ProcessStartInfo(ButlerPath, string.Join(" ", args.Select(Quote)))
            {
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(start);
            // read both streams as they come, so neither fills up and stalls butler
            var errors = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            // butler redraws its progress bar with carriage returns; keep only its result lines
            var lines = (output + "\n" + errors.Result).Split('\n', '\r')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("√") || line.StartsWith("✗") || line.Contains("rror"));
            report.AppendLine($"{channel}: butler exited {process.ExitCode}. {string.Join(" ", lines)}");
        }
        catch (Exception e)
        {
            report.AppendLine($"{channel}: push failed, {e.Message}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static string Quote(string arg) => arg.Contains(' ') || arg.Contains('*') ? $"\"{arg}\"" : arg;
}
