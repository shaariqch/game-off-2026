using System;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// One kind of battle sound: a few clips to pick from at random, a little pitch spread so repeats don't sound
/// identical, and a minimum gap so a melee of dozens of blows a second doesn't pile up into noise.
/// </summary>
[Serializable]
public class BattleSound
{
    public AudioClip[] clips = new AudioClip[0];
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Each play picks a pitch in this range.")]
    public Vector2 pitch = new Vector2(0.92f, 1.08f);
    [Tooltip("Real seconds before this sound can play again; plays asked for sooner are dropped.")]
    [Min(0f)] public float minGap = 0.05f;
    [Tooltip("Never cut off by other sounds when every voice is busy (for long ones such as the horns).")]
    public bool keep;

    [NonSerialized] float nextAt;

    /// <summary>
    /// Picks a clip and pitch for a play, or returns false when there's nothing to play or it played too recently.
    /// The caller plays it on a voice of its own, since a source's pitch changes every one-shot still playing on it.
    /// </summary>
    public bool Pick(out AudioClip clip, out float picked)
    {
        clip = null;
        picked = 1f;
        if (clips == null || clips.Length == 0 || Time.unscaledTime < nextAt)
            return false;
        clip = clips[Random.Range(0, clips.Length)];
        if (clip == null)
            return false;
        nextAt = Time.unscaledTime + minGap;
        picked = Random.Range(pitch.x, pitch.y);
        return true;
    }
}
