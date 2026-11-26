using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// There's no riding animation among the packs, so the riders' seated pose is built from a standing idle: its first
/// frame, held, with the legs bent astride and the hands forward on the reins, set through humanoid muscle values.
/// Muscle directions on the Synty rig: hip flexion is negative Upper Leg Front-Back, abduction positive In-Out, a
/// bent knee negative Lower Leg Stretch; an arm comes forward with negative Arm Front-Back and the elbow bends with
/// negative Forearm Stretch. The clip has no foot IK goals, so riders play it with foot IK off: with it on, both
/// feet were pulled to the same default goal and the legs folded up under the rider. Build with
/// Tools > Battle > Build Riding Pose; the clip goes to the git-ignored
/// ThirdParty folder, since it starts from Synty's clip.
/// </summary>
public static class RidingPose
{
    const string Source = "Assets/_Project/ThirdParty/Synty/AnimationBaseLocomotion/Animations/Polygon/Feminine/Idle/A_Idle_Standing_Femn.fbx";
    const string Output = "Assets/_Project/ThirdParty/Animations/Riding Pose.anim";

    /// <summary>Muscle values for both sides (the side's name is put in front).</summary>
    static readonly Dictionary<string, float> Sides = new Dictionary<string, float>
    {
        // legs astride the Quirky horse's wide barrel: thighs forward and out, shins hanging down its sides, found by
        // searching these four for knees and feet outside the barrel (at the War Horse's scale and seat)
        { "Upper Leg Front-Back", 0.0f },
        { "Upper Leg In-Out", 0.6f },
        { "Upper Leg Twist In-Out", 0.5f },
        { "Lower Leg Stretch", 0.5f },
        { "Foot Up-Down", -0.35f },
        { "Arm Front-Back", -0.25f },
        { "Arm Down-Up", -0.55f },
        { "Forearm Stretch", -0.35f },
    };

    /// <summary>Single muscles: the spine, and the sword arm, which differs from the rein hand.</summary>
    static readonly Dictionary<string, float> Middle = new Dictionary<string, float>
    {
        { "Spine Front-Back", 0.12f },
        { "Chest Front-Back", 0.05f },
        // the sword hand at the hip, blade up and a little forward (the sword lies along the hand's +Y); found by
        // searching these four muscles for the most upright blade with the hand in front of the hips
        { "Right Arm Front-Back", 0f },
        { "Right Forearm Stretch", -0.2f },
        { "Right Hand Down-Up", 0.5f },
        { "Right Forearm Twist In-Out", 1f },
    };

    [MenuItem("Tools/Battle/Build Riding Pose")]
    public static void Build()
    {
        AnimationClip idle = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(Source))
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                idle = clip;
        if (idle == null)
        {
            Debug.LogError("Riding pose: no idle clip at " + Source);
            return;
        }
        var muscles = new Dictionary<string, float>();
        foreach (var pair in Sides)
        {
            muscles["Left " + pair.Key] = pair.Value;
            muscles["Right " + pair.Key] = pair.Value;
        }
        // single muscles last, so the sword arm's own values win over both sides'
        foreach (var pair in Middle)
            muscles[pair.Key] = pair.Value;

        var pose = new AnimationClip { name = "Riding Pose" };
        var written = new HashSet<string>();
        foreach (var binding in AnimationUtility.GetCurveBindings(idle))
        {
            string property = binding.propertyName;
            // drop the hand and foot IK goals, which would pull the bent legs back to standing
            if (IsGoal(property))
                continue;
            float value = AnimationUtility.GetEditorCurve(idle, binding).Evaluate(0f);
            if (muscles.TryGetValue(property, out float set))
                value = set;
            AnimationUtility.SetEditorCurve(pose, binding, AnimationCurve.Constant(0f, 1f, value));
            written.Add(property);
        }
        foreach (var pair in muscles)
            if (!written.Contains(pair.Key))
                AnimationUtility.SetEditorCurve(pose, EditorCurveBinding.FloatCurve("", typeof(Animator), pair.Key), AnimationCurve.Constant(0f, 1f, pair.Value));
        var settings = AnimationUtility.GetAnimationClipSettings(pose);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(pose, settings);

        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(Output);
        if (existing != null)
        {
            EditorUtility.CopySerialized(pose, existing);
            EditorUtility.SetDirty(existing);
        }
        else
            AssetDatabase.CreateAsset(pose, Output);
        AssetDatabase.SaveAssets();
        Debug.Log("Riding pose written to " + Output + " (" + written.Count + " curves from " + idle.name + ")");
    }

    static bool IsGoal(string property) =>
        (property.StartsWith("LeftFoot") || property.StartsWith("RightFoot") || property.StartsWith("LeftHand") || property.StartsWith("RightHand"))
        && (property.Contains("T.") || property.Contains("Q.")) && !property.Contains(" ");
}
