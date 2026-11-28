using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Joins humanoid clips end to end into one clip, for actions a pack splits into parts. The archers' shot is Kevin
/// Iglesias' bow shot in three (load, hold, release), and a figure plays one clip per shot; the knockback is its
/// knockdown's fall and stand-up. Build with
/// Tools > Battle > Build Joined Clips; the clips go to the git-ignored ThirdParty folder, since they're made from
/// the pack's.
/// </summary>
public static class JoinedClips
{
    const string Bow = "Assets/_Project/ThirdParty/Kevin Iglesias/Human Animations/Animations/{0}/Combat/Bow/Human{1}@BowShot01 - {2}.fbx";
    const string Knockdown = "Assets/_Project/ThirdParty/Kevin Iglesias/Human Animations/Animations/{0}/Combat/Human{1}@Knockdown01 - {2}.fbx";
    const string Output ="Assets/_Project/ThirdParty/Animations/Joined/{0}.anim";

    [MenuItem("Tools/Battle/Build Joined Clips")]
    public static void Build()
    {
        foreach (var (folder, letter) in new[] { ("Male", "M"), ("Female", "F") })
        {
            Join("Bow Shot " + folder, new[]
            {
                string.Format(Bow, folder, letter, "Load"),
                string.Format(Bow, folder, letter, "Hold"),
                string.Format(Bow, folder, letter, "Release"),
            });
            // a charge's knockback: down and straight back up, so the figure doesn't snap upright from the ground
            Join("Knockdown " + folder, new[]
            {
                string.Format(Knockdown, folder, letter, "Fall"),
                string.Format(Knockdown, folder, letter, "StandUp"),
            });
        }
        AssetDatabase.SaveAssets();
    }

    static AnimationClip Load(string path)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                return clip;
        return null;
    }

    /// <summary>Writes the clips at <paramref name="paths"/>, one after another, as one clip called <paramref name="name"/>.</summary>
    public static AnimationClip Join(string name, IList<string> paths)
    {
        var joined = new AnimationClip { name = name };
        var curves = new Dictionary<EditorCurveBinding, AnimationCurve>();
        float offset = 0f;
        foreach (var path in paths)
        {
            var part = Load(path);
            if (part == null)
            {
                Debug.LogError("Joined clips: no clip at " + path);
                return null;
            }
            foreach (var binding in AnimationUtility.GetCurveBindings(part))
            {
                var source = AnimationUtility.GetEditorCurve(part, binding);
                if (!curves.TryGetValue(binding, out var curve))
                    curves[binding] = curve = new AnimationCurve();
                foreach (var key in source.keys)
                {
                    var moved = key;
                    moved.time += offset;
                    // a part's first key lands where the last one ended; keep the newer
                    if (curve.length > 0 && Mathf.Abs(curve.keys[curve.length - 1].time - moved.time) < 0.0001f)
                        curve.RemoveKey(curve.length - 1);
                    curve.AddKey(moved);
                }
            }
            offset += part.length;
        }
        foreach (var pair in curves)
            AnimationUtility.SetEditorCurve(joined, pair.Key, pair.Value);

        string output = string.Format(Output, name);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(output);
        if (existing != null)
        {
            EditorUtility.CopySerialized(joined, existing);
            EditorUtility.SetDirty(existing);
            joined = existing;
        }
        else
            AssetDatabase.CreateAsset(joined, output);
        Debug.Log($"Joined clips: {output}, {paths.Count} parts, {offset:0.00} s");
        return joined;
    }
}
