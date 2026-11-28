using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A battle figure's animations baked for the GPU: a mesh (body and props merged, bone indices and weights in UV3)
/// and a texture of every bone's pose in every frame of every clip, which the Baked Figure shader skins from.
/// Made by Tools > Battle > Bake Figures (<c>FigureBaker</c>); played by <see cref="BakedFigureAnimation"/>.
/// </summary>
/// <remarks>
/// <para>
/// The pose texture is RGBAHalf, two texels per bone, one row per frame. Each bone's texels are its rotation from
/// its bind pose (a quaternion) and its position in the figure's root space with a uniform scale in w, so skinning
/// is <c>p + s × rotate(q, v − pivot)</c>, where the pivot is the bone's bind position. Row 0 is a header: each bone's
/// pivot, and in w 1 for an upper-body bone (spine, head, arms) or 0. Clips follow one after another; a looping
/// clip ends with its first frame again so that filtering between rows wraps round.
/// </para>
/// <para>
/// Quaternions and positions blend without the shrinking that blending matrices gives mid-way between two poses.
/// Rows are read with bilinear filtering, which blends between frames for free.
/// </para>
/// </remarks>
public class BakedFigure : ScriptableObject
{
    [Serializable]
    public struct Clip
    {
        public AnimationClip clip;
        [Tooltip("The texture row of the clip's first frame.")]
        public int start;
        [Tooltip("Frames sampled. A looping clip has one row more: its first frame again.")]
        public int frames;
        public float length;
        public bool loop;
    }

    public const int TexelsPerBone = 2;
    /// <summary>Floats kept per row for a bone the CPU follows: rotation (4), then position and scale (4).</summary>
    public const int FloatsPerRow = 8;

    public Mesh mesh;
    [Tooltip("The same mesh with a dead face (the mount's eyes.dead blend shape), or none.")]
    public Mesh deadMesh;
    public Texture2D poses;
    [Tooltip("The material each figure's own copy is made from (the Baked Figure shader).")]
    public Material material;
    public int boneCount;
    public string[] boneNames = new string[0];
    public Clip[] clips = new Clip[0];
    [Tooltip("The bone actions are re-based on when they play on the upper body only (the hips), or -1.")]
    public int hipsBone = -1;
    [Tooltip("The bone a rider sits on (a mount's), or -1.")]
    public int seatBone = -1;
    [Tooltip("The hips bone's pose in every row, FloatsPerRow floats a row, row 0 unused.")]
    public float[] hipsPoses = new float[0];
    [Tooltip("The seat bone's pose in every row, as hipsPoses.")]
    public float[] seatPoses = new float[0];

    [NonSerialized] Dictionary<AnimationClip, int> lookup;

    // Play mode keeps the domain (and this cache) between sessions, so it's rebuilt whenever the asset changes
    void OnEnable() => lookup = null;
    void OnValidate() => lookup = null;
    /// <summary>Forgets the clip lookup, after a re-bake changed the clips.</summary>
    public void ResetLookup() => lookup = null;

    /// <summary>
    /// Whether this bake is usable and has every one of these clips (nulls are skipped). A figure whose clips aren't
    /// all baked (a clip changed on its type since the bake) uses the skinned path instead, because an unbaked
    /// moving clip would read the texture's header row and an unbaked death would never play.
    /// </summary>
    public bool Covers(IEnumerable<AnimationClip> needed)
    {
        if (mesh == null || material == null || poses == null)
            return false;
        foreach (var clip in needed)
            if (clip != null && !TryGetClip(clip, out _))
                return false;
        return true;
    }

    public bool TryGetClip(AnimationClip clip, out Clip baked)
    {
        baked = default;
        if (clip == null)
            return false;
        if (lookup == null)
        {
            lookup = new Dictionary<AnimationClip, int>();
            for (int i = 0; i < clips.Length; i++)
                if (clips[i].clip != null)
                    lookup[clips[i].clip] = i;
        }
        if (!lookup.TryGetValue(clip, out int index))
            return false;
        baked = clips[index];
        return true;
    }

    /// <summary>The texture row, between frames, for a time in a clip: wrapping round a looping clip, held at the end of any other.</summary>
    public static float Row(in Clip clip, float time)
    {
        float length = Mathf.Max(1e-4f, clip.length);
        if (clip.loop)
            return clip.start + Mathf.Repeat(time, length) / length * clip.frames;
        return clip.start + Mathf.Clamp01(time / length) * (clip.frames - 1);
    }

    /// <summary>A bone's pose read from one of the per-row arrays, blended between rows as the GPU's filtering does.</summary>
    public static void Sample(float[] data, float row, out Vector4 rotation, out Vector4 position)
    {
        int rows = data.Length / FloatsPerRow;
        if (rows == 0)
        {
            rotation = new Vector4(0f, 0f, 0f, 1f);
            position = new Vector4(0f, 0f, 0f, 1f);
            return;
        }
        int a = Mathf.Clamp(Mathf.FloorToInt(row), 0, rows - 1);
        int b = Mathf.Min(a + 1, rows - 1);
        float t = Mathf.Clamp01(row - a);
        rotation = Vector4.Lerp(Read(data, a, 0), Read(data, b, 0), t);
        position = Vector4.Lerp(Read(data, a, 4), Read(data, b, 4), t);
    }

    static Vector4 Read(float[] data, int row, int offset)
    {
        int i = row * FloatsPerRow + offset;
        return new Vector4(data[i], data[i + 1], data[i + 2], data[i + 3]);
    }
}
