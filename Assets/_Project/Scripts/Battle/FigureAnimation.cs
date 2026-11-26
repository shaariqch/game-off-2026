using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Plays one battle figure's animations through a small Playables graph on its Animator: idle, walk and run
/// blended by how fast the figure is moving, and one-off actions (an attack, a shot, a hit, a death) faded in over
/// them. Two action slots let a new action fade in while the last one fades out. The graph runs in game time, so
/// slow motion and freeze frames slow and stop it too. Call <see cref="Dispose"/> when the figure goes.
/// </summary>
public sealed class FigureAnimation
{
    const int Locomotion = 0;
    const int Idle = 0, Walk = 1, Run = 2;

    PlayableGraph graph;
    readonly AnimationMixerPlayable root;
    readonly AnimationMixerPlayable locomotion;
    readonly AnimationClipPlayable[] moves = new AnimationClipPlayable[3];
    readonly float walkSpeed, runSpeed;
    readonly float[] weights = { 1f, 0f, 0f };
    readonly float[] targets = { 1f, 0f, 0f };
    readonly float[] fadeRates = { 10f, 10f, 10f };
    int current = 1;
    float actionEndsIn;
    bool holding;

    public bool Valid => graph.IsValid();
    /// <summary>The clip of the action playing now, or null when only moving.</summary>
    public AnimationClip Action { get; private set; }
    /// <summary>Holding a final pose (a death) until disposed.</summary>
    public bool Holding => holding;

    public FigureAnimation(Animator animator, AnimationClip idle, AnimationClip walk, AnimationClip run, float walkSpeed, float runSpeed, float startTime)
    {
        this.walkSpeed = Mathf.Max(0.1f, walkSpeed);
        this.runSpeed = Mathf.Max(this.walkSpeed + 0.1f, runSpeed);
        graph = PlayableGraph.Create(animator.name + " figure");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Figure", animator);

        locomotion = AnimationMixerPlayable.Create(graph, 3);
        var clips = new[] { idle, walk != null ? walk : idle, run != null ? run : (walk != null ? walk : idle) };
        for (int i = 0; i < 3; i++)
        {
            moves[i] = AnimationClipPlayable.Create(graph, clips[i]);
            // start each figure at a different point in its cycles, so a formation doesn't step in unison
            moves[i].SetTime(startTime * clips[i].length);
            graph.Connect(moves[i], 0, locomotion, i);
        }
        locomotion.SetInputWeight(Idle, 1f);

        root = AnimationMixerPlayable.Create(graph, 3);
        graph.Connect(locomotion, 0, root, Locomotion);
        root.SetInputWeight(Locomotion, 1f);
        output.SetSourcePlayable(root);
        graph.Play();
    }

    /// <summary>Blends the moving clips for this ground speed and advances the action fades.</summary>
    public void Update(float deltaTime, float speed)
    {
        if (!graph.IsValid())
            return;
        // idle to walk up to walking speed, walk to run from there to running speed
        float walk = Mathf.Clamp01(speed / walkSpeed);
        float run = Mathf.Clamp01((speed - walkSpeed) / (runSpeed - walkSpeed));
        locomotion.SetInputWeight(Idle, 1f - walk);
        locomotion.SetInputWeight(Walk, walk * (1f - run));
        locomotion.SetInputWeight(Run, walk * run);
        // feet keep pace with the ground
        moves[Walk].SetSpeed(Mathf.Clamp(speed / walkSpeed, 0.6f, 1.5f));
        moves[Run].SetSpeed(Mathf.Clamp(speed / runSpeed, 0.6f, 1.6f));

        if (Action != null && !holding)
        {
            actionEndsIn -= deltaTime;
            if (actionEndsIn <= 0f)
            {
                targets[current] = 0f;
                Action = null;
            }
        }
        float actions = 0f;
        for (int slot = 1; slot < 3; slot++)
        {
            weights[slot] = Mathf.MoveTowards(weights[slot], targets[slot], fadeRates[slot] * deltaTime);
            actions += weights[slot];
        }
        if (actions > 1f)
            for (int slot = 1; slot < 3; slot++)
                weights[slot] /= actions;
        weights[Locomotion] = Mathf.Clamp01(1f - actions);
        for (int slot = 0; slot < 3; slot++)
            root.SetInputWeight(slot, weights[slot]);
    }

    /// <summary>Fades a one-off clip in over whatever is playing.</summary>
    /// <param name="duration">Seconds to play it for; the clip is sped up or slowed to fit. 0 plays it at its own speed.</param>
    /// <param name="hold">Stay on its last frame until disposed (a death).</param>
    public void Play(AnimationClip clip, float fade, float duration = 0f, bool hold = false)
    {
        if (!graph.IsValid() || clip == null || holding)
            return;
        float fadeRate = 1f / Mathf.Max(0.01f, fade);
        // the slot not in use takes the new clip; the old one fades out
        targets[current] = 0f;
        fadeRates[current] = fadeRate;
        current = current == 1 ? 2 : 1;
        var input = root.GetInput(current);
        if (input.IsValid())
        {
            graph.Disconnect(root, current);
            input.Destroy();
        }
        var playable = AnimationClipPlayable.Create(graph, clip);
        float speed = duration > 0f ? clip.length / duration : 1f;
        playable.SetSpeed(speed);
        playable.SetDuration(clip.length);
        if (hold)
            playable.SetApplyFootIK(false);
        graph.Connect(playable, 0, root, current);
        weights[current] = 0f;
        targets[current] = 1f;
        fadeRates[current] = fadeRate;
        Action = clip;
        holding = hold;
        // fade back out shortly before it ends
        actionEndsIn = clip.length / Mathf.Max(0.01f, speed) - fade;
    }

    public void Dispose()
    {
        if (graph.IsValid())
            graph.Destroy();
    }
}
