using UnityEngine;

/// <summary>
/// What <see cref="BattleView"/> asks of a figure's animation, skinned (<see cref="FigureAnimation"/>) or baked for
/// the GPU (<see cref="BakedFigureAnimation"/>).
/// </summary>
public interface IFigureAnimation
{
    /// <summary>The clip of the action playing now, or null when only moving.</summary>
    AnimationClip Action { get; }
    /// <summary>Holding a final pose (a death) until disposed.</summary>
    bool Holding { get; }
    /// <summary>A rider: actions never take the legs, except a held death.</summary>
    bool Mounted { get; set; }
    /// <summary>Blends the moving clips for this ground speed and advances the action fades.</summary>
    void Update(float deltaTime, float speed);
    /// <summary>Fades a one-off clip in over whatever is playing.</summary>
    void Play(AnimationClip clip, float fade, float duration = 0f, bool hold = false, bool wholeBodyAlways = false);
    /// <summary>Stops time for this figure (it holds its pose) or starts it again.</summary>
    void SetPaused(bool paused);
    void Dispose();
}
